using Robust.Shared.Prototypes;

namespace Content.Shared.Botany.Events;

[ByRefEvent]
public struct PlantAnalyzerAttemptEvent(EntityUid user)
{
    public EntityUid User { get; } = user;
    public EntityUid? Target;
    public EntityUid? PlantData;
    public EntProtoId? PlantProtoId;
    public bool Handled;
}

[ByRefEvent]
public readonly record struct PlantMutationsChangedEvent(EntityUid Plant);

/// <summary>
/// Raised when a plant entity is replaced by a new species entity.
/// </summary>
[ByRefEvent]
public readonly record struct PlantSpeciesChangedEvent(EntityUid OldPlant, EntityUid NewPlant, EntProtoId NewPrototype);
