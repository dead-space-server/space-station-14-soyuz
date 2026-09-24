using Content.Shared.Atmos;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Botany.Components;

/// <summary>
/// One directed, independently rolled species mutation edge.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class PlantSpeciesMutation
{
    [DataField(required: true)]
    public EntProtoId Target;

    [DataField(required: true)]
    public float Chance;

    [DataField]
    public float Weight = 1f;

    [DataField]
    public PlantSpeciesMutationRequirements Requirements = new();
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class PlantSpeciesMutationRequirements
{
    [DataField] public float? MinPotency;
    [DataField] public float? MaxPotency;
    [DataField] public float? MinGeneticInstability;
    [DataField] public float? MaxGeneticInstability;
    [DataField] public float? MinWater;
    [DataField] public float? MaxWater;
    [DataField] public float? MinNutrients;
    [DataField] public float? MaxNutrients;
    [DataField] public List<PlantMutationReagentRequirement> Reagents = [];
    [DataField] public PlantMutationEnvironmentRequirements? Environment;
    [DataField] public List<string> RequiredTraits = [];
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class PlantMutationReagentRequirement
{
    [DataField(required: true)] public ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype> Id;
    [DataField(required: true)] public float MinAmount;
    [DataField] public float ConsumeAmount;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class PlantMutationEnvironmentRequirements
{
    [DataField] public float? MinTemperature;
    [DataField] public float? MaxTemperature;
    [DataField] public float? MinLight;
    [DataField] public float? MaxLight;
    [DataField] public List<PlantMutationGasRequirement> Gases = [];
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class PlantMutationGasRequirement
{
    [DataField(required: true)] public Gas Id;
    [DataField(required: true)] public float MinAmount;
    [DataField] public float? MaxAmount;
}
