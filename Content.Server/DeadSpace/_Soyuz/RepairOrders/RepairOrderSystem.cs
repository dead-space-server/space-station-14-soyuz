// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Ghost;
using Content.Shared.Popups;
using Content.Shared.Prototypes;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Generates station-scoped offers and coordinates their authoritative activation.
/// </summary>
public sealed class RepairOrderSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RepairOrderSpawnSystem _spawn = default!;
    [Dependency] private readonly RepairOrderValidationSystem _validation = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private ISawmill _sawmill = default!;
    private TimeSpan _nextConsoleDiscovery;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("repair_orders");

        SubscribeLocalEvent<RepairOrderConsoleComponent, ComponentStartup>(OnConsoleStartup);
        SubscribeLocalEvent<RepairOrderStationComponent, ComponentShutdown>(OnStationShutdown);
        SubscribeLocalEvent<RepairOrderConsoleComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        Subs.BuiEvents<RepairOrderConsoleComponent>(RepairOrderUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<RepairOrderAcceptMessage>(OnAccept);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now >= _nextConsoleDiscovery)
        {
            DiscoverConsoles();
            _nextConsoleDiscovery = now + TimeSpan.FromSeconds(1);
        }

        var query = EntityQueryEnumerator<RepairOrderStationComponent>();
        while (query.MoveNext(out var stationUid, out var state))
        {
            if (!state.PoolInitialized)
                InitializeStation((stationUid, state));

            ProcessPendingGridCleanup((stationUid, state));

            if (state.Active is { } active &&
                !active.ExpirationFrozen &&
                (!active.GridUid.IsValid() || !Exists(active.GridUid)))
            {
                AbortActiveOrder(
                    stationUid,
                    active.GridUid,
                    RepairOrderAbortReason.RepairGridDeleted);
            }

            if (now >= state.NextOffer && !state.Accepting && !state.Completing)
            {
                GenerateOffer((stationUid, state), refresh: true);
                UpdateStationUis((stationUid, state));
            }
        }
    }

    private void OnConsoleStartup(Entity<RepairOrderConsoleComponent> console, ref ComponentStartup args)
    {
        EnsureStationState(console.Owner);
    }

    private void OnOpenAttempt(Entity<RepairOrderConsoleComponent> console, ref ActivatableUIOpenAttemptEvent args)
    {
        if (_access.IsAllowed(args.User, console.Owner))
            return;

        _popup.PopupEntity(
            Loc.GetString("repair-orders-error-access"),
            console.Owner,
            args.User,
            PopupType.Medium);
        args.Cancel();
    }

    private void OnUiOpened(Entity<RepairOrderConsoleComponent> console, ref BoundUIOpenedEvent args)
    {
        if (EnsureStationState(console.Owner) is not { } stationState)
            return;

        UpdateConsoleUi(console.Owner, stationState);
    }

    private void OnAccept(Entity<RepairOrderConsoleComponent> console, ref RepairOrderAcceptMessage args)
    {
        var stationUid = _station.GetOwningStation(console.Owner);
        if (stationUid == null || !TryComp<RepairOrderStationComponent>(stationUid.Value, out var state))
        {
            FailRequest(console.Owner, args.Actor, "repair-orders-error-no-station", "console has no owning station");
            return;
        }

        if (!_access.IsAllowed(args.Actor, console.Owner))
        {
            FailRequest(console.Owner, args.Actor, "repair-orders-error-access", "actor lacks engineering access");
            return;
        }

        if (!state.Available.TryGetValue(args.RuntimeId, out var offer) || state.NextOffer <= _timing.CurTime)
        {
            FailRequest(console.Owner, args.Actor, "repair-orders-error-unavailable", $"offer {args.RuntimeId} is missing or expired");
            return;
        }

        if (state.Active != null)
        {
            FailRequest(console.Owner, args.Actor, "repair-orders-error-active", "station already has an active order");
            return;
        }

        if (state.Accepting || state.Completing)
        {
            FailRequest(console.Owner, args.Actor, "repair-orders-error-busy", "another activation is in progress");
            return;
        }

        if (!_prototype.TryIndex<RepairOrderPrototype>(offer.Prototype, out var orderPrototype))
        {
            FailRequest(console.Owner, args.Actor, "repair-orders-error-unavailable", $"prototype {offer.Prototype} is missing");
            return;
        }

        state.Accepting = true;

        EntityUid? spawnedGrid = null;
        var committed = false;
        try
        {
            UpdateStationUis((stationUid.Value, state));

            if (!_spawn.TrySpawnDamagedGrid(console.Owner, orderPrototype, offer, out var gridUid, out var preparedActive, out var failure))
            {
                FailRequest(
                    console.Owner,
                    args.Actor,
                    GetSpawnFailureLoc(failure),
                    $"activation of offer {offer.RuntimeId} ({offer.Prototype}) failed: {failure}");
                return;
            }

            spawnedGrid = gridUid;
            var startedAt = _timing.CurTime;
            preparedActive.StartedAt = startedAt;
            preparedActive.ExpiresAt = startedAt + orderPrototype.RepairTime;
            preparedActive.ActivationConsole = console.Owner;
            var consoleCoordinates = TryGetRepairConsoleCoordinates(stationUid.Value, console.Owner, out var coordinates)
                ? coordinates
                : state.LastRepairConsoleCoordinates;

            // Authoritative commit: publish the complete session and its reward fallback coordinates together.
            // No callbacks or other potentially throwing preparation belong past this boundary.
            if (!state.Available.Remove(offer.RuntimeId))
            {
                FailRequest(
                    console.Owner,
                    args.Actor,
                    "repair-orders-error-unavailable",
                    $"offer {offer.RuntimeId} disappeared before activation commit");
                return;
            }

            state.LastRepairConsoleCoordinates = consoleCoordinates;
            state.Active = preparedActive;
            committed = true;
            spawnedGrid = null;
        }
        finally
        {
            try
            {
                if (!committed && spawnedGrid is { } rollbackGrid)
                {
                    try
                    {
                        _validation.DiscardPreparedSession(rollbackGrid);
                    }
                    finally
                    {
                        if (Exists(rollbackGrid))
                            Del(rollbackGrid);
                    }
                }
            }
            finally
            {
                state.Accepting = false;
                if (!committed)
                    UpdateStationUis((stationUid.Value, state));
            }
        }

        // Post-commit notifications cannot fail or roll back the activation. Attempt each independently,
        // so a failing extension subscriber does not prevent the success popup or the final UI refresh.
        var actor = args.Actor;
        var activeGrid = state.Active!.GridUid;
        RunPostCommitEffect("activation event", () =>
        {
            var activated = new RepairOrderActivatedEvent(stationUid.Value, offer.Prototype, activeGrid);
            RaiseLocalEvent(stationUid.Value, ref activated);
        });
        RunPostCommitEffect("activation log", () =>
            _sawmill.Info($"Activated repair order {offer.RuntimeId} ({offer.Prototype}) for station {stationUid}; grid {activeGrid}."));
        RunPostCommitEffect("success popup", () => _popup.PopupEntity(
            Loc.GetString("repair-orders-success"), console.Owner, actor, PopupType.Medium));
        RunPostCommitEffect("station UI refresh", () => UpdateStationUis((stationUid.Value, state)));
    }

    internal void RunPostCommitEffect(string effect, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            try
            {
                _sawmill.Error($"Repair order post-commit {effect} failed: {exception}");
            }
            catch (Exception)
            {
                // Even a failing log sink must not turn a committed operation into a failed request.
            }
        }
    }

    private void DiscoverConsoles()
    {
        var query = EntityQueryEnumerator<RepairOrderConsoleComponent>();
        while (query.MoveNext(out var consoleUid, out _))
        {
            EnsureStationState(consoleUid);
        }
    }

    private void OnStationShutdown(Entity<RepairOrderStationComponent> station, ref ComponentShutdown args)
    {
        // Repair grids are not children of their station. Losing the owner must also dispose its runtime,
        // including frozen expiration fragments and terminal grids which were waiting for players to leave.
        var grids = new HashSet<EntityUid>(station.Comp.PendingCleanupGrids);
        if (station.Comp.Active is { } active)
        {
            grids.Add(active.GridUid);
            grids.UnionWith(active.ExpirationAdditionalGrids);
        }

        station.Comp.Active = null;
        station.Comp.PendingCleanupGrids.Clear();
        station.Comp.Accepting = false;
        station.Comp.Completing = false;
        foreach (var grid in grids)
        {
            RunPostCommitEffect("station removal grid cleanup", () =>
            {
                try
                {
                    _validation.DiscardPreparedSession(grid);
                }
                finally
                {
                    if (Exists(grid) && MetaData(grid).EntityLifeStage < EntityLifeStage.Terminating)
                        QueueDel(grid);
                }
            });
        }
    }

    private Entity<RepairOrderStationComponent>? EnsureStationState(EntityUid console)
    {
        var stationUid = _station.GetOwningStation(console);
        if (stationUid == null)
            return null;

        var state = EnsureComp<RepairOrderStationComponent>(stationUid.Value);
        if (!state.PoolInitialized)
            InitializeStation((stationUid.Value, state));

        RememberRepairConsole((stationUid.Value, state), console);

        return (stationUid.Value, state);
    }

    /// <summary>
    /// Remembers a usable repair-orders console as grid-local station coordinates. Returns false for deleted,
    /// nullspace, off-grid, or foreign-station consoles without replacing the previous known position.
    /// </summary>
    public bool RememberRepairConsole(EntityUid stationUid, EntityUid consoleUid)
    {
        if (!TryComp<RepairOrderStationComponent>(stationUid, out var state))
            return false;

        return RememberRepairConsole((stationUid, state), consoleUid);
    }

    private bool RememberRepairConsole(Entity<RepairOrderStationComponent> station, EntityUid consoleUid)
    {
        if (!TryGetRepairConsoleCoordinates(station.Owner, consoleUid, out var coordinates))
            return false;

        station.Comp.LastRepairConsoleCoordinates = coordinates;
        return true;
    }

    private bool TryGetRepairConsoleCoordinates(
        EntityUid stationUid,
        EntityUid consoleUid,
        out EntityCoordinates coordinates)
    {
        coordinates = EntityCoordinates.Invalid;
        if (!consoleUid.IsValid() ||
            !Exists(consoleUid) ||
            MetaData(consoleUid).EntityLifeStage >= EntityLifeStage.Terminating ||
            !TryComp<RepairOrderConsoleComponent>(consoleUid, out _) ||
            !TryComp(consoleUid, out TransformComponent? transform) ||
            transform.MapID == MapId.Nullspace ||
            transform.GridUid is not { } gridUid ||
            !Exists(gridUid) ||
            MetaData(gridUid).EntityLifeStage >= EntityLifeStage.Terminating ||
            _station.GetOwningStation(consoleUid, transform) != stationUid)
        {
            return false;
        }

        coordinates = _transform.GetMoverCoordinates(consoleUid, transform);
        if (coordinates == EntityCoordinates.Invalid || coordinates.EntityId != gridUid)
            return false;

        return true;
    }

    private void InitializeStation(Entity<RepairOrderStationComponent> station)
    {
        if (station.Comp.PoolInitialized)
            return;

        GenerateOffer(station);
        station.Comp.PoolInitialized = true;
        station.Comp.NextOffer = _timing.CurTime + station.Comp.OfferInterval;
        UpdateStationUis(station);
    }

    /// <summary>Prepare a weighted batch without replacement, then publish it atomically.</summary>
    public void GenerateOffer(Entity<RepairOrderStationComponent> station, bool refresh = false)
    {
        if (station.Comp.Accepting || station.Comp.Completing)
            return;

        var limit = Math.Clamp(station.Comp.AvailableOfferCount, 0, RepairOrderStationComponent.MaximumAvailableOffers);
        if (!refresh && station.Comp.Available.Count >= limit) return;
        var available = refresh
            ? new Dictionary<int, AvailableRepairOrder>()
            : new Dictionary<int, AvailableRepairOrder>(station.Comp.Available);
        var occupied = available.Values.Select(offer => offer.Prototype.Id).ToHashSet();
        var previous = station.Comp.Available.Values.Select(offer => offer.Prototype.Id).ToHashSet();
        var candidates = _prototype.EnumeratePrototypes<RepairOrderPrototype>()
            .Where(order => float.IsFinite(order.Weight) && order.Weight > 0f && !occupied.Contains(order.ID))
            .OrderBy(order => order.ID, StringComparer.Ordinal).ToList();
        var seeds = available.Values.Select(offer => offer.DamageSeed).ToHashSet();
        var nextRuntimeId = station.Comp.NextRuntimeId;
        var nextOffer = refresh ? _timing.CurTime + station.Comp.OfferInterval : station.Comp.NextOffer;
        while (available.Count < limit && candidates.Count > 0)
        {
            // Prefer different offers; a small catalog can still fill the remaining slots.
            var fresh = candidates.Where(order => !previous.Contains(order.ID)).ToList();
            var selected = SelectWeightedOffer(fresh.Count > 0 ? fresh : candidates, _random.NextDouble());
            candidates.Remove(selected);
            var seed = _random.Next();
            while (!seeds.Add(seed)) seed = seed == int.MaxValue ? 0 : seed + 1;
            var runtimeId = nextRuntimeId++;
            available.Add(runtimeId, new AvailableRepairOrder(runtimeId, selected.ID, seed));
        }

        station.Comp.Available = available;
        station.Comp.NextRuntimeId = nextRuntimeId;
        station.Comp.NextOffer = nextOffer;
    }

    /// <summary>Select from the already filtered positive-weight candidates using a roll in [0, 1).</summary>
    public static RepairOrderPrototype SelectWeightedOffer(IReadOnlyList<RepairOrderPrototype> candidates, double unitRoll)
    {
        if (candidates.Count == 0 || !double.IsFinite(unitRoll) || unitRoll < 0 || unitRoll >= 1)
            throw new ArgumentOutOfRangeException(nameof(unitRoll));
        var roll = unitRoll * candidates.Sum(order => (double) order.Weight);
        foreach (var candidate in candidates)
        {
            roll -= candidate.Weight;
            if (roll < 0) return candidate;
        }
        return candidates[^1];
    }

    public void RefreshStationUis(EntityUid stationUid)
    {
        if (TryComp<RepairOrderStationComponent>(stationUid, out var station))
            UpdateStationUis((stationUid, station));
    }

    /// <summary>
    /// Commits an Active -> terminal result transition. Active order lifecycle mutations are owned here.
    /// </summary>
    public bool TryCommitTerminalResult(
        EntityUid stationUid,
        ActiveRepairOrder expectedActive,
        CompletedRepairOrder completed,
        out EntityUid repairGrid)
    {
        repairGrid = EntityUid.Invalid;
        if (!TryComp<RepairOrderStationComponent>(stationUid, out var state) ||
            !ReferenceEquals(state.Active, expectedActive) ||
            completed.RuntimeId != expectedActive.RuntimeId ||
            completed.Prototype != expectedActive.Prototype)
        {
            return false;
        }

        repairGrid = expectedActive.GridUid;
        completed.DamageGeneration = expectedActive.DamageGeneration;
        completed.Exclusions = expectedActive.Exclusions;
        state.Completed = completed;
        state.Active = null;
        return true;
    }

    public bool TryCommitCompletion(
        EntityUid stationUid,
        ActiveRepairOrder expectedActive,
        CompletedRepairOrder completed,
        out EntityUid repairGrid)
    {
        return TryCommitTerminalResult(stationUid, expectedActive, completed, out repairGrid);
    }

    /// <summary>
    /// Ends an active order without completion or rewards and disposes every grid owned by the failed session.
    /// The expected grid protects a newer active order from delayed lifecycle events belonging to an old grid.
    /// </summary>
    public bool AbortActiveOrder(
        EntityUid stationUid,
        EntityUid expectedGrid,
        RepairOrderAbortReason reason,
        IReadOnlyCollection<EntityUid>? additionalGrids = null)
    {
        if (!TryComp<RepairOrderStationComponent>(stationUid, out var state) ||
            state.Active is not { } active ||
            active.GridUid != expectedGrid)
        {
            return false;
        }

        // Once deadline progress is frozen, Expired owns the terminal transition. Grid loss or splitting can no
        // longer turn that already-claimed result into Abort, but every resulting grid is still owned for cleanup.
        if (active.ExpirationFrozen)
        {
            if (additionalGrids != null)
                active.ExpirationAdditionalGrids.UnionWith(additionalGrids);

            return false;
        }

        var ownedGrids = new HashSet<EntityUid> { active.GridUid };
        if (additionalGrids != null)
            ownedGrids.UnionWith(additionalGrids);

        // Claim the transition before cleanup so component shutdown and repeated abort calls are harmless.
        state.Active = null;
        state.Completing = false;

        RunPostCommitEffect("abort log", () => _sawmill.Warning(
            $"Aborted repair order {active.RuntimeId} ({active.Prototype}) for station {stationUid}: {reason}. " +
            $"No completion or rewards were produced; disposing {ownedGrids.Count} repair grid(s)."));

        try
        {
            _validation.DiscardPreparedSession(active.GridUid);
        }
        finally
        {
            try
            {
                foreach (var gridUid in ownedGrids)
                {
                    if (Exists(gridUid) && MetaData(gridUid).EntityLifeStage < EntityLifeStage.Terminating)
                        QueueDel(gridUid);
                }
            }
            finally
            {
                RunPostCommitEffect("abort UI refresh", () => UpdateStationUis((stationUid, state)));
            }
        }

        return true;
    }

    /// <summary>
    /// Removes validation immediately when safe, or defers grid deletion until all non-ghost players have left.
    /// </summary>
    public void CleanupTerminalGrid(EntityUid stationUid, EntityUid gridUid)
    {
        if (!TryComp<RepairOrderStationComponent>(stationUid, out var state))
            return;

        if (!Exists(gridUid))
        {
            state.PendingCleanupGrids.Remove(gridUid);
            return;
        }

        // Pending cleanup is authoritative ownership: keep the grid locked until deletion has actually completed.
        state.PendingCleanupGrids.Add(gridUid);

        if (TryFindPlayerOnRepairGrid(gridUid, out _))
            return;

        _validation.DiscardPreparedSession(gridUid);
        if (Exists(gridUid) && MetaData(gridUid).EntityLifeStage < EntityLifeStage.Terminating)
            QueueDel(gridUid);
    }

    /// <summary>
    /// Transfers every terminal grid to station-owned pending cleanup before cleanup of any individual grid begins.
    /// This preserves ownership of the remaining grids if validation cleanup for one grid throws.
    /// </summary>
    public void CleanupTerminalGrids(EntityUid stationUid, IReadOnlyCollection<EntityUid> gridUids)
    {
        if (!TryComp<RepairOrderStationComponent>(stationUid, out var state))
            return;

        state.PendingCleanupGrids.UnionWith(gridUids);

        foreach (var gridUid in gridUids)
            CleanupTerminalGrid(stationUid, gridUid);
    }

    /// <summary>
    /// Preserves ownership of grids produced by a split after Expired has committed but before player-safe cleanup.
    /// </summary>
    public void TrackPendingCleanupSplit(
        EntityUid stationUid,
        EntityUid originalGrid,
        IReadOnlyCollection<EntityUid> newGrids)
    {
        if (!TryComp<RepairOrderStationComponent>(stationUid, out var state) ||
            !state.PendingCleanupGrids.Contains(originalGrid))
        {
            return;
        }

        state.PendingCleanupGrids.UnionWith(newGrids);
    }

    public bool TryFindPlayerOnRepairGrid(EntityUid repairGrid, out EntityUid player)
    {
        player = EntityUid.Invalid;

        foreach (var session in _playerManager.Sessions)
        {
            if (session.Status != SessionStatus.InGame ||
                session.AttachedEntity is not { Valid: true } attached ||
                !Exists(attached) ||
                HasComp<GhostComponent>(attached) ||
                !TryComp(attached, out TransformComponent? transform) ||
                transform.GridUid != repairGrid)
            {
                continue;
            }

            player = attached;
            return true;
        }

        return false;
    }

    private void ProcessPendingGridCleanup(Entity<RepairOrderStationComponent> station)
    {
        foreach (var gridUid in station.Comp.PendingCleanupGrids.ToArray())
        {
            if (!Exists(gridUid))
            {
                station.Comp.PendingCleanupGrids.Remove(gridUid);
                continue;
            }

            if (TryFindPlayerOnRepairGrid(gridUid, out _))
                continue;

            _validation.DiscardPreparedSession(gridUid);
            if (Exists(gridUid) && MetaData(gridUid).EntityLifeStage < EntityLifeStage.Terminating)
                QueueDel(gridUid);
        }
    }

    private void UpdateStationUis(Entity<RepairOrderStationComponent> station)
    {
        var query = EntityQueryEnumerator<RepairOrderConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var consoleUid, out _, out var xform))
        {
            if (_station.GetOwningStation(consoleUid, xform) != station.Owner)
                continue;

            UpdateConsoleUi(consoleUid, station);
        }
    }

    private void UpdateConsoleUi(EntityUid console, Entity<RepairOrderStationComponent> station)
    {
        var available = station.Comp.Available.Values
            .OrderBy(offer => offer.RuntimeId)
            .Select(offer => new RepairOrderBuiEntry(
                offer.RuntimeId,
                offer.Prototype.Id,
                RepairOrderStatus.Available))
            .ToList();

        RepairOrderBuiEntry? active = null;
        var worklist = new List<RepairOrderWorklistEntry>();
        if (station.Comp.Active is { } activeOrder)
        {
            if (activeOrder.BlueprintReady)
                worklist = _validation.GetWorklist(activeOrder.GridUid);

            active = new RepairOrderBuiEntry(
                activeOrder.RuntimeId,
                activeOrder.Prototype.Id,
                RepairOrderStatus.Active,
                activeOrder.ExpiresAt,
                completedTasks: activeOrder.CompletedTasks,
                totalTasks: activeOrder.TotalTasks,
                blueprintReady: activeOrder.BlueprintReady,
                currentPoints: activeOrder.CurrentPoints,
                maxPoints: activeOrder.MaxPoints,
                damageEvents: activeOrder.DamageGeneration?.SelectedEvents.ToArray(),
                exclusions: activeOrder.Exclusions?.Totals ?? new RepairExclusionTotals(0, 0, RepairTechnicalExclusion.MaxWaivedPoints(activeOrder.MaxPoints), activeOrder.CurrentPoints));
        }

        RepairOrderCompletedBuiEntry? completed = null;
        if (station.Comp.Completed is { } completedOrder)
        {
            completed = new RepairOrderCompletedBuiEntry(
                completedOrder.RuntimeId,
                completedOrder.Prototype.Id,
                completedOrder.CompletedTasks,
                completedOrder.TotalTasks,
                completedOrder.FinalPoints,
                completedOrder.MaxPoints,
                completedOrder.RepairPercent,
                completedOrder.RewardBudget,
                completedOrder.Result,
                completedOrder.Delivered,
                completedOrder.Rewards
                    .Select(reward => new RepairOrderRewardBuiEntry(reward.Reward.Id, reward.Count))
                    .ToList(),
                completedOrder.DamageGeneration?.SelectedEvents.ToArray(),
                completedOrder.Exclusions?.Totals ?? new RepairExclusionTotals(0, 0, RepairTechnicalExclusion.MaxWaivedPoints(completedOrder.MaxPoints), completedOrder.FinalPoints));
        }

        _ui.SetUiState(console, RepairOrderUiKey.Key, new RepairOrderBoundUserInterfaceState(
            available,
            active,
            worklist,
            completed,
            station.Comp.NextOffer,
            station.Comp.OfferInterval,
            station.Comp.Accepting,
            station.Comp.Completing));
    }

    private void FailRequest(EntityUid console, EntityUid actor, string locKey, string logReason)
    {
        _sawmill.Warning($"Repair order request at {ToPrettyString(console)} rejected: {logReason}.");
        _popup.PopupEntity(Loc.GetString(locKey), console, actor, PopupType.Medium);
    }

    private static string GetSpawnFailureLoc(RepairOrderSpawnFailure failure)
    {
        return failure switch
        {
            RepairOrderSpawnFailure.NoStation => "repair-orders-error-no-station",
            RepairOrderSpawnFailure.NoStationGrid => "repair-orders-error-no-grid",
            RepairOrderSpawnFailure.DamageFailed => "repair-orders-error-damage",
            RepairOrderSpawnFailure.PrepareFailed => "repair-orders-error-prepare",
            RepairOrderSpawnFailure.LoadFailed => "repair-orders-error-load",
            RepairOrderSpawnFailure.InvalidGrid => "repair-orders-error-invalid-grid",
            RepairOrderSpawnFailure.NoSpace => "repair-orders-error-no-space",
            _ => "repair-orders-error-transfer",
        };
    }
}

/// <summary>
/// Post-commit notification that a fully prepared repair order is now active.
/// </summary>
[ByRefEvent]
public readonly record struct RepairOrderActivatedEvent(
    EntityUid Station,
    ProtoId<RepairOrderPrototype> OrderPrototype,
    EntityUid GridUid);

public enum RepairOrderAbortReason : byte
{
    RepairGridDeleted,
    RepairGridSplit,
    ValidationRuntimeLost,
}
