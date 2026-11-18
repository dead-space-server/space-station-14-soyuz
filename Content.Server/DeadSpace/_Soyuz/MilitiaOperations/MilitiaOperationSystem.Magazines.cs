// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.DeadSpace._Soyuz.MilitiaOperations;
using Content.Shared.Item;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    [Dependency] private readonly EntityWhitelistSystem _magazineWhitelist = default!;

    private static EntProtoId RandomMagazine(MilitiaEquipmentPrototype profile, MilitiaWeaponSet weapon, Random random)
        => profile.MagazineVariants.TryGetValue(weapon.Magazine, out var choices) && choices.Count > 0
            ? choices[random.Next(choices.Count)] : weapon.Magazine;

    private bool CompatibleMagazine(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid item)
    {
        if (TerminatingOrDeleted(item) || EntityManager.IsQueuedForDeletion(item) || !HasComp<BallisticAmmoProviderComponent>(item) ||
            !_slots.TryGetSlot(actor.Weapon, SharedGunSystem.MagazineSlot, out var slot) ||
            !_slots.CanInsert(actor.Weapon, item, uid, slot, swap: true) ||
            !_slots.TryGetSlot(actor.Weapon, SharedGunSystem.ChamberSlot, out var chamber))
            return false;
        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(item, ref ammo);
        if (ammo.Count <= 0)
            return false;
        if (ammo.NextAmmoEntity is { } round)
            return _magazineWhitelist.CheckBoth(round, chamber.Blacklist, chamber.Whitelist);
        return ammo.NextAmmoPrototype is { } id && _prototypes.TryIndex(id, out var prototype) &&
            AmmoPrototypeAllowed(prototype, chamber.Whitelist) &&
            (chamber.Blacklist == null || !AmmoPrototypeAllowed(prototype, chamber.Blacklist));
    }

    private bool AmmoPrototypeAllowed(EntityPrototype prototype, EntityWhitelist? list)
    {
        if (list == null)
            return true;
        if (list.Components != null)
        {
            foreach (var component in list.Components)
            {
                if (prototype.Components.ContainsKey(component))
                {
                    if (!list.RequireAll)
                        return true;
                }
                else if (list.RequireAll)
                    return false;
            }
        }
        if (list.Sizes != null && prototype.TryGetComponent<ItemComponent>(out var item, _squadComponents) && list.Sizes.Contains(item.Size))
            return true;
        if (list.Tags != null)
        {
            if (!prototype.TryGetComponent<TagComponent>(out var tags, _squadComponents))
                return list.RequireAll && list.Tags.Count == 0;
            return list.RequireAll ? _squadTags.HasAllTags(tags, list.Tags) : _squadTags.HasAnyTag(tags, list.Tags);
        }
        return list.RequireAll;
    }

    private EntityUid FindMagazine(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (!OwnsItem(uid, actor.Webbing) || !TryComp<StorageComponent>(actor.Webbing, out var webbing))
            return EntityUid.Invalid;
        foreach (var item in webbing.Container.ContainedEntities)
        {
            if (CompatibleMagazine(uid, actor, item))
                return item;
        }
        if (OwnsItem(uid, actor.Backpack) && TryComp<StorageComponent>(actor.Backpack, out var bag))
        {
            foreach (var item in bag.Container.ContainedEntities)
            {
                if (CompatibleMagazine(uid, actor, item) && _storage.CanInsert(actor.Webbing, item, out _))
                    return item;
            }
        }
        foreach (var item in _hands.EnumerateHeld(uid))
        {
            if (CompatibleMagazine(uid, actor, item) && _storage.CanInsert(actor.Webbing, item, out _))
                return item;
        }
        return EntityUid.Invalid;
    }

    private bool MoveMagazineToWebbing(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid magazine)
    {
        if (MagazineInWebbing(uid, actor, magazine))
            return true;
        if (!OwnsItem(uid, magazine) || !_storage.CanInsert(actor.Webbing, magazine, out _) ||
            CaptureEquipment(uid, magazine) is not { } equipment)
            return false;
        var committed = false;
        try
        {
            committed = _storage.Insert(actor.Webbing, magazine, out _, playSound: false, stackAutomatically: false) &&
                AvailableActor(uid, out _) && MagazineInWebbing(uid, actor, magazine);
            return committed;
        }
        catch (Exception exception)
        {
            _log.Error($"Militia magazine transfer failed for {uid}: {exception}");
            return false;
        }
        finally
        {
            if (!committed)
                RestoreEquipment(uid, equipment.Items, equipment.ActiveHand, equipment.Wielded, activated: equipment.Activated);
        }
    }
}
