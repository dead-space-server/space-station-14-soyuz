using Content.Shared.Botany.Components;
using Content.Shared.Botany.Systems;

namespace Content.Shared.EntityEffects.Effects.Botany;

// DS-14 Soyuz start: delegate species selection to the directed graph.
/// <summary>
/// Changes the planted plant's species by replacing the plant entity with a new entity spawned from one
/// of the current plant's <see cref="PlantDataComponent.Mutations"/>.
/// </summary>
/// <inheritdoc cref="EntityEffectSystem{T,TEffect}"/>
public sealed partial class PlantMutateSpeciesChangeEntityEffectSystem : EntityEffectSystem<PlantDataComponent, PlantMutateSpeciesChange>
{
    [Dependency] private readonly PlantMutationSystem _mutation = default!;

    protected override void Effect(Entity<PlantDataComponent> entity, ref EntityEffectEvent<PlantMutateSpeciesChange> args)
    {
        if (entity.Comp.Mutations.Count == 0)
            return;

        _mutation.TrySpeciesChange(entity.Owner);
    }
    // DS-14 Soyuz end
}

/// <inheritdoc cref="EntityEffect"/>
public sealed partial class PlantMutateSpeciesChange : EntityEffectBase<PlantMutateSpeciesChange>;
