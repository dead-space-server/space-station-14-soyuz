// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.NPC.Components;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    [Dependency] private readonly ClimbSystem _barrierClimb = default!;
    private static readonly PhysShapeCircle ShotPathShape = new(0.01f);
    private static readonly ShotCollision DefaultShotCollision = new((int) (CollisionGroup.Impassable | CollisionGroup.BulletImpassable));
    private readonly Dictionary<(EntityPrototype Prototype, string Ancestor), bool> _hologramAncestors = new();
    private readonly Dictionary<EntityPrototype, ShotCollision> _projectileCollisions = new();

    private bool HologramPrototype(EntityUid uid, string id)
    {
        if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) ||
            MetaData(uid).EntityPrototype is not { } prototype)
            return false;
        var key = (prototype, id);
        if (!_hologramAncestors.TryGetValue(key, out var matches))
            _hologramAncestors[key] = matches = _prototypes.EnumerateParents(prototype, includeSelf: true).Any(parent => parent.ID == id);
        return matches;
    }

    private bool CanCrossHolographicBarrier(EntityUid uid, EntityUid barrier)
        => HologramPrototype(barrier, "HolosignSecurity") &&
           TryComp<ClimbableComponent>(barrier, out var surface) && surface.Vaultable &&
           TryComp<ClimbingComponent>(uid, out var climbing) && climbing.CanClimb &&
           (climbing.IsClimbing || Comp<MilitiaOperationActorComponent>(uid).BarrierClimbRetryAfter <= _timing.CurTime);

    private bool BarrierClimbBusy(EntityUid uid)
        => TryComp<ClimbingComponent>(uid, out var climbing) &&
           (climbing.DoAfter != null || climbing.NextTransition != null);

    private bool StartBarrierClimb(EntityUid uid, EntityCoordinates[] route, ref int index, out bool blocked)
    {
        blocked = false;
        if (index >= route.Length || !route[index].IsValid(EntityManager) ||
            !TryComp<ClimbingComponent>(uid, out var climbing) || climbing.IsClimbing)
            return false;
        var origin = _transform.GetMapCoordinates(uid);
        var target = _transform.ToMapCoordinates(route[index]);
        var delta = target.Position - origin.Position;
        if (origin.MapId != target.MapId || delta.LengthSquared() < 0.001f)
            return false;
        var direction = Vector2.Normalize(delta);
        var hits = _squadRayCast.CastShape(origin.MapId, SightPathShape,
            _squadPhysics.GetPhysicsTransform(uid), direction * (delta.Length() + 0.45f),
            new QueryFilter { MaskBits = (uint) CollisionGroup.MobMask, IsIgnored = entity => entity == uid },
            RayCastSystem.RayCastAllCallback);
        foreach (var hit in hits.Results.OrderBy(hit => hit.Fraction))
        {
            var actor = Comp<MilitiaOperationActorComponent>(uid);
            if (HologramPrototype(hit.Entity, "HolosignSecurity") && actor.BarrierClimbRetryAfter > _timing.CurTime)
            {
                blocked = true;
                return false;
            }
            if (!CanCrossHolographicBarrier(uid, hit.Entity))
                return false;
            var surface = Comp<ClimbableComponent>(hit.Entity);
            if (!_barrierClimb.CanVault(surface, uid, hit.Entity, out _))
                return false;
            var center = _transform.GetMapCoordinates(hit.Entity);
            var resume = index;
            while (resume < route.Length && resume <= index + 4 && route[resume].IsValid(EntityManager) &&
                   Vector2.Dot(_transform.ToMapCoordinates(route[resume]).Position - center.Position, direction) < 0f)
                resume++;
            if (resume >= route.Length || resume > index + 4 || !route[resume].IsValid(EntityManager))
                return false;
            _steering.Unregister(uid);
            EndGuard(uid);
            if (TryComp<PhysicsComponent>(uid, out var body) && body.LinearVelocity.LengthSquared() > 0.01f)
                return true;
            if (!_barrierClimb.TryClimb(uid, uid, hit.Entity, out var action, surface, climbing))
                return false;
            index = resume;
            actor.BarrierClimbAction = action;
            var until = _timing.CurTime + TimeSpan.FromSeconds(surface.ClimbDelay + 2);
            if (actor.RunningSquadOrder is { } order && ReferenceEquals(actor.SquadOrder, order) && order.Until < until)
            {
                var extended = order with { Until = until };
                actor.SquadOrder = extended;
                actor.RunningSquadOrder = extended;
            }
            return true;
        }
        return false;
    }

    private void CancelBarrierClimb(EntityUid uid)
    {
        if (HasPlayerMind(uid) || !TryComp<MilitiaOperationActorComponent>(uid, out var actor))
            return;
        var id = actor.BarrierClimbAction;
        actor.BarrierClimbAction = null;
        if (id is { } action && _doAfter.GetStatus(action) == DoAfterStatus.Running)
            _doAfter.Cancel(uid, action.Index, null);
        if (!TryComp<ClimbingComponent>(uid, out var climbing) || climbing.DoAfter is not { } current || current == id ||
            !TryComp<DoAfterComponent>(uid, out var actions))
            return;
        IReadOnlyDictionary<ushort, Content.Shared.DoAfter.DoAfter> running = actions.DoAfters;
        if (running.TryGetValue(current.Index, out var native) && native.Args.Target is { } target &&
            HologramPrototype(target, "HolosignSecurity"))
            _doAfter.Cancel(uid, current.Index, actions);
    }

    private void SuppressBarrierClimb(Entity<MilitiaOperationActorComponent> actor)
    {
        actor.Comp.BarrierClimbRetryAfter = _timing.CurTime + TimeSpan.FromSeconds(5);
        var interrupted = actor.Comp.BarrierClimbAction is { } id &&
            _doAfter.GetStatus(id) is DoAfterStatus.Running or DoAfterStatus.Cancelled;
        if (!interrupted && TryComp<ClimbingComponent>(actor.Owner, out var climbing) &&
            climbing.DoAfter is { } current && TryComp<DoAfterComponent>(actor.Owner, out var actions))
        {
            IReadOnlyDictionary<ushort, Content.Shared.DoAfter.DoAfter> running = actions.DoAfters;
            interrupted = running.TryGetValue(current.Index, out var native) && native.Args.Target is { } target &&
                HologramPrototype(target, "HolosignSecurity");
        }
        if (!interrupted)
            return;
        CancelBarrierClimb(actor.Owner);
        InterruptActor(actor.Owner);
    }

    private float HolographicFireSpread(MilitiaOperationActorComponent actor)
        => TryComp<GunComponent>(actor.Weapon, out var gun) ? ReflectionShotSpread((actor.Weapon, gun)) : 0f;

    private bool HolographicProjectileBarrier(EntityUid uid)
        => TryComp<PhysicsComponent>(uid, out var body) && body.CanCollide &&
           (HologramPrototype(uid, "HolosignBulletShield") || HologramPrototype(uid, "XenoborgDeflectorField"));

    private Box2[] ReflectingFieldBounds(MapCoordinates origin, float range)
    {
        var area = Box2.CenteredAround(origin.Position, new Vector2(range * 2f));
        var bounds = new List<Box2>();
        var query = AllEntityQuery<ReflectComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var reflect, out var transform))
        {
            if (transform.MapID != origin.MapId || reflect.ReflectProb <= 0f || !HolographicProjectileBarrier(uid))
                continue;
            var position = _transform.GetWorldPosition(transform);
            var field = _squadPhysics.GetWorldAABB(uid).Union(new Box2(position, position));
            if (field.Intersects(area))
                bounds.Add(field);
        }
        return bounds.ToArray();
    }

    private bool HolographicShotBlocked(MapCoordinates origin, MapCoordinates target, float spread = 0f, bool attackMatrices = false,
        Box2[]? reflectingFields = null)
    {
        var delta = target.Position - origin.Position;
        if (origin.MapId != target.MapId || delta.LengthSquared() < 0.001f)
            return false;
        var direction = Vector2.Normalize(delta);
        if (!SquadLineClear(origin, target, mask: CollisionGroup.BulletImpassable,
                ignored: entity => !HolographicProjectileBarrier(entity) ||
                    attackMatrices && HologramPrototype(entity, "HolosignBulletShield")))
            return true;
        if (spread <= 0f)
            return false;
        if (reflectingFields != null && !reflectingFields.Any(bounds => bounds.Intersects(Box2.CenteredAround(origin.Position, new Vector2(48f)))))
            return false;
        foreach (var field in _squadLookup.GetEntitiesInRange<ReflectComponent>(origin, 24f))
        {
            if (!HolographicProjectileBarrier(field.Owner) || field.Comp.ReflectProb <= 0f)
                continue;
            var bounds = _squadPhysics.GetWorldAABB(field.Owner);
            var offset = bounds.Center - origin.Position;
            var along = Vector2.Dot(offset, direction);
            var radius = bounds.Size.Length() / 2f + 0.15f;
            if (along + radius > 0f && along - radius < 24f &&
                MathF.Abs(offset.X * direction.Y - offset.Y * direction.X) < radius + MathF.Max(0f, along) * spread &&
                SquadLineClear(origin, _transform.GetMapCoordinates(field.Owner), 0, CollisionGroup.Opaque))
                return true;
        }
        return false;
    }

    private ShotCollision GetShotCollision(EntityUid weapon)
    {
        if (!HasComp<GunComponent>(weapon))
            return DefaultShotCollision;
        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(weapon, ref ammo);
        return GetShotCollision(in ammo);
    }

    private ShotCollision GetShotCollision(in GetAmmoCountEvent ammo)
    {
        EntityPrototype? prototype = null;
        if (ammo.NextAmmoEntity is { } entity)
        {
            if (TryComp<CartridgeAmmoComponent>(entity, out var cartridge))
                _prototypes.TryIndex(cartridge.Prototype, out prototype);
            else if (TryComp<HitscanBasicRaycastComponent>(entity, out var hitscan))
                return new ShotCollision((int) hitscan.CollisionMask, Hitscan: true);
            else if (TryComp<FixturesComponent>(entity, out var fixtures))
                return ProjectileShotCollision(fixtures, CompOrNull<ProjectileComponent>(entity));
        }
        else if (ammo.NextAmmoPrototype is { } id)
            _prototypes.TryIndex(id, out prototype);
        if (prototype == null)
            return DefaultShotCollision;
        var ammoPrototype = prototype;
        if (_projectileCollisions.TryGetValue(ammoPrototype, out var cached))
            return cached;
        var result = DefaultShotCollision;
        for (var depth = 0; prototype != null && depth < 8; depth++)
        {
            if (prototype.TryGetComponent<CartridgeAmmoComponent>(out var cartridge, _squadComponents) &&
                _prototypes.TryIndex(cartridge.Prototype, out prototype))
                continue;
            if (prototype != null && prototype.TryGetComponent<HitscanBasicRaycastComponent>(out var hitscan, _squadComponents))
            {
                result = new ShotCollision((int) hitscan.CollisionMask, Hitscan: true);
                break;
            }
            if (prototype != null && prototype.TryGetComponent<FixturesComponent>(out var fixtures, _squadComponents))
            {
                prototype.TryGetComponent<ProjectileComponent>(out var projectile, _squadComponents);
                result = ProjectileShotCollision(fixtures, projectile);
            }
            break;
        }
        _projectileCollisions[ammoPrototype] = result;
        return result;
    }

    private static ShotCollision ProjectileShotCollision(FixturesComponent fixtures, ProjectileComponent? projectile)
    {
        // The fly-by sensor is not the fixture that impacts scenery. Match ProjectileSystem's fixture.
        if (!fixtures.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var fixture))
            return DefaultShotCollision;

        var shape = fixture.Shape;
        if (shape is PhysShapeAabb aabb)
        {
            var polygon = new PolygonShape();
            polygon.SetAsBox(aabb.LocalBounds);
            shape = polygon;
        }
        return new ShotCollision(fixture.CollisionMask, Shape: shape, Angle: projectile?.Angle ?? Angle.Zero);
    }

    private readonly record struct ShotCollision(int Mask, bool Hitscan = false, IPhysShape? Shape = null, Angle Angle = default);

    private bool SquadShotBlocked(EntityUid uid, EntityUid target, MapCoordinates origin, MapCoordinates destination,
        ShotCollision collision, float spread, Box2[]? reflectingFields = null)
        => ShotPathBlocked(uid, target, origin, destination, collision) ||
           HolographicShotBlocked(origin, destination, spread, attackMatrices: true, reflectingFields: reflectingFields);

    private bool ShotPathBlocked(EntityUid uid, EntityUid target, MapCoordinates origin, MapCoordinates destination,
        ShotCollision collision)
    {
        var delta = destination.Position - origin.Position;
        if (origin.MapId != destination.MapId || delta.LengthSquared() < 0.001f)
            return true;
        var length = delta.Length();
        if (collision.Hitscan)
        {
            // Hitscan damage uses the fixture bounds, including the bounds of angled walls.
            var ray = new CollisionRay(origin.Position, delta / length, collision.Mask);
            if (_squadPhysics.IntersectRayWithPredicate(origin.MapId, ray, length, IgnoreShotObstacle).Any())
                return true;
            return ShotEndsInDoor(destination, collision.Mask);
        }
        else
        {
            // ShootProjectile rotates the projectile to the firing direction, not the shooter's facing.
            var start = new Robust.Shared.Physics.Transform(origin.Position, delta.ToWorldAngle() + collision.Angle);
            if (_squadRayCast.CastShape(origin.MapId, collision.Shape ?? ShotPathShape, start, delta, new QueryFilter
                {
                    MaskBits = (uint) collision.Mask,
                    IsIgnored = IgnoreShotObstacle,
                }, RayCastSystem.RayCastClosestCallback).Hit)
                return true;

            return ShotLineBlocked(uid, target, origin, destination, collision);
        }

        bool IgnoreShotObstacle(EntityUid entity)
            => IgnoreShotEntity(entity, uid, target);
    }

    private bool IgnoreShotEntity(EntityUid entity, EntityUid shooter, EntityUid target)
        => entity == shooter || entity == target || HasComp<NpcFactionMemberComponent>(entity) ||
           CompOrNull<RequireProjectileTargetComponent>(entity)?.Active == true ||
           HologramPrototype(entity, "HolosignBulletShield");

    private bool ShotLineBlocked(EntityUid uid, EntityUid target, MapCoordinates origin, MapCoordinates destination,
        ShotCollision collision)
    {
        var delta = destination.Position - origin.Position;
        if (origin.MapId != destination.MapId || delta.LengthSquared() < 0.001f)
            return true;
        if (ShotEndsInDoor(destination, collision.Mask))
            return true;
        // Shape casts omit initial overlaps. Check all hard obstacles on the line, including
        // windows and machinery, using their real shape rather than treating a whole tile as solid.
        var ray = new CollisionRay(origin.Position, Vector2.Normalize(delta), collision.Mask);
        return _squadPhysics.IntersectRayWithPredicate(origin.MapId, ray,
            (System: this, Origin: origin.Position, Delta: delta, Mask: collision.Mask, Shooter: uid, Target: target),
            static (entity, line) => line.System.IgnoreShotEntity(entity, line.Shooter, line.Target) ||
                !line.System.ShotIntersectsObstacle(entity, line.Origin, line.Delta, line.Mask), delta.Length()).Any();
    }

    private bool ShotEndsInDoor(MapCoordinates destination, int mask)
    {
        // Small creatures can stand inside a closed airlock. Do not rely solely on a cast ending
        // inside its fixture: explicitly check the endpoint, without scanning the surrounding area.
        if (_turf.GetTileRef(_transform.ToCoordinates(destination)) is not { } tile ||
            !TryComp<MapGridComponent>(tile.GridUid, out var grid))
            return false;
        var anchored = _maps.GetAnchoredEntitiesEnumerator(tile.GridUid, grid, tile.GridIndices);
        while (anchored.MoveNext(out var entity))
        {
            if (entity is { } obstacle && HasComp<DoorComponent>(obstacle) &&
                ShotIntersectsObstacle(obstacle, destination.Position, Vector2.Zero, mask))
                return true;
        }
        return false;
    }

    private bool ShotIntersectsObstacle(EntityUid entity, Vector2 origin, Vector2 delta, int mask)
    {
        if (!TryComp<PhysicsComponent>(entity, out var body) || !body.CanCollide ||
            !TryComp<FixturesComponent>(entity, out var fixtures))
            return false;

        var transform = _squadPhysics.GetPhysicsTransform(entity);
        var start = Robust.Shared.Physics.Transform.InvTransformPoint(transform, origin);
        var direction = Quaternion2D.InvRotateVector(transform.Quaternion2D, delta);
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard || (fixture.CollisionLayer & mask) == 0)
                continue;
            if (fixture.Shape is PolygonShape polygon && ShotIntersectsPolygon(start, direction, polygon))
                return true;
            if (fixture.Shape is PhysShapeCircle circle)
            {
                var lengthSquared = direction.LengthSquared();
                var fraction = lengthSquared > 0f
                    ? Math.Clamp(Vector2.Dot(circle.Position - start, direction) / lengthSquared, 0f, 1f)
                    : 0f;
                if (Vector2.DistanceSquared(start + direction * fraction, circle.Position) <= circle.Radius * circle.Radius)
                    return true;
            }
        }
        return false;
    }

    private static bool ShotIntersectsPolygon(Vector2 origin, Vector2 direction, PolygonShape polygon)
    {
        var enter = 0f;
        var exit = 1f;
        for (var i = 0; i < polygon.VertexCount; i++)
        {
            var normal = polygon.Normals[i];
            var distance = Vector2.Dot(normal, origin - polygon.Vertices[i]) - polygon.Radius;
            var travel = Vector2.Dot(normal, direction);
            if (MathF.Abs(travel) < 0.00001f)
            {
                if (distance > 0f)
                    return false;
                continue;
            }
            var crossing = -distance / travel;
            if (travel < 0f)
                enter = MathF.Max(enter, crossing);
            else
                exit = MathF.Min(exit, crossing);
            if (enter > exit)
                return false;
        }
        return true;
    }

    private void RepositionBlockedFire(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        actor.FireBlockedUntil = _timing.CurTime + TimeSpan.FromSeconds(4);
        if (_timing.CurTime < actor.NextFireReposition ||
            actor.SquadOrder is { } order && order.Until > _timing.CurTime &&
            order.Task is MilitiaSquadTask.Retreat or MilitiaSquadTask.Evade or MilitiaSquadTask.SeekMedic or MilitiaSquadTask.Separate or MilitiaSquadTask.Breakout or MilitiaSquadTask.Melee or MilitiaSquadTask.MedicWithdraw)
            return;
        actor.NextFireReposition = _timing.CurTime + TimeSpan.FromSeconds(1);
        if (actor.SquadPosition.IsValid(EntityManager))
            actor.RejectedSquadPositions[actor.SquadPosition] = _timing.CurTime + TimeSpan.FromSeconds(2);
        actor.SquadOrder = null;
        _urgentSquadOrders.Add(uid);
    }
}
