// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>Owns the station-scoped Active to Expired terminal path.</summary>
public sealed class RepairOrderExpirationSystem : EntitySystem
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
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
            if (state.Active is not { } active || state.Completing ||
                now < active.ExpiresAt || now < active.NextExpirationAttempt)
                continue;

            TryExpireActiveOrder(stationUid, state);
        }
    }

    public bool TryExpireActiveOrder(
        EntityUid stationUid,
        RepairOrderStationComponent state,
        EntityUid? preferredConsole = null,
        EntityUid? actor = null)
    {
        if (state.Active is not { } active || state.Completing || _timing.CurTime < active.ExpiresAt)
            return false;

        state.Completing = true;
        var committed = false;
        try
        {
            _repairOrders.RefreshStationUis(stationUid);
            if (!_prototype.TryIndex<RepairOrderPrototype>(active.Prototype, out var order))
            {
                _sawmill.Error($"Cannot expire repair order {active.RuntimeId}: prototype {active.Prototype} is missing.");
                return false;
            }

            if (!active.ExpirationFrozen)
            {
                // Freeze once. Every retry commits the same progress, points and reputation.
                if (!_validation.TryRevalidateForCompletion(active.GridUid, out _))
                {
                    _sawmill.Error($"Cannot freeze expired repair order {active.RuntimeId}: validation is unavailable.");
                    return false;
                }

                var repairPercent = RepairOrderProgress.CalculatePercent(active.CompletedTasks, active.TotalTasks);
                if (!_rewards.TryCalculateReputation(order, repairPercent, out var earnedReputation))
                {
                    _sawmill.Error($"Cannot expire repair order {active.RuntimeId}: invalid reputation configuration.");
                    return false;
                }

                active.ExpiredRewardBudget = RepairOrderRewardBudget.ForExpiration(active.FinalPoints);
                active.ExpiredReputation = earnedReputation;
                active.ExpirationFrozen = true;
            }

            var completed = new CompletedRepairOrder(
                active.RuntimeId,
                active.Prototype,
                active.CompletedTasks,
                active.TotalTasks,
                active.FinalPoints,
                active.MaxPoints,
                active.ExpiredRewardBudget,
                active.ExpiredReputation,
                RepairOrderResult.Expired);
            var additionalGrids = active.ExpirationAdditionalGrids.ToArray();

            if (!_repairOrders.TryCommitTerminalResult(stationUid, active, completed, out var repairGrid))
                return false;

            committed = true;
            var cleanupGrids = new HashSet<EntityUid> { repairGrid };
            cleanupGrids.UnionWith(additionalGrids);
            _repairOrders.CleanupTerminalGrids(stationUid, cleanupGrids);

            _repairOrders.RunPostCommitEffect("expiration log", () => _sawmill.Info(
                $"Expired repair order {completed.RuntimeId} ({completed.Prototype}) for station {stationUid}: " +
                $"{completed.CompletedTasks}/{completed.TotalTasks} tasks, " +
                $"earned {completed.RewardBudget} repair points and {completed.EarnedReputation} reputation."));

            _repairOrders.RunPostCommitEffect("expiration popup", () => ShowExpirationPopup(
                completed.RewardBudget > 0,
                actor,
                preferredConsole ?? _station.GetLargestGrid(stationUid)));
            return true;
        }
        catch (Exception exception)
        {
            if (committed)
                _repairOrders.RunPostCommitEffect("expiration error log", () => _sawmill.Error(
                    $"Expired repair order {active.RuntimeId} was committed, but post-commit processing failed: {exception}"));
            else
                _sawmill.Error($"Failed to expire repair order {active.RuntimeId}: {exception}");

            return committed;
        }
        finally
        {
            if (!committed && ReferenceEquals(state.Active, active))
                active.NextExpirationAttempt = _timing.CurTime + RetryDelay;

            state.Completing = false;
            if (committed)
                _repairOrders.RunPostCommitEffect("expiration UI refresh", () => _repairOrders.RefreshStationUis(stationUid));
            else
                _repairOrders.RefreshStationUis(stationUid);
        }
    }

    private void ShowExpirationPopup(bool pointsEarned, EntityUid? actor, EntityUid? source)
    {
        var message = Loc.GetString(pointsEarned
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
