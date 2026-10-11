// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.DeadSpace._Soyuz.MedicalOrders;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;

namespace Content.Server.DeadSpace._Soyuz.MedicalOrders;

public sealed partial class MedicalOrderSystem
{
    private readonly Dictionary<string, Dictionary<string, decimal>> _marketBasePrices = new();
    private readonly Dictionary<string, Dictionary<string, decimal>> _marketBaseReputations = new();

    private decimal GetMarketBasePrice(MedicalOrderConfigPrototype config, MedicalReagentMarketEntry entry)
    {
        if (config.MarketDifficulties.Count == 0 &&
            (config.MarketBasePriceMin == null || config.MarketBasePriceMax == null))
            return entry.BasePrice;

        if (!_marketBasePrices.TryGetValue(config.ID, out var prices))
        {
            prices = new Dictionary<string, decimal>();
            foreach (var reagent in config.MarketReagents)
            {
                config.MarketDifficulties.TryGetValue(reagent.Difficulty, out var tier);
                var minimum = tier?.BasePriceMin ?? config.MarketBasePriceMin!.Value;
                var maximum = tier?.BasePriceMax ?? config.MarketBasePriceMax!.Value;
                var minimumCents = (int) decimal.Ceiling(minimum * 100m);
                var maximumCents = (int) decimal.Floor(maximum * 100m);
                prices.Add(reagent.Reagent.Id, _random.Next(minimumCents, maximumCents + 1) / 100m);
            }
            _marketBasePrices.Add(config.ID, prices);
        }

        return prices[entry.Reagent.Id];
    }

    private decimal GetMarketBaseReputation(MedicalOrderConfigPrototype config, MedicalReagentMarketEntry entry)
    {
        if (config.MarketDifficulties.Count == 0 &&
            (config.MarketBaseReputationMin == null || config.MarketBaseReputationMax == null))
            return entry.ReputationPerUnit;

        if (!_marketBaseReputations.TryGetValue(config.ID, out var reputations))
        {
            reputations = new Dictionary<string, decimal>();
            foreach (var reagent in config.MarketReagents)
            {
                config.MarketDifficulties.TryGetValue(reagent.Difficulty, out var tier);
                var minimum = tier?.BaseReputationMin ?? config.MarketBaseReputationMin!.Value;
                var maximum = tier?.BaseReputationMax ?? config.MarketBaseReputationMax!.Value;
                var minimumCents = (int) decimal.Ceiling(minimum * 100m);
                var maximumCents = (int) decimal.Floor(maximum * 100m);
                reputations.Add(reagent.Reagent.Id, _random.Next(minimumCents, maximumCents + 1) / 100m);
            }
            _marketBaseReputations.Add(config.ID, reputations);
        }

        return reputations[entry.Reagent.Id];
    }

    /// <summary>
    /// Integrates the declining price over the entire delivery, including the minimum-price tail.
    /// Integrating instead of multiplying by the opening price prevents gains from splitting a delivery.
    /// </summary>
    public static (decimal Points, decimal Reputation) GetMarketReturn(
        MedicalReagentMarketEntry entry, decimal pressure, decimal volume, decimal? basePrice = null,
        decimal? reputationPerUnit = null)
    {
        var openingPrice = basePrice ?? entry.BasePrice;
        var remainingDemand = Math.Max(0m, entry.SaturationVolume - pressure);
        var decliningVolume = Math.Min(volume, remainingDemand);
        var points = entry.MinimumPrice * volume + (openingPrice - entry.MinimumPrice) *
            decliningVolume * (remainingDemand - decliningVolume / 2m) / entry.SaturationVolume;
        return (points, points * (reputationPerUnit ?? entry.ReputationPerUnit) / openingPrice);
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
            var basePrice = GetMarketBasePrice(config, entry);
            var baseReputation = GetMarketBaseReputation(config, entry);
            var pressure = GetMarketPressure(state, entry);
            var price = basePrice - (basePrice - entry.MinimumPrice) *
                Math.Min(pressure / entry.SaturationVolume, 1m);
            var amount = solution?.GetTotalPrototypeQuantity(entry.Reagent) ?? FixedPoint2.Zero;
            var reward = GetMarketReturn(entry, pressure, amount.Value / 100m, basePrice, baseReputation);
            totalPoints += reward.Points;
            totalReputation += reward.Reputation;
            accepted += amount;
            views.Add(new MedicalReagentMarketView(entry.Reagent.Id, (double) price,
                (double) (price * baseReputation / basePrice), amount.Float()));
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
                var reward = GetMarketReturn(entry, pressure, volume, GetMarketBasePrice(config, entry),
                    GetMarketBaseReputation(config, entry));
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
