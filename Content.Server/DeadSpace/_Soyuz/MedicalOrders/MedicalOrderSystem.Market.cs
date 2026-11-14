// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using System.Diagnostics.CodeAnalysis;
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

    private bool RefreshMarketConfiguration(MedicalOrderStationComponent state, MedicalOrderConfigPrototype config)
    {
        if (ReferenceEquals(state.MarketConfiguration, config))
            return false;
        var enabled = config.MarketReagents.Where(r => r.Enabled).Select(r => r.Reagent.Id).ToHashSet();
        var news = state.MarketNews.Where(n => n.Remaining <= FixedPoint2.Zero ||
            n.ExpiresAt <= _timing.CurTime || enabled.Contains(n.Reagent)).ToList();
        state.MarketNews = news;
        state.MarketConfiguration = config;
        return true;
    }

    private bool TryFluctuateMarket(MedicalOrderStationComponent state, MedicalOrderConfigPrototype config)
    {
        if (_timing.CurTime < state.NextMarketFluctuation)
            return false;
        var trends = config.MarketReagents.ToDictionary(r => r.Reagent.Id, r =>
            Math.Clamp(state.MarketTrends.GetValueOrDefault(r.Reagent.Id) * 0.95m +
                _random.Next(-config.MarketPriceStepPercent, config.MarketPriceStepPercent + 1) / 100m, -0.9m, 0.25m));
        state.MarketTrends = trends;
        state.NextMarketFluctuation = _timing.CurTime + config.MarketFluctuationInterval;
        return true;
    }

    private bool TryGetMedicalBeaker(EntityUid terminal,
        [NotNullWhen(true)] out Entity<SolutionComponent>? entity,
        [NotNullWhen(true)] out Solution? solution)
    {
        entity = null;
        solution = null;
        return _itemSlots.GetItemOrNull(terminal, "beakerSlot") is { } beaker &&
            _solutions.TryGetFitsInDispenser(beaker, out entity, out solution) && entity != null && solution != null;
    }

    private decimal GetMarketBasePrice(MedicalOrderConfigPrototype config, MedicalReagentMarketEntry entry)
        => GetMarketBaseValue(config, entry, reputation: false);

    private decimal GetMarketBaseReputation(MedicalOrderConfigPrototype config, MedicalReagentMarketEntry entry)
        => GetMarketBaseValue(config, entry, reputation: true);

    private decimal GetMarketBaseValue(MedicalOrderConfigPrototype config, MedicalReagentMarketEntry entry, bool reputation)
    {
        config.MarketDifficulties.TryGetValue(entry.Difficulty, out var tier);
        var minimum = reputation ? tier?.BaseReputationMin ?? config.MarketBaseReputationMin
            : tier?.BasePriceMin ?? config.MarketBasePriceMin;
        var maximum = reputation ? tier?.BaseReputationMax ?? config.MarketBaseReputationMax
            : tier?.BasePriceMax ?? config.MarketBasePriceMax;
        if (minimum == null || maximum == null)
            return reputation ? entry.ReputationPerUnit : entry.BasePrice;

        var cache = reputation ? _marketBaseReputations : _marketBasePrices;
        if (!cache.TryGetValue(config.ID, out var values))
            cache.Add(config.ID, values = new Dictionary<string, decimal>());
        var minimumCents = (int) decimal.Ceiling(minimum.Value * 100m);
        var maximumCents = (int) decimal.Floor(maximum.Value * 100m);
        var value = values.TryGetValue(entry.Reagent.Id, out var previous)
            ? Math.Clamp(previous, minimumCents / 100m, maximumCents / 100m)
            : _random.Next(minimumCents, maximumCents + 1) / 100m;
        values[entry.Reagent.Id] = value;
        return value;
    }

    /// <summary>
    /// Integrates the declining price over the entire delivery, including the minimum-price tail.
    /// Integrating instead of multiplying by the opening price prevents gains from splitting a delivery.
    /// </summary>
    public static (decimal Points, decimal Reputation) GetMarketReturn(
        MedicalReagentMarketEntry entry, decimal pressure, decimal volume, decimal? basePrice = null,
        decimal? reputationPerUnit = null, decimal trend = 0m)
    {
        pressure = Math.Clamp(pressure, 0m, entry.SaturationVolume);
        volume = Math.Max(0m, volume);
        var declining = Math.Min(volume, entry.SaturationVolume - pressure);
        var weighted = declining * (MarketMultiplier(entry, pressure, trend) + MarketMultiplier(entry, pressure + declining, trend)) / 2m
            + (volume - declining) * 0.1m;
        return (weighted * (basePrice ?? entry.BasePrice), weighted * (reputationPerUnit ?? entry.ReputationPerUnit));
    }

    private static decimal MarketMultiplier(MedicalReagentMarketEntry entry, decimal pressure, decimal trend = 0m)
    {
        var baseline = Math.Clamp(1m + trend, 0.1m, 1.25m);
        return baseline - (baseline - 0.1m) * Math.Clamp(pressure / entry.SaturationVolume, 0m, 1m);
    }

    public static decimal RecoverMarketPressure(MedicalReagentMarketEntry entry,
        decimal pressure, TimeSpan elapsed)
    {
        var minutes = Math.Max(0m, elapsed.Ticks / (decimal) TimeSpan.TicksPerMinute);
        return Math.Max(0m, Math.Min(pressure, entry.SaturationVolume) - minutes * entry.RecoveryPerMinute);
    }

    private decimal GetMarketPressure(MedicalOrderStationComponent state,
        MedicalReagentMarketEntry entry)
    {
        return state.MarketDemand.TryGetValue(entry.Reagent.Id, out var demand)
            ? RecoverMarketPressure(entry, demand.Pressure, _timing.CurTime - demand.UpdatedAt)
            : 0m;
    }

    private MedicalMarketNews? ActiveMarketNews(IEnumerable<MedicalMarketNews> news, string reagent) =>
        news.FirstOrDefault(n => n.Reagent == reagent && n.Remaining > FixedPoint2.Zero && n.ExpiresAt > _timing.CurTime);

    private static (decimal Points, decimal Reputation) MarketQuote(MedicalReagentMarketEntry entry,
        decimal pressure, FixedPoint2 volume, decimal price, decimal reputation, decimal trend, MedicalMarketNews? news)
    {
        var premiumVolume = news == null ? 0m : Math.Min(volume.Value, news.Remaining.Value) / 100m;
        var reward = GetMarketReturn(entry, pressure, volume.Value / 100m - premiumVolume, price, reputation, trend);
        var premium = premiumVolume * MarketMultiplier(entry, pressure, trend) * (1m + (news?.BonusPercent ?? 0) / 100m);
        return (reward.Points + premium * price, reward.Reputation + premium * reputation);
    }

    private TimeSpan NextMarketNewsAt(MedicalOrderConfigPrototype config) => _timing.CurTime +
        TimeSpan.FromSeconds(_random.Next((int) config.MarketNewsMinInterval.TotalSeconds,
            (int) config.MarketNewsMaxInterval.TotalSeconds + 1));

    private bool TryPublishMarketNews(MedicalOrderStationComponent state, MedicalOrderConfigPrototype config)
    {
        if (state.Busy || _timing.CurTime < state.NextMarketNews)
            return false;
        var now = _timing.CurTime;
        var active = state.MarketNews.Where(n => n.Remaining > FixedPoint2.Zero && n.ExpiresAt > now).ToArray();
        var candidates = config.MarketReagents.Where(r => r.Enabled && active.All(n => n.Reagent != r.Reagent.Id)).ToArray();
        var next = NextMarketNewsAt(config);
        if (active.Length >= 3 || candidates.Length == 0)
        {
            state.NextMarketNews = next;
            return true;
        }

        var bodies = state.MarketNewsBodyPool.Where(config.MarketNewsBodies.Contains).Distinct().ToList();
        if (bodies.Count == 0)
            bodies = config.MarketNewsBodies.Distinct().ToList();
        var lastBody = state.MarketNews.LastOrDefault()?.Body;
        var eligible = bodies.Where(b => bodies.Count == 1 || b != lastBody).ToArray();
        var body = eligible[_random.Next(eligible.Length)];
        bodies.Remove(body);
        var reagent = candidates[_random.Next(candidates.Length)].Reagent.Id;
        var volume = FixedPoint2.New(_random.Next(config.MarketNewsMinVolume, config.MarketNewsMaxVolume + 1));
        var news = new MedicalMarketNews(state.NextMarketNewsId, reagent,
            config.MarketNewsHeadlines[_random.Next(config.MarketNewsHeadlines.Count)], body,
            config.MarketNewsSources[_random.Next(config.MarketNewsSources.Count)],
            config.MarketNewsBonuses[_random.Next(config.MarketNewsBonuses.Count)], volume, volume, now,
            now + config.MarketNewsDuration);
        var archive = state.MarketNews.Except(active).TakeLast(12);
        var updated = archive.Concat(active).Append(news).OrderBy(n => n.Id).ToList();
        state.MarketNews = updated;
        state.MarketNewsBodyPool = bodies;
        state.NextMarketNewsId++;
        state.NextMarketNews = next;
        return true;
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
            var trend = state.MarketTrends.GetValueOrDefault(entry.Reagent.Id);
            var multiplier = MarketMultiplier(entry, pressure, trend);
            var news = ActiveMarketNews(state.MarketNews, entry.Reagent.Id);
            var price = basePrice * multiplier * (1m + (news?.BonusPercent ?? 0) / 100m);
            var amount = solution?.GetTotalPrototypeQuantity(entry.Reagent) ?? FixedPoint2.Zero;
            var reward = MarketQuote(entry, pressure, amount, basePrice, baseReputation, trend, news);
            totalPoints += reward.Points;
            totalReputation += reward.Reputation;
            accepted += amount;
            views.Add(new MedicalReagentMarketView(entry.Reagent.Id, (double) price,
                (double) (price * baseReputation / basePrice), amount.Float(), (double) multiplier,
                (double) basePrice, news?.BonusPercent ?? 0, news?.Remaining.Float() ?? 0));
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
            if (!TryGetMedicalBeaker(terminal, out var solutionEntity, out var solution))
            {
                Reject(terminal, actor, "medical-orders-error-no-beaker");
                return;
            }

            var remaining = solution.Clone();
            remaining.Name = solution.Name;
            var demand = new Dictionary<string, MedicalReagentMarketDemand>(state.MarketDemand);
            var news = new List<MedicalMarketNews>(state.MarketNews);
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
                var bulletin = ActiveMarketNews(news, entry.Reagent.Id);
                var newsVolume = bulletin == null ? FixedPoint2.Zero : FixedPoint2.Min(removed, bulletin.Remaining);
                var ordinaryVolume = (removed - newsVolume).Value / 100m;
                var reward = MarketQuote(entry, pressure, removed, GetMarketBasePrice(config, entry),
                    GetMarketBaseReputation(config, entry), state.MarketTrends.GetValueOrDefault(entry.Reagent.Id), bulletin);
                if (bulletin != null)
                    news[news.IndexOf(bulletin)] = bulletin with
                    {
                        Remaining = FixedPoint2.Max(FixedPoint2.Zero, bulletin.Remaining - removed),
                    };
                totalPoints += reward.Points;
                totalReputation += reward.Reputation;
                if (ordinaryVolume > 0m)
                {
                    demand[entry.Reagent.Id] = new MedicalReagentMarketDemand
                    {
                        Pressure = Math.Min(entry.SaturationVolume, pressure + ordinaryVolume),
                        UpdatedAt = _timing.CurTime,
                    };
                }
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
            state.MarketNews = news;
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
