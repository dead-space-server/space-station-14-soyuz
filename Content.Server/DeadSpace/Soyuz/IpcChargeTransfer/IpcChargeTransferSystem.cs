using Content.Shared.DeadSpace.Soyuz.IpcChargeTransfer;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.Soyuz.IpcChargeTransfer;

public sealed class IpcChargeTransferSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedBatterySystem _battery = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IpcChargeTransferComponent, InteractHandEvent>(OnInteractHand);
    }

    private void OnInteractHand(Entity<IpcChargeTransferComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || args.User == ent.Owner)
            return;

        if (!TryComp<IpcChargeTransferComponent>(args.User, out var userTransfer))
            return;

        if (!TryComp<BatteryComponent>(ent, out var donorBattery) ||
            !TryComp<BatteryComponent>(args.User, out var receiverBattery))
            return;

        var now = _timing.CurTime;
        TryClearExpiredLock((args.User, userTransfer), now);
        TryClearExpiredLock(ent, now);

        // DS14-start: block reverse draining to prevent infinite ping-pong loops.
        if (userTransfer.LockedDrainTarget == ent.Owner && now < userTransfer.LockedDrainUntil)
        {
            _popup.PopupEntity(Loc.GetString("ipc-charge-transfer-reverse-locked"), args.User, args.User);
            args.Handled = true;
            return;
        }
        // DS14-end

        var donorCharge = _battery.GetCharge((ent.Owner, donorBattery));
        var receiverCharge = _battery.GetCharge((args.User, receiverBattery));
        var receiverMissing = Math.Max(0f, receiverBattery.MaxCharge - receiverCharge);
        var donorAvailable = Math.Max(0f, donorCharge - ent.Comp.DonorReserve);

        var moved = MathF.Min(ent.Comp.TransferPerUse, MathF.Min(receiverMissing, donorAvailable));
        if (moved <= 0f)
            return;

        _battery.ChangeCharge((ent.Owner, donorBattery), -moved);
        _battery.ChangeCharge((args.User, receiverBattery), moved);

        // Prevent reverse drain B->A for a short period after A->B happened.
        userTransfer.LockedDrainTarget = null;
        ent.Comp.LockedDrainTarget = args.User; // donor can not immediately drain receiver back.
        ent.Comp.LockedDrainUntil = now + ent.Comp.ReciprocalLockTime;
        Dirty(ent);
        Dirty(args.User, userTransfer);

        _popup.PopupEntity(Loc.GetString("ipc-charge-transfer-success"), args.User, args.User);
        args.Handled = true;
    }

    private void TryClearExpiredLock(Entity<IpcChargeTransferComponent> ent, TimeSpan now)
    {
        if (ent.Comp.LockedDrainTarget == null || now < ent.Comp.LockedDrainUntil)
            return;

        ent.Comp.LockedDrainTarget = null;
        ent.Comp.LockedDrainUntil = TimeSpan.Zero;
        Dirty(ent);
    }
}

