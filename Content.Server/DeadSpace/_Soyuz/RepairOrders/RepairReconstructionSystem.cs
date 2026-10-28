// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.Popups;
using Content.Shared.Access.Systems;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using Content.Shared.Storage.Components;
using Content.Shared.Tiles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Reconstructs only server-authored missing requirements. Each patch owns all newly
/// created entities, original tiles and its charge debit until final validation commits.
/// </summary>
public sealed class RepairReconstructionSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly SharedChargesSystem _charges = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly FloorTileSystem _floors = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly RepairOrderValidationSystem _validation = default!;
    [Dependency] private readonly RepairOrderSystem _orders = default!;
    [Dependency] private readonly ILogManager _logs = default!;
    private readonly Dictionary<EntityUid, ReconstructionTransaction> _rollbacks = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RepairReconstructorComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<RepairReconstructorComponent, InteractUsingEvent>(OnReload);
        SubscribeLocalEvent<RepairReconstructorComponent, UseInHandEvent>(OnUse);
        SubscribeLocalEvent<RepairReconstructorComponent, GotUnequippedHandEvent>(OnDropped);
        SubscribeLocalEvent<RepairReconstructorComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<RepairReconstructorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<RepairReconstructorComponent, RepairReconstructionDoAfterEvent>(OnComplete);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _rollbacks.Clear());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var tablets = EntityQueryEnumerator<RepairReconstructorComponent>();
        while (tablets.MoveNext(out _, out var tablet))
        {
            if (tablet.DoAfter != null && !_doAfter.IsRunning(tablet.DoAfter))
                tablet.DoAfter = null;
        }
        foreach (var transaction in _rollbacks.Values.ToArray())
        {
            if (transaction.NextAttempt > _timing.CurTime)
                continue;
            transaction.NextAttempt = _timing.CurTime + TimeSpan.FromSeconds(1);
            TryRollback(transaction);
        }
    }

    public override void Shutdown()
    {
        _rollbacks.Clear();
        base.Shutdown();
    }

    private bool IsBusy(Entity<RepairReconstructorComponent> tablet)
    {
        if (tablet.Comp.DoAfter != null && !_doAfter.IsRunning(tablet.Comp.DoAfter))
            tablet.Comp.DoAfter = null;
        return tablet.Comp.Busy;
    }

    private void OnReload(Entity<RepairReconstructorComponent> tablet, ref InteractUsingEvent args)
    {
        if (args.Handled || IsBusy(tablet) ||
            !HasComp<RepairReconstructionCartridgeComponent>(args.Used) ||
            !TryComp<LimitedChargesComponent>(args.Used, out var cartridge) ||
            !TryComp<LimitedChargesComponent>(tablet.Owner, out var reservoir))
            return;

        args.Handled = true;
        var amount = Math.Min(_charges.GetCurrentCharges((args.Used, cartridge)),
            reservoir.MaxCharges - _charges.GetCurrentCharges((tablet.Owner, reservoir)));
        if (amount <= 0 || !_charges.TryUseCharges((args.Used, cartridge), amount))
            return;
        _charges.AddCharges(tablet.Owner, amount);
        if (_charges.IsEmpty((args.Used, cartridge)))
            QueueDel(args.Used);
        Notify(args.User, "repair-reconstructor-refilled", ("charges", amount));
    }

    private void OnInteract(Entity<RepairReconstructorComponent> tablet, ref AfterInteractEvent args)
    {
        if (args.Handled || IsBusy(tablet) || !_hands.IsHolding(args.User, tablet.Owner, out _) || !HasAccess(args.User))
            return;
        args.Handled = true;
        var click = _transform.ToMapCoordinates(args.ClickLocation);
        var query = EntityQueryEnumerator<RepairBlueprintComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var grid, out _, out var mapGrid, out var xform))
        {
            if (xform.MapID != click.MapId || !TrySession(grid, out var blueprint, out var active, out _))
                continue;
            var cell = _map.TileIndicesFor(grid, mapGrid, _transform.ToCoordinates(grid, click));
            if (!blueprint.ExpectedCells.ContainsKey(cell) || !CanReachCell(args.User, (grid, mapGrid), cell, tablet.Comp.Range))
                continue;

            if (tablet.Comp.SelectionGrid != GetNetEntity(grid) || tablet.Comp.SelectionUser != GetNetEntity(args.User) ||
                tablet.Comp.SelectionRuntimeId != active.RuntimeId || tablet.Comp.SelectionStart is not { } start)
            {
                ClearSelection(tablet);
                tablet.Comp.SelectionGrid = GetNetEntity(grid);
                tablet.Comp.SelectionUser = GetNetEntity(args.User);
                tablet.Comp.SelectionRuntimeId = active.RuntimeId;
                tablet.Comp.SelectionStart = cell;
                tablet.Comp.SelectionEnd = null;
                Dirty(tablet);
                Notify(args.User, "repair-reconstructor-select-end");
                return;
            }
            if (!ValidArea(start, cell))
            {
                Notify(args.User, "repair-reconstructor-area-limit");
                return;
            }
            tablet.Comp.SelectionEnd = cell;
            Dirty(tablet);
            if (_validation.RefreshDirtyCells(grid))
            {
                var plan = BuildPlan(tablet, args.User, (grid, mapGrid), blueprint, start, cell, out var blocked);
                SetQuote(tablet, plan, blocked);
                ShowQuote(tablet, args.User);
            }
            return;
        }
        Notify(args.User, "repair-reconstructor-no-order");
    }

    private static bool ValidArea(Vector2i start, Vector2i end)
        => Math.Abs((long) start.X - end.X) < 5 && Math.Abs((long) start.Y - end.Y) < 5;

    private void OnUse(Entity<RepairReconstructorComponent> tablet, ref UseInHandEvent args)
    {
        if (args.Handled || IsBusy(tablet) || !HasAccess(args.User) || !_hands.IsHolding(args.User, tablet.Owner, out _))
            return;
        args.Handled = true;
        if (tablet.Comp.SelectionUser != GetNetEntity(args.User) || tablet.Comp.SelectionGrid is not { } netGrid ||
            tablet.Comp.SelectionStart is not { } start || tablet.Comp.SelectionEnd is not { } end || !ValidArea(start, end))
        {
            Notify(args.User, "repair-reconstructor-select-start");
            return;
        }
        var grid = GetEntity(netGrid);
        if (!TrySession(grid, out var blueprint, out var active, out _) ||
            active.RuntimeId != tablet.Comp.SelectionRuntimeId || !TryComp<MapGridComponent>(grid, out var mapGrid) ||
            !_validation.RefreshDirtyCells(grid))
        {
            ClearSelection(tablet);
            Notify(args.User, "repair-reconstructor-no-order");
            return;
        }
        var plan = BuildPlan(tablet, args.User, (grid, mapGrid), blueprint, start, end, out var blocked);
        if (plan.Count == 0)
        {
            SetQuote(tablet, plan, blocked);
            Notify(args.User, "repair-reconstructor-no-work");
            return;
        }
        var cost = plan.Sum(operation => operation.Cost);
        if (tablet.Comp.QuotedCost != cost ||
            !tablet.Comp.QuotedRequirements.ToHashSet().SetEquals(plan.Select(operation => operation.Requirement)) ||
            !tablet.Comp.BlockedCells.ToHashSet().SetEquals(blocked))
        {
            SetQuote(tablet, plan, blocked);
            ShowQuote(tablet, args.User, changed: true);
            return;
        }
        var capacity = Comp<LimitedChargesComponent>(tablet.Owner).MaxCharges;
        if (cost > capacity)
        {
            Notify(args.User, "repair-reconstructor-over-capacity", ("charges", cost), ("max", capacity));
            return;
        }
        if (!_charges.HasCharges(tablet.Owner, cost))
        {
            Notify(args.User, "repair-reconstructor-no-charge", ("charges", cost),
                ("available", _charges.GetCurrentCharges(tablet.Owner)));
            return;
        }
        var ev = new RepairReconstructionDoAfterEvent
        {
            Grid = netGrid, RuntimeId = active.RuntimeId, Start = start, End = end,
            Requirements = plan.Select(operation => operation.Requirement).ToArray(), QuotedCost = cost,
        };
        var delay = new DoAfterArgs(EntityManager, args.User, tablet.Comp.Delay, ev, tablet.Owner, used: tablet.Owner)
        {
            NeedHand = true, BreakOnMove = true, BreakOnDamage = true,
            BreakOnDropItem = true, BreakOnHandChange = true,
            BlockDuplicate = true, CancelDuplicate = false,
        };
        if (_doAfter.TryStartDoAfter(delay, out var id) && _doAfter.IsRunning(id))
        {
            tablet.Comp.DoAfter = id;
            Notify(args.User, "repair-reconstructor-start", ("count", plan.Count), ("charges", cost));
        }
    }

    private void OnDropped(Entity<RepairReconstructorComponent> tablet, ref GotUnequippedHandEvent args)
    {
        if (_doAfter.IsRunning(tablet.Comp.DoAfter))
            _doAfter.Cancel(tablet.Comp.DoAfter);
        tablet.Comp.DoAfter = null;
        ClearSelection(tablet);
    }

    private void OnExamined(Entity<RepairReconstructorComponent> tablet, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || tablet.Comp.SelectionUser != GetNetEntity(args.Examiner) ||
            tablet.Comp.SelectionGrid is not { } netGrid || tablet.Comp.SelectionStart is not { } start ||
            tablet.Comp.SelectionEnd is not { } end || !ValidArea(start, end) || !HasAccess(args.Examiner))
            return;
        if (!TrySession(GetEntity(netGrid), out _, out var active, out _) ||
            active.RuntimeId != tablet.Comp.SelectionRuntimeId || tablet.Comp.QuotedCost is not { } cost)
            return;
        args.PushMarkup(Loc.GetString("repair-reconstructor-quote", ("charges", cost),
            ("available", _charges.GetCurrentCharges(tablet.Owner)),
            ("max", Comp<LimitedChargesComponent>(tablet.Owner).MaxCharges), ("blocked", tablet.Comp.BlockedCells.Length)));
    }

    private void SetQuote(Entity<RepairReconstructorComponent> tablet, List<Operation> plan, HashSet<Vector2i> blocked)
    {
        tablet.Comp.QuotedCost = plan.Sum(operation => operation.Cost);
        tablet.Comp.QuotedRequirements = plan.Select(operation => operation.Requirement).ToArray();
        tablet.Comp.BlockedCells = blocked.OrderBy(cell => cell.X).ThenBy(cell => cell.Y).ToArray();
        Dirty(tablet);
    }

    private void ShowQuote(Entity<RepairReconstructorComponent> tablet, EntityUid user, bool changed = false)
        => Notify(user, changed ? "repair-reconstructor-price-changed" : "repair-reconstructor-quote",
            ("charges", tablet.Comp.QuotedCost ?? 0), ("available", _charges.GetCurrentCharges(tablet.Owner)),
            ("max", Comp<LimitedChargesComponent>(tablet.Owner).MaxCharges), ("blocked", tablet.Comp.BlockedCells.Length));

    private void ClearSelection(Entity<RepairReconstructorComponent> tablet)
    {
        tablet.Comp.SelectionGrid = null;
        tablet.Comp.SelectionUser = null;
        tablet.Comp.SelectionStart = null;
        tablet.Comp.SelectionEnd = null;
        tablet.Comp.SelectionRuntimeId = 0;
        tablet.Comp.QuotedCost = null;
        tablet.Comp.QuotedRequirements = Array.Empty<int>();
        tablet.Comp.BlockedCells = Array.Empty<Vector2i>();
        Dirty(tablet);
    }

    private void OnVerbs(Entity<RepairReconstructorComponent> tablet, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !_hands.IsHolding(args.User, tablet.Owner, out _))
            return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("repair-reconstructor-clear-selection"),
            Act = () =>
            {
                if (!IsBusy(tablet) && _hands.IsHolding(user, tablet.Owner, out _))
                    ClearSelection(tablet);
            },
        });
    }

    private bool HasAccess(EntityUid user)
        => !HasComp<InsideEntityStorageComponent>(user) &&
           _access.FindAccessTags(user).Any(tag => tag.Id == "Engineering");

    private bool TrySession(EntityUid grid, out RepairBlueprintComponent blueprint,
        out ActiveRepairOrder active, out RepairOrderStationComponent station)
    {
        blueprint = default!;
        active = default!;
        station = default!;
        if (!TryComp<RepairBlueprintComponent>(grid, out var foundBlueprint) || !foundBlueprint.Ready ||
            !TryComp<RepairOrderStationComponent>(foundBlueprint.Station, out var foundStation) ||
            foundStation.Accepting || foundStation.Completing || foundBlueprint.ReconstructionPending ||
            foundStation.Active is not { } current || current.GridUid != grid ||
            current.Prototype != foundBlueprint.OrderPrototype || current.ExpirationFrozen ||
            _timing.CurTime >= current.ExpiresAt || HasComp<RepairOrderCleanupComponent>(grid))
            return false;
        blueprint = foundBlueprint;
        station = foundStation;
        active = current;
        return true;
    }

    private List<Operation> BuildPlan(Entity<RepairReconstructorComponent> tablet, EntityUid user,
        Entity<MapGridComponent> grid, RepairBlueprintComponent blueprint, Vector2i start, Vector2i end,
        out HashSet<Vector2i> blocked, HashSet<int>? requirements = null)
    {
        blocked = new HashSet<Vector2i>();
        var minimum = new Vector2i(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y));
        var maximum = new Vector2i(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
        var result = new List<Operation>();
        for (var dx = 0; dx <= maximum.X - minimum.X; dx++)
        for (var dy = 0; dy <= maximum.Y - minimum.Y; dy++)
        {
            var cell = minimum + new Vector2i(dx, dy);
            if (!blueprint.TasksByCell.TryGetValue(cell, out var tasks))
                continue;
            var missingTasks = tasks.Where(task => !task.Waived && task.State != RepairTaskState.Correct && task.Points > 0 &&
                (requirements == null || requirements.Contains(task.RequirementId))).ToArray();
            if (missingTasks.Length == 0)
                continue;
            if (!CanReachCell(user, grid, cell, tablet.Comp.Range))
            {
                blocked.Add(cell);
                continue;
            }
            foreach (var task in missingTasks)
            {
                // Wrong anchored requirements are also absent: an unrelated object at the same
                // position must not prevent restoring the blueprint's own required multiplicity.
                if (task.State != RepairTaskState.Missing && task.Type != RepairTaskType.AnchoredEntity)
                {
                    blocked.Add(cell);
                    continue;
                }
                if (task.Type == RepairTaskType.Tile && task.ExpectedTileId != Tile.Empty.TypeId &&
                    _floors.CanPlaceTile(grid, grid.Comp, cell, out _))
                {
                    result.Add(new Operation(task.RequirementId, cell, task.TileLayer, task.ExpectedTileId, null,
                        default, Angle.Zero, task.Points));
                }
                else if (_validation.TryGetReconstructionPrototype(grid, task, out var prototype))
                {
                    result.Add(new Operation(task.RequirementId, cell, RepairTileLayer.None, 0, prototype,
                        task.ExpectedLocalPosition, task.DisplayLocalRotation, task.Points));
                }
                else
                    blocked.Add(cell);
            }
        }
        // Bounds are limited to 25 cells; never silently truncate the quoted work.
        var newCells = result.Where(operation => operation.Prototype == null &&
                _map.GetTileRef(grid, grid.Comp, operation.Cell).Tile.IsEmpty)
            .Select(operation => operation.Cell).ToHashSet();
        var depth = new Dictionary<Vector2i, int>();
        var pending = new Queue<Vector2i>();
        var offsets = new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) };
        foreach (var cell in newCells)
        {
            if (!offsets.Any(offset => !_map.GetTileRef(grid, grid.Comp, cell + offset).Tile.IsEmpty))
                continue;
            depth[cell] = 1;
            pending.Enqueue(cell);
        }
        while (pending.TryDequeue(out var cell))
        {
            foreach (var offset in offsets)
            {
                var next = cell + offset;
                if (newCells.Contains(next) && depth.TryAdd(next, depth[cell] + 1))
                    pending.Enqueue(next);
            }
        }
        // Never create a detached island: the grid splitter would otherwise abort the order.
        foreach (var cell in newCells)
        {
            if (!depth.ContainsKey(cell))
                blocked.Add(cell);
        }
        result.RemoveAll(operation => newCells.Contains(operation.Cell) && !depth.ContainsKey(operation.Cell));
        return result.OrderBy(operation => depth.GetValueOrDefault(operation.Cell)).ToList();
    }

    private bool CanReachCell(EntityUid user, Entity<MapGridComponent> grid, Vector2i cell, float range)
    {
        // Occupancy of the destination cell is irrelevant to blueprint reconstruction. Walls
        // between the user and that cell still obstruct access; stacks and wallmounts at it do not.
        return _interaction.InRangeUnobstructed(user, _map.GridTileToWorld(grid, grid.Comp, cell), range,
            predicate: entity => TryComp(entity, out TransformComponent? transform) && transform.Anchored &&
                transform.ParentUid == grid.Owner && _map.TileIndicesFor(grid, grid.Comp, transform.Coordinates) == cell);
    }

    private void OnComplete(Entity<RepairReconstructorComponent> tablet, ref RepairReconstructionDoAfterEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        tablet.Comp.DoAfter = null;
        var grid = GetEntity(args.Grid);
        if (args.Cancelled || !HasAccess(args.User) || !_hands.IsHolding(args.User, tablet.Owner, out _) ||
            !TrySession(grid, out var blueprint, out var active, out var station) || active.RuntimeId != args.RuntimeId ||
            !ValidArea(args.Start, args.End) || !TryComp<MapGridComponent>(grid, out var mapGrid) ||
            !_validation.RevalidateAll(grid))
            return;

        var plan = BuildPlan(tablet, args.User, (grid, mapGrid), blueprint, args.Start, args.End, out _, args.Requirements.ToHashSet());
        var cost = plan.Sum(operation => operation.Cost);
        var available = plan.Select(operation => operation.Requirement).ToHashSet();
        available.UnionWith(blueprint.TasksByCell.Values.SelectMany(tasks => tasks)
            .Where(task => task.Waived || task.State == RepairTaskState.Correct).Select(task => task.RequirementId));
        if (plan.Count == 0 || args.Requirements.Any(requirement => !available.Contains(requirement)) ||
            cost > args.QuotedCost)
        {
            Notify(args.User, "repair-reconstructor-changed");
            return;
        }
        if (!_charges.HasCharges(tablet.Owner, cost))
        {
            Notify(args.User, "repair-reconstructor-no-charge", ("charges", cost),
                ("available", _charges.GetCurrentCharges(tablet.Owner)));
            return;
        }

        var previousCorrect = blueprint.TasksByCell.Values.SelectMany(tasks => tasks)
            .Where(task => task.State == RepairTaskState.Correct).Select(task => task.RequirementId).ToHashSet();
        var previousWrong = blueprint.TasksByCell.Values.SelectMany(tasks => tasks)
            .Where(task => task.State == RepairTaskState.Wrong).Select(task => task.RequirementId).ToHashSet();
        var transaction = new ReconstructionTransaction(tablet, args.User, (grid, mapGrid), blueprint, station,
            _charges.GetCurrentCharges(tablet.Owner));
        // Publish the recovery journal and both locks before the first charge or world mutation.
        _rollbacks.Add(grid, transaction);
        blueprint.ReconstructionPending = true;
        tablet.Comp.Applying = true;
        station.ReconstructionPending = true;
        station.Completing = true;
        var committed = false;
        try
        {
            transaction.RefundDue = true;
            _charges.SetCharges(tablet.Owner, transaction.OriginalCharges - cost);
            foreach (var group in plan.GroupBy(operation => operation.Cell))
            {
                var cell = group.Key;
                var operations = group.ToArray();
                var old = _map.GetTileRef(grid, mapGrid, cell).Tile;
                transaction.Tiles.Add(cell, old);
                var finalTile = old;
                foreach (var operation in operations.Where(operation => operation.Prototype == null).OrderBy(operation => operation.Layer))
                    finalTile = new Tile(operation.TileId);
                _map.SetTile(grid, mapGrid, cell, finalTile);
                foreach (var operation in operations.Where(operation => operation.Prototype != null))
                {
                    var coordinates = new EntityCoordinates(grid, operation.Position);
                    // Own the UID before initialization callbacks can fail.
                    var entity = EntityManager.CreateEntityUninitialized(operation.Prototype!, coordinates, rotation: operation.Rotation);
                    transaction.Created.Add(entity);
                    // Keep failure cleanup in this transaction: InitializeAndStartEntity would
                    // attempt engine deletion immediately, before rollback can isolate the entity.
                    EntityManager.InitializeEntity(entity);
                    EntityManager.StartEntity(entity);
                    if (_map.IsInitialized(Transform(grid).MapID))
                        EntityManager.RunMapInit(entity, MetaData(entity));
                    var transform = Transform(entity);
                    if (!transform.Anchored && !_transform.AnchorEntity((entity, transform), (grid, mapGrid)))
                        throw new InvalidOperationException("Reconstructed object could not be anchored.");
                    _transform.SetCoordinates((entity, transform, MetaData(entity)), coordinates, operation.Rotation, unanchor: false);
                }
            }

            if (!ReferenceEquals(station.Active, active) || !_validation.RevalidateAll(grid))
                throw new InvalidOperationException("Repair order changed during reconstruction.");
            var correct = blueprint.TasksByCell.Values.SelectMany(tasks => tasks)
                .Where(task => task.State == RepairTaskState.Correct).Select(task => task.RequirementId).ToHashSet();
            if (plan.Any(operation => !correct.Contains(operation.Requirement)) || !previousCorrect.IsSubsetOf(correct) ||
                blueprint.TasksByCell.Values.SelectMany(tasks => tasks)
                    .Any(task => task.State == RepairTaskState.Wrong && !previousWrong.Contains(task.RequirementId)))
                throw new InvalidOperationException("Reconstructed patch does not match its authoritative blueprint.");

            // Authored overlaps (including duplicate walls, wires and directional windows) are
            // legitimate. The blueprint comparison above, rather than collision masks, is authoritative.
            committed = true;
        }
        catch (Exception exception)
        {
            LogFailure("reconstruction", grid, exception);
        }
        finally
        {
            if (committed)
            {
                _rollbacks.Remove(grid);
                FinishTransaction(transaction, success: true);
            }
            else if (!TryRollback(transaction) && Exists(args.User))
                Notify(args.User, "repair-reconstructor-recovery");
        }
    }

    private bool TryRollback(ReconstructionTransaction transaction)
    {
        try
        {
            // Retain descendants before any detach/delete callback can orphan them. Each entity is
            // isolated before deletion, so even an engine exception cannot leave a refundable object
            // in the playable world. Failed isolation retains the journal and lifecycle lock.
            CollectRollbackEntities(transaction);
            var disposed = true;
            foreach (var entity in transaction.Disposal.AsEnumerable().Reverse())
                disposed &= TryDisposeRollbackEntity(transaction, entity);
            if (!disposed)
                throw new InvalidOperationException("Reconstruction entities have not been removed from the playable world.");

            if (Exists(transaction.Grid.Owner) && MetaData(transaction.Grid.Owner).EntityLifeStage < EntityLifeStage.Terminating &&
                TryComp<MapGridComponent>(transaction.Grid.Owner, out var mapGrid))
            {
                if (!transaction.TilesRestored)
                {
                    _map.SetTiles(transaction.Grid, mapGrid, transaction.Tiles.Select(pair => (pair.Key, pair.Value)).ToList());
                    if (transaction.Tiles.Any(pair => _map.GetTileRef(transaction.Grid, mapGrid, pair.Key).Tile != pair.Value))
                        throw new InvalidOperationException("Reconstruction tiles were not restored.");
                    transaction.TilesRestored = true;
                }
                if (HasComp<RepairBlueprintComponent>(transaction.Grid) && !_validation.RevalidateAll(transaction.Grid))
                    throw new InvalidOperationException("Rollback validation is not available.");
            }
            if (transaction.RefundDue && Exists(transaction.Tablet.Owner))
            {
                if (!HasComp<LimitedChargesComponent>(transaction.Tablet.Owner))
                    throw new InvalidOperationException("Reconstruction reservoir is not available for refund.");
                _charges.SetCharges(transaction.Tablet.Owner, transaction.OriginalCharges);
                if (_charges.GetCurrentCharges(transaction.Tablet.Owner) != transaction.OriginalCharges)
                    throw new InvalidOperationException("Reconstruction charge refund is incomplete.");
            }
            transaction.RefundDue = false;
        }
        catch (Exception exception)
        {
            LogFailure("reconstruction rollback", transaction.Grid, exception);
            return false;
        }
        _rollbacks.Remove(transaction.Grid);
        FinishTransaction(transaction, success: false);
        return true;
    }

    private void CollectRollbackEntities(ReconstructionTransaction transaction)
    {
        var known = transaction.Disposal.ToHashSet();
        var visited = new HashSet<EntityUid>();
        var pending = new Queue<EntityUid>(transaction.Created.Concat(transaction.Disposal));
        while (pending.TryDequeue(out var entity))
        {
            if (!visited.Add(entity))
                continue;
            if (known.Add(entity))
                transaction.Disposal.Add(entity);
            // Never enroll a player's body or inventory as disposable container contents.
            if (_orders.TryFindPlayerInRepairEntity(entity, out _) ||
                !TryComp(entity, out TransformComponent? transform))
                continue;
            var children = transform.ChildEnumerator;
            while (children.MoveNext(out var child))
                pending.Enqueue(child);
        }
    }

    private bool TryDisposeRollbackEntity(ReconstructionTransaction transaction, EntityUid entity)
    {
        if (!Exists(entity))
            return true;
        if (_orders.TryFindPlayerInRepairEntity(entity, out _) ||
            !TryComp(entity, out TransformComponent? transform) || transform.ChildCount != 0 ||
            !TryComp(entity, out MetaDataComponent? metadata))
            return false;

        try
        {
            // Use supported transform/metadata APIs; never reset lifecycle state or re-enter a
            // terminating entity's deletion. Handle children separately before their parent.
            _lookup.RemoveFromEntityTree(entity, transform);
            TryComp(transform.ParentUid, out TransformComponent? parent);
            _transform.DetachEntity(entity, transform, metadata, parent,
                terminating: metadata.EntityLifeStage >= EntityLifeStage.Terminating);
            _metaData.SetEntityPaused(entity, true, metadata);
            if (!IsRollbackEntityIsolated(entity))
                return false;
            if (metadata.EntityLifeStage < EntityLifeStage.Terminating)
                Del(entity);
        }
        catch (Exception exception)
        {
            LogFailure("reconstruction entity rollback", transaction.Grid, exception);
        }

        if (!Exists(entity))
            return true;
        if (!TryComp(entity, out metadata) ||
            metadata.EntityLifeStage != EntityLifeStage.Terminating || !IsRollbackEntityIsolated(entity))
            return false;

        // The engine owns this broken lifecycle. Nullspace + pause + no broadphase/children is
        // the safe content-side fallback; do not confuse isolation with physical deletion.
        if (transaction.Quarantined.Add(entity))
            _orders.RunPostCommitEffect("reconstruction quarantine log", () =>
                _logs.GetSawmill("repair_orders").Error(
                    $"Reconstruction entity {entity} could not finish engine deletion and was isolated in nullspace. " +
                    "Its lifecycle remains owned by the engine; repair rollback can finish safely."));
        return true;
    }

    private bool IsRollbackEntityIsolated(EntityUid entity)
        => TryComp(entity, out MetaDataComponent? metadata) && metadata.EntityPaused &&
           TryComp(entity, out TransformComponent? transform) &&
           !transform.ParentUid.IsValid() && transform.MapID == MapId.Nullspace && transform.GridUid == null &&
           !transform.Anchored && transform.ChildCount == 0 && !_lookup.TryGetCurrentBroadphase(transform, out _);

    private void FinishTransaction(ReconstructionTransaction transaction, bool success)
    {
        transaction.Blueprint.ReconstructionPending = false;
        _orders.RunPostCommitEffect("reconstruction progress publication", () =>
            _validation.PublishReconstructionResult(transaction.Grid));
        transaction.Tablet.Comp.Applying = false;
        transaction.Station.ReconstructionPending = false;
        transaction.Station.Completing = false;
        _orders.RunPostCommitEffect("reconstruction selection reset", () =>
        {
            if (Exists(transaction.Tablet.Owner))
                ClearSelection(transaction.Tablet);
        });
        _orders.RunPostCommitEffect("reconstruction UI refresh", () => _orders.RefreshStationUis(transaction.Blueprint.Station));
        _orders.RunPostCommitEffect("reconstruction result popup", () =>
        {
            if (Exists(transaction.User))
                Notify(transaction.User, success ? "repair-reconstructor-success" : "repair-reconstructor-failed");
        });
    }

    private void LogFailure(string operation, EntityUid grid, Exception exception)
        => _orders.RunPostCommitEffect("reconstruction error log", () =>
            _logs.GetSawmill("repair_orders").Error($"{operation} of repair grid {grid} failed: {exception}"));

    private sealed class ReconstructionTransaction(Entity<RepairReconstructorComponent> tablet, EntityUid user,
        Entity<MapGridComponent> grid, RepairBlueprintComponent blueprint, RepairOrderStationComponent station, int originalCharges)
    {
        public readonly Entity<RepairReconstructorComponent> Tablet = tablet;
        public readonly EntityUid User = user;
        public readonly Entity<MapGridComponent> Grid = grid;
        public readonly RepairBlueprintComponent Blueprint = blueprint;
        public readonly RepairOrderStationComponent Station = station;
        public readonly int OriginalCharges = originalCharges;
        public readonly Dictionary<Vector2i, Tile> Tiles = new();
        public readonly List<EntityUid> Created = new();
        public readonly List<EntityUid> Disposal = new();
        public readonly HashSet<EntityUid> Quarantined = new();
        public bool RefundDue;
        public bool TilesRestored;
        public TimeSpan NextAttempt;
    }

    private void Notify(EntityUid user, string message, params (string, object)[] arguments)
        => _popup.PopupEntity(Loc.GetString(message, arguments), user, user);

    private sealed record Operation(int Requirement, Vector2i Cell, RepairTileLayer Layer, int TileId,
        string? Prototype, Vector2 Position, Angle Rotation, int Cost);
}
