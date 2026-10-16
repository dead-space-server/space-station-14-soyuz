using Content.Server.Hands.Systems;
using Content.Server.Kitchen.Components;
using Content.Server.Power.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Placeable;
using Content.Shared.Interaction;
using Content.Shared.Kitchen;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server.Kitchen.EntitySystems;

/// <summary>
/// Uses the existing electric heater for the cooktop and heats bakeware in the
/// storage compartment below it. A single range entity can therefore host both.
/// </summary>
public sealed class CookingStationSystem : EntitySystem
{
    [Dependency] private readonly TemperatureSystem _temperature = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly CookingRecipeCatalogSystem _catalog = default!;
    [Dependency] private readonly SharedPopupSystem _popups = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CookingStationComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<CookingStationComponent, ItemPlacedEvent>(OnItemPlaced);
        SubscribeLocalEvent<CookingStationComponent, ItemRemovedEvent>(OnItemRemoved);
        SubscribeLocalEvent<CookingStationComponent, EntInsertedIntoContainerMessage>(OnOvenInserted);
        SubscribeLocalEvent<CookingStationComponent, EntRemovedFromContainerMessage>(OnOvenRemoved);
        SubscribeLocalEvent<CookingStationComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CookingStationComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<CookingStationComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<CookingStationComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    private void OnInit(Entity<CookingStationComponent> ent, ref ComponentInit args)
    {
        if (ent.Comp.OvenAppliance != null)
            ent.Comp.OvenStorage = _containers.EnsureContainer<Container>(ent, ent.Comp.OvenContainerId);
    }

    private void OnInteractUsing(Entity<CookingStationComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.OvenStorage == null ||
            !TryComp<CookingVesselComponent>(args.Used, out var vessel) ||
            !_catalog.TryGetMethod(vessel.Method, out var method) ||
            method.Appliance != ent.Comp.OvenAppliance)
            return;

        args.Handled = true;
        if (!_hands.TryDropIntoContainer(args.User, args.Used, ent.Comp.OvenStorage))
            _popups.PopupEntity(Loc.GetString("cooking-oven-full"), ent, args.User);
    }

    private void OnInsertAttempt(Entity<CookingStationComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID != ent.Comp.OvenContainerId || ent.Comp.OvenStorage == null)
            return;

        if (!TryComp<CookingVesselComponent>(args.EntityUid, out var vessel) ||
            !_catalog.TryGetMethod(vessel.Method, out var method) ||
            method.Appliance != ent.Comp.OvenAppliance ||
            (!args.AssumeEmpty && ent.Comp.OvenStorage.Count >= ent.Comp.OvenCapacity))
            args.Cancel();
    }

    private void OnGetVerbs(Entity<CookingStationComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.OvenStorage is not { Count: > 0 } oven)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("cooking-oven-eject"),
            Act = () => _containers.EmptyContainer(oven),
        });
    }

    private void OnMapInit(Entity<CookingStationComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.SelfAppliance is not null && TryComp<CookingVesselComponent>(ent, out var vessel))
            vessel.HeatingAppliance = ent.Comp.SelfAppliance;
    }

    private void OnItemPlaced(Entity<CookingStationComponent> ent, ref ItemPlacedEvent args)
    {
        if (TryComp<CookingVesselComponent>(args.OtherEntity, out var vessel))
            vessel.HeatingAppliance = ent.Comp.TopAppliance;
    }

    private void OnItemRemoved(Entity<CookingStationComponent> ent, ref ItemRemovedEvent args)
    {
        if (TryComp<CookingVesselComponent>(args.OtherEntity, out var vessel) &&
            vessel.HeatingAppliance == ent.Comp.TopAppliance)
            vessel.HeatingAppliance = null;
    }

    private void OnOvenInserted(Entity<CookingStationComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != ent.Comp.OvenContainerId || ent.Comp.OvenStorage == null)
            return;

        if (TryComp<CookingVesselComponent>(args.Entity, out var vessel))
            vessel.HeatingAppliance = ent.Comp.OvenAppliance;
    }

    private void OnOvenRemoved(Entity<CookingStationComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ent.Comp.OvenContainerId || ent.Comp.OvenStorage == null)
            return;

        if (TryComp<CookingVesselComponent>(args.Entity, out var vessel) &&
            vessel.HeatingAppliance == ent.Comp.OvenAppliance)
            vessel.HeatingAppliance = null;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<CookingStationComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out var station, out var power))
        {
            if (!power.Powered || power.PowerReceived < 10f)
                continue;

            var heat = power.PowerReceived * frameTime;
            if (station.SelfAppliance is not null)
                _temperature.ChangeHeat(uid, heat);

            if (station.OvenAppliance is null || station.OvenStorage == null)
                continue;

            foreach (var item in station.OvenStorage.ContainedEntities)
                _temperature.ChangeHeat(item, heat);
        }
    }
}
