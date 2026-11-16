// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Containers;

namespace Content.Shared.DeadSpace._Soyuz.MilitiaOperations;

public sealed class MilitiaRestrictedGearSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MilitiaRestrictedGearComponent, BeingEquippedAttemptEvent>(OnEquip);
        SubscribeLocalEvent<MilitiaRestrictedGearComponent, ShotAttemptedEvent>(OnShoot);
        SubscribeLocalEvent<MilitiaRestrictedGearComponent, AttemptMeleeEvent>(OnMelee);
        SubscribeLocalEvent<MilitiaRestrictedGearComponent, GettingUsedAttemptEvent>(OnUse);
        SubscribeLocalEvent<MilitiaRestrictedGearComponent, ItemToggleActivateAttemptEvent>(OnActivate);
        SubscribeLocalEvent<MilitiaRestrictedGearComponent, ThrowItemAttemptEvent>(OnThrow);
        SubscribeLocalEvent<MilitiaRestrictedGearComponent, ExaminedEvent>(OnExamine);
    }

    private bool Allowed(EntityUid? user, MilitiaRestrictedGearComponent gear)
        => !gear.Surrendered && user is { } uid && HasComp<MilitiaOperativeComponent>(uid);

    private void OnEquip(Entity<MilitiaRestrictedGearComponent> gear, ref BeingEquippedAttemptEvent args)
    {
        if (gear.Comp.Kind is MilitiaContrabandKind.Firearm or MilitiaContrabandKind.Melee ||
            !TryComp<Content.Shared.Clothing.Components.ClothingComponent>(gear, out var clothing) ||
            (args.SlotFlags & clothing.Slots) == 0 || Allowed(args.EquipTarget, gear.Comp))
            return;
        args.Reason = "militia-gear-locked";
        args.Cancel();
    }

    private void OnShoot(Entity<MilitiaRestrictedGearComponent> gear, ref ShotAttemptedEvent args)
    {
        if (!Allowed(args.User, gear.Comp))
            args.Cancel();
    }

    private void OnMelee(Entity<MilitiaRestrictedGearComponent> gear, ref AttemptMeleeEvent args)
    {
        var user = args.User;
        if (!user.IsValid() && _containers.TryGetContainingContainer(gear.Owner, out var container))
            user = container.Owner;
        if (Allowed(user, gear.Comp))
            return;
        args.Cancelled = true;
        args.Message = Loc.GetString("militia-gear-locked");
    }

    private void OnUse(Entity<MilitiaRestrictedGearComponent> gear, ref GettingUsedAttemptEvent args)
    {
        if (!Allowed(args.User, gear.Comp))
            args.Cancel();
    }

    private void OnActivate(Entity<MilitiaRestrictedGearComponent> gear, ref ItemToggleActivateAttemptEvent args)
    {
        if (Allowed(args.User, gear.Comp))
            return;
        args.Cancelled = true;
        args.Popup = Loc.GetString("militia-gear-locked");
    }

    private void OnThrow(Entity<MilitiaRestrictedGearComponent> gear, ref ThrowItemAttemptEvent args)
    {
        if (gear.Comp.Kind is MilitiaContrabandKind.Firearm or MilitiaContrabandKind.Melee && !Allowed(args.User, gear.Comp))
            args.Cancelled = true;
    }

    private void OnExamine(Entity<MilitiaRestrictedGearComponent> gear, ref ExaminedEvent args)
        => args.PushMarkup(Loc.GetString("militia-gear-examine"));
}
