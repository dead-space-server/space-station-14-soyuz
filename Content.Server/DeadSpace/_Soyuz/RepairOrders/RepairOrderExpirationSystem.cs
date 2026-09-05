using System.Linq;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Owns the station-scoped Active -> Expired terminal path.
/// </summary>
public sealed class RepairOrderExpirationSystem : EntitySystem
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RepairOrderRewardDeliverySystem _delivery = default!;
    [Dependency] private readonly RepairOrderRewardSystem _rewards = default!;
    [Dependency] private readonly RepairOrderSystem _repairOrders = default!;
    [Dependency] private readonly RepairOrderValidationSystem _validation = default!;
    [Dependency] private readonly StationSystem _station = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("repair_orders");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<RepairOrderStationComponent>();
        while (query.MoveNext(out var stationUid, out var state))
        {
            if (state.Active is not { } active ||
                state.Completing ||
                now < active.ExpiresAt ||
                now < active.NextExpirationAttempt)
            {
                continue;
            }

            TryExpireActiveOrder(stationUid, state);
        }
    }

    /// <summary>
    /// Attempts the atomic Expired lifecycle. A failed pre-commit delivery leaves the frozen Active snapshot in
    /// place for an idempotent retry; no later grid changes can alter its reward.
    /// </summary>
    public bool TryExpireActiveOrder(
        EntityUid stationUid,
        RepairOrderStationComponent state,
        EntityUid? preferredConsole = null,
        EntityUid? actor = null)
    {
        if (state.Active is not { } active ||
            state.Completing ||
            _timing.CurTime < active.ExpiresAt)
        {
            return false;
        }

        state.Completing = true;
        _repairOrders.RefreshStationUis(stationUid);

        RepairOrderDelivery? delivery = null;
        EntityUid? liveDeliveryConsole = null;
        var committed = false;
        try
        {
            if (!_prototype.TryIndex<RepairOrderPrototype>(active.Prototype, out var order))
            {
                _sawmill.Error(
                    $"Cannot expire repair order {active.RuntimeId} for station {stationUid}: " +
                    $"prototype {active.Prototype} is missing.");
                return false;
            }

            if (!active.ExpirationFrozen)
            {
                // The boolean result describes technical validation availability. A complete match at or after the
                // deadline is still Expired, never successful completion.
                if (!_validation.TryRevalidateForCompletion(active.GridUid, out _))
                {
                    _sawmill.Error(
                        $"Cannot freeze expired repair order {active.RuntimeId} ({active.Prototype}) for station " +
                        $"{stationUid}: grid {active.GridUid} has no ready validation runtime.");
                    return false;
                }

                var rewardBudget = RepairOrderRewardBudget.ForExpiration(active.CurrentPoints);
                var frozenRewards = _rewards.GenerateRewards(order, rewardBudget);

                // Publish the freeze only after its complete immutable reward snapshot has been prepared.
                active.ExpiredRewardBudget = rewardBudget;
                active.PendingRewards = frozenRewards;
                active.ExpirationFrozen = true;
            }

            var rewards = active.PendingRewards ?? new List<RepairOrderRewardResult>();
            var rewardCount = rewards.Sum(reward => reward.Count);
            if (rewardCount > 0)
            {
                RepairOrderDelivery preparedDelivery;
                var deliveryConsole = FindDeliveryConsole(
                    stationUid,
                    active,
                    preferredConsole,
                    out var consoleSource);
                if (deliveryConsole is { } consoleUid)
                {
                    liveDeliveryConsole = consoleUid;
                    var sourceDescription = consoleSource switch
                    {
                        RepairOrderDeliveryConsoleSource.Preferred => "preferred",
                        RepairOrderDeliveryConsoleSource.Activation => "activation",
                        _ => "another same-station",
                    };
                    _sawmill.Debug(
                        $"Using {sourceDescription} repair orders console {ToPrettyString(consoleUid)} as the " +
                        "reward delivery anchor " +
                        $"for expired order {active.RuntimeId} on station {stationUid}.");

                    if (!_delivery.TryDeliver(
                            stationUid,
                            active.RuntimeId,
                            consoleUid,
                            order,
                            rewards,
                            out preparedDelivery))
                    {
                        return false;
                    }
                }
                else if (state.LastRepairConsoleCoordinates is { } lastConsoleCoordinates &&
                         IsUsableLastConsoleCoordinates(stationUid, lastConsoleCoordinates))
                {
                    _sawmill.Debug(
                        $"No usable RepairOrdersConsole remains on station {stationUid}; using last known console " +
                        $"position {lastConsoleCoordinates} for expired order {active.RuntimeId} reward delivery.");

                    if (!_delivery.TryDeliverAtCoordinates(
                            stationUid,
                            active.RuntimeId,
                            lastConsoleCoordinates,
                            order,
                            rewards,
                            out preparedDelivery))
                    {
                        _sawmill.Debug(
                            $"Last known repair-orders console position {lastConsoleCoordinates} on station " +
                            $"{stationUid} has no usable reward placement; using station-grid emergency fallback " +
                            $"for expired order {active.RuntimeId}.");

                        if (!_delivery.TryDeliverAtStation(
                                stationUid,
                                active.RuntimeId,
                                order,
                                rewards,
                                out preparedDelivery))
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    if (state.LastRepairConsoleCoordinates != null)
                        state.LastRepairConsoleCoordinates = null;

                    _sawmill.Debug(
                        $"No usable repair orders console or valid last known console position exists on station " +
                        $"{stationUid}; using station-grid emergency fallback for expired order {active.RuntimeId}.");

                    if (!_delivery.TryDeliverAtStation(
                            stationUid,
                            active.RuntimeId,
                            order,
                            rewards,
                            out preparedDelivery))
                    {
                        return false;
                    }
                }

                delivery = preparedDelivery;
            }

            var completed = new CompletedRepairOrder(
                active.RuntimeId,
                active.Prototype,
                active.CompletedTasks,
                active.TotalTasks,
                active.CurrentPoints,
                active.MaxPoints,
                active.ExpiredRewardBudget,
                RepairOrderResult.Expired,
                delivered: delivery != null,
                deliveryContainers: delivery?.Containers,
                rewards: rewards);
            var additionalGrids = active.ExpirationAdditionalGrids.ToArray();

            if (!_repairOrders.TryCommitTerminalResult(stationUid, active, completed, out var repairGrid))
            {
                _delivery.Rollback(delivery);
                delivery = null;
                return false;
            }

            committed = true;
            if (delivery != null)
                _delivery.Commit(delivery);

            var cleanupGrids = new HashSet<EntityUid> { repairGrid };
            cleanupGrids.UnionWith(additionalGrids);
            _repairOrders.CleanupTerminalGrids(stationUid, cleanupGrids);

            _sawmill.Info(
                $"Expired repair order {completed.RuntimeId} ({completed.Prototype}) for station {stationUid}: " +
                $"GridUid={repairGrid}, CompletedTasks={completed.CompletedTasks}, TotalTasks={completed.TotalTasks}, " +
                $"CurrentPoints={completed.FinalPoints}, MaxPoints={completed.MaxPoints}, " +
                $"RepairPercent={completed.RepairPercent}, RewardBudget={completed.RewardBudget}, " +
                $"RewardCount={rewardCount}, DeliveryContainerCount={completed.DeliveryContainers.Count}, " +
                $"Result={completed.Result}.");

            ShowExpirationPopup(
                rewardCount > 0 && delivery != null,
                actor,
                liveDeliveryConsole ?? _station.GetLargestGrid(stationUid));
            return true;
        }
        catch (Exception exception)
        {
            if (!committed)
            {
                _delivery.Rollback(delivery);
                _sawmill.Error(
                    $"Failed to expire repair order {active.RuntimeId} ({active.Prototype}) for station " +
                    $"{stationUid}: {exception}");
            }
            else
            {
                _sawmill.Error(
                    $"Expired repair order {active.RuntimeId} ({active.Prototype}) was committed for station " +
                    $"{stationUid}, but post-commit processing failed: {exception}");
            }

            return committed;
        }
        finally
        {
            if (!committed && ReferenceEquals(state.Active, active))
                active.NextExpirationAttempt = _timing.CurTime + RetryDelay;

            state.Completing = false;
            _repairOrders.RefreshStationUis(stationUid);
        }
    }

    private EntityUid? FindDeliveryConsole(
        EntityUid stationUid,
        ActiveRepairOrder active,
        EntityUid? preferredConsole,
        out RepairOrderDeliveryConsoleSource source)
    {
        source = RepairOrderDeliveryConsoleSource.Other;
        if (preferredConsole is { } preferred &&
            _repairOrders.RememberRepairConsole(stationUid, preferred))
        {
            source = RepairOrderDeliveryConsoleSource.Preferred;
            return preferred;
        }

        if (active.ActivationConsole is { } activationConsole &&
            _repairOrders.RememberRepairConsole(stationUid, activationConsole))
        {
            source = RepairOrderDeliveryConsoleSource.Activation;
            return activationConsole;
        }

        var query = EntityQueryEnumerator<RepairOrderConsoleComponent>();
        while (query.MoveNext(out var consoleUid, out _))
        {
            if (!_repairOrders.RememberRepairConsole(stationUid, consoleUid))
                continue;

            return consoleUid;
        }

        return null;
    }

    private bool IsUsableLastConsoleCoordinates(EntityUid stationUid, EntityCoordinates coordinates)
    {
        if (coordinates == EntityCoordinates.Invalid ||
            !coordinates.EntityId.IsValid() ||
            !Exists(coordinates.EntityId) ||
            MetaData(coordinates.EntityId).EntityLifeStage >= EntityLifeStage.Terminating ||
            !TryComp<MapGridComponent>(coordinates.EntityId, out _) ||
            !TryComp(coordinates.EntityId, out TransformComponent? gridTransform) ||
            gridTransform.MapID == MapId.Nullspace)
        {
            return false;
        }

        return _station.GetOwningStation(coordinates.EntityId, gridTransform) == stationUid;
    }

    private void ShowExpirationPopup(bool rewardIssued, EntityUid? actor, EntityUid? source)
    {
        var message = Loc.GetString(rewardIssued
            ? "repair-orders-expired-partial-reward"
            : "repair-orders-expired-no-reward");

        if (actor is { Valid: true } recipient && Exists(recipient))
        {
            _popup.PopupEntity(message, recipient, recipient, PopupType.Medium);
            return;
        }

        if (source is { Valid: true } sourceUid && Exists(sourceUid))
            _popup.PopupEntity(message, sourceUid, PopupType.Medium);
    }
}

internal enum RepairOrderDeliveryConsoleSource : byte
{
    Preferred,
    Activation,
    Other,
}
