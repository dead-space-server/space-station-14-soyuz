// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.NPC.HTN;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Lock;
using Content.Shared.Storage.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    [Dependency] private readonly EntityStorageSystem _lockerStorage = default!;
    [Dependency] private readonly LockSystem _lockerLocks = default!;

    private bool TryGetClosedLocker(EntityUid occupant, out EntityUid locker)
    {
        locker = EntityUid.Invalid;
        if (!_equipmentContainers.TryGetContainingContainer(occupant, out var container) ||
            !TryComp<EntityStorageComponent>(container.Owner, out var storage) || storage.Open ||
            storage.Contents != container || !SquadLocker(container.Owner))
            return false;
        locker = container.Owner;
        return true;
    }

    private bool TryLeaveLocker(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (!AvailableActor(uid, out _) || MetaData(uid).EntityPaused || IsRestrained(uid) || !_blocker.CanMove(uid) ||
            !TryGetClosedLocker(uid, out var locker) || !_lockerStorage.TryOpenStorage(uid, locker, silent: true) ||
            _equipmentContainers.IsEntityInContainer(uid))
            return false;
        RequestLockerDetour(uid, actor);
        return true;
    }

    private bool ValidLockerSweep(EntityUid uid, MilitiaSquadOrder order)
        => SquadThreat(uid, order.Target) && TryGetClosedLocker(order.Target, out var locker) && locker == order.Locker;

    private MilitiaSquadOrder? PlanLockerSweep(EntityUid uid, MilitiaOperationActorComponent actor,
        MilitiaSquadContact[] contacts, HashSet<EntityUid> assigned, List<MapCoordinates> occupied)
    {
        if (actor.SquadOrder is { Task: MilitiaSquadTask.SweepLocker } running && running.Until > _timing.CurTime &&
            !assigned.Contains(running.Locker) && ValidLockerSweep(uid, running) &&
            running.Positions.Any(position => SquadPositionAvailable(uid, position)))
            return running;
        if (Transform(uid).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return null;
        var origin = _transform.GetWorldPosition(uid);
        foreach (var contact in contacts)
        {
            if (contact.Expires <= _timing.CurTime || !TryGetClosedLocker(contact.Target, out var locker) || assigned.Contains(locker))
                continue;
            var center = _transform.GetMapCoordinates(locker);
            var lastSeen = _transform.ToMapCoordinates(contact.Position);
            var lockerPosition = _transform.ToCoordinates(grid, center);
            // Only check lockers near the last observed location of a hostile.
            if (center.MapId != lastSeen.MapId || Vector2.DistanceSquared(center.Position, lastSeen.Position) > 2.25f ||
                !OnDefensiveGrid(uid, lockerPosition) || !SquadSightClear(uid, locker, CombatVision(actor)))
                continue;
            var tile = _maps.CoordinatesToTile(grid, gridComp, lockerPosition);
            var spots = new List<EntityCoordinates>(SquadAdjacent.Length);
            foreach (var offset in SquadAdjacent)
            {
                var indices = tile + offset;
                var floor = _maps.GetTileRef(grid, gridComp, indices).Tile;
                if (floor.IsEmpty || _turf.IsSpace(floor) || !TrySquadTilePosition(uid, grid, gridComp, indices, out var spot) ||
                    !SquadPositionAvailable(uid, spot))
                    continue;
                var position = _transform.ToMapCoordinates(spot);
                if (Vector2.DistanceSquared(position.Position, center.Position) <= 2.25f &&
                    !occupied.Any(other => other.MapId == position.MapId && Vector2.DistanceSquared(other.Position, position.Position) < 0.81f) &&
                    SquadLineClear(position, center, ignored: entity => entity == uid || entity == locker))
                    spots.Add(spot);
            }
            if (spots.Count > 0)
                return new MilitiaSquadOrder(contact.Target,
                    spots.OrderBy(spot => Vector2.DistanceSquared(origin, _transform.ToMapCoordinates(spot).Position)).ToArray(),
                    MilitiaSquadTask.SweepLocker, _timing.CurTime + TimeSpan.FromSeconds(8), contacts) { Locker = locker };
        }
        return null;
    }

    private HTNOperatorStatus? TrySweepLocker(EntityUid uid, MilitiaOperationActorComponent actor, MilitiaSquadOrder order)
    {
        var locker = order.Locker;
        if (!_interaction.InRangeUnobstructed(uid, locker, 1.5f))
            return null;
        if (!_blocker.CanInteract(uid, locker))
            return HTNOperatorStatus.Failed;
        if (_timing.CurTime < actor.NextLockerInteraction)
            return HTNOperatorStatus.Continuing;
        actor.NextLockerInteraction = _timing.CurTime + TimeSpan.FromSeconds(0.7);
        _steering.Unregister(uid);
        EndGuard(uid);
        if (_lockerStorage.TryOpenStorage(uid, locker, silent: true))
        {
            MarkLockerGrenadeTarget(uid, actor, order.Target);
            return HTNOperatorStatus.Finished;
        }
        if (TryComp<LockComponent>(locker, out var lockComp) && lockComp.Locked && _lockerLocks.TryUnlock(locker, uid, lockComp))
        {
            actor.NextLockerInteraction += lockComp.UnlockTime;
            return HTNOperatorStatus.Continuing;
        }
        if (!OwnsItem(uid, actor.Backup) || !EquipOwnedItem(uid, actor, actor.Backup, true) ||
            !_squadMelee.TryGetWeapon(uid, out var weapon, out var melee) ||
            !_interaction.InRangeUnobstructed(uid, locker, melee.Range))
            return HTNOperatorStatus.Failed;
        if (melee.NextAttack <= _timing.CurTime)
        {
            _combat.SetInCombatMode(uid, true);
            _squadMelee.AttemptLightAttack(uid, weapon, melee, locker);
            if (TerminatingOrDeleted(locker) || EntityManager.IsQueuedForDeletion(locker))
                MarkLockerGrenadeTarget(uid, actor, order.Target);
        }
        return HTNOperatorStatus.Continuing;
    }

    private void MarkLockerGrenadeTarget(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid target)
    {
        foreach (var ally in _squadLookup.GetEntitiesInRange<MilitiaOperationActorComponent>(_transform.GetMapCoordinates(uid), actor.SquadRange))
        {
            if (!SquadMember(ally.Owner) || !SameOperation(uid, ally.Owner))
                continue;
            ally.Comp.LockerGrenadeTarget = target;
            ally.Comp.LockerGrenadeUntil = _timing.CurTime + TimeSpan.FromSeconds(8);
            var next = _timing.CurTime + TimeSpan.FromSeconds(1);
            if (ally.Comp.NextGrenade > next)
                ally.Comp.NextGrenade = next;
        }
    }
}
