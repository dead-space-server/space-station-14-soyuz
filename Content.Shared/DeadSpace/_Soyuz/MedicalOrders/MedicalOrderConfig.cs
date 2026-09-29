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

    [DataField(required: true)] public int ReagentOfferCount;
    [DataField(required: true)] public int PatientOfferCount;
    [DataField(required: true)] public TimeSpan OfferRefreshInterval;
    [DataField(required: true)] public int MinReagentEntries;
    [DataField(required: true)] public int MaxReagentEntries;
    [DataField(required: true)] public int MinDamageEntries;
    [DataField(required: true)] public int MaxDamageEntries;
    [DataField(required: true)] public int PointsPerDamage;
    [DataField(required: true)] public int ExpiredRewardMultiplierPercent;
    [DataField(required: true)] public int PatientCompletionDamageThreshold;
    [DataField(required: true)] public ProtoId<RandomHumanoidSettingsPrototype> PatientRandomHumanoidSettings;
    [DataField(required: true)] public EntProtoId PatientGown;
    [DataField(required: true)] public EntProtoId DeliveryContainer;
    [DataField(required: true)] public List<MedicalOrderReagent> Reagents = new();
    [DataField(required: true)] public List<MedicalOrderDamage> Damages = new();
    [DataField(required: true)] public List<MedicalOrderDifficulty> Difficulties = new();
    [DataField(required: true)] public List<MedicalOrderShopItem> ReagentShop = new();
    [DataField(required: true)] public List<MedicalOrderShopItem> PatientShop = new();
    [DataField(required: true)] public List<int> ShopLevelThresholds = new();
    [DataField(required: true)] public List<MedicalOrderQualityBand> QualityBands = new();

    void ISerializationHooks.AfterDeserialization()
    {
        if (ReagentOfferCount < 1 || PatientOfferCount < 1 || OfferRefreshInterval <= TimeSpan.Zero ||
            MinReagentEntries < 1 || MaxReagentEntries < MinReagentEntries ||
            MinDamageEntries < 1 || MaxDamageEntries < MinDamageEntries ||
            PointsPerDamage < 1 || ExpiredRewardMultiplierPercent is < 0 or > 100 ||
            PatientCompletionDamageThreshold < 0)
            throw new InvalidDataException($"Medical orders config {ID} has invalid generation limits.");

        if (Reagents.Count(r => r.CanGenerate) < MinReagentEntries ||
            Reagents.GroupBy(r => r.Reagent).Any(g => g.Count() != 1) ||
            Reagents.Any(r => r.Weight <= 0 || r.PointsPerUnit <= 0 || r.MinAmount <= 0 || r.MaxAmount < r.MinAmount) ||
            Reagents.Where(r => r.CanGenerate).Sum(r => (long) r.Weight) > int.MaxValue)
            throw new InvalidDataException($"Medical orders config {ID} has an invalid reagent catalog.");

        if (Damages.Count(d => d.CanGenerate) < MinDamageEntries ||
            Damages.GroupBy(d => d.DamageType).Any(g => g.Count() != 1) ||
            Damages.Any(d => d.Weight <= 0 || d.MinAmount <= 0 || d.MaxAmount < d.MinAmount) ||
            Damages.Where(d => d.CanGenerate).Sum(d => (long) d.Weight) > int.MaxValue)
            throw new InvalidDataException($"Medical orders config {ID} has an invalid damage catalog.");

        if (Difficulties.Count < 3 || Difficulties[0].MinScore != 0 ||
            Difficulties.Any(d => d.MaxScore < d.MinScore || d.TimeLimit <= TimeSpan.Zero || d.BaseReputation < 0))
            throw new InvalidDataException($"Medical orders config {ID} has invalid difficulties.");

        for (var i = 1; i < Difficulties.Count; i++)
        {
            if (Difficulties[i].MinScore != Difficulties[i - 1].MaxScore + 1)
                throw new InvalidDataException($"Medical orders config {ID} has gaps in difficulty scores.");
        }

        var maxReagentScore = Reagents.Where(r => r.CanGenerate)
            .Select(r => (long) r.MaxAmount * r.PointsPerUnit)
            .OrderByDescending(value => value).Take(MaxReagentEntries).Sum();
        var maxPatientScore = Damages.Where(d => d.CanGenerate)
            .Select(d => (long) d.MaxAmount * PointsPerDamage)
            .OrderByDescending(value => value).Take(MaxDamageEntries).Sum();
        if (maxReagentScore > Difficulties[^1].MaxScore || maxPatientScore > Difficulties[^1].MaxScore)
            throw new InvalidDataException($"Medical orders config {ID} has generated scores outside difficulty ranges.");

        if (ShopLevelThresholds.Count == 0 || ShopLevelThresholds[0] != 0 ||
            ReagentShop.Count == 0 || PatientShop.Count == 0)
            throw new InvalidDataException($"Medical orders config {ID} has an empty shop.");

        for (var i = 1; i < ShopLevelThresholds.Count; i++)
        {
            if (ShopLevelThresholds[i] <= ShopLevelThresholds[i - 1])
                throw new InvalidDataException($"Medical orders config {ID} has unordered shop levels.");
        }

        foreach (var shop in new[] { ReagentShop, PatientShop })
        {
            if (shop.GroupBy(i => i.ID).Any(g => g.Count() != 1) ||
                shop.Any(i => i.Cost <= 0 || i.MaxCount <= 0 || i.MinimumShopLevel < 1 ||
                              i.MinimumShopLevel > ShopLevelThresholds.Count))
                throw new InvalidDataException($"Medical orders config {ID} has invalid shop items.");
        }

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
public sealed partial class MedicalOrderReagent
{
    [DataField(required: true)] public ProtoId<ReagentPrototype> Reagent;
    [DataField] public bool CanGenerate = true;
    [DataField(required: true)] public int Weight;
    [DataField(required: true)] public int PointsPerUnit;
    [DataField(required: true)] public int MinAmount;
    [DataField(required: true)] public int MaxAmount;
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
