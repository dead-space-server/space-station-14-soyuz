using System.Numerics;
using System.Linq;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Maps;
using Content.Shared.Tag;
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
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("repair_orders");

        SubscribeLocalEvent<RepairOrderStationComponent, RepairOrderActivatedEvent>(OnOrderActivated);
        SubscribeLocalEvent<RepairBlueprintComponent, ComponentShutdown>(OnBlueprintShutdown);
        SubscribeLocalEvent<TransformComponent, EntityTerminatingEvent>(OnTransformTerminating);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<AnchorStateChangedEvent>(OnAnchorStateChanged);
        _transform.OnGlobalMoveEvent += OnMove;
    }

    public override void Shutdown()
    {
        _transform.OnGlobalMoveEvent -= OnMove;
        _dirtyCells.Clear();
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
                SyncAnalyzerData((gridUid, blueprint));
            }
        }
    }

    private void OnOrderActivated(
        Entity<RepairOrderStationComponent> station,
        ref RepairOrderActivatedEvent args)
    {
        if (station.Comp.Active is not { } active ||
            active.GridUid != args.GridUid ||
            active.Prototype != args.OrderPrototype ||
            !TryComp<MapGridComponent>(args.GridUid, out _))
        {
            _sawmill.Error($"Cannot build repair blueprint for station {station.Owner}: activation state or grid {args.GridUid} is invalid.");
            return;
        }

        if (!TryGetOrderPrototype(args.OrderPrototype, out var order))
            return;

        var blueprint = EnsureComp<RepairBlueprintComponent>(args.GridUid);
        blueprint.Station = station.Owner;
        blueprint.OrderPrototype = args.OrderPrototype;
        blueprint.TasksByCell.Clear();
        blueprint.TargetEntitySignatures.Clear();
        blueprint.EntityIdentityRules.Clear();
        blueprint.TotalTasks = 0;
        blueprint.CompletedTasks = 0;
        blueprint.MaxPoints = 0;
        blueprint.CurrentPoints = 0;
        blueprint.Ready = false;
        blueprint.BaselineInitialized = false;
        RemComp<RepairAnalyzerDataComponent>(args.GridUid);

        if (!TryBuildBlueprint((args.GridUid, blueprint), order))
        {
            active.BlueprintReady = false;
            active.CompletedTasks = 0;
            active.TotalTasks = 0;
            active.CurrentPoints = 0;
            active.MaxPoints = 0;
            return;
        }

        blueprint.Ready = true;
        RevalidateAll(args.GridUid);
        _sawmill.Info(
            $"Built repair blueprint for order {args.OrderPrototype} on grid {args.GridUid}: " +
            $"{blueprint.TotalTasks} target requirements, {blueprint.CompletedTasks} initially correct.");
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
            _map.SetPaused(temporaryMap, true);

            // TryLoadGrid also rejects files which do not contain exactly one grid.
            if (!_loader.TryLoadGrid(mapId, order.TargetGridPath, out var loadedTarget))
            {
                _sawmill.Error($"Cannot build repair blueprint for {order.ID}: target grid {order.TargetGridPath} failed to load or does not contain exactly one grid.");
                return false;
            }

            var target = loadedTarget.Value;
            var scoreLookup = BuildScoreLookup(order);
            blueprint.Comp.EntityIdentityRules.Clear();
            blueprint.Comp.EntityIdentityRules.AddRange(scoreLookup.EntityIdentityRules);
            BuildTileTasks(blueprint, target, scoreLookup);
            BuildAnchoredEntityTasks(blueprint, target, (blueprint.Owner, repairGrid), scoreLookup);
            return true;
        }
        catch (Exception exception)
        {
            blueprint.Comp.TasksByCell.Clear();
            blueprint.Comp.TargetEntitySignatures.Clear();
            blueprint.Comp.EntityIdentityRules.Clear();
            blueprint.Comp.TotalTasks = 0;
            blueprint.Comp.CompletedTasks = 0;
            blueprint.Comp.MaxPoints = 0;
            blueprint.Comp.CurrentPoints = 0;
            blueprint.Comp.BaselineInitialized = false;
            _sawmill.Error($"Cannot build repair blueprint for {order.ID}: {exception}");
            return false;
        }
        finally
        {
            // This deletes the target grid and all of its children; none of them enter the playable map.
            if (temporaryMapId is { } mapId)
                _map.DeleteMap(mapId);
        }
    }

    private void BuildTileTasks(
        Entity<RepairBlueprintComponent> blueprint,
        Entity<MapGridComponent> target,
        RepairScoreLookup scoreLookup)
    {
        foreach (var targetTile in _map.GetAllTiles(target.Owner, target.Comp))
        {
            var expectedTileId = targetTile.Tile.TypeId;
            var expectedTilePrototype = ((ContentTileDefinition) _tileDefinitions[expectedTileId]).ID;

            AddTask(blueprint.Comp, new RepairTask
            {
                Type = RepairTaskType.Tile,
                Cell = targetTile.GridIndices,
                ExpectedTileId = expectedTileId,
                ExpectedTilePrototype = expectedTilePrototype,
                Points = ResolveTilePoints(scoreLookup, expectedTilePrototype),
            });
        }
    }

    private void BuildAnchoredEntityTasks(
        Entity<RepairBlueprintComponent> blueprint,
        Entity<MapGridComponent> target,
        Entity<MapGridComponent> damaged,
        RepairScoreLookup scoreLookup)
    {
        var targetEntities = SnapshotAnchoredEntities(target, scoreLookup);

        foreach (var signature in targetEntities.Keys)
        {
            blueprint.Comp.TargetEntitySignatures.Add(new RepairTargetEntitySignature(
                signature.Prototype,
                signature.LocalPosition,
                signature.LocalRotation,
                signature.RotationMode));
        }

        foreach (var (signature, targetEntity) in targetEntities)
        {
            for (var requiredCount = 1; requiredCount <= targetEntity.Count; requiredCount++)
            {
                AddTask(blueprint.Comp, new RepairTask
                {
                    Type = RepairTaskType.AnchoredEntity,
                    Cell = signature.Cell,
                    ExpectedEntityPrototype = signature.Prototype,
                    ExpectedLocalPosition = signature.LocalPosition,
                    ExpectedLocalRotation = signature.LocalRotation,
                    DisplayLocalRotation = targetEntity.DisplayLocalRotation,
                    RotationMode = signature.RotationMode,
                    RequiredMatchingCount = requiredCount,
                    Points = ResolveEntityPoints(scoreLookup, signature.Prototype),
                });
            }
        }

        BuildExtraAnchoredEntityTasks(blueprint, targetEntities, SnapshotAnchoredEntities(damaged, scoreLookup), scoreLookup);
    }

    private void BuildExtraAnchoredEntityTasks(
        Entity<RepairBlueprintComponent> blueprint,
        Dictionary<AnchoredEntitySignature, AnchoredEntitySnapshot> targetEntities,
        Dictionary<AnchoredEntitySignature, AnchoredEntitySnapshot> damagedEntities,
        RepairScoreLookup scoreLookup)
    {
        foreach (var (signature, damagedEntity) in damagedEntities)
        {
            var targetCount = targetEntities.TryGetValue(signature, out var targetEntity)
                ? targetEntity.Count
                : 0;

            for (var presentCount = targetCount + 1; presentCount <= damagedEntity.Count; presentCount++)
            {
                AddTask(blueprint.Comp, new RepairTask
                {
                    Type = RepairTaskType.RemoveAnchoredEntity,
                    Cell = signature.Cell,
                    ExpectedEntityPrototype = signature.Prototype,
                    ExpectedLocalPosition = signature.LocalPosition,
                    ExpectedLocalRotation = signature.LocalRotation,
                    DisplayLocalRotation = damagedEntity.DisplayLocalRotation,
                    RotationMode = signature.RotationMode,
                    RequiredMatchingCount = presentCount,
                    Points = ResolveEntityPoints(scoreLookup, signature.Prototype),
                });
            }
        }
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

    private static void AddTask(RepairBlueprintComponent blueprint, RepairTask task)
    {
        if (!blueprint.TasksByCell.TryGetValue(task.Cell, out var tasks))
        {
            tasks = new List<RepairTask>();
            blueprint.TasksByCell.Add(task.Cell, tasks);
        }

        tasks.Add(task);
        blueprint.TotalTasks++;
    }

    /// <summary>
    /// Fully revalidates a fixed blueprint. Intended for recovery and future final submission checks.
    /// </summary>
    public bool RevalidateAll(EntityUid repairGrid)
    {
        if (!TryComp<RepairBlueprintComponent>(repairGrid, out var blueprint) || !blueprint.Ready)
            return false;

        var initializeBaseline = !blueprint.BaselineInitialized;
        var completed = 0;
        var currentPoints = 0;
        var maxPoints = 0;
        foreach (var tasks in blueprint.TasksByCell.Values)
        {
            foreach (var task in tasks)
            {
                task.State = EvaluateTask((repairGrid, blueprint), task);
                if (initializeBaseline)
                    task.InitiallyCorrect = task.State == RepairTaskState.Correct;

                if (task.State == RepairTaskState.Correct)
                    completed++;

                currentPoints += GetPointContribution(task, task.State);
                if (!task.InitiallyCorrect)
                    maxPoints += task.Points;
            }
        }

        blueprint.CompletedTasks = completed;
        blueprint.CurrentPoints = currentPoints;
        if (initializeBaseline)
        {
            blueprint.MaxPoints = maxPoints;
            blueprint.BaselineInitialized = true;
        }
        SyncProgress((repairGrid, blueprint));
        SyncAnalyzerData((repairGrid, blueprint));
        return true;
    }

    private bool RevalidateCell(Entity<RepairBlueprintComponent> blueprint, Vector2i cell)
    {
        if (!blueprint.Comp.TasksByCell.TryGetValue(cell, out var tasks))
            return false;

        var changed = false;
        foreach (var task in tasks)
        {
            var oldState = task.State;
            var newState = EvaluateTask(blueprint, task);
            if (newState == oldState)
                continue;

            blueprint.Comp.CurrentPoints -= GetPointContribution(task, oldState);
            task.State = newState;
            if (oldState == RepairTaskState.Correct)
                blueprint.Comp.CompletedTasks--;
            if (newState == RepairTaskState.Correct)
                blueprint.Comp.CompletedTasks++;
            blueprint.Comp.CurrentPoints += GetPointContribution(task, newState);
            changed = true;
        }

        return changed;
    }

    private static int GetPointContribution(RepairTask task, RepairTaskState state)
    {
        if (task.InitiallyCorrect)
            return state == RepairTaskState.Correct ? 0 : -task.Points;

        return state == RepairTaskState.Correct ? task.Points : 0;
    }

    private RepairTaskState EvaluateTask(Entity<RepairBlueprintComponent> blueprint, RepairTask task)
    {
        if (!TryComp<MapGridComponent>(blueprint.Owner, out var grid))
            return RepairTaskState.Missing;

        if (task.Type == RepairTaskType.Tile)
        {
            var actualTile = _map.GetTileRef(blueprint.Owner, grid, task.Cell).Tile;
            if (actualTile.TypeId == task.ExpectedTileId)
                return RepairTaskState.Correct;
            return actualTile.IsEmpty ? RepairTaskState.Missing : RepairTaskState.Wrong;
        }

        var exactMatches = 0;
        var hasWrongEntityAtPosition = false;
        foreach (var entity in _map.GetAnchoredEntities(blueprint.Owner, grid, task.Cell))
        {
            if (!TryComp(entity, out TransformComponent? xform) ||
                !xform.Anchored ||
                xform.ParentUid != blueprint.Owner ||
                xform.LocalPosition != task.ExpectedLocalPosition)
            {
                continue;
            }

            var prototype = MetaData(entity).EntityPrototype?.ID;
            var canonicalPrototype = prototype is null
                ? null
                : CanonicalizeEntityPrototype(blueprint.Comp.EntityIdentityRules, prototype);
            var rotation = CanonicalizeRotation(xform.LocalRotation, task.RotationMode);
            if (canonicalPrototype == task.ExpectedEntityPrototype &&
                rotation == task.ExpectedLocalRotation)
            {
                exactMatches++;
            }
            else if (canonicalPrototype == null ||
                     !IsAcceptedTargetEntity(
                         blueprint.Comp,
                         canonicalPrototype,
                         xform.LocalPosition,
                         xform.LocalRotation))
            {
                hasWrongEntityAtPosition = true;
            }
        }

        if (task.Type == RepairTaskType.RemoveAnchoredEntity)
        {
            return exactMatches >= task.RequiredMatchingCount
                ? RepairTaskState.Wrong
                : RepairTaskState.Correct;
        }

        // Extra anchored entities do not invalidate an exact match for this particular task.
        if (exactMatches >= task.RequiredMatchingCount)
            return RepairTaskState.Correct;

        return hasWrongEntityAtPosition
            ? RepairTaskState.Wrong
            : RepairTaskState.Missing;
    }

    private static bool IsAcceptedTargetEntity(
        RepairBlueprintComponent blueprint,
        string prototype,
        Vector2 localPosition,
        Angle localRotation)
    {
        foreach (var signature in blueprint.TargetEntitySignatures)
        {
            if (signature.Prototype == prototype &&
                signature.LocalPosition == localPosition &&
                signature.LocalRotation == CanonicalizeRotation(localRotation, signature.RotationMode))
            {
                return true;
            }
        }

        return false;
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

    private void SyncAnalyzerData(Entity<RepairBlueprintComponent> blueprint)
    {
        if (!blueprint.Comp.Ready ||
            !TryComp<MapGridComponent>(blueprint.Owner, out var grid))
        {
            RemComp<RepairAnalyzerDataComponent>(blueprint.Owner);
            return;
        }

        var tasks = new List<RepairAnalyzerTaskData>();
        foreach (var cellTasks in blueprint.Comp.TasksByCell.Values)
        {
            foreach (var task in cellTasks)
            {
                if (task.State == RepairTaskState.Correct)
                    continue;

                var localPosition = task.Type == RepairTaskType.Tile
                    ? new Vector2(
                        (task.Cell.X + 0.5f) * grid.TileSize,
                        (task.Cell.Y + 0.5f) * grid.TileSize)
                    : task.ExpectedLocalPosition;
                var expectedPrototype = task.Type == RepairTaskType.Tile
                    ? task.ExpectedTilePrototype ?? string.Empty
                    : task.ExpectedEntityPrototype ?? string.Empty;

                tasks.Add(new RepairAnalyzerTaskData(
                    task.Type,
                    localPosition,
                    task.DisplayLocalRotation,
                    expectedPrototype,
                    task.State));
            }
        }

        var analyzerData = EnsureComp<RepairAnalyzerDataComponent>(blueprint.Owner);
        analyzerData.Tasks = tasks;
        Dirty(blueprint.Owner, analyzerData);
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
            !blueprint.Ready ||
            !blueprint.TasksByCell.ContainsKey(cell))
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
        if (MetaData(blueprint.Owner).EntityLifeStage < EntityLifeStage.Terminating)
            RemCompDeferred<RepairAnalyzerDataComponent>(blueprint.Owner);

        if (!TryComp<RepairOrderStationComponent>(blueprint.Comp.Station, out var station) ||
            station.Active is not { } active ||
            active.GridUid != blueprint.Owner)
        {
            return;
        }

        active.GridUid = EntityUid.Invalid;
        active.CompletedTasks = 0;
        active.TotalTasks = 0;
        active.CurrentPoints = 0;
        active.MaxPoints = 0;
        active.BlueprintReady = false;
        blueprint.Comp.TasksByCell.Clear();
        blueprint.Comp.TargetEntitySignatures.Clear();
        blueprint.Comp.EntityIdentityRules.Clear();
        blueprint.Comp.CompletedTasks = 0;
        blueprint.Comp.TotalTasks = 0;
        blueprint.Comp.CurrentPoints = 0;
        blueprint.Comp.MaxPoints = 0;
        blueprint.Comp.Ready = false;
        blueprint.Comp.BaselineInitialized = false;

        _sawmill.Warning(
            $"Repair grid {blueprint.Owner} for active order {active.RuntimeId} on station {blueprint.Comp.Station} was deleted; blueprint runtime state was cleared.");
        _repairOrders.RefreshStationUis(blueprint.Comp.Station);
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
}
