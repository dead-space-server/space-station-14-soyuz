// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.DeadSpace._Soyuz.MeteorDefense;
using Content.Shared.Interaction;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server.DeadSpace._Soyuz.MeteorDefense;

public sealed class MeteorDefenseSystem : EntitySystem
{
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedBatterySystem _battery = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    private float _uiElapsed;

    public override void Initialize()
    {
        SubscribeLocalEvent<MeteorInterceptAttemptEvent>(OnIntercept);
        SubscribeLocalEvent<StationGridRemovedEvent>(OnGridRemoved);
        SubscribeLocalEvent<MeteorDefenseBeaconComponent, MeteorDefenseSetEnabledMessage>(OnSetEnabled);
        SubscribeLocalEvent<MeteorDefenseBeaconComponent, MeteorDefenseSetMaxChargeMessage>(OnSetMaxCharge);
        SubscribeLocalEvent<MeteorDefenseBeaconComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<MeteorDefenseBeaconComponent, AnchorStateChangedEvent>(OnAnchorChanged);
    }

    private bool CanUseUi(EntityUid uid, EntityUid actor)
    {
        return !TerminatingOrDeleted(uid) && !TerminatingOrDeleted(actor)
            && HasComp<ActorComponent>(actor)
            && _ui.IsUiOpen(uid, MeteorDefenseUiKey.Key, actor)
            && _blocker.CanInteract(actor, uid)
            && _interaction.InRangeUnobstructed(actor, uid);
    }

    private void OnSetEnabled(EntityUid uid, MeteorDefenseBeaconComponent comp, MeteorDefenseSetEnabledMessage args)
    {
        if (!CanUseUi(uid, args.Actor))
            return;

        if (!TrySetEnabled(uid, args.Enabled, out var error))
            _popup.PopupEntity(Loc.GetString(error!), uid, args.Actor);
    }

    /// <summary>Validate ownership and exclusivity before committing a new enabled station.</summary>
    public bool TrySetEnabled(EntityUid uid, bool enabled, out string? error)
    {
        error = "meteor-defense-unavailable";
        if (TerminatingOrDeleted(uid) || !TryComp<MeteorDefenseBeaconComponent>(uid, out var comp))
            return false;

        EntityUid? newStation = null;
        if (enabled)
        {
            if (!Transform(uid).Anchored || _station.GetOwningStation(uid) is not { } station
                || TerminatingOrDeleted(station) || !HasComp<BatteryComponent>(uid))
                return false;

            var query = EntityQueryEnumerator<MeteorDefenseBeaconComponent>();
            while (query.MoveNext(out var other, out var otherComp))
            {
                if (other == uid || !IsActive(other, otherComp, station))
                    continue;

                error = "meteor-defense-already-active";
                return false;
            }

            newStation = station;
        }

        comp.EnabledStation = newStation;
        error = null;
        UpdateUi(uid, comp);
        return true;
    }

    private void OnSetMaxCharge(EntityUid uid, MeteorDefenseBeaconComponent comp, MeteorDefenseSetMaxChargeMessage args)
    {
        if (!CanUseUi(uid, args.Actor))
            return;

        if (!TrySetMaxCharge(uid, args.MaxCharge))
            _popup.PopupEntity(Loc.GetString("meteor-defense-invalid-capacity"), uid, args.Actor);
    }

    public bool TrySetMaxCharge(EntityUid uid, float value)
    {
        if (TerminatingOrDeleted(uid) || !TryComp<MeteorDefenseBeaconComponent>(uid, out var comp)
            || !TryComp<BatteryComponent>(uid, out var battery)
            || !float.IsFinite(value) || value < 0
            || !float.IsFinite(comp.MaxAllowedCharge) || value > comp.MaxAllowedCharge)
            return false;

        // Same clamp semantics as Energy Seller: lowering capacity discards excess charge; raising it adds none.
        _battery.SetMaxCharge((uid, battery), value);
        UpdateUi(uid, comp);
        return true;
    }

    private bool IsActive(EntityUid uid, MeteorDefenseBeaconComponent comp, EntityUid station)
    {
        return !TerminatingOrDeleted(uid) && !TerminatingOrDeleted(station)
            && comp.EnabledStation == station && Transform(uid).Anchored
            && _station.GetOwningStation(uid) == station;
    }

    private void OnIntercept(ref MeteorInterceptAttemptEvent args)
    {
        if (args.Cancelled || TerminatingOrDeleted(args.Target)
            || _station.GetOwningStation(args.Target) is not { } station || TerminatingOrDeleted(station))
            return;

        var query = EntityQueryEnumerator<MeteorDefenseBeaconComponent, BatteryComponent>();
        while (query.MoveNext(out var uid, out var comp, out var battery))
        {
            if (!IsActive(uid, comp, station))
                continue;

            var cost = comp.EnergyPerIntercept;
            var charge = _battery.GetCharge((uid, battery));
            if (!float.IsFinite(cost) || cost <= 0 || !float.IsFinite(charge) || charge < cost)
                return;

            // All validation is complete. The battery API commits charge synchronously, so the next
            // attempt in this tick sees the remainder. Never partially pay for a failed interception.
            _battery.SetCharge((uid, battery), charge - cost);
            args.Cancelled = true;
            UpdateUi(uid, comp);
            return;
        }
    }

    private void OnUiOpened(EntityUid uid, MeteorDefenseBeaconComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, comp);
    }

    private void OnAnchorChanged(EntityUid uid, MeteorDefenseBeaconComponent comp, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            TrySetEnabled(uid, false, out _);
    }

    private void OnGridRemoved(StationGridRemovedEvent args)
    {
        var query = EntityQueryEnumerator<MeteorDefenseBeaconComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var transform))
        {
            if (comp.EnabledStation == args.Station && transform.GridUid == args.GridId)
                TrySetEnabled(uid, false, out _);
        }
    }

    public override void Update(float frameTime)
    {
        _uiElapsed += frameTime;
        if (_uiElapsed < 1f)
            return;
        _uiElapsed = 0;

        var query = EntityQueryEnumerator<MeteorDefenseBeaconComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            // A transferred grid cannot bring an active reservation to a different station.
            if (comp.EnabledStation is { } station && !IsActive(uid, comp, station))
                TrySetEnabled(uid, false, out _);
            else
                UpdateUi(uid, comp);
        }
    }

    private void UpdateUi(EntityUid uid, MeteorDefenseBeaconComponent comp)
    {
        if (!_ui.HasUi(uid, MeteorDefenseUiKey.Key) || !TryComp<BatteryComponent>(uid, out var battery)
            || !TryComp<PowerNetworkBatteryComponent>(uid, out var network))
            return;

        var charge = _battery.GetCharge((uid, battery));
        var ready = comp.EnergyPerIntercept > 0 && float.IsFinite(comp.EnergyPerIntercept) && float.IsFinite(charge)
            ? (int) Math.Min(int.MaxValue, Math.Floor((double) charge / comp.EnergyPerIntercept)) : 0;
        var enabled = comp.EnabledStation is { } station && IsActive(uid, comp, station);
        _ui.SetUiState(uid, MeteorDefenseUiKey.Key, new MeteorDefenseBoundUserInterfaceState(
            enabled, charge, battery.MaxCharge, comp.MaxAllowedCharge, comp.EnergyPerIntercept,
            network.MaxChargeRate, ready));
    }
}
