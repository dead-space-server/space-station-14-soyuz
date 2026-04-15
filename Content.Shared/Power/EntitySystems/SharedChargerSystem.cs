using Content.Shared.Emp;
using Content.Shared.Power.Components;

namespace Content.Shared.Power.EntitySystems;

public abstract class SharedChargerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChargerComponent, EmpPulseEvent>(OnEmpPulse);
    }

    protected virtual void OnEmpPulse(Entity<ChargerComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        args.Disabled = true;
    }
}
