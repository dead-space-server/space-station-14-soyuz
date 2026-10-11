// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.IO;
using System.Linq;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.MedicalOrders;

/// <summary>All gameplay values for the station medical orders economy.</summary>
[Prototype]
public sealed partial class MedicalOrderConfigPrototype : IPrototype, ISerializationHooks
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)] public int PatientOfferCount;
    [DataField(required: true)] public TimeSpan OfferRefreshInterval;
    [DataField(required: true)] public int MinDamageEntries;
    [DataField(required: true)] public int MaxDamageEntries;
    [DataField(required: true)] public int PointsPerDamage;
    [DataField(required: true)] public int ExpiredRewardMultiplierPercent;
    [DataField(required: true)] public int PatientCompletionDamageThreshold;
    [DataField(required: true)] public ProtoId<RandomHumanoidSettingsPrototype> PatientRandomHumanoidSettings;
    [DataField(required: true)] public EntProtoId PatientGown;
    [DataField(required: true)] public EntProtoId DeliveryContainer;
    [DataField] public decimal? MarketBasePriceMin;
    [DataField] public decimal? MarketBasePriceMax;
    [DataField] public decimal? MarketBaseReputationMin;
    [DataField] public decimal? MarketBaseReputationMax;
    [DataField] public Dictionary<MedicalReagentDifficulty, MedicalReagentMarketTier> MarketDifficulties = new();
    [DataField(required: true)] public List<MedicalReagentMarketEntry> MarketReagents = new();
    [DataField(required: true)] public List<MedicalOrderDamage> Damages = new();
    [DataField(required: true)] public List<MedicalOrderDifficulty> Difficulties = new();
    [DataField(required: true)] public List<MedicalOrderShopItem> Shop = new();
    [DataField(required: true)] public List<int> ShopLevelThresholds = new();
    [DataField(required: true)] public List<MedicalOrderQualityBand> QualityBands = new();

    void ISerializationHooks.AfterDeserialization()
    {
        if (PatientOfferCount < 1 || OfferRefreshInterval <= TimeSpan.Zero ||
            MinDamageEntries < 1 || MaxDamageEntries < MinDamageEntries ||
            PointsPerDamage < 1 || ExpiredRewardMultiplierPercent is < 0 or > 100 ||
            PatientCompletionDamageThreshold < 0)
            throw new InvalidDataException($"Medical orders config {ID} has invalid generation limits.");

        if (!MarketReagents.Any(r => r.Enabled) ||
            MarketReagents.GroupBy(r => r.Reagent).Any(g => g.Count() != 1) ||
            MarketReagents.Any(r => r.BasePrice <= 0 || r.BasePrice > 1000000 ||
                r.MinimumPrice <= 0 || r.MinimumPrice > r.BasePrice ||
                r.ReputationPerUnit < 0 || r.ReputationPerUnit > 1000000 ||
                r.SaturationVolume <= 0 || r.SaturationVolume > 1000000 ||
                r.RecoveryPerMinute <= 0 || r.RecoveryPerMinute > 1000000))
            throw new InvalidDataException($"Medical orders config {ID} has an invalid reagent market.");

        if (MarketBasePriceMin != null || MarketBasePriceMax != null)
        {
            if (MarketBasePriceMin is not { } minimum || MarketBasePriceMax is not { } maximum ||
                minimum <= 0 || maximum > 1000000 || minimum > maximum ||
                decimal.Ceiling(minimum * 100m) > decimal.Floor(maximum * 100m) ||
                MarketReagents.Any(r => r.MinimumPrice > minimum))
                throw new InvalidDataException($"Medical orders config {ID} has an invalid market base price range.");
        }

        if (MarketBaseReputationMin != null || MarketBaseReputationMax != null)
        {
            if (MarketBaseReputationMin is not { } minimum || MarketBaseReputationMax is not { } maximum ||
                minimum < 0 || maximum > 1000000 || minimum > maximum ||
                decimal.Ceiling(minimum * 100m) > decimal.Floor(maximum * 100m))
                throw new InvalidDataException($"Medical orders config {ID} has an invalid market base reputation range.");
        }

        foreach (var tier in MarketDifficulties.Values)
        {
            if (tier.BasePriceMin <= 0 || tier.BasePriceMax > 1000000 || tier.BasePriceMin > tier.BasePriceMax ||
                decimal.Ceiling(tier.BasePriceMin * 100m) > decimal.Floor(tier.BasePriceMax * 100m) ||
                tier.BaseReputationMin < 0 || tier.BaseReputationMax > 1000000 ||
                tier.BaseReputationMin > tier.BaseReputationMax ||
                decimal.Ceiling(tier.BaseReputationMin * 100m) > decimal.Floor(tier.BaseReputationMax * 100m))
                throw new InvalidDataException($"Medical orders config {ID} has an invalid market difficulty range.");
        }

        if (MarketDifficulties.Count > 0 && MarketReagents.Any(r =>
                !MarketDifficulties.TryGetValue(r.Difficulty, out var tier) || r.MinimumPrice > tier.BasePriceMin))
            throw new InvalidDataException($"Medical orders config {ID} has a missing or incompatible market difficulty.");

        if (Damages.Count(d => d.CanGenerate) < MinDamageEntries ||
            Damages.GroupBy(d => d.DamageType).Any(g => g.Count() != 1) ||
            Damages.Any(d => d.Weight <= 0 || d.MinAmount <= 0 || d.MaxAmount < d.MinAmount) ||
            Damages.Where(d => d.CanGenerate).Sum(d => (long) d.Weight) > int.MaxValue)
            throw new InvalidDataException($"Medical orders config {ID} has an invalid damage catalog.");

        if (Difficulties.Count < 3 || Difficulties[0].MinScore != 0 ||
            Difficulties.Any(d => d.MaxScore < d.MinScore || d.TimeLimit <= TimeSpan.Zero ||
                d.BaseReputation < 0 || d.Weight <= 0) ||
            Difficulties.Sum(d => (long) d.Weight) > int.MaxValue)
            throw new InvalidDataException($"Medical orders config {ID} has invalid difficulties.");

        for (var i = 1; i < Difficulties.Count; i++)
        {
            if (Difficulties[i].MinScore != Difficulties[i - 1].MaxScore + 1)
                throw new InvalidDataException($"Medical orders config {ID} has gaps in difficulty scores.");
        }

        var maxPatientScore = Damages.Where(d => d.CanGenerate)
            .Select(d => (long) d.MaxAmount * PointsPerDamage)
            .OrderByDescending(value => value).Take(MaxDamageEntries).Sum();
        if (maxPatientScore > Difficulties[^1].MaxScore)
            throw new InvalidDataException($"Medical orders config {ID} has generated scores outside difficulty ranges.");

        if (ShopLevelThresholds.Count == 0 || ShopLevelThresholds[0] != 0 ||
            Shop.Count == 0)
            throw new InvalidDataException($"Medical orders config {ID} has an empty shop.");

        for (var i = 1; i < ShopLevelThresholds.Count; i++)
        {
            if (ShopLevelThresholds[i] <= ShopLevelThresholds[i - 1])
                throw new InvalidDataException($"Medical orders config {ID} has unordered shop levels.");
        }

        if (Shop.GroupBy(i => i.ID).Any(g => g.Count() != 1) ||
            Shop.Any(i => i.Cost <= 0 || i.MaxCount <= 0 || i.MinimumShopLevel < 1 ||
                          i.MinimumShopLevel > ShopLevelThresholds.Count))
            throw new InvalidDataException($"Medical orders config {ID} has invalid shop items.");

        var next = 0;
        foreach (var band in QualityBands)
        {
            if (band.MinPercent != next || band.MaxPercent < next || band.MaxPercent > 100 || band.MultiplierPercent < 0)
                throw new InvalidDataException($"Medical orders config {ID} has invalid quality bands.");
            next = band.MaxPercent + 1;
        }

        if (next != 101)
            throw new InvalidDataException($"Medical orders config {ID} must cover every quality percentage.");
    }
}

[DataDefinition]
public sealed partial class MedicalReagentMarketEntry
{
    [DataField(required: true)] public ProtoId<ReagentPrototype> Reagent;
    [DataField] public bool Enabled = true;
    [DataField] public MedicalReagentDifficulty Difficulty;
    [DataField(required: true)] public decimal BasePrice;
    [DataField(required: true)] public decimal MinimumPrice;
    [DataField(required: true)] public decimal ReputationPerUnit;
    [DataField(required: true)] public decimal SaturationVolume;
    [DataField(required: true)] public decimal RecoveryPerMinute;
}

public enum MedicalReagentDifficulty : byte
{
    Easy,
    Medium,
    Hard,
}

[DataDefinition]
public sealed partial class MedicalReagentMarketTier
{
    [DataField(required: true)] public decimal BasePriceMin;
    [DataField(required: true)] public decimal BasePriceMax;
    [DataField(required: true)] public decimal BaseReputationMin;
    [DataField(required: true)] public decimal BaseReputationMax;
}

[DataDefinition]
public sealed partial class MedicalOrderDamage
{
    [DataField(required: true)] public ProtoId<DamageTypePrototype> DamageType;
    [DataField] public bool CanGenerate = true;
    [DataField(required: true)] public int Weight;
    [DataField(required: true)] public int MinAmount;
    [DataField(required: true)] public int MaxAmount;
}

[DataDefinition]
public sealed partial class MedicalOrderDifficulty
{
    [DataField(required: true)] public int Weight;
    [DataField(required: true)] public int MinScore;
    [DataField(required: true)] public int MaxScore;
    [DataField(required: true)] public TimeSpan TimeLimit;
    [DataField(required: true)] public int BaseReputation;
}

[DataDefinition]
public sealed partial class MedicalOrderShopItem
{
    [DataField("id", required: true)] public string ID = string.Empty;
    [DataField(required: true)] public EntProtoId Entity;
    [DataField(required: true)] public int Cost;
    [DataField(required: true)] public int MaxCount;
    [DataField(required: true)] public int MinimumShopLevel;
    [DataField] public bool Enabled = true;
}

[DataDefinition]
public sealed partial class MedicalOrderQualityBand
{
    [DataField(required: true)] public int MinPercent;
    [DataField(required: true)] public int MaxPercent;
    [DataField(required: true)] public int MultiplierPercent;
}
