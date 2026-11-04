// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Hands.Systems;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Server.DeadSpace.HardsuitIdentification;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Armor;
using Content.Shared.CombatMode;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DeadSpace._Soyuz.MilitiaOperations;
using Content.Shared.DeadSpace.Abilities.AutoInjectReagent.Components;
using Content.Shared.DoAfter;
using Content.Shared.Medical.Healing;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using Content.Shared.Temperature.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Storage;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.NPC.Systems;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedGunSystem _guns = default!;
    [Dependency] private readonly SharedWieldableSystem _wield = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    [Dependency] private readonly HealingSystem _healing = default!;
    [Dependency] private readonly SharedContainerSystem _equipmentContainers = default!;

    private void OnActorMapInit(Entity<MilitiaOperationActorComponent> actor, ref MapInitEvent args)
    {
        if (actor.Comp.EquipmentReady)
            return;
        actor.Comp.EquipmentSeed = _random.Next();
        try
        {
            RemComp<RespiratorComponent>(actor.Owner);
            RemComp<BarotraumaComponent>(actor.Owner);
            RemComp<MovedByPressureComponent>(actor.Owner);
            if (TryComp<TemperatureDamageComponent>(actor.Owner, out var temperature))
            {
                temperature.ColdDamageThreshold = 0;
                temperature.ParentColdDamageThreshold = 0;
            }
            EquipActor(actor.Owner, new Random(actor.Comp.EquipmentSeed));
            actor.Comp.DefensivePosition = Transform(actor.Owner).Coordinates;
            actor.Comp.StrafeSide = (actor.Comp.EquipmentSeed & 1) == 0 ? 1f : -1f;
            actor.Comp.NextGrenade = _timing.CurTime + TimeSpan.FromSeconds(12 + _random.NextDouble() * 12);
            var items = EntityQueryEnumerator<Content.Shared.Item.ItemComponent, TransformComponent>();
            while (items.MoveNext(out var item, out _, out var transform))
            {
                if (!_transform.ContainsEntity(actor.Owner, (item, transform)))
                    continue;
                EnsureComp<MilitiaOperationEquipmentComponent>(item).Enemy = actor.Owner;
            }
        }
        catch
        {
            foreach (var item in actor.Comp.CreatedEquipment)
                QueueDel(item);
            QueueDel(actor.Owner);
            throw;
        }
    }

    private void EquipActor(EntityUid uid, Random random)
    {
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        if (actor.EquipmentReady)
            return;
        EnsureComp<DoAfterComponent>(uid);
        var profile = _prototypes.Index(actor.Equipment);
        if (profile.Protection.Count == 0 || profile.Weapons.Count == 0 || profile.ReloadTime <= TimeSpan.Zero)
            throw new InvalidOperationException($"Incomplete militia equipment profile {profile.ID}.");
        var equipment = new Dictionary<string, EntProtoId>();
        foreach (var (slot, choices) in profile.Equipment)
        {
            if (choices.Count == 0)
                throw new InvalidOperationException($"Empty militia equipment slot {slot} in {profile.ID}.");
            equipment[slot] = choices[random.Next(choices.Count)];
        }
        var protection = profile.Protection[random.Next(profile.Protection.Count)];
        equipment["head"] = protection.Helmet;
        equipment["outerClothing"] = protection.Suit;
        foreach (var slot in new[] { "head", "outerClothing", "back", "belt", "shoes", "gloves", "mask", "jumpsuit", "id" })
        {
            if (!equipment.ContainsKey(slot))
                throw new InvalidOperationException($"Missing mandatory militia equipment slot {slot} in {profile.ID}.");
        }
        foreach (var (slot, prototype) in equipment)
        {
            var item = Spawn(prototype, Transform(uid).Coordinates);
            actor.CreatedEquipment.Add(item);
            RemComp<HardsuitIdentificationComponent>(item);
            RemComp<AutoInjectReagentClothingOnAllowedStateComponent>(item);
            if (!_inventory.TryEquip(uid, item, slot, silent: true, force: true))
                throw new InvalidOperationException($"Cannot equip militia slot {slot}: {prototype}.");
        }
        foreach (var (slot, expected) in equipment)
        {
            if (!_inventory.TryGetSlotEntity(uid, slot, out var item) || MetaData(item.Value).EntityPrototype?.ID != expected.Id)
                throw new InvalidOperationException($"Cannot equip militia slot {slot}: {expected}.");
        }
        if (!_inventory.TryGetSlotEntity(uid, "back", out var backpack) ||
            !_inventory.TryGetSlotEntity(uid, "belt", out var webbing) || !HasComp<StorageComponent>(webbing.Value) ||
            !_inventory.TryGetSlotEntity(uid, "outerClothing", out var armor) || !HasComp<ArmorComponent>(armor.Value) ||
            !_inventory.TryGetSlotEntity(uid, "head", out var helmet) || !HasComp<ArmorComponent>(helmet.Value))
            throw new InvalidOperationException("Militia actor has no backpack or body armor.");
        actor.Backpack = backpack.Value;
        actor.Webbing = webbing.Value;
        var weapon = profile.Weapons[random.Next(profile.Weapons.Count)];
        if (weapon.MinimumMagazines < 1 || weapon.MaximumMagazines < weapon.MinimumMagazines || weapon.MaximumMagazines > 6)
            throw new InvalidOperationException($"Invalid militia ammunition allowance in {profile.ID}.");
        actor.Weapon = Spawn(weapon.Weapon, Transform(uid).Coordinates);
        actor.CreatedEquipment.Add(actor.Weapon);
        actor.ReloadTime = profile.ReloadTime;
        if (!HasComp<GunComponent>(actor.Weapon) || !_hands.TryPickupAnyHand(uid, actor.Weapon, animate: false) ||
            !_slots.TryGetSlot(actor.Weapon, SharedGunSystem.MagazineSlot, out var magazineSlot))
            throw new InvalidOperationException($"Unsupported militia weapon {weapon.Weapon}.");
        var count = random.Next(weapon.MinimumMagazines, weapon.MaximumMagazines + 1);
        for (var i = 0; i < count; i++)
        {
            var magazine = PackItem(uid, actor, weapon.Magazine, actor.Webbing);
            if (_guns.GetAmmoCount(magazine) <= 0 || !_slots.CanInsert(actor.Weapon, magazine, uid, magazineSlot, swap: true))
                throw new InvalidOperationException($"Incompatible or empty magazine {weapon.Magazine} for {weapon.Weapon}.");
            actor.Magazines.Add(magazine);
        }
        foreach (var id in profile.Supplies)
        {
            var supply = PackItem(uid, actor, id);
            if (HasComp<HealingComponent>(supply) || HasComp<InjectorComponent>(supply))
                actor.Treatments.Add(supply);
            else if (_medicalSolutions.TryGetDrawableSolution(supply, out _, out var medicine) && IsMedicineSolution(medicine))
                actor.MedicineBottles.Add(supply);
            else if (HasComp<Content.Shared.Prying.Components.PryingComponent>(supply))
            {
                actor.PryTool = supply;
                if (!actor.Backup.IsValid() && HasComp<MeleeWeaponComponent>(supply))
                    actor.Backup = supply;
            }
            else if (HasComp<MeleeWeaponComponent>(supply))
                actor.Backup = supply;
        }
        foreach (var id in profile.Grenades)
        {
            var grenade = PackItem(uid, actor, id);
            if (!HasComp<Content.Shared.Trigger.Components.TimerTriggerComponent>(grenade))
                throw new InvalidOperationException($"Militia grenade has no timer: {id}.");
            actor.PersonalGrenades.Add(grenade);
        }
        PrimeWeapon(uid, actor.Weapon);
        if (actor.Treatments.Count == 0 || !WeaponReady(actor.Weapon) || !ArmActor(uid))
            throw new InvalidOperationException($"Militia equipment is not combat ready: {profile.ID}.");
        actor.EquipmentReady = true;
    }

    private EntityUid PackItem(EntityUid uid, MilitiaOperationActorComponent actor, EntProtoId prototype, EntityUid? storage = null)
    {
        var item = Spawn(prototype, Transform(uid).Coordinates);
        actor.CreatedEquipment.Add(item);
        if (!_storage.Insert(storage ?? actor.Backpack, item, out _, playSound: false, stackAutomatically: false) || !OwnsItem(uid, item))
            throw new InvalidOperationException($"Militia storage cannot hold {prototype}.");
        return item;
    }

    private bool OwnsItem(EntityUid uid, EntityUid item)
        => Exists(item) && !TerminatingOrDeleted(item) && !EntityManager.IsQueuedForDeletion(item) &&
           TryComp(item, out TransformComponent? transform) && _transform.ContainsEntity(uid, (item, transform));

    private bool AvailableActor(EntityUid uid, [NotNullWhen(true)] out MilitiaOperationActorComponent? actor)
    {
        actor = null;
        return Exists(uid) && !TerminatingOrDeleted(uid) && TryComp(uid, out actor) && actor.EquipmentReady && actor.EquipmentRollback == null &&
               (!TryComp<MilitiaOperationEnemyComponent>(uid, out var owner) || owner.Operation == Guid.Empty || IsActive(owner.Station, owner.Operation)) &&
               !_mobs.IsIncapacitated(uid) && !HasPlayerMind(uid);
    }

    private bool HasPlayerMind(EntityUid uid)
        => TryComp<MindContainerComponent>(uid, out var mind) && mind.Mind != null;

    private void OnActorDamaged(Entity<MilitiaOperationActorComponent> actor, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || !args.InterruptsDoAfters)
            return;
        actor.Comp.SafeAfter = _timing.CurTime + actor.Comp.TreatmentSafetyDelay;
        actor.Comp.AlertUntil = _timing.CurTime + TimeSpan.FromSeconds(12);
        var medics = EntityQueryEnumerator<MilitiaOperationActorComponent>();
        while (medics.MoveNext(out var uid, out var medic))
        {
            if (SameOperation(uid, actor.Owner) && Transform(uid).MapID == Transform(actor.Owner).MapID &&
                System.Numerics.Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(actor.Owner)) <=
                    medic.SquadRange * medic.SquadRange)
                medic.AlertUntil = actor.Comp.AlertUntil;
            if ((uid == actor.Owner || medic.Patient == actor.Owner) && medic.HealingDoAfter is { } id &&
                _doAfter.GetStatus(uid, id, null) == DoAfterStatus.Running)
                _doAfter.Cancel(uid, id, null);
        }
    }

    private bool SafeForTreatment(EntityUid uid)
    {
        return TryComp<MilitiaOperationActorComponent>(uid, out var actor) && actor.Grenades.Length == 0 &&
               _timing.CurTime >= actor.SafeAfter &&
               _timing.CurTime < actor.ThreatScanValidUntil && !actor.ExposedToThreat;
    }

    public bool ReceivingTreatment(EntityUid uid)
        => !TerminatingOrDeleted(uid) && !HasPlayerMind(uid) && !MetaData(uid).EntityPaused && !_mobs.IsDead(uid) &&
           TryComp<MilitiaOperationActorComponent>(uid, out var patient) &&
           patient.TreatingMedic.IsValid() && SquadMember(patient.TreatingMedic) &&
           TryComp<MilitiaOperationActorComponent>(patient.TreatingMedic, out var medic) && medic.Patient == uid;

    private void InterruptActor(EntityUid uid)
    {
        if (TryComp<HTNComponent>(uid, out var htn))
        {
            _htn.CancelPlanning(htn);
            if (htn.Plan != null)
            {
                _htn.ShutdownTask(htn.Plan.CurrentOperator, htn.Blackboard, HTNOperatorStatus.Failed);
                _htn.ShutdownPlan(htn);
            }
            _htn.Replan(htn);
        }
        EndSquadOrder(uid);
        EndSquadHold(uid);
        _steering.Unregister(uid);
        RemComp<NPCRangedCombatComponent>(uid);
        RemComp<NPCMeleeCombatComponent>(uid);
        if (TryComp<MilitiaOperationActorComponent>(uid, out var actor) && TryComp<GunComponent>(actor.Weapon, out var gun))
            _guns.StopExecutionShooting((actor.Weapon, gun));
        _combat.SetInCombatMode(uid, false);
    }

    private void OnActorShutdown(Entity<MilitiaOperationActorComponent> actor, ref ComponentShutdown args)
    {
        EndMedicalRefill(actor.Owner);
        EndRoutePry(actor.Owner);
        ClearSquadFacing(actor);
        var medic = actor.Comp.TreatingMedic;
        EndTreatment(actor.Owner);
        if (medic.IsValid())
            EndTreatment(medic);
    }

    private void UpdateSupportSafety(bool checkRoute)
    {
        if (checkRoute)
        {
            var pending = new List<EntityUid>();
            var recovery = EntityQueryEnumerator<MilitiaOperationActorComponent>();
            while (recovery.MoveNext(out var uid, out var actor))
            {
                if (actor.EquipmentRollback != null && !MetaData(uid).EntityPaused && !HasPlayerMind(uid) && !_mobs.IsDead(uid))
                    pending.Add(uid);
            }
            foreach (var uid in pending)
            {
                if (TryComp<MilitiaOperationActorComponent>(uid, out var actor))
                    RestorePendingEquipment(uid, actor);
            }
        }
        var query = AllEntityQuery<MilitiaOperationActorComponent>();
        var stop = new List<EntityUid>();
        while (query.MoveNext(out var uid, out var actor))
        {
            if (actor.Patient.IsValid() && (!AvailableActor(uid, out _) || !ValidPatient(uid, actor.Patient, actor) ||
                checkRoute && actor.TreatmentPath is { } path && !TreatmentPathSafe(uid, actor.Patient, path, actor.SquadRouteIndex)) ||
                actor.MedicalRefill != null && (!AvailableActor(uid, out _) || !SafeForTreatment(uid)))
                stop.Add(uid);
        }
        foreach (var uid in stop)
        {
            InterruptActor(uid);
            EndTreatment(uid);
            EndMedicalRefill(uid);
        }
    }

    private bool CanEquipOwnedItem(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid item, bool wield,
        out EntityUid[] stow, bool freeHands = false)
    {
        stow = Array.Empty<EntityUid>();
        if (actor.EquipmentRollback != null || !OwnsItem(uid, item))
            return false;
        var clearHands = freeHands || wield && TryComp<WieldableComponent>(item, out var target) && target.FreeHandsRequired > 0;
        stow = _hands.EnumerateHeld(uid).Where(entity => entity != item && !HasComp<VirtualItemComponent>(entity) &&
            (entity != actor.Weapon || clearHands)).ToArray();
        return stow.All(entity => _storage.CanInsert(actor.Backpack, entity, out _, ignoreStacks: true));
    }

    private bool EquipOwnedItem(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid item, bool wield, bool freeHands = false)
    {
        if (!CanEquipOwnedItem(uid, actor, item, wield, out var stow, freeHands))
            return false;
        if (stow.Length == 0 && _hands.GetActiveItem(uid) == item &&
            (!wield || !TryComp<WieldableComponent>(item, out var ready) || ready.Wielded))
            return true;
        if (CaptureEquipment(uid, item) is not { } snapshot)
            return false;
        var committed = false;
        try
        {
            UnwieldForEquipment(uid);
            foreach (var entity in stow)
            {
                if (!_storage.Insert(actor.Backpack, entity, out _, playSound: false, stackAutomatically: false))
                    return false;
            }
            if (!_hands.IsHolding(uid, item, out var hand) &&
                (!_hands.TryGetEmptyHand(uid, out hand) || !_hands.TryPickup(uid, item, hand, animate: false)))
                return false;
            if (_hands.GetActiveHand(uid) != hand && !_hands.SetActiveHand(uid, hand))
                return false;
            committed = (!wield || !TryComp<WieldableComponent>(item, out var wieldable) || wieldable.Wielded ||
                _wield.TryWield(item, wieldable, uid)) && OwnsItem(uid, item) && _hands.GetActiveItem(uid) == item;
            return committed;
        }
        catch (Exception exception)
        {
            _log.Error($"Militia equipment change failed for {uid}: {exception}");
            return false;
        }
        finally
        {
            if (!committed)
                RestoreEquipment(uid, snapshot.Items, snapshot.ActiveHand, snapshot.Wielded);
        }
    }

    private MilitiaEquipmentRollback? CaptureEquipment(EntityUid uid, params EntityUid[] items)
    {
        var held = _hands.EnumerateHeld(uid).Where(item => !HasComp<VirtualItemComponent>(item)).ToArray();
        var snapshots = new List<(EntityUid Item, BaseContainer Container, ItemStorageLocation? Location)>();
        foreach (var item in held.Concat(items).Distinct())
        {
            if (!_equipmentContainers.TryGetContainingContainer(item, out var container))
                return null;
            ItemStorageLocation? location = _storage.TryGetStorageLocation(item, out _, out _, out var stored) ? stored : null;
            snapshots.Add((item, container, location));
        }
        return new MilitiaEquipmentRollback(snapshots, _hands.GetActiveHand(uid),
            held.Where(item => TryComp<WieldableComponent>(item, out var component) && component.Wielded).ToArray(), null);
    }

    private void RestoreEquipment(EntityUid uid,
        List<(EntityUid Item, BaseContainer Container, ItemStorageLocation? Location)> snapshots,
        string? activeHand, EntityUid[] wielded, SharedGunSystem.MagazineReloadState? ammo = null)
    {
        if (TerminatingOrDeleted(uid) || HasPlayerMind(uid) || !TryComp<MilitiaOperationActorComponent>(uid, out var actor))
            return;
        if (actor.EquipmentRollback == null)
        {
            var rollback = new MilitiaEquipmentRollback(snapshots, activeHand, wielded, ammo);
            foreach (var snapshot in snapshots)
                RememberLooseEquipment(uid, rollback, snapshot.Item);
            actor.EquipmentRollback = rollback;
        }
        RestorePendingEquipment(uid, actor);
    }

    private void RememberLooseEquipment(EntityUid uid, MilitiaEquipmentRollback rollback, EntityUid item)
    {
        if (TerminatingOrDeleted(item) || EntityManager.IsQueuedForDeletion(item) ||
            _equipmentContainers.TryGetContainingContainer(item, out _) || Transform(item).MapID != Transform(uid).MapID ||
            System.Numerics.Vector2.DistanceSquared(_transform.GetWorldPosition(item), _transform.GetWorldPosition(uid)) > 4f)
            return;
        rollback.Loose[item] = Transform(item).Coordinates;
    }

    private bool CanRecoverEquipment(EntityUid uid, MilitiaEquipmentRollback rollback, EntityUid item)
        => OwnsItem(uid, item) || !TerminatingOrDeleted(item) && !EntityManager.IsQueuedForDeletion(item) &&
           !_equipmentContainers.TryGetContainingContainer(item, out _) && rollback.Loose.TryGetValue(item, out var coordinates) &&
           Transform(item).Coordinates == coordinates && Transform(item).MapID == Transform(uid).MapID &&
           System.Numerics.Vector2.DistanceSquared(_transform.GetWorldPosition(item), _transform.GetWorldPosition(uid)) <= 4f;

    private void RestorePendingEquipment(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (actor.EquipmentRollback is not { } rollback || rollback.Restoring || TerminatingOrDeleted(uid))
            return;
        rollback.Restoring = true;
        rollback.Attempts++;
        var restored = false;
        try
        {
            if (!rollback.AmmoHandled)
            {
                rollback.AmmoHandled = true;
                if (rollback.Ammo is { } ammo && OwnsItem(uid, ammo.Weapon) && CanRecoverEquipment(uid, rollback, ammo.Magazine) &&
                    !_guns.RestoreMagazineReload(ammo, uid))
                    _log.Error($"Militia ammunition recovery could not be completed for {uid}.");
            }
            restored = TryRestoreEquipment(uid, rollback);
        }
        catch (Exception exception)
        {
            if (!rollback.Reported)
                _log.Error($"Militia equipment recovery failed for {uid}: {exception}");
        }
        finally
        {
            rollback.Restoring = false;
        }
        if (restored || rollback.Attempts >= 8)
        {
            rollback.Restoring = true;
            try
            {
                if (!restored)
                    _log.Warning($"Militia equipment recovery stopped after external inventory changes for {uid}.");
                foreach (var item in rollback.Loose.Keys.ToArray())
                {
                    if (CanRecoverEquipment(uid, rollback, item) && !_equipmentContainers.TryGetContainingContainer(item, out _))
                        _transform.DropNextTo(item, uid);
                }
            }
            catch (Exception exception) { _log.Error($"Militia equipment recovery cleanup failed for {uid}: {exception}"); }
            finally
            {
                actor.EquipmentRollback = null;
                rollback.Restoring = false;
            }
        }
        else
        {
            if (!rollback.Reported)
                _log.Error($"Militia equipment recovery remains pending for {uid}.");
            rollback.Reported = true;
            EndGuard(uid);
            _steering.Unregister(uid);
        }
    }

    private bool TryRestoreEquipment(EntityUid uid, MilitiaEquipmentRollback rollback)
    {
        for (var i = rollback.Items.Count - 1; i >= 0; i--)
        {
            var snapshot = rollback.Items[i];
            if (CanRecoverEquipment(uid, rollback, snapshot.Item) &&
                (snapshot.Container.Owner == uid || OwnsItem(uid, snapshot.Container.Owner)) &&
                !TerminatingOrDeleted(snapshot.Container.Owner) &&
                _equipmentContainers.TryGetContainer(snapshot.Container.Owner, snapshot.Container.ID, out var original) && original == snapshot.Container)
                continue;
            if (CanRecoverEquipment(uid, rollback, snapshot.Item) && !_equipmentContainers.TryGetContainingContainer(snapshot.Item, out _))
                _transform.DropNextTo(snapshot.Item, uid);
            rollback.Loose.Remove(snapshot.Item);
            rollback.Items.RemoveAt(i);
        }
        UnwieldForEquipment(uid, force: true);
        foreach (var snapshot in rollback.Items)
        {
            if (TerminatingOrDeleted(snapshot.Item) || EntityManager.IsQueuedForDeletion(snapshot.Item))
                continue;
            if (!CanRecoverEquipment(uid, rollback, snapshot.Item))
                return false;
            if (_equipmentContainers.TryGetContainingContainer(snapshot.Item, out var current) && current != snapshot.Container)
            {
                if (!_equipmentContainers.Remove(snapshot.Item, current, force: true))
                    return false;
                RememberLooseEquipment(uid, rollback, snapshot.Item);
            }
        }
        foreach (var snapshot in rollback.Items)
        {
            if (TerminatingOrDeleted(snapshot.Item) || EntityManager.IsQueuedForDeletion(snapshot.Item))
                continue;
            if (!CanRecoverEquipment(uid, rollback, snapshot.Item) ||
                snapshot.Container.Owner != uid && !OwnsItem(uid, snapshot.Container.Owner))
                return false;
            if (!snapshot.Container.Contains(snapshot.Item) && !_equipmentContainers.Insert(snapshot.Item, snapshot.Container, force: true))
                return false;
            rollback.Loose.Remove(snapshot.Item);
            if (snapshot.Location is { } location &&
                (!_storage.TrySetItemStorageLocation(snapshot.Item, snapshot.Container.Owner, location) ||
                 !_storage.TryGetStorageLocation(snapshot.Item, out _, out _, out var actual) || !actual.Equals(location)))
                return false;
            if (!snapshot.Container.Contains(snapshot.Item))
                return false;
        }
        var restoreHand = rollback.ActiveHand == null || _hands.TryGetHand(uid, rollback.ActiveHand, out _);
        if (restoreHand && _hands.GetActiveHand(uid) != rollback.ActiveHand && !_hands.SetActiveHand(uid, rollback.ActiveHand))
            return false;
        foreach (var entity in rollback.Wielded)
        {
            if (TerminatingOrDeleted(entity) || EntityManager.IsQueuedForDeletion(entity))
                continue;
            if (!OwnsItem(uid, entity) || !TryComp<WieldableComponent>(entity, out var component))
                continue;
            if (!_wield.TryWield(entity, component, uid, force: true) || !component.Wielded ||
                _hands.EnumerateHeld(uid).Count(item => TryComp<VirtualItemComponent>(item, out var virtualItem) &&
                    !EntityManager.IsQueuedForDeletion(item) && virtualItem.BlockingEntity == entity) != component.FreeHandsRequired)
                return false;
        }
        foreach (var snapshot in rollback.Items)
        {
            if (TerminatingOrDeleted(snapshot.Item) || EntityManager.IsQueuedForDeletion(snapshot.Item))
                continue;
            if (!snapshot.Container.Contains(snapshot.Item) || snapshot.Location is { } location &&
                (!_storage.TryGetStorageLocation(snapshot.Item, out _, out _, out var actual) || !actual.Equals(location)))
                return false;
        }
        return !restoreHand || _hands.GetActiveHand(uid) == rollback.ActiveHand;
    }

    private void UnwieldForEquipment(EntityUid uid, bool force = false)
    {
        _wield.UnwieldAll(uid, force);
        foreach (var item in _hands.EnumerateHeld(uid).ToArray())
        {
            if (HasComp<VirtualItemComponent>(item) && EntityManager.IsQueuedForDeletion(item) &&
                _equipmentContainers.TryGetContainingContainer(item, out var container))
                _equipmentContainers.Remove(item, container, force: true);
        }
    }

    public bool NeedsWeapon(EntityUid uid)
    {
        if (!AvailableActor(uid, out var actor))
            return false;
        if (actor.MedicalRefill != null || actor.ReloadUntil != null || actor.DoorPry != null || actor.Patient.IsValid())
            return false;
        var weapon = DesiredWeapon(uid, actor);
        return CanEquipOwnedItem(uid, actor, weapon, true, out _) && (_hands.GetActiveItem(uid) != weapon ||
            TryComp<WieldableComponent>(weapon, out var wieldable) && !wieldable.Wielded);
    }

    private EntityUid DesiredWeapon(EntityUid uid, MilitiaOperationActorComponent actor)
        => OwnsItem(uid, actor.Weapon) && (_guns.GetAmmoCount(actor.Weapon) > 0 || FindMagazine(uid, actor).IsValid())
            ? actor.Weapon : OwnsItem(uid, actor.Backup) ? actor.Backup : EntityUid.Invalid;

    public bool ArmActor(EntityUid uid)
        => !HasPlayerMind(uid) && !_mobs.IsIncapacitated(uid) && TryComp<MilitiaOperationActorComponent>(uid, out var actor) &&
           actor.EquipmentRollback == null && actor.MedicalRefill == null &&
           EquipOwnedItem(uid, actor, DesiredWeapon(uid, actor), true);

    private EntityUid FindMagazine(EntityUid uid, MilitiaOperationActorComponent actor)
        => actor.Magazines.FirstOrDefault(item => MagazineInWebbing(uid, actor, item) && _guns.GetAmmoCount(item) > 0);

    private bool MagazineInWebbing(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid item)
        => OwnsItem(uid, actor.Webbing) && OwnsItem(uid, item) &&
           _equipmentContainers.TryGetContainingContainer(item, out var container) && container.Owner == actor.Webbing;

    public bool NeedsReload(EntityUid uid)
        => AvailableActor(uid, out var actor) && actor.MedicalRefill == null && actor.DoorPry == null && actor.Grenades.Length == 0 &&
           CanEquipOwnedItem(uid, actor, actor.Weapon, false, out _) &&
           _guns.GetAmmoCount(actor.Weapon) == 0 && FindMagazine(uid, actor).IsValid();

    public void BeginReload(EntityUid uid)
    {
        if (!AvailableActor(uid, out var actor) || !NeedsReload(uid))
            return;
        if (!EquipOwnedItem(uid, actor, actor.Weapon, false))
            return;
        UnwieldForEquipment(uid);
        actor.ReloadMagazine = FindMagazine(uid, actor);
        actor.ReloadUntil = _timing.CurTime + actor.ReloadTime;
        StartReloadStep(uid, actor);
    }

    private bool WeaponReady(EntityUid weapon)
    {
        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(weapon, ref ammo);
        return ammo.Count > 0 && (ammo.NextAmmoEntity != null || ammo.NextAmmoPrototype != null);
    }

    private void PrimeWeapon(EntityUid uid, EntityUid weapon)
    {
        if (WeaponReady(weapon))
            return;
        if (TryComp<ChamberMagazineAmmoProviderComponent>(weapon, out var chamber) && chamber.BoltClosed != null)
        {
            _guns.SetBoltClosed(weapon, chamber, false, uid);
            _guns.SetBoltClosed(weapon, chamber, true, uid);
        }
        else
            _interaction.UseInHandInteraction(uid, weapon, checkUseDelay: false);
    }

    public HTNOperatorStatus ReloadActor(EntityUid uid)
    {
        if (!AvailableActor(uid, out var actor) || actor.ReloadUntil == null ||
            !MagazineInWebbing(uid, actor, actor.ReloadMagazine) || _hands.GetActiveItem(uid) != actor.Weapon)
            return HTNOperatorStatus.Failed;
        if (_timing.CurTime < actor.ReloadUntil.Value)
            return HTNOperatorStatus.Continuing;
        if (!_blocker.CanInteract(uid, actor.Weapon) || !_blocker.CanUseHeldEntity(uid, actor.Weapon))
            return HTNOperatorStatus.Failed;
        if (_guns.GetAmmoCount(actor.Weapon) > 0)
            return HTNOperatorStatus.Finished;
        if (!_slots.TryGetSlot(actor.Weapon, SharedGunSystem.MagazineSlot, out var slot) ||
            !_slots.CanInsert(actor.Weapon, actor.ReloadMagazine, uid, slot, swap: true))
            return HTNOperatorStatus.Failed;
        var magazine = actor.ReloadMagazine;
        if (!_equipmentContainers.TryGetContainingContainer(magazine, out var source))
            return HTNOperatorStatus.Failed;
        ItemStorageLocation? location = _storage.TryGetStorageLocation(magazine, out _, out _, out var stored) ? stored : null;
        var snapshots = new List<(EntityUid Item, BaseContainer Container, ItemStorageLocation? Location)> { (magazine, source, location) };
        if (slot.Item is { } old)
        {
            if (!_equipmentContainers.TryGetContainingContainer(old, out var original))
                return HTNOperatorStatus.Failed;
            snapshots.Add((old, original, null));
        }
        var activeHand = _hands.GetActiveHand(uid);
        var ammo = _guns.CaptureMagazineReload(actor.Weapon, magazine);
        if (HasComp<ChamberMagazineAmmoProviderComponent>(actor.Weapon) && ammo == null)
            return HTNOperatorStatus.Failed;
        var committed = false;
        try
        {
            if (slot.Item != null && !_slots.TryEject(actor.Weapon, slot, uid, out _))
                return HTNOperatorStatus.Failed;
            if (!_slots.TryInsert(actor.Weapon, slot, magazine, uid) || slot.Item != magazine || !OwnsItem(uid, magazine))
                return HTNOperatorStatus.Failed;
            if (ammo != null && !_guns.PrimeMagazineReload(ammo, uid) || !WeaponReady(actor.Weapon) ||
                !AvailableActor(uid, out _) || !OwnsItem(uid, actor.Webbing) || _hands.GetActiveItem(uid) != actor.Weapon || slot.Item != magazine ||
                !_blocker.CanInteract(uid, actor.Weapon) || !_blocker.CanUseHeldEntity(uid, actor.Weapon))
                return HTNOperatorStatus.Failed;
            committed = true;
            return HTNOperatorStatus.Finished;
        }
        catch (Exception exception)
        {
            _log.Error($"Militia reload failed for {uid}: {exception}");
            return HTNOperatorStatus.Failed;
        }
        finally
        {
            if (!committed)
                RestoreEquipment(uid, snapshots, activeHand, Array.Empty<EntityUid>(), ammo);
        }
    }

    public void EndReload(EntityUid uid)
    {
        if (TryComp<MilitiaOperationActorComponent>(uid, out var actor))
        {
            var moving = actor.ReloadUntil != null;
            actor.ReloadUntil = null;
            actor.ReloadMagazine = EntityUid.Invalid;
            if (moving)
            {
                actor.SquadPosition = default;
                _steering.Unregister(uid);
            }
            ArmActor(uid);
        }
    }
}

[RegisterComponent]
public sealed partial class MilitiaOperationActorComponent : Component
{
    [DataField(required: true)] public ProtoId<MilitiaEquipmentPrototype> Equipment;
    [DataField] public bool Medic;
    [DataField] public float TreatmentThreshold = 45;
    [DataField] public float TreatmentRange = 12;
    [DataField] public TimeSpan TreatmentCooldown = TimeSpan.FromSeconds(4);
    [DataField] public float ThreatRange = 24;
    [DataField] public TimeSpan TreatmentSafetyDelay = TimeSpan.FromSeconds(8);
    [DataField] public MilitiaSquadRole SquadRole;
    [DataField] public float SquadVision = 12f;
    [DataField] public float SquadRange = 24f;
    [DataField] public TimeSpan ContactMemory = TimeSpan.FromSeconds(6);
    [DataField] public float DefensiveRange = 24f;
    public EntityCoordinates DefensivePosition;
    public EntityUid DirectTarget;
    public EntityUid LastDirectTarget;
    public TimeSpan LastDirectAt;
    public EntityUid[] Grenades = Array.Empty<EntityUid>();
    public readonly List<EntityUid> PersonalGrenades = new();
    public TimeSpan NextGrenade;
    public readonly Dictionary<EntityUid, bool> Hostility = new();
    public EntityCoordinates PeekHome;
    public EntityCoordinates PeekEnemyPosition;
    public TimeSpan PeekFireUntil;
    public TimeSpan NextPeek;
    public bool ReturningFromPeek;
    public float StrafeSide = 1f;
    public bool ControlsFacing;
    public TimeSpan AlertUntil;
    public bool TriageRequired;
    public readonly Dictionary<EntityUid, MilitiaSquadContact> Contacts = new();
    public MilitiaSquadContact[] SquadContacts = Array.Empty<MilitiaSquadContact>();
    public readonly Dictionary<EntityCoordinates, TimeSpan> RejectedSquadPositions = new();
    public EntityCoordinates[] SquadRoute = Array.Empty<EntityCoordinates>();
    public EntityUid[] RetreatDoors = Array.Empty<EntityUid>();
    public int SquadRouteIndex;
    public EntityCoordinates RetreatOrigin;
    public bool RetreatCovered;
    public bool ExposedToThreat;
    public TimeSpan ThreatScanValidUntil;
    public TimeSpan NextFireReposition;
    public TimeSpan SquadHoldUntil;
    public MilitiaSquadOrder? SquadOrder;
    public MilitiaSquadOrder? RunningSquadOrder;
    public EntityCoordinates SquadPosition;
    public bool EquipmentReady;
    public MilitiaEquipmentRollback? EquipmentRollback;
    public int EquipmentSeed;
    public readonly HashSet<EntityUid> CreatedEquipment = new();
    public TimeSpan SafeAfter;
    public EntityUid Weapon;
    public EntityUid Backpack;
    public EntityUid Webbing;
    public EntityUid Backup;
    public EntityUid PryTool;
    public DoAfterId? DoorPry;
    public EntityUid Patient;
    public EntityUid TreatingMedic;
    public EntityUid Treatment;
    public MilitiaTreatmentPath? TreatmentPath;
    public ushort? HealingDoAfter;
    public TimeSpan NextTreatment;
    public TimeSpan ReloadTime;
    public TimeSpan? ReloadUntil;
    public EntityUid ReloadMagazine;
    public readonly List<EntityUid> Magazines = new();
    public readonly List<EntityUid> Treatments = new();
    public readonly List<EntityUid> MedicineBottles = new();
    public MilitiaMedicalRefill? MedicalRefill;
    public TimeSpan NextRefill;
}

public sealed record MilitiaEquipmentRollback(
    List<(EntityUid Item, BaseContainer Container, ItemStorageLocation? Location)> Items,
    string? ActiveHand, EntityUid[] Wielded, SharedGunSystem.MagazineReloadState? Ammo)
{
    public bool Restoring;
    public bool Reported;
    public bool AmmoHandled;
    public int Attempts;
    public readonly Dictionary<EntityUid, EntityCoordinates> Loose = new();
}

public enum MilitiaSupportKind : byte { SelectTreatment, Treat, Reload, Arm, Squad, Retreat, Hold, WaitForTreatment, Evade, Guard, ApproachTreatment, RefillMedicine }

public sealed partial class MilitiaSupportOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entities = default!;
    private MilitiaOperationSystem _operations = default!;
    private PathfindingSystem _pathfinding = default!;
    [DataField] public MilitiaSupportKind Kind;
    public const string PatientKey = "MilitiaPatient";
    public const string TreatmentKey = "MilitiaTreatment";
    private const string TreatmentPathKey = "MilitiaTreatmentPath";
    private const string SquadPlanKey = "MilitiaSquadPlan";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _operations = sysManager.GetEntitySystem<MilitiaOperationSystem>();
        _pathfinding = sysManager.GetEntitySystem<PathfindingSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (Kind == MilitiaSupportKind.WaitForTreatment)
            return (_operations.ReceivingTreatment(owner), null);
        if (_operations.ReceivingTreatment(owner))
            return (false, null);
        if (Kind is MilitiaSupportKind.Squad or MilitiaSupportKind.Retreat or MilitiaSupportKind.Evade)
        {
            if (_operations.GetSquadOrder(owner, Kind == MilitiaSupportKind.Retreat, Kind == MilitiaSupportKind.Evade) is not { } order)
                return (false, null);
            if (_operations.RunningSquadPlan(owner, order) is { } running)
                return (true, new Dictionary<string, object> { [SquadPlanKey] = running });
            foreach (var position in order.Positions)
            {
                cancelToken.ThrowIfCancellationRequested();
                if (!_operations.SquadPositionAvailable(owner, position))
                    continue;
                if (!_entities.TryGetComponent(owner, out TransformComponent? transform))
                    return (false, null);
                var path = await _pathfinding.GetPath(owner, transform.Coordinates, position, 0.6f,
                    cancelToken, _pathfinding.GetFlags(blackboard));
                if (path.Result == PathResult.Path && _operations.PrepareSquadPath(owner, order, position, path.Path) is { } plan)
                    return (true, new Dictionary<string, object> { [SquadPlanKey] = plan });
            }
            return (false, null);
        }
        if (Kind == MilitiaSupportKind.SelectTreatment)
        {
            foreach (var (patient, item) in _operations.TreatmentCandidates(owner))
            {
                cancelToken.ThrowIfCancellationRequested();
                var route = new List<PathPoly>();
                if (owner != patient)
                {
                    var path = await _pathfinding.GetPath(owner, patient, SharedInteractionSystem.InteractionRange - 1f,
                        cancelToken, _pathfinding.GetFlags(blackboard));
                    if (path.Result != PathResult.Path)
                        continue;
                    route = path.Path;
                }
                if (_operations.PrepareTreatmentPath(owner, patient, route) is not { } treatmentPath)
                    continue;
                return (true, new Dictionary<string, object>
                {
                    [PatientKey] = patient, [TreatmentKey] = item,
                    [TreatmentPathKey] = treatmentPath,
                });
            }
            return (false, null);
        }
        var valid = Kind switch
        {
            MilitiaSupportKind.RefillMedicine => _operations.NeedsMedicalRefill(owner),
            MilitiaSupportKind.Reload => _operations.NeedsReload(owner),
            MilitiaSupportKind.Arm => _operations.NeedsWeapon(owner),
            MilitiaSupportKind.Hold => _operations.NeedsSquadHold(owner),
            _ => true,
        };
        return (valid, null);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        if (Kind == MilitiaSupportKind.RefillMedicine)
            _operations.BeginMedicalRefill(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
        if (Kind is MilitiaSupportKind.Squad or MilitiaSupportKind.Retreat or MilitiaSupportKind.Evade)
            _operations.BeginSquadOrder(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner),
                blackboard.GetValue<MilitiaSquadPlan>(SquadPlanKey));
        if (Kind == MilitiaSupportKind.Reload)
            _operations.BeginReload(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
        if (Kind == MilitiaSupportKind.Hold)
            _operations.BeginSquadHold(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
        if (Kind == MilitiaSupportKind.ApproachTreatment)
            _operations.BeginTreatmentApproach(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return Kind switch
        {
            MilitiaSupportKind.RefillMedicine => _operations.RefillMedicine(owner),
            MilitiaSupportKind.SelectTreatment => _operations.SelectTreatment(owner,
                blackboard.GetValue<EntityUid>(PatientKey), blackboard.GetValue<EntityUid>(TreatmentKey),
                blackboard.GetValue<MilitiaTreatmentPath>(TreatmentPathKey))
                    ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed,
            MilitiaSupportKind.ApproachTreatment => _operations.ApproachTreatment(owner),
            MilitiaSupportKind.Treat => _operations.TreatActor(owner),
            MilitiaSupportKind.Reload => _operations.ReloadActor(owner),
            MilitiaSupportKind.Squad or MilitiaSupportKind.Retreat or MilitiaSupportKind.Evade => _operations.RunSquadOrder(owner),
            MilitiaSupportKind.Guard => _operations.GuardPosition(owner),
            MilitiaSupportKind.Hold => _operations.HoldSquadPosition(owner),
            MilitiaSupportKind.WaitForTreatment => _operations.ReceivingTreatment(owner)
                ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Finished,
            _ => _operations.ArmActor(owner) ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed,
        };
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (Kind == MilitiaSupportKind.RefillMedicine)
            _operations.EndMedicalRefill(owner);
        if (Kind == MilitiaSupportKind.Treat || Kind == MilitiaSupportKind.SelectTreatment && status == HTNOperatorStatus.Failed)
            _operations.EndTreatment(owner);
        if (Kind == MilitiaSupportKind.Reload)
            _operations.EndReload(owner);
        if (Kind is MilitiaSupportKind.Squad or MilitiaSupportKind.Retreat or MilitiaSupportKind.Evade)
            _operations.EndSquadOrder(owner);
        if (Kind == MilitiaSupportKind.Guard)
            _operations.EndGuard(owner);
        if (Kind == MilitiaSupportKind.Hold)
            _operations.EndSquadHold(owner);
        if (Kind == MilitiaSupportKind.ApproachTreatment)
            _operations.EndTreatmentApproach(owner);
    }

    public override void PlanShutdown(NPCBlackboard blackboard)
    {
        if (Kind == MilitiaSupportKind.RefillMedicine)
            _operations.EndMedicalRefill(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
        if (Kind == MilitiaSupportKind.SelectTreatment)
            _operations.EndTreatment(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
        if (Kind is MilitiaSupportKind.Squad or MilitiaSupportKind.Retreat or MilitiaSupportKind.Evade)
            _operations.EndSquadOrder(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
        if (Kind == MilitiaSupportKind.Guard)
            _operations.EndGuard(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
        if (Kind == MilitiaSupportKind.Hold)
            _operations.EndSquadHold(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
