// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Server.Gatherable;
using Content.Shared.Destructible;
using Content.Shared.Trigger.Systems;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server.DeadSpace._Soyuz.Mining;

/// <summary>Gibtonite must finish its fuse instead of being removed by direct mining destruction.</summary>
[RegisterComponent]
public sealed partial class GibtoniteComponent : Component;

public sealed class GibtoniteSystem : EntitySystem
{
    [Dependency] private readonly TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GibtoniteComponent, AttackedEvent>(OnAttacked, before: [typeof(GatherableSystem)]);
        SubscribeLocalEvent<GibtoniteComponent, DestructionAttemptEvent>(OnDestructionAttempt);
    }

    private void OnAttacked(Entity<GibtoniteComponent> ent, ref AttackedEvent args)
    {
        // Also covers mining attacks whose structural damage is absorbed by the basalt variant.
        _trigger.ActivateTimerTrigger(ent.Owner, args.User);
    }

    private void OnDestructionAttempt(Entity<GibtoniteComponent> ent, ref DestructionAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        // All gather paths eventually reach this event, including projectiles and resonance fields.
        // The existing timer ignores repeated activation and its explosion removes the entity itself.
        args.Cancel();
        _trigger.ActivateTimerTrigger(ent.Owner);
    }
}
