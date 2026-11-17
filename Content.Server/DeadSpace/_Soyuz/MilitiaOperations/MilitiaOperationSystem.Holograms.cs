// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.DoAfter;
using Content.Shared.NPC.Components;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Map;
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
    private readonly Dictionary<(EntityPrototype Prototype, string Ancestor), bool> _hologramAncestors = new();
    private readonly Dictionary<EntityPrototype, long> _projectileMasks = new();

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
        var ray = new CollisionRay(origin.Position, direction, (int) CollisionGroup.MobMask);
        foreach (var hit in _squadPhysics.IntersectRayWithPredicate(origin.MapId, ray, delta.Length() + 0.45f,
                     predicate: entity => entity == uid).OrderBy(hit => hit.Distance))
        {
            var actor = Comp<MilitiaOperationActorComponent>(uid);
            if (HologramPrototype(hit.HitEntity, "HolosignSecurity") && actor.BarrierClimbRetryAfter > _timing.CurTime)
            {
                blocked = true;
                return false;
            }
            if (!CanCrossHolographicBarrier(uid, hit.HitEntity))
                return false;
            var surface = Comp<ClimbableComponent>(hit.HitEntity);
            if (!_barrierClimb.CanVault(surface, uid, hit.HitEntity, out _))
                return false;
            var center = _transform.GetMapCoordinates(hit.HitEntity);
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
            if (!_barrierClimb.TryClimb(uid, uid, hit.HitEntity, out var action, surface, climbing))
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

    private bool HolographicShotBlocked(MapCoordinates origin, MapCoordinates target, float spread = 0f, bool attackMatrices = false)
    {
        var delta = target.Position - origin.Position;
        if (origin.MapId != target.MapId || delta.LengthSquared() < 0.001f)
            return false;
        var direction = Vector2.Normalize(delta);
        var ray = new CollisionRay(origin.Position, direction, (int) CollisionGroup.BulletImpassable);
        if (_squadPhysics.IntersectRayWithPredicate(origin.MapId, ray, delta.Length(),
                predicate: entity => !HolographicProjectileBarrier(entity) ||
                    attackMatrices && HologramPrototype(entity, "HolosignBulletShield")).Any())
            return true;
        if (spread <= 0f)
            return false;
        foreach (var field in _squadLookup.GetEntitiesInRange<ReflectComponent>(origin, 24f))
        {
            if (!HolographicProjectileBarrier(field.Owner) || field.Comp.ReflectProb <= 0f ||
                !_interaction.InRangeUnobstructed(origin, _transform.GetMapCoordinates(field.Owner), 0, CollisionGroup.Opaque))
                continue;
            var bounds = _squadPhysics.GetWorldAABB(field.Owner);
            var offset = bounds.Center - origin.Position;
            var along = Vector2.Dot(offset, direction);
            var radius = bounds.Size.Length() / 2f + 0.15f;
            if (along + radius > 0f && along - radius < 24f &&
                MathF.Abs(offset.X * direction.Y - offset.Y * direction.X) < radius + MathF.Max(0f, along) * spread)
                return true;
        }
        return false;
    }

    private long ShotCollisionMask(EntityUid weapon)
    {
        const long fallback = (long) (CollisionGroup.Impassable | CollisionGroup.BulletImpassable);
        if (!HasComp<GunComponent>(weapon))
            return fallback;
        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(weapon, ref ammo);
        EntityPrototype? prototype = null;
        if (ammo.NextAmmoEntity is { } entity)
        {
            if (TryComp<CartridgeAmmoComponent>(entity, out var cartridge))
                _prototypes.TryIndex(cartridge.Prototype, out prototype);
            else if (TryComp<HitscanBasicRaycastComponent>(entity, out var hitscan))
                return (long) hitscan.CollisionMask;
            else if (TryComp<FixturesComponent>(entity, out var fixtures))
                return fixtures.Fixtures.Values.Aggregate(0L, (mask, fixture) => mask | (uint) fixture.CollisionMask);
        }
        else if (ammo.NextAmmoPrototype is { } id)
            _prototypes.TryIndex(id, out prototype);
        if (prototype == null)
            return fallback;
        var ammoPrototype = prototype;
        if (_projectileMasks.TryGetValue(ammoPrototype, out var cached))
            return cached;
        var result = fallback;
        for (var depth = 0; prototype != null && depth < 8; depth++)
        {
            if (prototype.TryGetComponent<CartridgeAmmoComponent>(out var cartridge, _squadComponents) &&
                _prototypes.TryIndex(cartridge.Prototype, out prototype))
                continue;
            if (prototype != null && prototype.TryGetComponent<HitscanBasicRaycastComponent>(out var hitscan, _squadComponents))
            {
                result = (long) hitscan.CollisionMask;
                break;
            }
            if (prototype != null && prototype.TryGetComponent<FixturesComponent>(out var fixtures, _squadComponents))
                result = fixtures.Fixtures.Values.Aggregate(0L, (mask, fixture) => mask | (uint) fixture.CollisionMask);
            break;
        }
        _projectileMasks[ammoPrototype] = result;
        return result;
    }

    private bool SquadShotBlocked(EntityUid uid, EntityUid target, MapCoordinates origin, MapCoordinates destination,
        long collisionMask, float spread)
    {
        var delta = destination.Position - origin.Position;
        if (origin.MapId != destination.MapId || delta.LengthSquared() < 0.001f)
            return true;
        var start = _squadPhysics.GetPhysicsTransform(uid);
        start.Position = origin.Position;
        if (_squadRayCast.CastShape(origin.MapId, ShotPathShape, start, delta, new QueryFilter
            {
                MaskBits = collisionMask,
                IsIgnored = entity => entity == uid || entity == target || HasComp<NpcFactionMemberComponent>(entity) ||
                    CompOrNull<RequireProjectileTargetComponent>(entity)?.Active == true ||
                    HologramPrototype(entity, "HolosignBulletShield"),
            }, RayCastSystem.RayCastClosestCallback).Hit)
            return true;
        return HolographicShotBlocked(origin, destination, spread, attackMatrices: true);
    }

    private void RepositionBlockedFire(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        actor.FireBlockedUntil = _timing.CurTime + TimeSpan.FromSeconds(4);
        if (_timing.CurTime < actor.NextFireReposition ||
            actor.SquadOrder is { } order && order.Until > _timing.CurTime &&
            order.Task is MilitiaSquadTask.Retreat or MilitiaSquadTask.Evade or MilitiaSquadTask.SeekMedic or MilitiaSquadTask.Separate)
            return;
        actor.NextFireReposition = _timing.CurTime + TimeSpan.FromSeconds(1);
        if (actor.SquadPosition.IsValid(EntityManager))
            actor.RejectedSquadPositions[actor.SquadPosition] = _timing.CurTime + TimeSpan.FromSeconds(2);
        actor.SquadOrder = null;
        _nextSquadUpdate = _timing.CurTime;
    }
}
