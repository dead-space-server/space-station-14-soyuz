using Content.Server.Destructible;
using Content.Server.Stack;
using Content.Shared.DoAfter;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DeadSpace.Soyuz.BlueInsulatingTape;
using Content.Shared.DeadSpace.Soyuz.BlueInsulatingTape.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Repairable;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace.Soyuz.BlueInsulatingTape;

public sealed class BlueInsulatingTapeSystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";

    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly DestructibleSystem _destructible = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WindowRepairTapeComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<DamageableComponent, WindowRepairTapeDoAfterEvent>(OnDoAfter);
    }

    private void OnAfterInteract(Entity<WindowRepairTapeComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (!CanRepairWindow(ent, target, out _))
            return;

        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay, new WindowRepairTapeDoAfterEvent(), target, target: target, used: ent)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnWeightlessMove = false,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        _audio.PlayPredicted(ent.Comp.RepairBeginSound, ent, args.User);

        args.Handled = true;
    }

    private void OnDoAfter(Entity<DamageableComponent> target, ref WindowRepairTapeDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used is not { } used)
            return;

        if (!TryComp(used, out WindowRepairTapeComponent? tape))
            return;

        if (!CanRepairWindow((used, tape), target, out var stack) || stack is null)
            return;

        var destroyedAt = _destructible.DestroyedAt(target);
        if (destroyedAt == FixedPoint2.MaxValue || destroyedAt <= FixedPoint2.Zero)
            return;

        var healAmount = -(destroyedAt * tape.RepairFraction);
        if (healAmount >= FixedPoint2.Zero)
            return;

        if (!_stack.TryUse((used, stack), 1))
            return;

        var healed = _damageable.HealEvenly(target.Owner, healAmount, origin: args.User);
        if (healed.Empty)
            return;

        _audio.PlayPredicted(tape.RepairEndSound, target, args.User);
        args.Handled = true;
    }

    private bool CanRepairWindow(Entity<WindowRepairTapeComponent> tape, EntityUid target, out StackComponent? stack)
    {
        if (!TryComp(tape, out stack) || stack.Count <= 0)
            return false;

        if (!HasComp<RepairableComponent>(target) || !_tag.HasTag(target, WindowTag))
            return false;

        return TryComp<DamageableComponent>(target, out var damageable) && damageable.TotalDamage > FixedPoint2.Zero;
    }
}
