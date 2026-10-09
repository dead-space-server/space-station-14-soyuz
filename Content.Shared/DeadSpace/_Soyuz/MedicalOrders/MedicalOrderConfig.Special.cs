// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.IO;
using System.Linq;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.MedicalOrders;

public sealed partial class MedicalOrderConfigPrototype
{
    [DataField] public int SpecialOfferCount = 6;
    [DataField] public TimeSpan SpecialRefreshInterval = TimeSpan.FromMinutes(20);
    [DataField] public List<MedicalSpecialDifficulty> SpecialDifficulties = new();
    [DataField] public List<MedicalSpecialScenario> SpecialScenarios = new();
    [DataField] public List<string> SpecialSites = new();

    private void ValidateSpecialContracts()
    {
        if (SpecialDifficulties.Count == 0)
            return;

        if (SpecialOfferCount is < 1 or > 32 || SpecialRefreshInterval <= TimeSpan.Zero ||
            SpecialScenarios.Count == 0 || SpecialSites.Count == 0 ||
            SpecialSites.Any(string.IsNullOrWhiteSpace) ||
            SpecialScenarios.Any(s => string.IsNullOrWhiteSpace(s.Title) || string.IsNullOrWhiteSpace(s.Description)) ||
            SpecialDifficulties.Sum(d => (long) d.Weight) > int.MaxValue)
            throw new InvalidDataException($"Medical orders config {ID} has invalid special contract generation.");

        foreach (var difficulty in SpecialDifficulties)
        {
            if (difficulty.Weight <= 0 || difficulty.TimeLimit <= TimeSpan.Zero ||
                difficulty.MinPatients < 1 || difficulty.MaxPatients < difficulty.MinPatients || difficulty.MaxPatients > 32 ||
                difficulty.MinPatientDifficulty < 1 || difficulty.MaxPatientDifficulty < difficulty.MinPatientDifficulty ||
                difficulty.MaxPatientDifficulty > Difficulties.Count ||
                difficulty.MinReagents < 1 || difficulty.MaxReagents < difficulty.MinReagents || difficulty.MaxReagents > 32 ||
                difficulty.MinVolume < 1 || difficulty.MaxVolume < difficulty.MinVolume || difficulty.MaxVolume > 100000 ||
                difficulty.VolumeStep < 1 || difficulty.MinVolume % difficulty.VolumeStep != 0 ||
                difficulty.MaxVolume % difficulty.VolumeStep != 0 || difficulty.PointBonus < 0 ||
                difficulty.ReputationBonus < 0 || difficulty.ReputationCap < difficulty.ReputationBonus ||
                difficulty.ReputationPercent is < 0 or > 100 || difficulty.PenaltyPercent is < 0 or > 100 ||
                difficulty.Reagents.Count == 0 ||
                difficulty.Reagents.Values.Sum(q => (long) q.Minimum) > difficulty.MinReagents ||
                difficulty.Reagents.Values.Sum(q => (long) q.Maximum) < difficulty.MaxReagents)
                throw new InvalidDataException($"Medical orders config {ID} has an invalid special difficulty.");

            foreach (var (tier, quota) in difficulty.Reagents)
            {
                if (quota.Minimum < 0 || quota.Maximum < quota.Minimum || quota.Maximum > difficulty.MaxReagents ||
                    MarketReagents.Count(r => r.Enabled && r.Difficulty == tier) < quota.Maximum)
                    throw new InvalidDataException($"Medical orders config {ID} has an invalid special reagent quota.");
            }
        }
    }
}

[DataDefinition]
public sealed partial class MedicalSpecialDifficulty
{
    [DataField(required: true)] public int Weight;
    [DataField(required: true)] public TimeSpan TimeLimit;
    [DataField(required: true)] public int MinPatients;
    [DataField(required: true)] public int MaxPatients;
    [DataField(required: true)] public int MinPatientDifficulty;
    [DataField(required: true)] public int MaxPatientDifficulty;
    [DataField(required: true)] public int MinReagents;
    [DataField(required: true)] public int MaxReagents;
    [DataField(required: true)] public int MinVolume;
    [DataField(required: true)] public int MaxVolume;
    [DataField] public int VolumeStep = 50;
    [DataField(required: true)] public Dictionary<MedicalReagentDifficulty, MedicalSpecialReagentCount> Reagents = new();
    [DataField(required: true)] public int PointBonus;
    [DataField(required: true)] public int ReputationBonus;
    [DataField] public int ReputationPercent = 20;
    [DataField(required: true)] public int ReputationCap;
    [DataField(required: true)] public int PenaltyPercent;
}

[DataDefinition]
public sealed partial class MedicalSpecialReagentCount
{
    [DataField(required: true)] public int Minimum;
    [DataField(required: true)] public int Maximum;
}

[DataDefinition]
public sealed partial class MedicalSpecialScenario
{
    [DataField(required: true)] public string Title = string.Empty;
    [DataField(required: true)] public string Description = string.Empty;
}
