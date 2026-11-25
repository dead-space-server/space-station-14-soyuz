// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Shared.Physics;
using Content.Shared.Storage.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    private static readonly PhysShapeCircle SightPathShape = new(0.001f);
    private static readonly Vector2[] DiagonalPositionOffsets =
        [new(-0.3f, 0.3f), new(0.3f, -0.3f), new(-0.3f, -0.3f), new(0.3f, 0.3f)];
    private readonly Dictionary<EntityUid, float> _squadCoverRadii = new();
    private readonly HashSet<Entity<EntityStorageComponent>> _squadLockersAtPosition = new();

    private bool SquadLockerAt(MapCoordinates position)
    {
        _squadLockersAtPosition.Clear();
        _squadLookup.GetEntitiesIntersecting(position.MapId,
            Box2.CenteredAround(position.Position, new Vector2(0.12f)), _squadLockersAtPosition,
            LookupFlags.Static | LookupFlags.Dynamic);
        return _squadLockersAtPosition.Any(locker => SquadLocker(locker.Owner));
    }

    private bool SquadLocker(EntityUid entity)
        => HasComp<EntityStorageComponent>(entity) && TryComp<PhysicsComponent>(entity, out var body) && body.CanCollide &&
           TryComp<FixturesComponent>(entity, out var fixtures) && fixtures.Fixtures.Values.Any(fixture =>
               fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.BulletImpassable) != 0);

    private bool SquadLockerOnRoute(EntityUid uid, MapCoordinates from, MapCoordinates to)
    {
        if (from.MapId != to.MapId || !TryComp<FixturesComponent>(uid, out var fixtures))
            return false;
        var delta = to.Position - from.Position;
        if (delta.LengthSquared() < 0.0001f)
            return false;
        var start = _squadPhysics.GetPhysicsTransform(uid);
        start.Position = from.Position;
        var filter = new QueryFilter
        {
            MaskBits = (uint) CollisionGroup.BulletImpassable,
            IsIgnored = entity => entity == uid || !SquadLocker(entity),
        };
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard && _squadRayCast.CastShape(from.MapId, fixture.Shape, start, delta, filter,
                    RayCastSystem.RayCastClosestCallback).Hit)
                return true;
        }
        return SquadLockerAt(from) || SquadLockerAt(to);
    }

    private void RequestLockerDetour(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (actor.LockerDetourUntil > _timing.CurTime)
            return;
        actor.LockerDetourUntil = _timing.CurTime + TimeSpan.FromSeconds(4);
        _urgentSquadOrders.Add(uid);
    }

    private bool SquadCoverExposed(EntityUid uid, MapCoordinates position, MapCoordinates threat, float range = 0f)
    {
        if (CoverExposed(position, threat, range))
            return true;
        var delta = threat.Position - position.Position;
        if (position.MapId != threat.MapId || delta.LengthSquared() < 0.0001f ||
            range > 0f && delta.LengthSquared() > range * range)
            return false;
        if (!_reuseSquadQueries || !_squadCoverRadii.TryGetValue(uid, out var radius))
        {
            var bounds = _squadPhysics.GetWorldAABB(uid);
            radius = MathF.Max(bounds.Width, bounds.Height) * 0.5f;
            if (_reuseSquadQueries)
                _squadCoverRadii[uid] = radius;
        }
        var side = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * radius;
        return CoverExposed(new MapCoordinates(position.Position + side, position.MapId), threat) ||
               CoverExposed(new MapCoordinates(position.Position - side, position.MapId), threat);
    }

    private bool SquadLineClear(MapCoordinates origin, MapCoordinates target, float range = 0f,
        CollisionGroup mask = SquadSight, Func<EntityUid, bool>? ignored = null)
    {
        if (origin.MapId != target.MapId)
            return false;
        var delta = target.Position - origin.Position;
        if (range > 0f && delta.LengthSquared() > range * range)
            return false;
        if (delta.LengthSquared() < 0.000001f)
            return true;
        return !CastSquadLine(origin, delta, mask, ignored).Hit;
    }

    private RayResult CastSquadLine(MapCoordinates origin, Vector2 delta, CollisionGroup mask,
        Func<EntityUid, bool>? ignored)
        => _squadRayCast.CastShape(origin.MapId, SightPathShape,
            new Robust.Shared.Physics.Transform(origin.Position, 0f), delta,
            new QueryFilter { MaskBits = (uint) mask, IsIgnored = ignored }, RayCastSystem.RayCastClosestCallback);

    private bool SquadLineClear(EntityUid uid, EntityUid target, float range = 0f,
        CollisionGroup mask = SquadSight, Func<EntityUid, bool>? ignored = null)
    {
        var origin = _transform.GetMapCoordinates(uid);
        var destination = _transform.GetMapCoordinates(target);
        if (origin.MapId != destination.MapId || range > 0f &&
            Vector2.DistanceSquared(origin.Position, destination.Position) > range * range &&
            (!_squadPhysics.TryGetDistance(uid, target, out var distance) || distance > range))
            return false;
        return SquadLineClear(origin, destination, mask: mask,
            ignored: entity => entity == uid || entity == target || ignored?.Invoke(entity) == true);
    }

    private bool SquadPositionClear(EntityUid uid, MapCoordinates position)
    {
        if (Transform(uid).MapID != position.MapId || !TryComp<FixturesComponent>(uid, out var fixtures))
            return false;
        var transform = _squadPhysics.GetPhysicsTransform(uid);
        transform.Position = position.Position;
        var intersecting = new HashSet<EntityUid>();
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;
            var bounds = fixture.Shape.ComputeAABB(transform, 0);
            intersecting.Clear();
            _squadLookup.GetEntitiesIntersecting(position.MapId, fixture.Shape, transform, intersecting,
                LookupFlags.Static | LookupFlags.Dynamic);
            foreach (var obstacle in intersecting)
            {
                if (obstacle == uid || !TryComp<PhysicsComponent>(obstacle, out var body) || !body.CanCollide ||
                    !TryComp<FixturesComponent>(obstacle, out var otherFixtures))
                    continue;
                foreach (var other in otherFixtures.Fixtures.Values)
                {
                    if (other.Hard && ((fixture.CollisionMask & other.CollisionLayer) != 0 ||
                                       (other.CollisionMask & fixture.CollisionLayer) != 0 ||
                                       (other.CollisionLayer & (int) CollisionGroup.BulletImpassable) != 0 &&
                                       HasComp<EntityStorageComponent>(obstacle)) &&
                        (other.Shape.ChildCount > 1 ||
                         other.Shape.ComputeAABB(_squadPhysics.GetPhysicsTransform(obstacle), 0).Intersects(bounds)))
                        return false;
                }
            }
        }
        return true;
    }

    private bool TrySquadTilePosition(EntityUid uid, EntityUid grid, MapGridComponent gridComp, Vector2i tile,
        out EntityCoordinates position)
    {
        position = _maps.GridTileToLocal(grid, gridComp, tile);
        if (SquadPositionClear(uid, _transform.ToMapCoordinates(position)))
            return true;

        // Only diagonal geometry needs extra samples; ordinary blocked tiles stay cheap.
        var diagonal = false;
        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, tile);
        while (anchored.MoveNext(out var obstacle))
        {
            if (!TryComp<FixturesComponent>(obstacle, out var fixtures))
                continue;
            foreach (var fixture in fixtures.Fixtures.Values)
                diagonal |= fixture.Hard && fixture.Shape is PolygonShape { VertexCount: 3 };
        }
        if (!diagonal)
            return false;
        var center = position;
        foreach (var offset in DiagonalPositionOffsets)
        {
            var candidate = new EntityCoordinates(grid, center.Position + offset * gridComp.TileSize);
            if (!SquadPositionClear(uid, _transform.ToMapCoordinates(candidate)))
                continue;
            position = candidate;
            return true;
        }
        return false;
    }
}
