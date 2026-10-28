// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    public sealed record MagazineReloadState(EntityUid Weapon, EntityUid Magazine, bool? BoltClosed, bool ChamberLocked)
    {
        public TakeAmmoEvent? Taken;
        public bool RestoreAttempted;
        public bool RestoreSucceeded;
    }

    public MagazineReloadState? CaptureMagazineReload(EntityUid weapon, EntityUid magazine)
    {
        if (!TryComp<ChamberMagazineAmmoProviderComponent>(weapon, out var chamber) || GetChamberEntity(weapon) != null ||
            !TryComp<BallisticAmmoProviderComponent>(magazine, out var ammo) ||
            !_slots.TryGetSlot(weapon, ChamberSlot, out var slot))
            return null;
        return new MagazineReloadState(weapon, magazine, chamber.BoltClosed, slot.Locked);
    }

    public bool PrimeMagazineReload(MagazineReloadState state, EntityUid user)
    {
        if (!TryComp<ChamberMagazineAmmoProviderComponent>(state.Weapon, out var chamber) ||
            GetMagazineEntity(state.Weapon) != state.Magazine || GetChamberEntity(state.Weapon) != null)
            return false;
        if (chamber.BoltClosed != null)
            SetBoltClosed(state.Weapon, chamber, false, user);
        var taken = new TakeAmmoEvent(1, new(), Transform(state.Weapon).Coordinates, user);
        state.Taken = taken;
        RaiseLocalEvent(state.Magazine, taken);
        if (taken.Ammo.Count != 1 || taken.Ammo[0].Entity is not { } round ||
            TerminatingOrDeleted(round) || EntityManager.IsQueuedForDeletion(round) || !TryInsertChamber(state.Weapon, round))
            return false;
        if (chamber.BoltClosed != null)
            SetBoltClosed(state.Weapon, chamber, true, user);
        if (TryComp<BallisticAmmoProviderComponent>(state.Magazine, out var ammo))
        {
            UpdateBallisticAppearance((state.Magazine, ammo));
            UpdateMagazineAppearance(state.Weapon, chamber, state.Magazine);
        }
        UpdateAmmoCount(state.Weapon);
        return GetMagazineEntity(state.Weapon) == state.Magazine && GetChamberEntity(state.Weapon) == round && chamber.BoltClosed != false;
    }

    public bool RestoreMagazineReload(MagazineReloadState state, EntityUid user)
    {
        if (state.RestoreAttempted)
            return state.RestoreSucceeded;
        state.RestoreAttempted = true;
        if (!ReloadEntityOwned(user, state.Weapon) || !ReloadEntityOwned(user, state.Magazine) ||
            !TryComp<ChamberMagazineAmmoProviderComponent>(state.Weapon, out var chamber) ||
            !TryComp<BallisticAmmoProviderComponent>(state.Magazine, out var ammo) ||
            !Containers.TryGetContainer(state.Weapon, ChamberSlot, out var chamberContainer))
            return false;
        foreach (var round in chamberContainer.ContainedEntities)
        {
            if (state.Taken == null || !state.Taken.Ammo.Exists(entry => entry.Entity == round))
                return false;
        }
        if (state.Taken is { } taken)
        {
            foreach (var (round, _) in taken.Ammo)
            {
                if (round is not { } entity || TerminatingOrDeleted(entity) || EntityManager.IsQueuedForDeletion(entity) ||
                    TryComp<CartridgeAmmoComponent>(entity, out var cartridge) && cartridge.Spent)
                    continue;
                if (!ReloadEntityOwned(user, entity) || !ReloadEntityOwned(user, state.Magazine))
                    return false;
                if (!ammo.Container.Contains(entity))
                {
                    if (!CanInsertBallistic((state.Magazine, ammo), entity))
                        return false;
                    if (Containers.TryGetContainingContainer(entity, out var current) && !Containers.Remove(entity, current, force: true))
                        return false;
                    if (!ReloadEntityOwned(user, entity) || !ReloadEntityOwned(user, state.Magazine) ||
                        !Containers.Insert(entity, ammo.Container, force: true))
                        return false;
                }
                if (!ammo.Container.Contains(entity))
                    return false;
                if (!ammo.Entities.Contains(entity))
                    ammo.Entities.Add(entity);
            }
        }
        if (!ReloadEntityOwned(user, state.Weapon) || GetChamberEntity(state.Weapon) != null)
            return false;
        chamber.BoltClosed = state.BoltClosed;
        _slots.SetLock(state.Weapon, ChamberSlot, state.ChamberLocked);
        if (state.BoltClosed is { } boltClosed)
            Appearance.SetData(state.Weapon, AmmoVisuals.BoltClosed, boltClosed);
        Dirty(state.Magazine, ammo);
        Dirty(state.Weapon, chamber);
        UpdateBallisticAppearance((state.Magazine, ammo));
        if (GetMagazineEntity(state.Weapon) is { } magazine)
            UpdateMagazineAppearance(state.Weapon, chamber, magazine);
        UpdateAmmoCount(state.Weapon);
        state.Taken = null;
        state.RestoreSucceeded = GetChamberEntity(state.Weapon) == null &&
            _slots.TryGetSlot(state.Weapon, ChamberSlot, out var slot) && slot.Locked == state.ChamberLocked && chamber.BoltClosed == state.BoltClosed;
        return state.RestoreSucceeded;
    }

    private bool ReloadEntityOwned(EntityUid user, EntityUid entity)
    {
        if (TerminatingOrDeleted(entity) || EntityManager.IsQueuedForDeletion(entity))
            return false;
        var transform = Transform(entity);
        return TransformSystem.ContainsEntity(user, (entity, transform)) ||
            !Containers.TryGetContainingContainer(entity, out _) && transform.MapID == Transform(user).MapID &&
            System.Numerics.Vector2.DistanceSquared(TransformSystem.GetWorldPosition(entity), TransformSystem.GetWorldPosition(user)) <= 4f;
    }
}
