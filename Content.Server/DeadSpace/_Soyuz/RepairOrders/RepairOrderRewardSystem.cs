// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>Calculates repair reputation and validates the station shop against its configured reward pool.</summary>
public sealed class RepairOrderRewardSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly RepairOrderSystem _repairOrders = default!;
    [Dependency] private readonly RepairOrderRewardDeliverySystem _delivery = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("repair_orders");
        Subs.BuiEvents<RepairOrderConsoleComponent>(RepairOrderUiKey.Key, subs =>
        {
            subs.Event<RepairOrderShopPurchaseMessage>(OnPurchase);
        });
    }

    private void OnPurchase(Entity<RepairOrderConsoleComponent> console, ref RepairOrderShopPurchaseMessage args)
    {
        var station = _station.GetOwningStation(console.Owner);
        if (station == null || !TryComp<RepairOrderStationComponent>(station.Value, out var state))
        {
            Reply(console.Owner, args.Actor, args.RequestId, false, "repair-orders-error-no-station");
            return;
        }

        if (!_access.IsAllowed(args.Actor, console.Owner))
        {
            Reply(console.Owner, args.Actor, args.RequestId, false, "repair-orders-error-access");
            return;
        }

        if (!Guid.TryParseExact(args.RequestId, "N", out var requestId))
        {
            Reply(console.Owner, args.Actor, args.RequestId, false, "repair-orders-shop-error-invalid");
            return;
        }

        if (state.CompletedShopRequests.Contains(requestId))
        {
            Reply(console.Owner, args.Actor, args.RequestId, true, "repair-orders-shop-success");
            return;
        }

        if (state.ShopPurchaseInProgress)
        {
            Reply(console.Owner, args.Actor, args.RequestId, false, "repair-orders-shop-error-busy");
            return;
        }

        state.ShopPurchaseInProgress = true;
        RepairOrderDelivery? delivery = null;
        var committed = false;
        var result = "repair-orders-shop-error-invalid";
        try
        {
            if (!_prototype.TryIndex<RepairRewardPoolPrototype>(console.Comp.ShopRewardPool, out var pool) ||
                !_prototype.TryIndex<EntityPrototype>(pool.DeliveryContainer, out _))
                return;

            var shopLevel = GetShopLevel(pool, state.EngineeringReputation);
            if (!TryBuildPurchase(pool, args.Lines, shopLevel, out var rewards, out var totalCost, out result))
                return;

            if (state.RepairPoints < totalCost)
            {
                result = "repair-orders-shop-error-points";
                return;
            }

            var purchaseId = Guid.NewGuid();
            if (!_delivery.TryDeliver(station.Value, purchaseId, console.Owner, pool, rewards, out delivery))
            {
                result = "repair-orders-shop-error-delivery";
                return;
            }

            // No callback or other throwing work belongs between the balance check and commit.
            if (state.RepairPoints < totalCost)
            {
                result = "repair-orders-shop-error-points";
                return;
            }

            state.CompletedShopRequests.Add(requestId);
            state.RepairPoints -= totalCost;
            _delivery.Commit(delivery);
            committed = true;
            result = "repair-orders-shop-success";
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Shop purchase at {console.Owner} failed before commit: {exception}");
            result = "repair-orders-shop-error-delivery";
        }
        finally
        {
            try
            {
                if (!committed)
                    _delivery.Rollback(delivery);
            }
            finally
            {
                var actor = args.Actor;
                var requestMessageId = args.RequestId;
                state.ShopPurchaseInProgress = false;
                _repairOrders.RunPostCommitEffect("shop UI refresh", () => _repairOrders.RefreshStationUis(station.Value));
                _repairOrders.RunPostCommitEffect("shop result", () => Reply(console.Owner, actor, requestMessageId, committed, result));
            }
        }
    }

    private void Reply(EntityUid console, EntityUid actor, string requestId, bool success, string message)
    {
        _ui.ServerSendUiMessage(console, RepairOrderUiKey.Key,
            new RepairOrderShopResultMessage(requestId, success, message), actor);
    }

    public static int GetShopLevel(RepairRewardPoolPrototype pool, long reputation)
    {
        var level = 1;
        for (var i = 1; i < pool.ShopLevelThresholds.Count; i++)
        {
            if (reputation < pool.ShopLevelThresholds[i])
                break;

            level = i + 1;
        }

        return level;
    }

    public bool TryCalculateReputation(RepairOrderPrototype order, int repairPercent, out int reputation)
    {
        reputation = 0;
        if (!_prototype.TryIndex<RepairRewardPoolPrototype>(order.RewardPool, out var pool) ||
            order.Difficulty < RepairOrderDifficulty.Minimum ||
            order.Difficulty > pool.BaseReputationByDifficulty.Count ||
            repairPercent is < 0 or > 100)
            return false;

        foreach (var band in pool.QualityBands)
        {
            if (repairPercent < band.MinPercent || repairPercent > band.MaxPercent)
                continue;

            var award = (long) pool.BaseReputationByDifficulty[order.Difficulty - 1] * band.MultiplierPercent / 100;
            if (award < 0 || award > int.MaxValue)
                return false;

            reputation = (int) award;
            return true;
        }

        return false;
    }

    public bool TryBuildPurchase(
        RepairRewardPoolPrototype pool,
        IReadOnlyList<RepairOrderRewardBuiEntry> lines,
        int shopLevel,
        out List<RepairOrderRewardResult> rewards,
        out long totalCost,
        out string error)
    {
        rewards = new List<RepairOrderRewardResult>();
        totalCost = 0;
        error = "repair-orders-shop-error-invalid";
        if (lines == null || lines.Count == 0)
        {
            error = "repair-orders-shop-error-empty";
            return false;
        }

        if (lines.Count > pool.Rewards.Count)
            return false;

        var allowed = new HashSet<string>();
        foreach (var id in pool.Rewards)
            allowed.Add(id.Id);

        var seen = new HashSet<string>();
        foreach (var line in lines)
        {
            if (line == null || string.IsNullOrEmpty(line.RewardPrototypeId) ||
                !seen.Add(line.RewardPrototypeId) ||
                !allowed.Contains(line.RewardPrototypeId) ||
                !_prototype.TryIndex<RepairRewardPrototype>(line.RewardPrototypeId, out var reward) ||
                !_prototype.TryIndex<EntityPrototype>(reward.Entity, out _) ||
                line.Count <= 0 || line.Count > reward.MaxCount || reward.Cost <= 0)
                return false;

            if (reward.MinimumShopLevel > shopLevel)
            {
                error = "repair-orders-shop-error-level";
                return false;
            }

            // Positive int values multiply within long; reject an overflowing cart sum.
            var lineCost = (long) reward.Cost * line.Count;
            if (lineCost > long.MaxValue - totalCost)
                return false;

            totalCost += lineCost;

            rewards.Add(new RepairOrderRewardResult(line.RewardPrototypeId, line.Count));
        }

        return true;
    }
}
