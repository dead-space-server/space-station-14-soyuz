using System.Linq;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Authoritatively finalizes active orders and removes their repair grids from the playable map.
/// </summary>
public sealed class RepairOrderCompletionSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RepairOrderSystem _repairOrders = default!;
    [Dependency] private readonly RepairOrderRewardDeliverySystem _delivery = default!;
    [Dependency] private readonly RepairOrderRewardSystem _rewards = default!;
    [Dependency] private readonly RepairOrderValidationSystem _validation = default!;
    [Dependency] private readonly StationSystem _station = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("repair_orders");
        Subs.BuiEvents<RepairOrderConsoleComponent>(RepairOrderUiKey.Key, subs =>
        {
            subs.Event<RepairOrderCompleteMessage>(OnComplete);
        });
    }

    private void OnComplete(Entity<RepairOrderConsoleComponent> console, ref RepairOrderCompleteMessage args)
    {
        var stationUid = _station.GetOwningStation(console.Owner);
        if (stationUid == null || !TryComp<RepairOrderStationComponent>(stationUid.Value, out var state))
        {
            Fail(console.Owner, args.Actor, "repair-orders-error-no-station", "console has no owning station");
            return;
        }

        if (!_access.IsAllowed(args.Actor, console.Owner))
        {
            Fail(console.Owner, args.Actor, "repair-orders-error-access", "actor lacks engineering access");
            return;
        }

        if (state.Active is not { } active || active.RuntimeId != args.RuntimeId)
        {
            Fail(console.Owner, args.Actor, "repair-orders-error-complete-unavailable", $"active order {args.RuntimeId} is missing");
            return;
        }

        if (state.Completing)
        {
            Fail(console.Owner, args.Actor, "repair-orders-error-complete-busy", "another completion is in progress");
            return;
        }

        if (!_prototype.TryIndex<RepairOrderPrototype>(active.Prototype, out var order))
        {
            Fail(console.Owner, args.Actor, "repair-orders-error-complete-unavailable", $"prototype {active.Prototype} is missing");
            return;
        }

        state.Completing = true;
        _repairOrders.RefreshStationUis(stationUid.Value);

        RepairOrderDelivery? delivery = null;
        var committed = false;
        try
        {
            // A final full pass makes submission independent of deferred realtime cell updates.
            if (!_validation.TryRevalidateForCompletion(active.GridUid, out var fullyMatchesTarget))
            {
                Fail(
                    console.Owner,
                    args.Actor,
                    "repair-orders-error-blueprint",
                    $"grid {active.GridUid} has no ready repair blueprint");
                return;
            }

            if (!fullyMatchesTarget)
            {
                Fail(
                    console.Owner,
                    args.Actor,
                    "repair-orders-error-incomplete",
                    $"grid {active.GridUid} does not fully match its target blueprint");
                return;
            }

            active.PendingRewards ??= _rewards.GenerateRewards(order, active.CurrentPoints);
            var rewards = active.PendingRewards;
            var completed = new CompletedRepairOrder(
                active.RuntimeId,
                active.Prototype,
                active.CompletedTasks,
                active.TotalTasks,
                active.CurrentPoints,
                active.MaxPoints,
                delivered: false,
                deliveryContainers: null,
                rewards: rewards);

            if (!_delivery.TryDeliver(
                    stationUid.Value,
                    active.RuntimeId,
                    console.Owner,
                    order,
                    rewards,
                    out var preparedDelivery))
            {
                Fail(
                    console.Owner,
                    args.Actor,
                    "repair-orders-error-delivery",
                    $"physical reward delivery failed for order {active.RuntimeId}");
                return;
            }

            delivery = preparedDelivery;
            completed.Delivered = true;
            completed.DeliveryContainers.AddRange(delivery.Containers);

            if (!_repairOrders.TryCommitCompletion(stationUid.Value, active, completed, out var repairGrid))
            {
                _delivery.Rollback(delivery);
                delivery = null;

                Fail(
                    console.Owner,
                    args.Actor,
                    "repair-orders-error-complete-unavailable",
                    $"active order {active.RuntimeId} changed before completion commit");
                return;
            }

            committed = true;
            _delivery.Commit(delivery);

            // The completed snapshot contains every persistent result; the grid and blueprint are no longer runtime state.
            try
            {
                _validation.DiscardPreparedSession(repairGrid);
            }
            finally
            {
                if (Exists(repairGrid))
                    QueueDel(repairGrid);
            }

            _sawmill.Info(
                $"Completed repair order {completed.RuntimeId} ({completed.Prototype}) for station {stationUid}: " +
                $"{completed.CompletedTasks}/{completed.TotalTasks} tasks, {completed.FinalPoints}/{completed.MaxPoints} points, " +
                $"{completed.Rewards.Sum(reward => reward.Count)} physical rewards delivered in " +
                $"{completed.DeliveryContainers.Count} protected container(s) " +
                $"[{string.Join(", ", completed.DeliveryContainers)}]; " +
                $"queued grid {repairGrid} for deletion.");
            _popup.PopupEntity(
                Loc.GetString("repair-orders-complete-success"),
                console.Owner,
                args.Actor,
                PopupType.Medium);
        }
        catch (Exception exception)
        {
            if (committed)
            {
                _sawmill.Error(
                    $"Repair order {active.RuntimeId} ({active.Prototype}) was committed for station {stationUid}, " +
                    $"but post-commit notification failed: {exception}");
                return;
            }

            _delivery.Rollback(delivery);

            _sawmill.Error(
                $"Failed to complete repair order {active.RuntimeId} ({active.Prototype}) for station {stationUid}: {exception}");
            _popup.PopupEntity(
                Loc.GetString("repair-orders-error-complete-unavailable"),
                console.Owner,
                args.Actor,
                PopupType.Medium);
        }
        finally
        {
            state.Completing = false;
            _repairOrders.RefreshStationUis(stationUid.Value);
        }
    }

    private void Fail(EntityUid console, EntityUid actor, string locKey, string reason)
    {
        _sawmill.Warning($"Repair order completion at {ToPrettyString(console)} rejected: {reason}.");
        _popup.PopupEntity(Loc.GetString(locKey), console, actor, PopupType.Medium);
    }
}
