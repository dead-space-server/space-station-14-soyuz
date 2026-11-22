// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Server.Atmos.Components;
using Content.Shared.Armor;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Contraband;
using Content.Shared.DeadSpace._Soyuz.MilitiaOperations;
using Content.Shared.Fluids.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    private void RegisterRestrictedGear(EntityUid item, MilitiaContrabandKind kind)
    {
        var gear = EnsureComp<MilitiaRestrictedGearComponent>(item);
        // Repeated traversal must not issue a second receipt or revive surrendered equipment.
        if (gear.Surrendered || _redeemedContraband.Contains(gear.Serial) ||
            _issuedContraband.TryGetValue(gear.Serial, out var issued) && issued == item)
            return;
        gear.Kind = kind;
        gear.Serial = Guid.NewGuid();
        _issuedContraband.Add(gear.Serial, item);
        if (kind == MilitiaContrabandKind.Hypospray)
            RemComp<SpillableComponent>(item);
        Dirty(item, gear);
    }

    /// <summary>
    /// Called once, on the isolated preparation map, after all map and container fills have spawned.
    /// Never register items merely because a player brings them aboard the operation shuttle.
    /// </summary>
    private void RestrictOperationLoot(EntityUid root, MilitiaOperationConfigPrototype config)
    {
        var pending = new Stack<EntityUid>();
        pending.Push(root);
        while (pending.TryPop(out var item))
        {
            if (TerminatingOrDeleted(item) || !TryComp<TransformComponent>(item, out var xform))
                continue;
            if (MapContrabandKind(item, config) is { } kind)
                RegisterRestrictedGear(item, kind);
            // Transform children include storage contents, gun magazines and attached suit helmets.
            var children = xform.ChildEnumerator;
            while (children.MoveNext(out var child))
                pending.Push(child);
        }
    }

    private MilitiaContrabandKind? MapContrabandKind(EntityUid item, MilitiaOperationConfigPrototype config)
    {
        // Attached helmets deliberately have no ItemComponent, but belong to the surrendered suit.
        if (MetaData(item).EntityPrototype is { } prototype && config.MapContraband.TryGetValue(prototype.ID, out var kind))
            return kind;
        if (!HasComp<ItemComponent>(item) || !HasComp<ContrabandComponent>(item))
            return null;
        if (HasComp<GunComponent>(item))
            return MilitiaContrabandKind.Firearm;
        if (HasComp<MagbootsComponent>(item))
            return MilitiaContrabandKind.Boots;
        if (!HasComp<ArmorComponent>(item) || !TryComp<ClothingComponent>(item, out var clothing))
            return null;
        if ((clothing.Slots & SlotFlags.HEAD) != 0)
            return MilitiaContrabandKind.Helmet;
        // Ordinary armored coats remain usable. Only pressure-protective suits are locked.
        return (clothing.Slots & SlotFlags.OUTERCLOTHING) != 0 && HasComp<PressureProtectionComponent>(item)
            ? MilitiaContrabandKind.Armor : null;
    }
}
