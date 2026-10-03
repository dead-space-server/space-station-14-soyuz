// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.DeadSpace._Soyuz.MedicalOrders;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;

namespace Content.Server.DeadSpace._Soyuz.MedicalOrders;

public sealed partial class MedicalOrderSystem
{
    /// <summary>
    /// Integrates the declining price over the entire delivery, including the minimum-price tail.
    /// Integrating instead of multiplying by the opening price prevents gains from splitting a delivery.
    /// </summary>
    public static (decimal Points, decimal Reputation) GetMarketReturn(
        MedicalReagentMarketEntry entry, decimal pressure, decimal volume)
    {
        var remainingDemand = Math.Max(0m, entry.SaturationVolume - pressure);
        var decliningVolume = Math.Min(volume, remainingDemand);
        var points = entry.MinimumPrice * volume + (entry.BasePrice - entry.MinimumPrice) *
            decliningVolume * (remainingDemand - decliningVolume / 2m) / entry.SaturationVolume;
        return (points, points * entry.ReputationPerUnit / entry.BasePrice);
    }

    public static decimal RecoverMarketPressure(MedicalReagentMarketEntry entry,
        decimal pressure, TimeSpan elapsed)
    {
        var minutes = Math.Max(0m, elapsed.Ticks / (decimal) TimeSpan.TicksPerMinute);
        return Math.Max(0m, pressure - minutes * entry.RecoveryPerMinute);
    }

    private decimal GetMarketPressure(MedicalOrderStationComponent state,
        MedicalReagentMarketEntry entry)
    {
        return state.MarketDemand.TryGetValue(entry.Reagent.Id, out var demand)
            ? RecoverMarketPressure(entry, demand.Pressure, _timing.CurTime - demand.UpdatedAt)
            : 0m;
    }

    private MedicalReagentMarketView[] BuildMarketView(EntityUid terminal,
        MedicalOrderStationComponent state, MedicalOrderConfigPrototype config,
        out long points, out long reputation, out float acceptedVolume, out float rejectedVolume)
    {
        Solution? solution = null;
        if (_itemSlots.GetItemOrNull(terminal, "beakerSlot") is { } beaker)
            _solutions.TryGetFitsInDispenser(beaker, out _, out solution);

        var views = new List<MedicalReagentMarketView>();
        decimal totalPoints = state.MarketPointRemainder;
        decimal totalReputation = state.MarketReputationRemainder;
        var accepted = FixedPoint2.Zero;
        foreach (var entry in config.MarketReagents.Where(r => r.Enabled))
        {
            var pressure = GetMarketPressure(state, entry);
            var price = entry.BasePrice - (entry.BasePrice - entry.MinimumPrice) *
                Math.Min(pressure / entry.SaturationVolume, 1m);
            var amount = solution?.GetTotalPrototypeQuantity(entry.Reagent) ?? FixedPoint2.Zero;
            var reward = GetMarketReturn(entry, pressure, amount.Value / 100m);
            totalPoints += reward.Points;
            totalReputation += reward.Reputation;
            accepted += amount;
            views.Add(new MedicalReagentMarketView(entry.Reagent.Id, (double) price,
                (double) (price * entry.ReputationPerUnit / entry.BasePrice), amount.Float()));
        }

        points = (long) Math.Min(decimal.Floor(totalPoints), long.MaxValue - state.Points);
        reputation = (long) Math.Min(decimal.Floor(totalReputation), long.MaxValue - state.Reputation);
        acceptedVolume = accepted.Float();
        rejectedVolume = ((solution?.Volume ?? FixedPoint2.Zero) - accepted).Float();
        return views.ToArray();
    }

    private void TrySellReagents(EntityUid terminal, EntityUid actor,
        MedicalOrderStationComponent state, MedicalOrderConfigPrototype config)
    {
        if (state.Busy)
        {
            Reject(terminal, actor, "medical-orders-error-unavailable");
            return;
        }

        state.Busy = true;
        var committed = false;
        try
        {
            if (_itemSlots.GetItemOrNull(terminal, "beakerSlot") is not { } beaker ||
                !_solutions.TryGetFitsInDispenser(beaker, out var solutionEntity, out var solution) ||
                solutionEntity == null)
            {
                Reject(terminal, actor, "medical-orders-error-no-beaker");
                return;
            }

            var remaining = solution.Clone();
            remaining.Name = solution.Name;
            var demand = new Dictionary<string, MedicalReagentMarketDemand>(state.MarketDemand);
            var sold = false;
            decimal totalPoints = state.MarketPointRemainder;
            decimal totalReputation = state.MarketReputationRemainder;
            foreach (var entry in config.MarketReagents.Where(r => r.Enabled))
            {
                var amount = remaining.GetTotalPrototypeQuantity(entry.Reagent);
                var removed = remaining.RemoveReagent(entry.Reagent.Id, amount, ignoreReagentData: true);
                if (removed <= FixedPoint2.Zero)
                    continue;
                sold = true;
                var pressure = GetMarketPressure(state, entry);
                var volume = removed.Value / 100m;
                var reward = GetMarketReturn(entry, pressure, volume);
                totalPoints += reward.Points;
                totalReputation += reward.Reputation;
                demand[entry.Reagent.Id] = new MedicalReagentMarketDemand
                {
                    Pressure = pressure + volume,
                    UpdatedAt = _timing.CurTime,
                };
            }

            if (!sold)
            {
                Reject(terminal, actor, "medical-orders-market-no-reagents");
                return;
            }
            if (decimal.Floor(totalPoints) > long.MaxValue - state.Points ||
                decimal.Floor(totalReputation) > long.MaxValue - state.Reputation)
            {
                Reject(terminal, actor, "medical-orders-market-balance-full");
                return;
            }

            var points = (long) decimal.Floor(totalPoints);
            var reputation = (long) decimal.Floor(totalReputation);
            solutionEntity.Value.Comp.Solution = remaining;
            state.MarketDemand = demand;
            state.MarketPointRemainder = totalPoints - points;
            state.MarketReputationRemainder = totalReputation - reputation;
            state.Points += points;
            state.Reputation += reputation;
            state.LastMarketPoints = points;
            state.LastMarketReputation = reputation;
            committed = true;

            _solutions.UpdateChemicals(solutionEntity.Value);
            _popup.PopupEntity(Loc.GetString("medical-orders-market-sold",
                ("points", points), ("reputation", reputation)), terminal, actor, PopupType.Small);
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Medical market sale at {terminal} failed (committed: {committed}): {exception}");
            if (!committed)
                Reject(terminal, actor, "medical-orders-error-unavailable");
        }
        finally
        {
            state.Busy = false;
        }
    }
}
