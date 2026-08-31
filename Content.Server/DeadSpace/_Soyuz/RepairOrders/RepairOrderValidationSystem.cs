using System.Numerics;
using System.Linq;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Robust.Server.Physics;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Builds immutable repair blueprints and incrementally validates only affected grid cells.
/// </summary>
public sealed class RepairOrderValidationSystem : EntitySystem
{
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly RepairOrderSystem _repairOrders = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefinitions = default!;

    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _dirtyCells = new();
    private readonly Dictionary<EntityUid, RepairScoreLookup> _scoreLookups = new();
    private readonly Dictionary<EntityUid, MapId> _temporaryTargetMaps = new();
    private readonly HashSet<EntityUid> _blueprintsShuttingDown = new();
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("repair_orders");

        SubscribeLocalEvent<RepairBlueprintComponent, ComponentShutdown>(OnBlueprintShutdown);
        SubscribeLocalEvent<RepairBlueprintComponent, GridSplitEvent>(OnGridSplit);
        SubscribeLocalEvent<TransformComponent, EntityTerminatingEvent>(OnTransformTerminating);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<AnchorStateChangedEvent>(OnAnchorStateChanged);
        _transform.OnGlobalMoveEvent += OnMove;
    }

    public override void Shutdown()
    {
        _transform.OnGlobalMoveEvent -= OnMove;
        _dirtyCells.Clear();
        _scoreLookups.Clear();
        _temporaryTargetMaps.Clear();
        _blueprintsShuttingDown.Clear();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_dirtyCells.Count == 0)
            return;

        // Events can arrive before all snap-grid bookkeeping is finalized. Coalesce cells and validate next update.
        var pending = _dirtyCells.ToArray();
        _dirtyCells.Clear();

        foreach (var (gridUid, cells) in pending)
        {
            if (!TryComp<RepairBlueprintComponent>(gridUid, out var blueprint) || !blueprint.Ready)
                continue;

            var progressChanged = false;
            foreach (var cell in cells)
            {
                progressChanged |= RevalidateCell((gridUid, blueprint), cell);
            }

            if (progressChanged)
            {
                SyncProgress((gridUid, blueprint));
            }
        }
    }

    /// <summary>
    /// Builds and validates all runtime state required by an order before it is published as active.
    /// On failure, no validation components or deferred dirty-cell state are retained on the damaged grid.
    /// </summary>
    public bool TryPrepareSession(
        EntityUid stationUid,
        int runtimeId,
        ProtoId<RepairOrderPrototype> orderPrototype,
        EntityUid gridUid,
        out ActiveRepairOrder preparedSession)
    {
        preparedSession = new ActiveRepairOrder(runtimeId, orderPrototype, gridUid);
        var runtimeAttached = false;
        var prepared = false;

        try
        {
            if (!TryComp<RepairOrderStationComponent>(stationUid, out var station) || station.Active != null)
            {
                _sawmill.Error(
                    $"Cannot prepare repair session for station {stationUid}: station state is missing or already active.");
                return false;
            }

            if (!TryComp<MapGridComponent>(gridUid, out _))
            {
                _sawmill.Error(
                    $"Cannot prepare repair session for station {stationUid}: damaged grid {gridUid} is invalid.");
                return false;
            }

            if (!TryGetOrderPrototype(orderPrototype, out var order))
                return false;

            var blueprint = EnsureComp<RepairBlueprintComponent>(gridUid);
            runtimeAttached = true;
            ResetBlueprintRuntime(blueprint, stationUid, orderPrototype);

            if (!TryBuildBlueprint((gridUid, blueprint), order))
                return false;

            blueprint.Ready = true;
            if (!RevalidateAll(gridUid))
            {
                _sawmill.Error(
                    $"Cannot prepare repair session for order {orderPrototype} on grid {gridUid}: initial validation failed.");
                return false;
            }

            preparedSession.CompletedTasks = blueprint.CompletedTasks;
            preparedSession.TotalTasks = blueprint.TotalTasks;
            preparedSession.BlueprintReady = blueprint.Ready;
            preparedSession.CurrentPoints = blueprint.CurrentPoints;
            preparedSession.MaxPoints = blueprint.MaxPoints;
            prepared = true;

            _sawmill.Info(
                $"Prepared repair blueprint for order {orderPrototype} on grid {gridUid}: " +
                $"{blueprint.TotalTasks} target requirements, {blueprint.CompletedTasks} initially correct.");
            return true;
        }
        catch (Exception exception)
        {
            _sawmill.Error(
                $"Cannot prepare repair session for order {orderPrototype} on grid {gridUid}: {exception}");
            return false;
        }
        finally
        {
            if (!prepared && runtimeAttached)
                DiscardPreparedSession(gridUid);
        }
    }

    private static void ResetBlueprintRuntime(
        RepairBlueprintComponent blueprint,
        EntityUid stationUid,
        ProtoId<RepairOrderPrototype> orderPrototype)
    {
        blueprint.Station = stationUid;
        blueprint.OrderPrototype = orderPrototype;
        blueprint.TasksByCell.Clear();
        blueprint.ExpectedCells.Clear();
        blueprint.UnexpectedBaselineCells.Clear();
        blueprint.EntityIdentityRules.Clear();
        blueprint.TileIdentityIds.Clear();
        blueprint.TotalTasks = 0;
        blueprint.CompletedTasks = 0;
        blueprint.MaxPoints = 0;
        blueprint.CurrentPoints = 0;
        blueprint.Ready = false;
        blueprint.BaselineInitialized = false;
        blueprint.FullyMatchesTarget = false;
    }

    /// <summary>
    /// Removes validation state owned by an activation attempt which was not committed.
    /// </summary>
    public void DiscardPreparedSession(EntityUid gridUid)
    {
        _dirtyCells.Remove(gridUid);
        _scoreLookups.Remove(gridUid);
        DeleteTemporaryTargetMap(gridUid);
        if (!Exists(gridUid))
            return;

        if (MetaData(gridUid).EntityLifeStage < EntityLifeStage.Terminating &&
            !_blueprintsShuttingDown.Contains(gridUid))
        {
            RemComp<RepairBlueprintComponent>(gridUid);
        }
    }

    private bool TryGetOrderPrototype(
        ProtoId<RepairOrderPrototype> prototype,
        out RepairOrderPrototype order)
    {
        if (_prototype.TryIndex<RepairOrderPrototype>(prototype, out order!))
        {
            return true;
        }

        _sawmill.Error($"Cannot build repair blueprint: order prototype {prototype} no longer exists.");
        return false;
    }

    private bool TryBuildBlueprint(
        Entity<RepairBlueprintComponent> blueprint,
        RepairOrderPrototype order)
    {
        MapId? temporaryMapId = null;

        try
        {
            if (!TryComp<MapGridComponent>(blueprint.Owner, out var repairGrid))
            {
                _sawmill.Error($"Cannot build repair blueprint for {order.ID}: damaged grid {blueprint.Owner} no longer exists.");
                return false;
            }

            var temporaryMap = _map.CreateMap(out var mapId);
            temporaryMapId = mapId;
            _temporaryTargetMaps[blueprint.Owner] = mapId;
            _map.SetPaused(temporaryMap, true);

            // TryLoadGrid also rejects files which do not contain exactly one grid.
            if (!_loader.TryLoadGrid(mapId, order.TargetGridPath, out var loadedTarget))
            {
                _sawmill.Error($"Cannot build repair blueprint for {order.ID}: target grid {order.TargetGridPath} failed to load or does not contain exactly one grid.");
                return false;
            }

            var target = loadedTarget.Value;
            var scoreLookup = BuildScoreLookup(order);
            _scoreLookups[blueprint.Owner] = scoreLookup;
            blueprint.Comp.EntityIdentityRules.Clear();
            blueprint.Comp.EntityIdentityRules.AddRange(scoreLookup.EntityIdentityRules);
            blueprint.Comp.TileIdentityIds.Clear();
            foreach (var (tileId, canonicalTileId) in scoreLookup.TileIdentityIds)
            {
                blueprint.Comp.TileIdentityIds[tileId] = canonicalTileId;
            }

            BuildExpectedTarget(blueprint, target, scoreLookup);
            return true;
        }
        catch (Exception exception)
        {
            blueprint.Comp.TasksByCell.Clear();
            blueprint.Comp.ExpectedCells.Clear();
            blueprint.Comp.UnexpectedBaselineCells.Clear();
            blueprint.Comp.EntityIdentityRules.Clear();
            blueprint.Comp.TileIdentityIds.Clear();
            blueprint.Comp.TotalTasks = 0;
            blueprint.Comp.CompletedTasks = 0;
            blueprint.Comp.MaxPoints = 0;
            blueprint.Comp.CurrentPoints = 0;
            blueprint.Comp.BaselineInitialized = false;
            blueprint.Comp.FullyMatchesTarget = false;
            _scoreLookups.Remove(blueprint.Owner);
            _sawmill.Error($"Cannot build repair blueprint for {order.ID}: {exception}");
            return false;
        }
        finally
        {
            // This deletes the target grid and all of its children; none of them enter the playable map.
            DeleteTemporaryTargetMap(blueprint.Owner, temporaryMapId);
        }
    }

    private void DeleteTemporaryTargetMap(EntityUid repairGrid, MapId? fallbackMap = null)
    {
        var tracked = _temporaryTargetMaps.TryGetValue(repairGrid, out var trackedMap);
        var targetMap = tracked ? trackedMap : fallbackMap;
        if (targetMap is not { } mapId)
            return;

        if (_map.MapExists(mapId))
            _map.DeleteMap(mapId);

        if (tracked)
            _temporaryTargetMaps.Remove(repairGrid);
    }

    private void BuildExpectedTarget(
        Entity<RepairBlueprintComponent> blueprint,
        Entity<MapGridComponent> target,
        RepairScoreLookup scoreLookup)
    {
        blueprint.Comp.ExpectedCells.Clear();
        blueprint.Comp.UnexpectedBaselineCells.Clear();
        blueprint.Comp.TasksByCell.Clear();

        foreach (var targetTile in _map.GetAllTiles(target.Owner, target.Comp))
        {
            var expectedTileId = targetTile.Tile.TypeId;
            var expectedTilePrototype = ((ContentTileDefinition) _tileDefinitions[expectedTileId]).ID;
            var expectedCanonicalTileId = CanonicalizeTileId(scoreLookup.TileIdentityIds, expectedTileId);
            var expectedCanonicalTilePrototype = ((ContentTileDefinition) _tileDefinitions[expectedCanonicalTileId]).ID;
            var expectedCell = GetOrCreateExpectedCell(blueprint.Comp, targetTile.GridIndices);
            expectedCell.Tile = new RepairExpectedTileState
            {
                TileId = expectedTileId,
                TilePrototype = expectedTilePrototype,
                CanonicalTileId = expectedCanonicalTileId,
                CanonicalTilePrototype = expectedCanonicalTilePrototype,
                Points = ResolveTilePoints(scoreLookup, expectedCanonicalTilePrototype),
            };
        }

        foreach (var (signature, targetEntity) in SnapshotAnchoredEntities(target, scoreLookup))
        {
            var expectedCell = GetOrCreateExpectedCell(blueprint.Comp, signature.Cell);
            expectedCell.Entities.Add(new RepairExpectedEntityState
            {
                Signature = new RepairAnchoredEntitySignature(
                    signature.Prototype,
                    signature.LocalPosition,
                    signature.LocalRotation,
                    signature.RotationMode),
                DisplayLocalRotation = targetEntity.DisplayLocalRotation,
                Count = targetEntity.Count,
                Points = ResolveEntityPoints(scoreLookup, signature.Prototype),
            });
        }
    }

    private static RepairExpectedCellState GetOrCreateExpectedCell(
        RepairBlueprintComponent blueprint,
        Vector2i cell)
    {
        if (!blueprint.ExpectedCells.TryGetValue(cell, out var expected))
        {
            expected = new RepairExpectedCellState();
            blueprint.ExpectedCells.Add(cell, expected);
        }

        return expected;
    }

    private Dictionary<AnchoredEntitySignature, AnchoredEntitySnapshot> SnapshotAnchoredEntities(
        Entity<MapGridComponent> grid,
        RepairScoreLookup scoreLookup)
    {
        var result = new Dictionary<AnchoredEntitySignature, AnchoredEntitySnapshot>();
        var children = Transform(grid.Owner).ChildEnumerator;

        while (children.MoveNext(out var child))
        {
            if (!TryComp(child, out TransformComponent? xform) ||
                !xform.Anchored ||
                xform.ParentUid != grid.Owner ||
                MetaData(child).EntityPrototype?.ID is not { } prototypeId)
            {
                continue;
            }

            var rotationMode = ResolveRotationMode(scoreLookup, prototypeId);
            var canonicalPrototype = CanonicalizeEntityPrototype(scoreLookup.EntityIdentityRules, prototypeId);
            var displayRotation = xform.LocalRotation.Reduced().FlipPositive();
            var signature = new AnchoredEntitySignature(
                canonicalPrototype,
                xform.LocalPosition,
                CanonicalizeRotation(xform.LocalRotation, rotationMode),
                rotationMode,
                LocalPositionToCell(grid.Comp, xform.LocalPosition));

            if (result.TryGetValue(signature, out var entry))
            {
                entry.Count++;
                continue;
            }

            result.Add(signature, new AnchoredEntitySnapshot
            {
                Count = 1,
                DisplayLocalRotation = displayRotation,
            });
        }

        return result;
    }

    /// <summary>
    /// Authoritatively compares the complete current grid against the immutable target snapshot.
    /// </summary>
    public bool RevalidateAll(EntityUid repairGrid)
    {
        if (!TryComp<RepairBlueprintComponent>(repairGrid, out var blueprint) ||
            !blueprint.Ready ||
            !_scoreLookups.TryGetValue(repairGrid, out var scoreLookup) ||
            !TryComp<MapGridComponent>(repairGrid, out var grid))
        {
            return false;
        }

        var actual = SnapshotActualGrid((repairGrid, grid), scoreLookup);
        var cells = new HashSet<Vector2i>(blueprint.ExpectedCells.Keys);
        cells.UnionWith(blueprint.UnexpectedBaselineCells.Keys);
        cells.UnionWith(blueprint.TasksByCell.Keys);
        cells.UnionWith(actual.Cells.Keys);

        var initializeBaseline = !blueprint.BaselineInitialized;
        if (initializeBaseline)
        {
            blueprint.UnexpectedBaselineCells.Clear();
            foreach (var cell in cells)
            {
                blueprint.ExpectedCells.TryGetValue(cell, out var expectedCell);
                actual.Cells.TryGetValue(cell, out var actualCell);
                CaptureBaselineCell(
                    blueprint,
                    cell,
                    expectedCell,
                    actualCell ?? ActualCellState.Empty,
                    scoreLookup);
            }

            blueprint.BaselineInitialized = true;
        }

        blueprint.TasksByCell.Clear();
        foreach (var cell in cells)
        {
            blueprint.ExpectedCells.TryGetValue(cell, out var expectedCell);
            blueprint.UnexpectedBaselineCells.TryGetValue(cell, out var baselineCell);
            actual.Cells.TryGetValue(cell, out var actualCell);
            RebuildCellTasks(
                blueprint,
                cell,
                expectedCell,
                baselineCell,
                actualCell ?? ActualCellState.Empty,
                scoreLookup);
        }

        RecalculateProgress(blueprint, initializeBaseline);
        SyncProgress((repairGrid, blueprint));
        return true;
    }

    /// <summary>
    /// Performs the authoritative expected/actual comparison required before completion.
    /// A false return means validation could not be performed; a true return exposes whether the grid fully matches.
    /// </summary>
    public bool TryRevalidateForCompletion(EntityUid repairGrid, out bool fullyMatchesTarget)
    {
        fullyMatchesTarget = false;
        if (!RevalidateAll(repairGrid) ||
            !TryComp<RepairBlueprintComponent>(repairGrid, out var blueprint) ||
            !blueprint.Ready)
        {
            return false;
        }

        fullyMatchesTarget = blueprint.FullyMatchesTarget;
        return true;
    }

    private bool RevalidateCell(Entity<RepairBlueprintComponent> blueprint, Vector2i cell)
    {
        if (!_scoreLookups.TryGetValue(blueprint.Owner, out var scoreLookup) ||
            !TryComp<MapGridComponent>(blueprint.Owner, out var grid))
        {
            return false;
        }

        blueprint.Comp.ExpectedCells.TryGetValue(cell, out var expectedCell);
        blueprint.Comp.UnexpectedBaselineCells.TryGetValue(cell, out var baselineCell);
        var actualCell = SnapshotActualCell((blueprint.Owner, grid), cell, scoreLookup);
        RebuildCellTasks(blueprint.Comp, cell, expectedCell, baselineCell, actualCell, scoreLookup);
        RecalculateProgress(blueprint.Comp, initializeMaxPoints: false);
        return true;
    }

    private void CaptureBaselineCell(
        RepairBlueprintComponent blueprint,
        Vector2i cell,
        RepairExpectedCellState? expected,
        ActualCellState actual,
        RepairScoreLookup scoreLookup)
    {
        if (expected?.Tile is { } expectedTile)
        {
            expectedTile.InitiallyCorrect = actual.CanonicalTileId == expectedTile.CanonicalTileId;
        }
        else if (!actual.TileIsEmpty)
        {
            var baseline = GetOrCreateUnexpectedBaselineCell(blueprint, cell);
            baseline.Tile = new RepairUnexpectedTileBaseline
            {
                TilePrototype = actual.TilePrototype ?? string.Empty,
                Points = ResolveTilePoints(scoreLookup, actual.CanonicalTilePrototype ?? string.Empty),
            };
        }

        var comparison = CompareEntities(expected, actual);
        if (expected != null)
        {
            foreach (var expectedEntity in expected.Entities)
            {
                expectedEntity.InitiallyCorrectCount = comparison.MatchedCounts.GetValueOrDefault(expectedEntity);
            }
        }

        if (comparison.Unexpected.Count == 0)
            return;

        var unexpectedBaseline = GetOrCreateUnexpectedBaselineCell(blueprint, cell);
        foreach (var unexpected in comparison.Unexpected.Values)
        {
            unexpectedBaseline.Entities.Add(new RepairUnexpectedEntityBaseline
            {
                Signature = unexpected.Signature,
                DisplayLocalRotation = unexpected.DisplayLocalRotation,
                Count = unexpected.Count,
                Points = ResolveEntityPoints(scoreLookup, unexpected.Signature.Prototype),
            });
        }
    }

    private static RepairUnexpectedCellBaseline GetOrCreateUnexpectedBaselineCell(
        RepairBlueprintComponent blueprint,
        Vector2i cell)
    {
        if (!blueprint.UnexpectedBaselineCells.TryGetValue(cell, out var baseline))
        {
            baseline = new RepairUnexpectedCellBaseline();
            blueprint.UnexpectedBaselineCells.Add(cell, baseline);
        }

        return baseline;
    }

    private void RebuildCellTasks(
        RepairBlueprintComponent blueprint,
        Vector2i cell,
        RepairExpectedCellState? expected,
        RepairUnexpectedCellBaseline? baseline,
        ActualCellState actual,
        RepairScoreLookup scoreLookup)
    {
        var tasks = new List<RepairTask>();

        if (expected?.Tile is { } expectedTile)
        {
            tasks.Add(new RepairTask
            {
                Type = RepairTaskType.Tile,
                Cell = cell,
                ExpectedTileId = expectedTile.TileId,
                ExpectedTilePrototype = expectedTile.TilePrototype,
                ExpectedCanonicalTileId = expectedTile.CanonicalTileId,
                ExpectedCanonicalTilePrototype = expectedTile.CanonicalTilePrototype,
                Points = expectedTile.Points,
                InitiallyCorrect = expectedTile.InitiallyCorrect,
                State = actual.CanonicalTileId == expectedTile.CanonicalTileId
                    ? RepairTaskState.Correct
                    : actual.TileIsEmpty
                        ? RepairTaskState.Missing
                        : RepairTaskState.Wrong,
            });
        }
        else if (baseline?.Tile is { } baselineTile)
        {
            tasks.Add(CreateUnexpectedTileTask(
                cell,
                actual.TilePrototype ?? baselineTile.TilePrototype,
                baselineTile.Points,
                initiallyCorrect: false,
                actual.TileIsEmpty ? RepairTaskState.Correct : RepairTaskState.Wrong));
        }
        else if (!actual.TileIsEmpty)
        {
            tasks.Add(CreateUnexpectedTileTask(
                cell,
                actual.TilePrototype ?? string.Empty,
                ResolveTilePoints(scoreLookup, actual.CanonicalTilePrototype ?? string.Empty),
                initiallyCorrect: true,
                RepairTaskState.Wrong));
        }

        var comparison = CompareEntities(expected, actual);
        if (expected != null)
        {
            foreach (var expectedEntity in expected.Entities)
            {
                var matchedCount = comparison.MatchedCounts.GetValueOrDefault(expectedEntity);
                var hasUnexpectedAtPosition = comparison.UnexpectedPositions.Contains(
                    expectedEntity.Signature.LocalPosition);

                for (var requiredCount = 1; requiredCount <= expectedEntity.Count; requiredCount++)
                {
                    tasks.Add(new RepairTask
                    {
                        Type = RepairTaskType.AnchoredEntity,
                        Cell = cell,
                        ExpectedEntityPrototype = expectedEntity.Signature.Prototype,
                        ExpectedLocalPosition = expectedEntity.Signature.LocalPosition,
                        ExpectedLocalRotation = expectedEntity.Signature.LocalRotation,
                        DisplayLocalRotation = expectedEntity.DisplayLocalRotation,
                        RotationMode = expectedEntity.Signature.RotationMode,
                        RequiredMatchingCount = requiredCount,
                        Points = expectedEntity.Points,
                        InitiallyCorrect = requiredCount <= expectedEntity.InitiallyCorrectCount,
                        State = requiredCount <= matchedCount
                            ? RepairTaskState.Correct
                            : hasUnexpectedAtPosition
                                ? RepairTaskState.Wrong
                                : RepairTaskState.Missing,
                    });
                }
            }
        }

        var baselineBySignature = baseline?.Entities.ToDictionary(entry => entry.Signature)
                                  ?? new Dictionary<RepairAnchoredEntitySignature, RepairUnexpectedEntityBaseline>();
        foreach (var baselineEntity in baselineBySignature.Values)
        {
            var currentCount = comparison.Unexpected.TryGetValue(baselineEntity.Signature, out var current)
                ? current.Count
                : 0;

            for (var requiredCount = 1; requiredCount <= baselineEntity.Count; requiredCount++)
            {
                tasks.Add(CreateUnexpectedEntityTask(
                    cell,
                    baselineEntity.Signature,
                    baselineEntity.DisplayLocalRotation,
                    baselineEntity.Points,
                    requiredCount,
                    initiallyCorrect: false,
                    currentCount >= requiredCount ? RepairTaskState.Wrong : RepairTaskState.Correct));
            }
        }

        foreach (var unexpected in comparison.Unexpected.Values)
        {
            var baselineCount = baselineBySignature.TryGetValue(unexpected.Signature, out var baselineEntity)
                ? baselineEntity.Count
                : 0;
            var points = baselineEntity?.Points ?? ResolveEntityPoints(scoreLookup, unexpected.Signature.Prototype);

            for (var requiredCount = baselineCount + 1; requiredCount <= unexpected.Count; requiredCount++)
            {
                tasks.Add(CreateUnexpectedEntityTask(
                    cell,
                    unexpected.Signature,
                    unexpected.DisplayLocalRotation,
                    points,
                    requiredCount,
                    initiallyCorrect: true,
                    RepairTaskState.Wrong));
            }
        }

        if (tasks.Count == 0)
            blueprint.TasksByCell.Remove(cell);
        else
            blueprint.TasksByCell[cell] = tasks;
    }

    private static RepairTask CreateUnexpectedTileTask(
        Vector2i cell,
        string displayPrototype,
        int points,
        bool initiallyCorrect,
        RepairTaskState state)
    {
        return new RepairTask
        {
            Type = RepairTaskType.Tile,
            Cell = cell,
            ExpectedTileId = Tile.Empty.TypeId,
            ExpectedTilePrototype = displayPrototype,
            ExpectedCanonicalTileId = Tile.Empty.TypeId,
            Points = points,
            InitiallyCorrect = initiallyCorrect,
            State = state,
        };
    }

    private static RepairTask CreateUnexpectedEntityTask(
        Vector2i cell,
        RepairAnchoredEntitySignature signature,
        Angle displayRotation,
        int points,
        int requiredCount,
        bool initiallyCorrect,
        RepairTaskState state)
    {
        return new RepairTask
        {
            Type = RepairTaskType.RemoveAnchoredEntity,
            Cell = cell,
            ExpectedEntityPrototype = signature.Prototype,
            ExpectedLocalPosition = signature.LocalPosition,
            ExpectedLocalRotation = signature.LocalRotation,
            DisplayLocalRotation = displayRotation,
            RotationMode = signature.RotationMode,
            RequiredMatchingCount = requiredCount,
            Points = points,
            InitiallyCorrect = initiallyCorrect,
            State = state,
        };
    }

    private CellEntityComparison CompareEntities(
        RepairExpectedCellState? expected,
        ActualCellState actual)
    {
        var result = new CellEntityComparison();
        var matchedActual = new bool[actual.Entities.Count];
        if (expected != null)
        {
            foreach (var expectedEntity in expected.Entities
                         .OrderByDescending(entity => RotationSpecificity(entity.Signature.RotationMode)))
            {
                var matchedCount = 0;
                for (var i = 0; i < actual.Entities.Count && matchedCount < expectedEntity.Count; i++)
                {
                    if (matchedActual[i] || !MatchesExpected(actual.Entities[i], expectedEntity))
                        continue;

                    matchedActual[i] = true;
                    matchedCount++;
                }

                result.MatchedCounts[expectedEntity] = matchedCount;
            }
        }

        for (var i = 0; i < actual.Entities.Count; i++)
        {
            if (matchedActual[i])
                continue;

            var entity = actual.Entities[i];
            result.UnexpectedPositions.Add(entity.Signature.LocalPosition);
            if (result.Unexpected.TryGetValue(entity.Signature, out var group))
            {
                group.Count++;
                continue;
            }

            result.Unexpected.Add(entity.Signature, new ActualEntityGroup
            {
                Signature = entity.Signature,
                DisplayLocalRotation = entity.DisplayLocalRotation,
                Count = 1,
            });
        }

        return result;
    }

    private static bool MatchesExpected(ActualAnchoredEntity actual, RepairExpectedEntityState expected)
    {
        return actual.Anchored == expected.Anchored &&
               actual.Signature.Prototype == expected.Signature.Prototype &&
               actual.Signature.LocalPosition == expected.Signature.LocalPosition &&
               CanonicalizeRotation(actual.RawLocalRotation, expected.Signature.RotationMode) ==
               expected.Signature.LocalRotation;
    }

    private static int RotationSpecificity(RepairRotationMode mode)
    {
        return mode switch
        {
            RepairRotationMode.Exact => 2,
            RepairRotationMode.Axis => 1,
            _ => 0,
        };
    }

    private ActualGridSnapshot SnapshotActualGrid(
        Entity<MapGridComponent> grid,
        RepairScoreLookup scoreLookup)
    {
        var result = new ActualGridSnapshot();
        foreach (var tile in _map.GetAllTiles(grid.Owner, grid.Comp))
        {
            var cell = result.GetOrCreateCell(tile.GridIndices);
            SetActualTile(cell, tile.Tile.TypeId, scoreLookup);
        }

        var children = Transform(grid.Owner).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryCreateActualAnchoredEntity(grid, child, scoreLookup, out var entity, out var cell))
                continue;

            result.GetOrCreateCell(cell).Entities.Add(entity);
        }

        return result;
    }

    private ActualCellState SnapshotActualCell(
        Entity<MapGridComponent> grid,
        Vector2i cell,
        RepairScoreLookup scoreLookup)
    {
        var result = new ActualCellState();
        var tile = _map.GetTileRef(grid.Owner, grid.Comp, cell).Tile;
        if (!tile.IsEmpty)
            SetActualTile(result, tile.TypeId, scoreLookup);

        foreach (var child in _map.GetAnchoredEntities(grid.Owner, grid.Comp, cell))
        {
            if (!TryCreateActualAnchoredEntity(grid, child, scoreLookup, out var entity, out var entityCell) ||
                entityCell != cell)
            {
                continue;
            }

            result.Entities.Add(entity);
        }

        return result;
    }

    private bool TryCreateActualAnchoredEntity(
        Entity<MapGridComponent> grid,
        EntityUid child,
        RepairScoreLookup scoreLookup,
        out ActualAnchoredEntity entity,
        out Vector2i cell)
    {
        entity = default!;
        cell = default;
        if (!TryComp(child, out TransformComponent? xform) ||
            !xform.Anchored ||
            xform.ParentUid != grid.Owner ||
            MetaData(child).EntityPrototype?.ID is not { } prototypeId)
        {
            return false;
        }

        var rotationMode = ResolveRotationMode(scoreLookup, prototypeId);
        var canonicalPrototype = CanonicalizeEntityPrototype(scoreLookup.EntityIdentityRules, prototypeId);
        cell = LocalPositionToCell(grid.Comp, xform.LocalPosition);
        entity = new ActualAnchoredEntity
        {
            Anchored = xform.Anchored,
            Signature = new RepairAnchoredEntitySignature(
                canonicalPrototype,
                xform.LocalPosition,
                CanonicalizeRotation(xform.LocalRotation, rotationMode),
                rotationMode),
            RawLocalRotation = xform.LocalRotation,
            DisplayLocalRotation = xform.LocalRotation.Reduced().FlipPositive(),
        };
        return true;
    }

    private void SetActualTile(ActualCellState cell, int tileId, RepairScoreLookup scoreLookup)
    {
        cell.TileId = tileId;
        cell.TilePrototype = ((ContentTileDefinition) _tileDefinitions[tileId]).ID;
        cell.CanonicalTileId = CanonicalizeTileId(scoreLookup.TileIdentityIds, tileId);
        cell.CanonicalTilePrototype = ((ContentTileDefinition) _tileDefinitions[cell.CanonicalTileId]).ID;
    }

    private static void RecalculateProgress(RepairBlueprintComponent blueprint, bool initializeMaxPoints)
    {
        var total = 0;
        var completed = 0;
        var currentPoints = 0;
        var maxPoints = 0;
        foreach (var tasks in blueprint.TasksByCell.Values)
        {
            foreach (var task in tasks)
            {
                total++;
                if (task.State == RepairTaskState.Correct)
                    completed++;

                currentPoints += GetPointContribution(task, task.State);
                if (!task.InitiallyCorrect)
                    maxPoints += task.Points;
            }
        }

        blueprint.TotalTasks = total;
        blueprint.CompletedTasks = completed;
        blueprint.CurrentPoints = currentPoints;
        blueprint.FullyMatchesTarget = completed == total;
        if (initializeMaxPoints)
            blueprint.MaxPoints = maxPoints;
    }

    private static int GetPointContribution(RepairTask task, RepairTaskState state)
    {
        if (task.InitiallyCorrect)
            return state == RepairTaskState.Correct ? 0 : -task.Points;

        return state == RepairTaskState.Correct ? task.Points : 0;
    }

    private void SyncProgress(Entity<RepairBlueprintComponent> blueprint)
    {
        if (!TryComp<RepairOrderStationComponent>(blueprint.Comp.Station, out var station) ||
            station.Active is not { } active ||
            active.GridUid != blueprint.Owner)
        {
            return;
        }

        active.CompletedTasks = blueprint.Comp.CompletedTasks;
        active.TotalTasks = blueprint.Comp.TotalTasks;
        active.BlueprintReady = blueprint.Comp.Ready;
        active.CurrentPoints = blueprint.Comp.CurrentPoints;
        active.MaxPoints = blueprint.Comp.MaxPoints;
        _repairOrders.RefreshStationUis(blueprint.Comp.Station);
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        foreach (var change in args.Changes)
        {
            MarkDirty(args.Entity.Owner, change.GridIndices);
        }
    }

    private void OnAnchorStateChanged(ref AnchorStateChangedEvent args)
    {
        if (args.Transform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return;
        }

        MarkDirty(gridUid, LocalPositionToCell(grid, args.Transform.LocalPosition));
    }

    private void OnMove(ref MoveEvent args)
    {
        // Anchor/unanchor events cover detached entities. Ordinary unanchored movement is irrelevant.
        if (!args.Component.Anchored)
            return;

        MarkDirtyFromCoordinates(args.OldPosition);
        MarkDirtyFromCoordinates(args.NewPosition);
    }

    private void OnTransformTerminating(Entity<TransformComponent> entity, ref EntityTerminatingEvent args)
    {
        if (!entity.Comp.Anchored ||
            entity.Comp.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return;
        }

        MarkDirty(gridUid, LocalPositionToCell(grid, entity.Comp.LocalPosition));
    }

    private void MarkDirtyFromCoordinates(EntityCoordinates coordinates)
    {
        if (!TryComp<MapGridComponent>(coordinates.EntityId, out var grid))
            return;

        MarkDirty(coordinates.EntityId, LocalPositionToCell(grid, coordinates.Position));
    }

    private void MarkDirty(EntityUid gridUid, Vector2i cell)
    {
        if (!TryComp<RepairBlueprintComponent>(gridUid, out var blueprint) ||
            !blueprint.Ready)
        {
            return;
        }

        if (!_dirtyCells.TryGetValue(gridUid, out var cells))
        {
            cells = new HashSet<Vector2i>();
            _dirtyCells.Add(gridUid, cells);
        }

        cells.Add(cell);
    }

    private void OnBlueprintShutdown(
        Entity<RepairBlueprintComponent> blueprint,
        ref ComponentShutdown args)
    {
        _dirtyCells.Remove(blueprint.Owner);
        _scoreLookups.Remove(blueprint.Owner);

        var reason = MetaData(blueprint.Owner).EntityLifeStage >= EntityLifeStage.Terminating
            ? RepairOrderAbortReason.RepairGridDeleted
            : RepairOrderAbortReason.ValidationRuntimeLost;

        _blueprintsShuttingDown.Add(blueprint.Owner);
        try
        {
            _repairOrders.AbortActiveOrder(
                blueprint.Comp.Station,
                blueprint.Owner,
                reason);
        }
        finally
        {
            _blueprintsShuttingDown.Remove(blueprint.Owner);
        }
    }

    private void OnGridSplit(
        Entity<RepairBlueprintComponent> blueprint,
        ref GridSplitEvent args)
    {
        _repairOrders.AbortActiveOrder(
            blueprint.Comp.Station,
            blueprint.Owner,
            RepairOrderAbortReason.RepairGridSplit,
            args.NewGrids);
    }

    private static Vector2i LocalPositionToCell(MapGridComponent grid, Vector2 position)
    {
        return new Vector2i(
            (int) Math.Floor(position.X / grid.TileSize),
            (int) Math.Floor(position.Y / grid.TileSize));
    }

    private static Angle CanonicalizeRotation(Angle rotation, RepairRotationMode mode)
    {
        if (mode == RepairRotationMode.None)
            return Angle.Zero;

        var normalized = rotation.Reduced().FlipPositive();
        if (mode == RepairRotationMode.Axis)
            return new Angle(normalized.Theta % Math.PI);

        return normalized;
    }

    private RepairScoreLookup BuildScoreLookup(RepairOrderPrototype order)
    {
        var lookup = new RepairScoreLookup(order.ScoreProfile.Id);
        if (!_prototype.TryIndex<RepairScoreProfilePrototype>(order.ScoreProfile, out var profile))
        {
            _sawmill.Warning(
                $"Repair order {order.ID} references missing score profile {order.ScoreProfile}; all target requirements will be worth 0 points.");
            return lookup;
        }

        if (profile.DefaultTilePoints < 0 || profile.DefaultEntityPoints < 0)
        {
            _sawmill.Warning(
                $"Repair score profile {profile.ID} contains a negative default point value; negative defaults are replaced with 0.");
        }

        lookup.DefaultTilePoints = Math.Max(0, profile.DefaultTilePoints);
        lookup.DefaultEntityPoints = Math.Max(0, profile.DefaultEntityPoints);

        foreach (var rule in profile.TileIdentityRules)
        {
            if (rule.Tiles.Count == 0)
            {
                _sawmill.Warning(
                    $"Repair score profile {profile.ID} contains an empty tile identity rule; the rule is ignored.");
                continue;
            }

            if (!_tileDefinitions.TryGetDefinition(rule.Canonical, out var canonicalDefinition))
            {
                _sawmill.Warning(
                    $"Repair score profile {profile.ID} contains a tile identity rule with missing canonical tile {rule.Canonical}; the rule is ignored.");
                continue;
            }

            var validRule = true;
            var tileDefinitions = new List<ITileDefinition>();
            foreach (var tile in rule.Tiles)
            {
                if (_tileDefinitions.TryGetDefinition(tile, out var tileDefinition))
                {
                    tileDefinitions.Add(tileDefinition);
                    continue;
                }

                _sawmill.Warning(
                    $"Repair score profile {profile.ID} contains a tile identity rule with missing tile {tile}; the rule is ignored.");
                validRule = false;
                break;
            }

            if (!validRule)
                continue;

            foreach (var tileDefinition in tileDefinitions)
            {
                if (!lookup.TileIdentityIds.TryAdd(tileDefinition.TileId, canonicalDefinition.TileId))
                {
                    _sawmill.Warning(
                        $"Repair score profile {profile.ID} contains duplicate tile identity for {tileDefinition.ID}; the first value is used.");
                }
            }
        }

        foreach (var rule in profile.IdentityRules)
        {
            if (!_prototype.HasIndex<EntityPrototype>(rule.Canonical))
            {
                _sawmill.Warning(
                    $"Repair score profile {profile.ID} contains an identity rule with missing canonical entity prototype {rule.Canonical}; the rule is ignored.");
                continue;
            }

            if (!ValidateSelector(profile.ID, rule.Selector, "identity"))
                continue;

            lookup.EntityIdentityRules.Add(rule);
        }

        foreach (var value in profile.Values)
        {
            var hasTile = value.Tile is not null;
            var hasEntity = value.Entity is not null;
            if (hasTile == hasEntity)
            {
                _sawmill.Warning(
                    $"Repair score profile {profile.ID} contains an entry that must specify exactly one of tile/entity; the entry is ignored.");
                continue;
            }

            if (value.Points < 0)
            {
                _sawmill.Warning(
                $"Repair score profile {profile.ID} contains a negative point value; the entry is ignored.");
                continue;
            }

            if (value.Tile is { } tile)
            {
                if (!lookup.TilePoints.TryAdd(tile.Id, value.Points))
                {
                    _sawmill.Warning(
                        $"Repair score profile {profile.ID} contains duplicate tile ID {tile}; the first value is used.");
                }

                continue;
            }

            if (value.Entity is { } entity &&
                !lookup.EntityPoints.TryAdd(entity.Id, value.Points))
            {
                _sawmill.Warning(
                    $"Repair score profile {profile.ID} contains duplicate entity ID {entity}; the first value is used.");
            }
        }

        foreach (var rule in profile.Rules)
        {
            if (rule.Points < 0)
            {
                _sawmill.Warning(
                    $"Repair score profile {profile.ID} contains a rule with a negative point value; the rule is ignored.");
                continue;
            }

            if (!ValidateSelector(profile.ID, rule.Selector, "score"))
                continue;

            lookup.EntityRules.Add(rule);
        }

        foreach (var rule in profile.RotationRules)
        {
            if (!ValidateSelector(profile.ID, rule.Selector, "rotation"))
                continue;

            lookup.RotationRules.Add(rule);
        }

        return lookup;
    }

    private int ResolveTilePoints(RepairScoreLookup lookup, string tilePrototype)
    {
        if (lookup.TilePoints.TryGetValue(tilePrototype, out var points))
            return points;

        return lookup.DefaultTilePoints;
    }

    private static int CanonicalizeTileId(IReadOnlyDictionary<int, int> tileIdentityIds, int tileId)
    {
        return tileIdentityIds.TryGetValue(tileId, out var canonicalTileId)
            ? canonicalTileId
            : tileId;
    }

    private int ResolveEntityPoints(RepairScoreLookup lookup, string entityPrototype)
    {
        if (lookup.EntityPoints.TryGetValue(entityPrototype, out var points))
            return points;

        if (!_prototype.TryIndex<EntityPrototype>(entityPrototype, out var prototype))
        {
            if (lookup.MissingValues.Add($"entity:{entityPrototype}"))
            {
                _sawmill.Warning(
                    $"Repair score profile {lookup.Profile} cannot inspect missing entity prototype {entityPrototype}; the default value is used.");
            }

            return lookup.DefaultEntityPoints;
        }

        foreach (var rule in lookup.EntityRules)
        {
            if (MatchesSelector(prototype, rule.Selector))
                return rule.Points;
        }

        return lookup.DefaultEntityPoints;
    }

    private string CanonicalizeEntityPrototype(
        IReadOnlyList<RepairEntityIdentityRule> identityRules,
        string entityPrototype)
    {
        if (identityRules.Count == 0 ||
            !_prototype.TryIndex<EntityPrototype>(entityPrototype, out var prototype))
        {
            return entityPrototype;
        }

        foreach (var rule in identityRules)
        {
            if (MatchesSelector(prototype, rule.Selector))
                return rule.Canonical.Id;
        }

        return entityPrototype;
    }

    private RepairRotationMode ResolveRotationMode(RepairScoreLookup lookup, string entityPrototype)
    {
        if (!_prototype.TryIndex<EntityPrototype>(entityPrototype, out var prototype))
            return RepairRotationMode.None;

        foreach (var rule in lookup.RotationRules)
        {
            if (MatchesSelector(prototype, rule.Selector))
                return rule.Mode;
        }

        return RepairRotationMode.None;
    }

    private bool MatchesSelector(EntityPrototype prototype, RepairEntitySelector selector)
    {
        if (selector.Entities.Count > 0 &&
            !selector.Entities.Any(entity => entity.Id == prototype.ID))
        {
            return false;
        }

        if (selector.Parents.Count > 0 &&
            !selector.Parents.Any(parent => IsPrototypeOrDescendant(prototype.ID, parent.Id)))
        {
            return false;
        }

        foreach (var component in selector.AllComponents)
        {
            if (!prototype.Components.ContainsKey(component))
                return false;
        }

        if (selector.AllTags.Count == 0)
            return true;

        return prototype.TryGetComponent(out TagComponent? tags, EntityManager.ComponentFactory) &&
               _tag.HasAllTags(tags, selector.AllTags);
    }

    private bool IsPrototypeOrDescendant(string prototypeId, string parentId)
    {
        if (prototypeId == parentId)
            return true;

        var visited = new HashSet<string>();
        var pending = new Stack<string>();
        pending.Push(prototypeId);

        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current) ||
                !_prototype.TryIndex<EntityPrototype>(current, out var prototype) ||
                prototype.Parents is not { } parents)
            {
                continue;
            }

            foreach (var parent in parents)
            {
                if (parent == parentId)
                    return true;

                pending.Push(parent);
            }
        }

        return false;
    }

    private bool ValidateSelector(string profile, RepairEntitySelector selector, string ruleType)
    {
        if (selector.Entities.Count == 0 &&
            selector.Parents.Count == 0 &&
            selector.AllTags.Count == 0 &&
            selector.AllComponents.Count == 0)
        {
            _sawmill.Warning(
                $"Repair score profile {profile} contains an empty {ruleType} selector; the rule is ignored.");
            return false;
        }

        foreach (var entity in selector.Entities.Concat(selector.Parents))
        {
            if (_prototype.HasIndex<EntityPrototype>(entity))
                continue;

            _sawmill.Warning(
                $"Repair score profile {profile} contains a {ruleType} selector with missing entity prototype {entity}; the rule is ignored.");
            return false;
        }

        foreach (var tag in selector.AllTags)
        {
            if (_prototype.HasIndex<TagPrototype>(tag))
                continue;

            _sawmill.Warning(
                $"Repair score profile {profile} contains a {ruleType} selector with missing tag {tag}; the rule is ignored.");
            return false;
        }

        foreach (var component in selector.AllComponents)
        {
            if (EntityManager.ComponentFactory.TryGetRegistration(component, out _))
                continue;

            _sawmill.Warning(
                $"Repair score profile {profile} contains a {ruleType} selector with unknown component {component}; the rule is ignored.");
            return false;
        }

        return true;
    }

    private sealed class RepairScoreLookup(string profile)
    {
        public readonly string Profile = profile;
        public int DefaultTilePoints;
        public int DefaultEntityPoints;
        public readonly Dictionary<int, int> TileIdentityIds = new();
        public readonly Dictionary<string, int> TilePoints = new();
        public readonly Dictionary<string, int> EntityPoints = new();
        public readonly List<RepairScoreRule> EntityRules = new();
        public readonly List<RepairEntityIdentityRule> EntityIdentityRules = new();
        public readonly List<RepairRotationRule> RotationRules = new();
        public readonly HashSet<string> MissingValues = new();
    }

    private readonly record struct AnchoredEntitySignature(
        string Prototype,
        Vector2 LocalPosition,
        Angle LocalRotation,
        RepairRotationMode RotationMode,
        Vector2i Cell);

    private sealed class AnchoredEntitySnapshot
    {
        public int Count;
        public Angle DisplayLocalRotation;
    }

    private sealed class ActualGridSnapshot
    {
        public readonly Dictionary<Vector2i, ActualCellState> Cells = new();

        public ActualCellState GetOrCreateCell(Vector2i cell)
        {
            if (!Cells.TryGetValue(cell, out var state))
            {
                state = new ActualCellState();
                Cells.Add(cell, state);
            }

            return state;
        }
    }

    private sealed class ActualCellState
    {
        public static readonly ActualCellState Empty = new();

        public int TileId = Tile.Empty.TypeId;
        public string? TilePrototype;
        public int CanonicalTileId = Tile.Empty.TypeId;
        public string? CanonicalTilePrototype;
        public readonly List<ActualAnchoredEntity> Entities = new();

        public bool TileIsEmpty => TileId == Tile.Empty.TypeId;
    }

    private sealed class ActualAnchoredEntity
    {
        public bool Anchored;
        public RepairAnchoredEntitySignature Signature;
        public Angle RawLocalRotation;
        public Angle DisplayLocalRotation;
    }

    private sealed class ActualEntityGroup
    {
        public RepairAnchoredEntitySignature Signature;
        public Angle DisplayLocalRotation;
        public int Count;
    }

    private sealed class CellEntityComparison
    {
        public readonly Dictionary<RepairExpectedEntityState, int> MatchedCounts = new();
        public readonly Dictionary<RepairAnchoredEntitySignature, ActualEntityGroup> Unexpected = new();
        public readonly HashSet<Vector2> UnexpectedPositions = new();
    }
}
