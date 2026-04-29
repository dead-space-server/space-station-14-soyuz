// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Server.DeadSpace.IPC.Components;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.DeadSpace.IPC.Events;
using Content.Shared.Inventory;
using Content.Shared.Interaction;
using Content.Shared.Movement.Systems;
using Content.Shared.Ninja.Components;
using Content.Shared.Ninja.Systems;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Power;
using Content.Shared.Popups;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.IPC;

public sealed class IPCSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly SharedBatteryDrainerSystem _batteryDrainer = default!;
    [Dependency] private readonly SharedBatterySystem _battery = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifier = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private const string PortableRechargerProto = "PortableRecharger"; // DS14
    private const string RoboBurgerProto = "FoodBurgerRobot"; // DS14
    private const string MachineOilReagent = "MachineOil"; // DS14
    private readonly Dictionary<EntityUid, EntityUid> _lastIngestedByIpc = new(); // DS14

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IPCComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<IPCComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<IPCComponent, ChangeChargeEvent>(OnBatteryChanged);
        SubscribeLocalEvent<IPCComponent, ToggleDrainActionEvent>(OnToggleAction);
        SubscribeLocalEvent<IPCComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovement);
        SubscribeLocalEvent<IPCComponent, AttemptIngestEvent>(OnAttemptIngest); // DS14
        SubscribeLocalEvent<IPCComponent, BeforeIngestedEvent>(OnBeforeIngested); // DS14
        SubscribeLocalEvent<IPCComponent, IngestingEvent>(OnIngesting); // DS14
        SubscribeLocalEvent<IPCComponent, InteractHandEvent>(OnInteractHand); // DS14
    }

    private void OnAttemptIngest(Entity<IPCComponent> ent, ref AttemptIngestEvent args)
    {
        // DS14-start: IPCs can only ingest roboburgers or pure machine oil.
        _lastIngestedByIpc[ent.Owner] = args.Ingested;

        if (CanIpcIngest(args.Ingested))
            return;

        if (args.Ingest)
        {
            _popup.PopupEntity("Вы не можете переварить это", ent, ent); // DS14
            args.Ingest = false; // DS14: stop before eating/drinking do-after starts.
            args.Handled = true; // DS14
        }
        // DS14-end
    }

    private void OnBeforeIngested(Entity<IPCComponent> ent, ref BeforeIngestedEvent args)
    {
        // DS14-start: hard-stop any non-allowed ingestion at the last safe stage.
        if (!_lastIngestedByIpc.TryGetValue(ent.Owner, out var ingested))
            return;

        if (CanIpcIngest(ingested))
            return;

        args.Cancelled = true;
        // DS14-end
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<IPCComponent, BatteryComponent>();

        while (query.MoveNext(out var uid, out var comp, out var battery))
        {
            // DS14-start: runtime override helper for VV.
            if (comp.SetChargePercent >= 0f)
            {
                var clampedPercent = Math.Clamp(comp.SetChargePercent, 0f, 100f);
                var targetCharge = battery.MaxCharge * (clampedPercent / 100f);
                _battery.SetCharge((uid, battery), targetCharge);
            }
            // DS14-end

            // DS14-start: IPC passive power logic merged from IpcPowerSystem.
            var delta = -(comp.PassiveDrainRate + comp.IdleDrainRate) * frameTime;
            if (_inventory.TryGetSlotEntity(uid, "back", out var backItem) &&
                MetaData(backItem.Value).EntityPrototype?.ID == PortableRechargerProto)
            {
                delta += comp.BackRechargerRate * frameTime;
            }

            if (!MathHelper.CloseTo(delta, 0f))
                _battery.ChangeCharge((uid, battery), delta);
            // DS14-end

            var now = _timing.CurTime;
            if (comp.NextBatteryAlertUpdate > now)
                continue;

            comp.NextBatteryAlertUpdate = now + TimeSpan.FromSeconds(1);
            UpdateBatteryAlert(uid, comp, battery);
        }
    }

    private void OnIngesting(Entity<IPCComponent> ent, ref IngestingEvent args)
    {
        // DS14-start
        _lastIngestedByIpc.Remove(ent.Owner);
        // DS14-end

        // DS14-start: merged from IpcPowerSystem.
        if (MetaData(args.Food).EntityPrototype?.ID != RoboBurgerProto)
            return;

        if (!TryComp<BatteryComponent>(ent, out var battery))
            return;

        var chargeAmount = battery.MaxCharge * 0.5f;
        _battery.ChangeCharge((ent, battery), chargeAmount);
        // DS14-end
    }

    private bool CanIpcIngest(EntityUid ingested)
    {
        // DS14-start: explicit food allow-list.
        if (MetaData(ingested).EntityPrototype?.ID == RoboBurgerProto)
            return true;

        if (!TryComp<EdibleComponent>(ingested, out var edible))
            return false;

        if (!_solutionContainer.TryGetSolution(ingested, edible.Solution, out _, out var solution))
            return false;

        if (solution.Contents.Count == 0)
            return false;

        foreach (var reagent in solution.Contents)
        {
            if (reagent.Reagent.Prototype != MachineOilReagent)
                return false;
        }

        return true;
        // DS14-end
    }

    private void OnInteractHand(Entity<IPCComponent> ent, ref InteractHandEvent args)
    {
        // DS14-start: merged from IpcChargeTransferSystem.
        if (args.Handled || args.User == ent.Owner)
            return;

        if (!TryComp<IPCComponent>(args.User, out var userIpc))
            return;

        if (!TryComp<BatteryComponent>(args.User, out var donorBattery) ||
            !TryComp<BatteryComponent>(ent, out var receiverBattery))
            return;

        var now = _timing.CurTime;
        TryClearExpiredLock((args.User, userIpc), now);
        TryClearExpiredLock(ent, now);

        if (userIpc.LockedDrainTarget == ent.Owner && now < userIpc.LockedDrainUntil)
        {
            _popup.PopupEntity(Loc.GetString("ipc-charge-transfer-reverse-locked"), args.User, args.User);
            args.Handled = true;
            return;
        }

        var donorCharge = _battery.GetCharge((args.User, donorBattery));
        var receiverCharge = _battery.GetCharge((ent.Owner, receiverBattery));
        var receiverMissing = Math.Max(0f, receiverBattery.MaxCharge - receiverCharge);
        var donorAvailable = Math.Max(0f, donorCharge - userIpc.DonorReserve);
        var perUseTransfer = donorBattery.MaxCharge * userIpc.TransferPercentPerUse;

        var moved = MathF.Min(perUseTransfer, MathF.Min(receiverMissing, donorAvailable));
        moved = MathF.Min(moved, userIpc.TransferPerUse);
        if (moved <= 0f)
            return;

        _battery.ChangeCharge((args.User, donorBattery), -moved);
        _battery.ChangeCharge((ent.Owner, receiverBattery), moved);

        ent.Comp.LockedDrainTarget = null;
        userIpc.LockedDrainTarget = ent.Owner;
        userIpc.LockedDrainUntil = now + userIpc.ReciprocalLockTime;
        Dirty(args.User, userIpc);
        Dirty(ent);

        _popup.PopupEntity(Loc.GetString("ipc-charge-transfer-success"), args.User, args.User);
        args.Handled = true;
        // DS14-end
    }

    private void TryClearExpiredLock(Entity<IPCComponent> ent, TimeSpan now)
    {
        if (ent.Comp.LockedDrainTarget == null || now < ent.Comp.LockedDrainUntil)
            return;

        ent.Comp.LockedDrainTarget = null;
        ent.Comp.LockedDrainUntil = TimeSpan.Zero;
        Dirty(ent);
    }

    private void OnComponentInit(EntityUid uid, IPCComponent comp, ComponentInit args)
    {
        if (TryComp<BatteryComponent>(uid, out var battery))
            UpdateBatteryAlert(uid, comp, battery);

        _actions.AddAction(uid, ref comp.ActionEntity, comp.DrainBatteryAction);

        _movementSpeedModifier.RefreshMovementSpeedModifiers(uid);
    }

    private void OnComponentShutdown(EntityUid uid, IPCComponent comp, ComponentShutdown args)
    {
        _lastIngestedByIpc.Remove(uid); // DS14
        _actions.RemoveAction(uid, comp.ActionEntity);
        RemComp<BatteryDrainerComponent>(uid);
    }

    private void OnBatteryChanged(EntityUid uid, IPCComponent comp, ChangeChargeEvent args)
    {
        if (!TryComp<BatteryComponent>(uid, out var battery))
            return;

        UpdateBatteryAlert(uid, comp, battery);
    }

    private void OnToggleAction(EntityUid uid, IPCComponent comp, ToggleDrainActionEvent args)
    {
        if (args.Handled)
            return;

        SetDrainActivated(uid, comp, !comp.DrainActivated);
        args.Handled = true;
    }

    private void SetDrainActivated(EntityUid uid, IPCComponent comp, bool value)
    {
        comp.DrainActivated = value;
        _actions.SetToggled(comp.ActionEntity, value);

        if (value && TryComp<BatteryComponent>(uid, out _))
        {
            EnsureComp<BatteryDrainerComponent>(uid);
            _batteryDrainer.SetBattery(uid, uid);
        }
        else
        {
            RemComp<BatteryDrainerComponent>(uid);
        }
    }

    private void OnRefreshMovement(EntityUid uid, IPCComponent comp, RefreshMovementSpeedModifiersEvent args)
    {
        if (!TryComp<BatteryComponent>(uid, out var battery))
            return;

        var chargePercent = _battery.GetChargeLevel((uid, battery));

        if (chargePercent < comp.BatteryLowThreshold)
            args.ModifySpeed(comp.MovementPenalty);
    }

    private void UpdateBatteryAlert(EntityUid uid, IPCComponent comp, BatteryComponent battery)
    {
        var currentCharge = _battery.GetCharge((uid, battery));
        var chargePercent = _battery.GetChargeLevel((uid, battery));

        short newLevel;
        var maxLevels = IPCComponent.MaxBatteryAlertLevels;

        if (currentCharge <= 0)
            newLevel = 0;
        else
            newLevel = (short)Math.Clamp(MathF.Ceiling(chargePercent * maxLevels), 1, maxLevels);

        if (comp.LastBatteryLevel != newLevel)
        {
            if (newLevel == 0)
            {
                _alerts.ClearAlert(uid, comp.BatteryAlert);
                _alerts.ShowAlert(uid, comp.NoBatteryAlert);
            }
            else
            {
                _alerts.ClearAlert(uid, comp.NoBatteryAlert);
                _alerts.ShowAlert(uid, comp.BatteryAlert, newLevel);
            }

            comp.LastBatteryLevel = newLevel;
            _movementSpeedModifier.RefreshMovementSpeedModifiers(uid);
        }
    }
}
