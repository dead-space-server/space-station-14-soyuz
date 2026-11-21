// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Shared.Doors.Components;
using Content.Shared.Physics;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    private readonly Dictionary<EntityUid, BreakoutDoorSnapshot> _breakoutDoorSnapshots = new();

    private bool CanReturnFire(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (!DirectContact(uid, actor) || !OwnsItem(uid, actor.Weapon) || !WeaponReady(actor.Weapon) || UnsafeRangedTarget(actor, actor.DirectTarget))
            return false;
        var target = _transform.GetMapCoordinates(actor.DirectTarget);
        var incomingThreat = RecentDamageThreat(uid, actor);
        if (incomingThreat is { } incoming &&
            (actor.DirectTarget != actor.RangedAttacker || actor.RangedAttackerUntil <= _timing.CurTime) &&
            (target.MapId != incoming.MapId || Vector2.DistanceSquared(target.Position, incoming.Position) > 9f))
            return false;
        if (actor.SquadOrder is { Task: MilitiaSquadTask.Breakout } order &&
            incomingThreat == null && order.Target != actor.DirectTarget)
            return false;
        var origin = _transform.GetMapCoordinates(uid);
        var spread = HolographicFireSpread(actor);
        return !SquadShotBlocked(uid, actor.DirectTarget, origin, target, GetShotCollision(actor.Weapon), spread) &&
               !SquadFireLaneBlocked(uid, actor.DirectTarget, origin, target, spread, out _);
    }

    private MapCoordinates? BreakoutSource(EntityUid uid, MilitiaOperationActorComponent actor)
        => RecentDamageThreat(uid, actor) ?? (DirectContact(uid, actor)
            ? _transform.GetMapCoordinates(actor.DirectTarget)
            : RememberedWindowThreats(actor).Select(point => (MapCoordinates?) point).FirstOrDefault());

    private bool BreakoutNeeded(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid target)
    {
        if (actor.Grenades.Length > 0 || !BreakoutTargetInArea(uid, actor, target))
            return false;
        var position = _transform.GetMapCoordinates(target);
        var seen = SquadSightClear(uid, target, CombatVision(actor, target));
        var knownShooter = actor.WindowExposed && RecentDamageThreat(uid, actor) is { } incoming &&
            incoming.MapId == position.MapId && Vector2.DistanceSquared(position.Position, incoming.Position) <= 9f;
        return (seen || knownShooter) && SquadShotBlocked(uid, target, _transform.GetMapCoordinates(uid), position,
            GetShotCollision(actor.Weapon), HolographicFireSpread(actor));
    }

    private bool ProactiveBreakout(EntityUid uid, MilitiaOperationActorComponent actor)
        => DirectContact(uid, actor) && !MedicSupportPriority(actor) &&
           BreakoutNeeded(uid, actor, actor.DirectTarget);

    private bool ValidBreakoutOrder(EntityUid uid, MilitiaOperationActorComponent actor, MilitiaSquadOrder order)
    {
        if (order.Until <= _timing.CurTime || !actor.DefensivePosition.IsValid(EntityManager) ||
            !order.BreakoutThreat.IsValid(EntityManager) || order.BreakoutGoals is not { } goals)
            return false;
        return ReferenceEquals(actor.RunningSquadOrder, order)
            ? goals.TryGetValue(actor.SquadPosition, out var goal) &&
              (goal != MilitiaBreakoutGoal.Fight || BreakoutTargetInArea(uid, actor, order.Target))
            : goals.Count > 0;
    }

    private bool BreakoutTargetInArea(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid target)
    {
        if (!actor.DefensivePosition.IsValid(EntityManager) || !SquadThreat(uid, target))
            return false;
        var coordinates = Transform(target).Coordinates;
        return OnDefensiveGrid(uid, coordinates) && _turf.GetTileRef(coordinates) is { } tile &&
               !tile.Tile.IsEmpty && !_turf.IsSpace(tile.Tile) &&
               Vector2.DistanceSquared(_transform.GetWorldPosition(target), _transform.ToMapCoordinates(actor.DefensivePosition).Position) <=
                   actor.DefensiveRange * actor.DefensiveRange;
    }

    private bool BreakoutDestinationValid(EntityUid uid, MilitiaSquadOrder order, EntityCoordinates destination, EntityCoordinates start)
    {
        if (order.BreakoutGoals is not { } goals || !goals.TryGetValue(destination, out var goal) ||
            !destination.IsValid(EntityManager) || !OnDefensiveGrid(uid, destination) ||
            _turf.GetTileRef(destination) is not { } tile || tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile))
            return false;

        var actor = Comp<MilitiaOperationActorComponent>(uid);
        if (!actor.DefensivePosition.IsValid(EntityManager) || !order.BreakoutThreat.IsValid(EntityManager))
            return false;
        var position = _transform.ToMapCoordinates(destination);
        var home = _transform.ToMapCoordinates(actor.DefensivePosition);
        if (position.MapId != home.MapId ||
            Vector2.DistanceSquared(position.Position, home.Position) > actor.DefensiveRange * actor.DefensiveRange)
            return false;

        if (goal != MilitiaBreakoutGoal.Fight)
        {
            var source = BreakoutSource(uid, actor) ?? _transform.ToMapCoordinates(order.BreakoutThreat);
            if (goal == MilitiaBreakoutGoal.Withdraw && start.IsValid(EntityManager) &&
                ImprovesWithdrawal(_transform.ToMapCoordinates(start), position, source))
                return true;
            return !RecentlyHitAt(actor, position) && !SquadCoverExposed(uid, position, source) &&
                   !RetreatThreats(uid, order).Any(threat => threat != source && SquadCoverExposed(uid, position, threat));
        }
        if (!BreakoutTargetInArea(uid, actor, order.Target))
            return false;
        var enemy = _transform.GetMapCoordinates(order.Target);
        if (DesiredWeapon(uid, actor) == actor.Backup && TryComp<MeleeWeaponComponent>(actor.Backup, out var melee))
            return BreakoutCanFight(uid, order.Target, position, enemy, melee, default, 0f);
        return OwnsItem(uid, actor.Weapon) && !UnsafeRangedTarget(actor, order.Target) &&
               BreakoutCanFight(uid, order.Target, position, enemy, null, GetShotCollision(actor.Weapon), HolographicFireSpread(actor));
    }

    private static bool ImprovesWithdrawal(MapCoordinates origin, MapCoordinates destination, MapCoordinates source)
        => origin.MapId == destination.MapId && origin.MapId == source.MapId &&
           Vector2.Distance(destination.Position, source.Position) > Vector2.Distance(origin.Position, source.Position) + 0.5f;

    private bool BreakoutCanFight(EntityUid uid, EntityUid target,
        MapCoordinates position, MapCoordinates enemy, MeleeWeaponComponent? melee, ShotCollision collision, float spread, Box2[]? fields = null)
    {
        if (melee != null)
            return Vector2.DistanceSquared(position.Position, enemy.Position) <= melee.Range * melee.Range &&
                   SquadLineClear(position, enemy, mask: SquadSight);
        return !SquadShotBlocked(uid, target, position, enemy, collision, spread, fields);
    }

    private MilitiaSquadOrder? FindBreakoutOrder(EntityUid uid, MilitiaOperationActorComponent actor,
        List<MapCoordinates> occupied, bool allowCover, MilitiaSquadContact[] contacts,
        List<(EntityUid Owner, MapCoordinates Position)> reservations)
    {
        if (_timing.CurTime < actor.NextBreakoutAttempt || !actor.DefensivePosition.IsValid(EntityManager) ||
            BreakoutSource(uid, actor) is not { } incoming ||
            Transform(uid).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return null;
        actor.NextBreakoutAttempt = _timing.CurTime + TimeSpan.FromSeconds(2);
        var target = EntityUid.Invalid;
        foreach (var enemy in SquadCombatHostiles(uid, actor, actor.ThreatRange)
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
        var threats = actor.SquadContacts.Where(contact => contact.Expires > _timing.CurTime && contact.Position.IsValid(EntityManager))
            .Select(contact => _transform.ToMapCoordinates(contact.Position)).Concat(RememberedWindowThreats(actor)).Append(incoming).Distinct().ToArray();
        var candidates = new List<(EntityCoordinates Position, MilitiaBreakoutGoal Goal, float Score)>();
        var enemyPosition = target.IsValid() ? _transform.GetMapCoordinates(target) : default;
        MeleeWeaponComponent? melee = null;
        if (target.IsValid() && DesiredWeapon(uid, actor) == actor.Backup)
            TryComp(actor.Backup, out melee);
        var canShoot = target.IsValid() && melee == null && OwnsItem(uid, actor.Weapon) && !UnsafeRangedTarget(actor, target);
        var collision = canShoot ? GetShotCollision(actor.Weapon) : default;
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
                SquadPositionReserved(uid, position, reservations, occupied) ||
                !positionChecked && !SquadPositionClear(uid, position))
                return;
            MilitiaBreakoutGoal goal;
            if (allowCover && !RecentlyHitAt(actor, position) && !threats.Any(threat => SquadCoverExposed(uid, position, threat)))
                goal = MilitiaBreakoutGoal.Cover;
            else if ((melee != null || canShoot) &&
                BreakoutCanFight(uid, target, position, enemyPosition, melee, collision, spread, fields))
                goal = MilitiaBreakoutGoal.Fight;
            else if (allowCover && ImprovesWithdrawal(origin, position, incoming))
                goal = MilitiaBreakoutGoal.Withdraw;
            else
                return;
            var score = travel;
            if (goal == MilitiaBreakoutGoal.Withdraw)
                score -= 2f * (Vector2.Distance(position.Position, incoming.Position) - Vector2.Distance(origin.Position, incoming.Position));
            if (exit)
                score -= 4f;
            var index = candidates.FindIndex(candidate => goal < candidate.Goal || goal == candidate.Goal && score < candidate.Score);
            if (index < 0)
                index = candidates.Count;
            if (index >= 8 || candidates.Any(candidate => candidate.Position == coordinates))
                return;
            candidates.Insert(index, (coordinates, goal, score));
            if (candidates.Count > 8)
                candidates.RemoveAt(8);
        }

        const int doorBudget = 8;
        var doors = BreakoutDoors(uid, grid, origin);
        var rotation = _transform.GetWorldRotation(grid);
        Span<Vector2> doorOffsets = stackalloc Vector2[SquadAdjacent.Length];
        for (var i = 0; i < SquadAdjacent.Length; i++)
        {
            var direction = SquadAdjacent[i];
            doorOffsets[i] = rotation.RotateVec(new Vector2(direction.X, direction.Y)) * gridComp.TileSize * 1.25f;
        }
        var checkedDoors = Math.Min(doorBudget, doors.Length);
        // Advance the search after each attempt so nearer blocked doors cannot starve further exits.
        for (var i = 0; i < checkedDoors; i++)
        {
            var (door, doorPosition) = doors[(actor.BreakoutDoorOffset + i) % doors.Length];
            if (!RetreatDoor(uid, door.Owner))
                continue;
            var approach = doorPosition.Position - origin.Position;
            foreach (var offset in doorOffsets)
            {
                if (Vector2.Dot(offset, approach) <= 0.1f)
                    continue;
                var position = new MapCoordinates(doorPosition.Position + offset, origin.MapId);
                var before = new MapCoordinates(doorPosition.Position - offset * 0.6f, origin.MapId);
                if (SquadRouteClear(uid, before, position,
                        entity => entity == door.Owner || CanCrossHolographicBarrier(uid, entity)))
                    AddPosition(_transform.ToCoordinates(grid, position), exit: true);
            }
        }
        actor.BreakoutDoorOffset = doors.Length == 0 ? 0 : (actor.BreakoutDoorOffset + checkedDoors) % doors.Length;
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
        if (allowCover && !candidates.Any(candidate => candidate.Goal == MilitiaBreakoutGoal.Cover))
        {
            // Sample further floor points in eight directions when there is no immediate shelter.
            var center = _maps.CoordinatesToTile(grid, gridComp, Transform(uid).Coordinates);
            for (var radius = 4; radius <= 24; radius += 4)
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;
                var tile = _maps.GetTileRef(grid, gridComp, center + new Vector2i(x * radius, y * radius));
                if (!tile.Tile.IsEmpty && !_turf.IsSpace(tile.Tile) &&
                    TrySquadTilePosition(uid, grid, gridComp, tile.GridIndices, out var point))
                    AddPosition(point, exit: false, positionChecked: true);
            }
        }
        if (candidates.Count == 0)
        {
            AllowTrappedReload(uid, actor);
            return null;
        }
        return new MilitiaSquadOrder(target, candidates.Select(candidate => candidate.Position).ToArray(),
            MilitiaSquadTask.Breakout, _timing.CurTime + TimeSpan.FromSeconds(20), contacts)
        {
            BreakoutThreat = _transform.ToCoordinates(actor.DefensivePosition.EntityId, incoming),
            BreakoutGoals = candidates.ToDictionary(candidate => candidate.Position, candidate => candidate.Goal),
        };
    }

    private (Entity<DoorComponent> Door, MapCoordinates Position)[] BreakoutDoors(EntityUid uid, EntityUid grid, MapCoordinates origin)
    {
        var nearby = _squadLookup.GetEntitiesInRange<DoorComponent>(origin, 24f);
        nearby.RemoveWhere(door => TerminatingOrDeleted(door.Owner) || Transform(door.Owner).GridUid != grid);
        if (_breakoutDoorSnapshots.TryGetValue(uid, out var cached) && cached.Grid == grid && cached.Origin == origin &&
            cached.Doors.Length == nearby.Count &&
            cached.Doors.All(door => nearby.Contains(door.Door) && _transform.GetMapCoordinates(door.Door.Owner) == door.Position))
            return cached.Doors;

        var ordered = nearby.Select(door => (Door: door, Position: _transform.GetMapCoordinates(door.Owner)))
            .OrderBy(door => Vector2.DistanceSquared(origin.Position, door.Position.Position))
            .ThenBy(door => door.Door.Owner.GetHashCode())
            .ToArray();
        _breakoutDoorSnapshots[uid] = new BreakoutDoorSnapshot(grid, origin, ordered);
        return ordered;
    }

    private sealed record BreakoutDoorSnapshot(EntityUid Grid, MapCoordinates Origin,
        (Entity<DoorComponent> Door, MapCoordinates Position)[] Doors);

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
