using Content.Shared.Botany.Components;
using Content.Shared.Botany.Systems;

namespace Content.Shared.EntityEffects.Effects.Botany;

public sealed partial class PlantAdjustGeneticInstabilityEntityEffectSystem : EntityEffectSystem<PlantComponent, PlantAdjustGeneticInstability>
{
    [Dependency] private readonly PlantSystem _plants = default!;
    [Dependency] private readonly PlantHolderSystem _holders = default!;

    protected override void Effect(Entity<PlantComponent> entity, ref EntityEffectEvent<PlantAdjustGeneticInstability> args)
    {
        if (!_holders.IsDead(entity.Owner))
            _plants.AdjustGeneticInstability(entity.AsNullable(), args.Effect.Amount);
    }
}

public sealed partial class PlantAdjustGeneticInstability : EntityEffectBase<PlantAdjustGeneticInstability>
{
    [DataField]
    public float Amount = 10f;
}
