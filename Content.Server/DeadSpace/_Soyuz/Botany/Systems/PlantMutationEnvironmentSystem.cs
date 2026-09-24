using Content.Server.Atmos.EntitySystems;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Events;

namespace Content.Server.Botany.Systems;

/// <summary>
/// Supplies atmosphere readings only on the server when a species mutation is attempted.
/// </summary>
public sealed partial class PlantMutationEnvironmentSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlantComponent, PlantMutationEnvironmentEvent>(OnEnvironment);
    }

    private void OnEnvironment(Entity<PlantComponent> ent, ref PlantMutationEnvironmentEvent args)
    {
        args.Atmosphere = _atmosphere.GetContainingMixture(ent.Owner, true, true);
    }
}
