// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.Chemistry.Reagent;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.PharmaceuticalResearch;

[Serializable, NetSerializable]
public enum PharmaceuticalUiKey : byte { Key }

[Serializable, NetSerializable]
public enum PharmaceuticalAction : byte
{
    Analyze, Synthesize, Separate, Assay, Register, Reproduce, Dispose, Print, EjectInput, EjectOutput, Cancel,
}

[Flags]
public enum PharmaceuticalRole
{
    None = 0, Active = 1, Solvent = 2, Stabilizer = 4, Buffer = 8,
    Organic = 16, Mineral = 32, Biological = 64, Hazard = 128, Restricted = 256,
}

[Prototype]
public sealed partial class PharmaceuticalProfilePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public List<string> Reagents = new();
    [DataField] public List<string> Groups = new();
    [DataField] public PharmaceuticalRole Roles;
    [DataField] public float Solubility = 0.5f;
    [DataField] public float Acidity;
    [DataField] public float Stability = 0.7f;
    [DataField] public float Toxicity;
}

[ImplicitDataDefinitionForInheritors, Serializable, NetSerializable]
public sealed partial class PharmaceuticalData : ReagentData
{
    public const string PrototypeId = "SoyuzExperimentalMedicine";
    [DataField] public string Formula = string.Empty;
    [DataField] public int Quality;
    [DataField] public string Label = string.Empty;
    [DataField] public float SourceTemperature;
    [DataField] public int SourceQuality;
    [DataField] public bool Registered;
    [DataField] public Color? Tint;

    public override ReagentData Clone() => new PharmaceuticalData
    {
        Formula = Formula, Quality = Quality, Label = Label, SourceTemperature = SourceTemperature, SourceQuality = SourceQuality,
        Registered = Registered, Tint = Tint,
    };
    public override bool Equals(ReagentData? other) => other is PharmaceuticalData data && data.Formula == Formula &&
        data.Quality == Quality && data.SourceQuality == SourceQuality && data.SourceTemperature.Equals(SourceTemperature);
    public override int GetHashCode() => HashCode.Combine(Formula, Quality, SourceQuality, SourceTemperature);
}

[RegisterComponent]
public sealed partial class PharmaceuticalLaboratoryComponent : Component;

[RegisterComponent]
public sealed partial class PharmaceuticalBiomodelComponent : Component;

[Serializable, NetSerializable]
public sealed class PharmaceuticalCommand : BoundUserInterfaceMessage
{
    public PharmaceuticalAction Action;
    public long Revision;
    public string Selection;
    public float Amount;
    public string[] Ingredients;
    public string Name;

    public PharmaceuticalCommand(PharmaceuticalAction action, long revision, string selection = "", float amount = 20,
        string name = "", string[]? ingredients = null)
    {
        Action = action;
        Revision = revision;
        Selection = selection;
        Amount = amount;
        Ingredients = ingredients ?? Array.Empty<string>();
        Name = name;
    }
}

[Serializable, NetSerializable]
public sealed class PharmaceuticalReagentView
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public string Properties = string.Empty;
    public float Amount;
    public bool Analyzed;
    public bool CanUse;
    public bool IsProduct;
    public bool CanPurify;
    public bool Tested;
    public bool Passed;
}

[Serializable, NetSerializable]
public sealed class PharmaceuticalFormulaView
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Ingredients = string.Empty;
    public string Findings = string.Empty;
    public string Author = string.Empty;
    public bool Tested;
    public bool Registered;
    public Color Tint;
    public PharmaceuticalReagentView[] Parts = Array.Empty<PharmaceuticalReagentView>();
}

[Serializable, NetSerializable]
public sealed class PharmaceuticalState : BoundUserInterfaceState
{
    public long Revision;
    public bool Powered;
    public bool Busy;
    public float Progress;
    public string Operation = string.Empty;
    public string Input = string.Empty;
    public string Output = string.Empty;
    public string Status = string.Empty;
    public string Analysis = string.Empty;
    public string[] Journal = Array.Empty<string>();
    public PharmaceuticalReagentView[] Sample = Array.Empty<PharmaceuticalReagentView>();
    public PharmaceuticalReagentView[] Studied = Array.Empty<PharmaceuticalReagentView>();
    public PharmaceuticalFormulaView[] Formulas = Array.Empty<PharmaceuticalFormulaView>();
    public bool CanRegister;
}

[ByRefEvent]
public readonly record struct PharmaceuticalReactionEvent(EntityUid Target, ReactionEntityEvent Reaction);

[ByRefEvent]
public struct ResolveReagentMetabolismEvent(EntityUid body, EntityUid organ, Entity<SolutionComponent> solutionEntity, bool isDead, ReagentId reagent, string group, IReadOnlyList<ReagentQuantity> reagents, Solution solution, ReagentEffectsEntry entry)
{
    public readonly EntityUid Body = body;
    public readonly EntityUid Organ = organ;
    public readonly Entity<SolutionComponent> SolutionEntity = solutionEntity;
    public readonly bool IsDead = isDead;
    public readonly ReagentId Reagent = reagent;
    public readonly string Group = group;
    public readonly IReadOnlyList<ReagentQuantity> Reagents = reagents;
    public readonly Solution Solution = solution;
    public ReagentEffectsEntry Entry = entry;
    public bool Skip = false;
}
