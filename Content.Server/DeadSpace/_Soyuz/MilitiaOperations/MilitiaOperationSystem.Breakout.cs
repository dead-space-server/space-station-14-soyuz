// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    private bool CanReturnFire(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (!DirectContact(uid, actor) || RecentDamageThreat(uid, actor) is not { } incoming ||
            !OwnsItem(uid, actor.Weapon) || !WeaponReady(actor.Weapon) || UnsafeRangedTarget(actor, actor.DirectTarget))
            return false;
        var target = _transform.GetMapCoordinates(actor.DirectTarget);
        return target.MapId == incoming.MapId && Vector2.DistanceSquared(target.Position, incoming.Position) <= 9f &&
            !SquadShotBlocked(uid, actor.DirectTarget, _transform.GetMapCoordinates(uid), target,
                ShotCollisionMask(actor.Weapon), HolographicFireSpread(actor));
    }

    private bool BreakoutNeeded(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid target)
    {
        if (!actor.WindowExposed || actor.Grenades.Length > 0 ||
            RecentDamageThreat(uid, actor) is not { } incoming || !SquadThreat(uid, target) ||
            !actor.DefensivePosition.IsValid(EntityManager))
            return false;
        var position = _transform.GetMapCoordinates(target);
        if (position.MapId != incoming.MapId || Vector2.DistanceSquared(position.Position, incoming.Position) > 9f ||
            _turf.GetTileRef(Transform(target).Coordinates) is not { } tile || tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile) ||
            !OnDefensiveGrid(uid, Transform(target).Coordinates) ||
            Vector2.DistanceSquared(position.Position, _transform.ToMapCoordinates(actor.DefensivePosition).Position) > actor.DefensiveRange * actor.DefensiveRange ||
            !SquadSightClear(uid, target, actor.ThreatRange))
            return false;
        return SquadShotBlocked(uid, target, _transform.GetMapCoordinates(uid), position,
            ShotCollisionMask(actor.Weapon), HolographicFireSpread(actor));
    }

    private EntityCoordinates[] BreakoutPositions(EntityUid uid, MilitiaOperationActorComponent actor,
        List<MapCoordinates> occupied, out EntityUid target)
    {
        target = EntityUid.Invalid;
        if (_timing.CurTime < actor.NextBreakoutAttempt || RecentDamageThreat(uid, actor) is not { } incoming)
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
        if (!target.IsValid() || Transform(target).GridUid is not { } grid ||
            !TryComp<MapGridComponent>(grid, out var gridComp))
        {
            AllowTrappedReload(uid, actor);
            return Array.Empty<EntityCoordinates>();
        }
        var origin = _transform.GetMapCoordinates(uid);
        var enemyPosition = _transform.GetMapCoordinates(target);
        var home = _transform.ToMapCoordinates(actor.DefensivePosition);
        var center = _maps.CoordinatesToTile(grid, gridComp, Transform(target).Coordinates);
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
        var mask = ShotCollisionMask(actor.Weapon);
        var spread = HolographicFireSpread(actor);
        var fields = spread > 0f ? ReflectingFieldBounds(enemyPosition, 32f) : Array.Empty<Box2>();
        var candidates = new List<(EntityCoordinates Position, float Score)>();
        for (var x = -5; x <= 5; x++)
        for (var y = -5; y <= 5; y++)
        {
            var tile = _maps.GetTileRef(grid, gridComp, center + new Vector2i(x, y));
            if (tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile) ||
                !TrySquadTilePosition(uid, grid, gridComp, tile.GridIndices, out var coordinates))
                continue;
            var position = _transform.ToMapCoordinates(coordinates);
            var travel = Vector2.Distance(origin.Position, position.Position);
            var distance = Vector2.Distance(enemyPosition.Position, position.Position);
            if (travel < 0.9f || travel > 16f || distance < 1.25f || distance > 5f ||
                !OnDefensiveGrid(uid, coordinates) ||
                Vector2.DistanceSquared(position.Position, home.Position) > actor.DefensiveRange * actor.DefensiveRange ||
                actor.RejectedSquadPositions.GetValueOrDefault(coordinates) > _timing.CurTime ||
                reservations.Any(ally => ally.MapId == position.MapId && Vector2.DistanceSquared(ally.Position, position.Position) < 0.81f) ||
                !SquadLineClear(position, enemyPosition, 0, CollisionGroup.Opaque) ||
                SquadShotBlocked(uid, target, position, enemyPosition, mask, spread, fields))
                continue;
            var score = travel + MathF.Abs(distance - 3f);
            var index = candidates.FindIndex(candidate => score < candidate.Score);
            if (index < 0)
                index = candidates.Count;
            if (index >= 4)
                continue;
            candidates.Insert(index, (coordinates, score));
            if (candidates.Count > 4)
                candidates.RemoveAt(4);
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
        _nextSquadUpdate = _timing.CurTime;
    }
}
