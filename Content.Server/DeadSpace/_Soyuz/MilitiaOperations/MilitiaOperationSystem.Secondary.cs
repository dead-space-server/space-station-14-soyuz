// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Numerics;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Reflect;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.Map;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    private float SecondaryRange(MilitiaOperationActorComponent actor)
        => MathF.Min(13f, CombatVision(actor));

    private bool UsingSecondary(MilitiaOperationActorComponent actor)
        => actor.LaserWeapon.IsValid() && actor.Weapon == actor.LaserWeapon;

    private bool SecondaryInRange(MilitiaOperationActorComponent actor, MapCoordinates origin, MapCoordinates target)
    {
        var range = SecondaryRange(actor);
        return origin.MapId == target.MapId &&
               Vector2.DistanceSquared(origin.Position, target.Position) <= range * range;
    }

    private void UpdateSecondaryWeapon(EntityUid uid, MilitiaOperationActorComponent actor, bool throughBarrier, bool meleeCounter)
    {
        // Reuse the safety scan's target and barrier query. No separate enemy scan or planner is needed.
        if (_timing.CurTime < actor.NextSecondaryCheck || actor.SquadOrder?.Task == MilitiaSquadTask.BreachGirder || !SquadMember(uid) ||
            actor.MedicalRefill != null || actor.Patient.IsValid() || ReceivingTreatment(uid) ||
            actor.ReloadUntil != null || actor.DoorPry != null || BarrierClimbBusy(uid) ||
            meleeCounter || !actor.LaserWeapon.IsValid() || !actor.PrimaryWeapon.IsValid())
            return;
        actor.NextSecondaryCheck = _timing.CurTime + TimeSpan.FromSeconds(0.5);

        var usingLaser = UsingSecondary(actor);
        if (!usingLaser && (!throughBarrier || _timing.CurTime < actor.NextRangedWeaponSwitch))
            return;
        var origin = _transform.GetMapCoordinates(uid);
        var target = actor.DirectTarget;
        var targetPosition = target.IsValid() ? _transform.GetMapCoordinates(target) : default;
        var inRange = target.IsValid() && SecondaryInRange(actor, origin, targetPosition);
        var ammo = new GetAmmoCountEvent();
        if (OwnsItem(uid, actor.LaserWeapon))
            RaiseLocalEvent(actor.LaserWeapon, ref ammo);
        if (ammo.Count == 0)
            actor.SecondaryDepleted = true;
        else if (ammo.Count >= 5)
            actor.SecondaryDepleted = false;
        var laserReady = ammo.Count > 0 && (ammo.NextAmmoEntity != null || ammo.NextAmmoPrototype != null);
        var primaryCanFire = throughBarrier && inRange && laserReady && OwnsItem(uid, actor.PrimaryWeapon) &&
            WeaponReady(actor.PrimaryWeapon) &&
            !ShotPathBlocked(uid, target, origin, targetPosition, GetShotCollision(actor.PrimaryWeapon));
        if (_timing.CurTime < actor.NextRangedWeaponSwitch && throughBarrier && laserReady && inRange &&
            !actor.SecondaryDepleted && !primaryCanFire)
            return;
        var wantLaser = throughBarrier && inRange && laserReady && !actor.SecondaryDepleted && !primaryCanFire &&
            // After full depletion, wait for five shots; a partially used sidearm needs no full recharge.
            (usingLaser || ammo.Count >= 2) &&
            !ShotPathBlocked(uid, target, origin, targetPosition, GetShotCollision(in ammo));

        if (usingLaser == wantLaser)
            return;
        var next = wantLaser ? actor.LaserWeapon : actor.PrimaryWeapon;
        if (!OwnsItem(uid, next))
            return;

        EndGuard(uid);
        // Reserve storage for the primary during equipment generation; use the existing rollback on a failed swap.
        if (EquipOwnedItem(uid, actor, next, wield: true, freeHands: true))
        {
            actor.Weapon = next;
            _urgentSquadOrders.Add(uid);
        }
        actor.NextRangedWeaponSwitch = _timing.CurTime + TimeSpan.FromSeconds(2);
    }

    private bool CanHoldFiringPosition(EntityUid uid, MilitiaOperationActorComponent actor)
        // A sidearm permits covering fire during withdrawal; it must not cancel the escape itself.
        => !UsingSecondary(actor) && CanReturnFire(uid, actor);

    private bool IsEnergyReflector(EntityUid item)
        => TryComp<ReflectComponent>(item, out var reflect) && reflect.InRightPlace && reflect.ReflectingInHands &&
           (reflect.Reflects & ReflectType.Energy) != 0 &&
           (CompOrNull<ItemToggleComponent>(item)?.Activated == true ? reflect.ReflectProb : reflect.ReflectProbNotToggled) > 0;
}
