// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Shared.Physics;
using Content.Shared.Doors.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    private bool CanReturnFire(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (!DirectContact(uid, actor) || !OwnsItem(uid, actor.Weapon) || !WeaponReady(actor.Weapon) || UnsafeRangedTarget(actor, actor.DirectTarget))
            return false;
        var target = _transform.GetMapCoordinates(actor.DirectTarget);
        if (RecentDamageThreat(uid, actor) is { } incoming &&
            (target.MapId != incoming.MapId || Vector2.DistanceSquared(target.Position, incoming.Position) > 9f))
            return false;
        if (actor.SquadOrder is { Task: MilitiaSquadTask.Breakout } order &&
            RecentDamageThreat(uid, actor) == null && order.Target != actor.DirectTarget)
            return false;
        return !SquadShotBlocked(uid, actor.DirectTarget, _transform.GetMapCoordinates(uid), target,
            ShotCollisionMask(actor.Weapon), HolographicFireSpread(actor));
    }

    private MapCoordinates? BreakoutSource(EntityUid uid, MilitiaOperationActorComponent actor)
        => RecentDamageThreat(uid, actor) ?? (DirectContact(uid, actor)
            ? _transform.GetMapCoordinates(actor.DirectTarget)
            : RememberedWindowThreats(actor).Select(point => (MapCoordinates?) point).FirstOrDefault());

    private bool BreakoutNeeded(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid target)
    {
        if (actor.Grenades.Length > 0 || !SquadThreat(uid, target) ||
            !actor.DefensivePosition.IsValid(EntityManager) || !OnDefensiveGrid(uid, Transform(target).Coordinates))
            return false;
        var position = _transform.GetMapCoordinates(target);
        if (_turf.GetTileRef(Transform(target).Coordinates) is not { } tile || tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile) ||
            Vector2.DistanceSquared(position.Position, _transform.ToMapCoordinates(actor.DefensivePosition).Position) > actor.DefensiveRange * actor.DefensiveRange)
            return false;
        var seen = SquadSightClear(uid, target, CombatVision(actor));
        var knownShooter = actor.WindowExposed && RecentDamageThreat(uid, actor) is { } incoming &&
            incoming.MapId == position.MapId && Vector2.DistanceSquared(position.Position, incoming.Position) <= 9f;
        return (seen || knownShooter) && SquadShotBlocked(uid, target, _transform.GetMapCoordinates(uid), position,
            ShotCollisionMask(actor.Weapon), HolographicFireSpread(actor));
    }

    private bool ProactiveBreakout(EntityUid uid, MilitiaOperationActorComponent actor)
        => DirectContact(uid, actor) && (!actor.Medic || actor.DamageThreatUntil > _timing.CurTime && !actor.TriageRequired) &&
           BreakoutNeeded(uid, actor, actor.DirectTarget);

    private bool ValidBreakoutOrder(EntityUid uid, MilitiaOperationActorComponent actor, MilitiaSquadOrder order)
        => order.Until > _timing.CurTime && actor.DefensivePosition.IsValid(EntityManager) && order.BreakoutThreat.IsValid(EntityManager) &&
           (!order.Target.IsValid() || SquadThreat(uid, order.Target) && OnDefensiveGrid(uid, Transform(order.Target).Coordinates) &&
               Vector2.DistanceSquared(_transform.GetWorldPosition(order.Target), _transform.ToMapCoordinates(actor.DefensivePosition).Position) <= actor.DefensiveRange * actor.DefensiveRange) &&
           order.Positions.Any(position => position.IsValid(EntityManager) && OnDefensiveGrid(uid, position) &&
               _turf.GetTileRef(position) is { } tile && !tile.Tile.IsEmpty && !_turf.IsSpace(tile.Tile) &&
               Vector2.DistanceSquared(_transform.ToMapCoordinates(position).Position,
                   _transform.ToMapCoordinates(actor.DefensivePosition).Position) <= actor.DefensiveRange * actor.DefensiveRange);

    private bool BreakoutCanFight(EntityUid uid, EntityUid target,
        MapCoordinates position, MapCoordinates enemy, MeleeWeaponComponent? melee, long mask, float spread, Box2[] fields)
    {
        if (melee != null)
            return Vector2.DistanceSquared(position.Position, enemy.Position) <= melee.Range * melee.Range &&
                   SquadLineClear(position, enemy, mask: SquadSight);
        return !SquadShotBlocked(uid, target, position, enemy, mask, spread, fields);
    }

    private EntityCoordinates[] BreakoutPositions(EntityUid uid, MilitiaOperationActorComponent actor,
        List<MapCoordinates> occupied, bool allowCover, out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (_timing.CurTime < actor.NextBreakoutAttempt || BreakoutSource(uid, actor) is not { } incoming ||
            Transform(uid).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return Array.Empty<EntityCoordinates>();
        actor.NextBreakoutAttempt = _timing.CurTime + TimeSpan.FromSeconds(2);
        foreach (var enemy in NearbySquadHostiles(uid, actor.ThreatRange)
                     .Where(enemy => SquadThreat(uid, enemy))
                     .OrderBy(enemy => Vector2.DistanceSquared(_transform.GetWorldPosition(enemy), incoming.Position)))
        {
            if (BreakoutNeeded(uid, actor, enemy))
            {
                target = enemy;
                break;
            }
        }
        var origin = _transform.GetMapCoordinates(uid);
        var home = _transform.ToMapCoordinates(actor.DefensivePosition);
        var reservations = new List<MapCoordinates>(occupied);
        var allies = EntityQueryEnumerator<MilitiaOperationActorComponent>();
        while (allies.MoveNext(out var other, out var ally))
        {
            if (other == uid || !SquadMember(other) || !SameOperation(uid, other))
                continue;
            reservations.Add(_transform.GetMapCoordinates(other));
            if ((ally.RunningSquadOrder != null || ally.ReloadUntil != null) && ally.SquadPosition.IsValid(EntityManager))
                reservations.Add(_transform.ToMapCoordinates(ally.SquadPosition));
        }
        var threats = actor.SquadContacts.Where(contact => contact.Expires > _timing.CurTime && contact.Position.IsValid(EntityManager))
            .Select(contact => _transform.ToMapCoordinates(contact.Position)).Concat(RememberedWindowThreats(actor)).Append(incoming).Distinct().ToArray();
        var candidates = new List<(EntityCoordinates Position, float Score)>();
        var enemyTarget = target;
        var enemyPosition = target.IsValid() ? _transform.GetMapCoordinates(target) : default;
        MeleeWeaponComponent? melee = null;
        if (target.IsValid() && DesiredWeapon(uid, actor) == actor.Backup)
            TryComp(actor.Backup, out melee);
        var canShoot = target.IsValid() && melee == null && OwnsItem(uid, actor.Weapon) && !UnsafeRangedTarget(actor, target);
        var mask = canShoot ? ShotCollisionMask(actor.Weapon) : 0L;
        var spread = canShoot ? HolographicFireSpread(actor) : 0f;
        var fields = spread > 0f ? ReflectingFieldBounds(origin, 48f) : Array.Empty<Box2>();

        void AddPosition(EntityCoordinates coordinates, bool exit, bool positionChecked = false)
        {
            var position = _transform.ToMapCoordinates(coordinates);
            var travel = Vector2.Distance(origin.Position, position.Position);
            if (travel < 0.75f || travel > 24f || !OnDefensiveGrid(uid, coordinates) ||
                _turf.GetTileRef(coordinates) is not { } tile || tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile) ||
                Vector2.DistanceSquared(position.Position, home.Position) > actor.DefensiveRange * actor.DefensiveRange ||
                actor.RejectedSquadPositions.GetValueOrDefault(coordinates) > _timing.CurTime ||
                reservations.Any(ally => ally.MapId == position.MapId && Vector2.DistanceSquared(ally.Position, position.Position) < 0.81f) ||
                !positionChecked && !SquadPositionClear(uid, position))
                return;
            var safe = allowCover && !threats.Any(threat => SquadCoverExposed(uid, position, threat));
            if (!safe && (melee == null && !canShoot ||
                !BreakoutCanFight(uid, enemyTarget, position, enemyPosition, melee, mask, spread, fields)))
                return;
            var score = travel + (safe ? -12f : 0f) + (exit ? -8f : 0f);
            var index = candidates.FindIndex(candidate => score < candidate.Score);
            if (index < 0)
                index = candidates.Count;
            if (index >= 8 || candidates.Any(candidate => candidate.Position == coordinates))
                return;
            candidates.Insert(index, (coordinates, score));
            if (candidates.Count > 8)
                candidates.RemoveAt(8);
        }

        // Try the far side of reachable doors before requiring a firing position beside the attacker.
        foreach (var door in _squadLookup.GetEntitiesInRange<DoorComponent>(origin, 16f)
                     .Where(door => !TerminatingOrDeleted(door.Owner) && Transform(door.Owner).GridUid == grid && RetreatDoor(uid, door.Owner))
                     .OrderBy(door => Vector2.DistanceSquared(origin.Position, _transform.GetWorldPosition(door.Owner))).Take(6))
        {
            var doorPosition = _transform.GetMapCoordinates(door.Owner);
            var approach = doorPosition.Position - origin.Position;
            foreach (var direction in SquadAdjacent)
            {
                var offset = _transform.GetWorldRotation(grid).RotateVec(new Vector2(direction.X, direction.Y)) * gridComp.TileSize * 1.25f;
                if (Vector2.Dot(offset, approach) <= 0.1f)
                    continue;
                var position = new MapCoordinates(doorPosition.Position + offset, origin.MapId);
                var before = new MapCoordinates(doorPosition.Position - offset * 0.6f, origin.MapId);
                if (SquadRouteClear(uid, before, position,
                        entity => entity == door.Owner || CanCrossHolographicBarrier(uid, entity)))
                    AddPosition(_transform.ToCoordinates(grid, position), exit: true);
            }
        }
        if (target.IsValid())
        {
            var center = _maps.CoordinatesToTile(grid, gridComp, _transform.ToCoordinates(grid, enemyPosition));
            for (var x = -5; x <= 5; x++)
            for (var y = -5; y <= 5; y++)
            {
                var tile = _maps.GetTileRef(grid, gridComp, center + new Vector2i(x, y));
                if (tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile) ||
                    !TrySquadTilePosition(uid, grid, gridComp, tile.GridIndices, out var coordinates))
                    continue;
                var position = _transform.ToMapCoordinates(coordinates);
                var distance = Vector2.Distance(enemyPosition.Position, position.Position);
                if (distance < 0.85f || distance > 5f)
                    continue;
                AddPosition(coordinates, exit: false, positionChecked: true);
            }
        }
        if (candidates.Count == 0)
            AllowTrappedReload(uid, actor);
        return candidates.Select(candidate => candidate.Position).ToArray();
    }

    private void AllowTrappedReload(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (NeedsReload(uid))
            actor.EmergencyReloadUntil = _timing.CurTime + actor.ReloadTime + TimeSpan.FromSeconds(2);
    }

    public void RejectUnreachableBreakout(EntityUid uid, MilitiaSquadOrder order)
    {
        if (order.Task != MilitiaSquadTask.Breakout || !AvailableActor(uid, out var actor) ||
            !ReferenceEquals(actor.SquadOrder, order))
            return;
        actor.NextBreakoutAttempt = _timing.CurTime + TimeSpan.FromSeconds(2);
        actor.RetreatRetryAfter = actor.NextBreakoutAttempt;
        AllowTrappedReload(uid, actor);
        actor.SquadOrder = null;
        _urgentSquadOrders.Add(uid);
    }
}
