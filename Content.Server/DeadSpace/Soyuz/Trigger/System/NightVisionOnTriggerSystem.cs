using Content.Server.Soyuz.Trigger.Components;
// using Content.Server.DeadSpace.NightVision;
using Content.Server.DeadSpace.Components.NightVision;
using Content.Shared.Trigger;
using Content.Shared.DeadSpace.NightVision;

namespace Content.Server.DeadSpace.Soyuz.Trigger.System;
public sealed class NightVisionOnTriggerSystem : XOnTriggerSystem<NightVisionOnTriggerComponent>
{
    // [Dependency] private readonly NightVisionSystem _nightVision = default!;

    public override void OnTrigger(Entity<NightVisionOnTriggerComponent> ent, EntityUid target, ref TriggerEvent args)
    {
        if (TryComp<NightVisionComponent>(target, out var nightVision))
        {
            var ev = new ToggleNightVisionActionEvent();
            RaiseLocalEvent(target, ev);
        }
    }
}