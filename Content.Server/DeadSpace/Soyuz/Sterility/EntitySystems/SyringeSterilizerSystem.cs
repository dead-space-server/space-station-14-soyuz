using System;
using Content.Server.DeadSpace.Soyuz.Sterility.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeadSpace.Soyuz.Sterility;
using Content.Shared.DeadSpace.Soyuz.Sterility.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.Soyuz.Sterility.EntitySystems;

public sealed class SyringeSterilizerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SterilitySystem _sterility = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SyringeSterilizerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SyringeSterilizerComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<SyringeSterilizerComponent, SyringeSterilizerDoAfterEvent>(OnDoAfter);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SyringeSterilizerComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.IsRunning ||
                component.SuccessEndsAt == TimeSpan.Zero ||
                _timing.CurTime < component.SuccessEndsAt)
            {
                continue;
            }

            component.SuccessEndsAt = TimeSpan.Zero;
            SetStatus(uid, SyringeSterilizerStatus.Idle);
            Dirty(uid, component);
        }
    }

    private void OnMapInit(Entity<SyringeSterilizerComponent> ent, ref MapInitEvent args)
    {
        SetStatus(ent.Owner, SyringeSterilizerStatus.Idle);
    }

    private void OnInteractHand(Entity<SyringeSterilizerComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!_itemSlots.TryGetSlot(ent.Owner, ent.Comp.SlotId, out var slot) || slot.Item == null)
        {
            _popup.PopupClient(Loc.GetString("sterility-sterilizer-empty"), ent, args.User);
            return;
        }

        if (ent.Comp.IsRunning)
        {
            _popup.PopupClient(Loc.GetString("sterility-sterilizer-busy"), ent, args.User);
            return;
        }

        ent.Comp.IsRunning = true;
        ent.Comp.SuccessEndsAt = TimeSpan.Zero;
        _itemSlots.SetLock(ent.Owner, slot, true);
        SetStatus(ent.Owner, SyringeSterilizerStatus.Working);

        if (ent.Comp.StartSound != null)
            _audio.PlayPredicted(ent.Comp.StartSound, ent, args.User);

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(ent.Comp.Duration), new SyringeSterilizerDoAfterEvent(), ent, target: ent)
        {
            BreakOnMove = true,
            NeedHand = false
        });

        Dirty(ent);
        args.Handled = true;
    }

    private void OnDoAfter(Entity<SyringeSterilizerComponent> ent, ref SyringeSterilizerDoAfterEvent args)
    {
        if (!_itemSlots.TryGetSlot(ent.Owner, ent.Comp.SlotId, out var slot))
            return;

        if (args.Cancelled || slot.Item == null)
        {
            FinishCycle(ent, slot, null, success: false);
            return;
        }

        SterilizeItem(slot.Item.Value);
        FinishCycle(ent, slot, args.User, success: true);
        args.Handled = true;
    }

    private void SterilizeItem(EntityUid item)
    {
        if (TryComp(item, out SterilityComponent? sterility))
            _sterility.ResetSterility((item, sterility));

        if (!TryComp(item, out InjectorComponent? injector))
            return;

        if (_solutions.TryGetSolution(item, injector.SolutionName, out var solutionEnt, out _))
            _solutions.RemoveAllSolution(solutionEnt.Value);
    }

    private void FinishCycle(
        Entity<SyringeSterilizerComponent> ent,
        ItemSlot slot,
        EntityUid? user,
        bool success)
    {
        ent.Comp.IsRunning = false;
        _itemSlots.SetLock(ent.Owner, slot, false);

        if (success)
        {
            ent.Comp.SuccessEndsAt = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.SuccessDuration);
            SetStatus(ent.Owner, SyringeSterilizerStatus.Success);

            if (ent.Comp.FinishSound != null)
                _audio.PlayPredicted(ent.Comp.FinishSound, ent, user);

            _itemSlots.TryEjectToHands(ent.Owner, slot, user, excludeUserAudio: true);
        }
        else
        {
            ent.Comp.SuccessEndsAt = TimeSpan.Zero;
            SetStatus(ent.Owner, SyringeSterilizerStatus.Idle);
        }

        Dirty(ent);
    }

    private void SetStatus(EntityUid uid, SyringeSterilizerStatus status)
    {
        _appearance.SetData(uid, SyringeSterilizerVisuals.Status, status);
    }
}
