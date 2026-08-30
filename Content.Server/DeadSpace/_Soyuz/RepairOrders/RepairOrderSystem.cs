using System.Linq;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Popups;
using Content.Shared.Prototypes;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
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
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RepairOrderSpawnSystem _spawn = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private ISawmill _sawmill = default!;
    private TimeSpan _nextConsoleDiscovery;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("repair_orders");

        SubscribeLocalEvent<RepairOrderConsoleComponent, ComponentStartup>(OnConsoleStartup);
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

            var changed = false;
            foreach (var (runtimeId, offer) in state.Available.ToArray())
            {
                if (offer.ExpiresAt > now)
                    continue;

                state.Available.Remove(runtimeId);
                changed = true;
            }

            if (now >= state.NextOffer)
            {
                GenerateOffer((stationUid, state));
                // Do not catch up missed intervals in a batch.
                state.NextOffer = now + state.OfferInterval;
                changed = true;
            }

            if (changed)
                UpdateStationUis((stationUid, state));
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

        if (!state.Available.TryGetValue(args.RuntimeId, out var offer) || offer.ExpiresAt <= _timing.CurTime)
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
        UpdateStationUis((stationUid.Value, state));

        try
        {
            if (!_spawn.TrySpawnDamagedGrid(console.Owner, orderPrototype, out var gridUid, out var failure))
            {
                FailRequest(
                    console.Owner,
                    args.Actor,
                    GetSpawnFailureLoc(failure),
                    $"activation of offer {offer.RuntimeId} ({offer.Prototype}) failed: {failure}");
                return;
            }

            state.Available.Remove(offer.RuntimeId);
            state.Active = new ActiveRepairOrder(offer.RuntimeId, offer.Prototype, gridUid);

            var activated = new RepairOrderActivatedEvent(stationUid.Value, offer.Prototype, gridUid);
            RaiseLocalEvent(stationUid.Value, ref activated);

            _sawmill.Info($"Activated repair order {offer.RuntimeId} ({offer.Prototype}) for station {stationUid}; grid {gridUid}.");
            _popup.PopupEntity(
                Loc.GetString("repair-orders-success"),
                console.Owner,
                args.Actor,
                PopupType.Medium);
        }
        finally
        {
            state.Accepting = false;
            UpdateStationUis((stationUid.Value, state));
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

    private Entity<RepairOrderStationComponent>? EnsureStationState(EntityUid console)
    {
        var stationUid = _station.GetOwningStation(console);
        if (stationUid == null)
            return null;

        var state = EnsureComp<RepairOrderStationComponent>(stationUid.Value);
        if (!state.PoolInitialized)
            InitializeStation((stationUid.Value, state));

        return (stationUid.Value, state);
    }

    private void InitializeStation(Entity<RepairOrderStationComponent> station)
    {
        if (station.Comp.PoolInitialized)
            return;

        station.Comp.PoolInitialized = true;
        GenerateOffer(station);
        station.Comp.NextOffer = _timing.CurTime + station.Comp.OfferInterval;
        UpdateStationUis(station);
    }

    private void GenerateOffer(Entity<RepairOrderStationComponent> station)
    {
        var candidates = _prototype.EnumeratePrototypes<RepairOrderPrototype>()
            .Where(order => order.Weight > 0f)
            .ToList();

        var totalWeight = candidates.Sum(order => order.Weight);
        if (candidates.Count == 0 || totalWeight <= 0f)
        {
            _sawmill.Warning($"No positively weighted repair order prototypes are available for station {station.Owner}.");
            return;
        }

        var roll = _random.NextFloat(0f, totalWeight);
        var selected = candidates[^1];
        foreach (var candidate in candidates)
        {
            roll -= candidate.Weight;
            if (roll > 0f)
                continue;

            selected = candidate;
            break;
        }

        var runtimeId = station.Comp.NextRuntimeId++;
        station.Comp.Available[runtimeId] = new AvailableRepairOrder(
            runtimeId,
            selected.ID,
            _timing.CurTime + station.Comp.OfferLifetime);
    }

    public void RefreshStationUis(EntityUid stationUid)
    {
        if (TryComp<RepairOrderStationComponent>(stationUid, out var station))
            UpdateStationUis((stationUid, station));
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
            .OrderBy(offer => offer.ExpiresAt)
            .Select(offer => new RepairOrderBuiEntry(
                offer.RuntimeId,
                offer.Prototype.Id,
                RepairOrderStatus.Available,
                offer.ExpiresAt))
            .ToList();

        RepairOrderBuiEntry? active = null;
        if (station.Comp.Active is { } activeOrder)
        {
            active = new RepairOrderBuiEntry(
                activeOrder.RuntimeId,
                activeOrder.Prototype.Id,
                RepairOrderStatus.Active,
                completedTasks: activeOrder.CompletedTasks,
                totalTasks: activeOrder.TotalTasks,
                blueprintReady: activeOrder.BlueprintReady,
                currentPoints: activeOrder.CurrentPoints,
                maxPoints: activeOrder.MaxPoints);
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
                completedOrder.Delivered,
                completedOrder.Rewards
                    .Select(reward => new RepairOrderRewardBuiEntry(reward.Reward.Id, reward.Count))
                    .ToList());
        }

        _ui.SetUiState(console, RepairOrderUiKey.Key, new RepairOrderBoundUserInterfaceState(
            available,
            active,
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
            RepairOrderSpawnFailure.LoadFailed => "repair-orders-error-load",
            RepairOrderSpawnFailure.InvalidGrid => "repair-orders-error-invalid-grid",
            RepairOrderSpawnFailure.NoSpace => "repair-orders-error-no-space",
            _ => "repair-orders-error-transfer",
        };
    }
}

[ByRefEvent]
public readonly record struct RepairOrderActivatedEvent(
    EntityUid Station,
    ProtoId<RepairOrderPrototype> OrderPrototype,
    EntityUid GridUid);
