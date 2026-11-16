// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    [Dependency] private readonly ClimbSystem _barrierClimb = default!;

    private bool HologramPrototype(EntityUid uid, string id)
        => !TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid) &&
           MetaData(uid).EntityPrototype is { } prototype &&
           _prototypes.EnumerateParents(prototype, includeSelf: true).Any(parent => parent.ID == id);

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

    private EntityUid? BlockingBulletMatrix(MapCoordinates origin, MapCoordinates target)
    {
        var delta = target.Position - origin.Position;
        if (origin.MapId != target.MapId || delta.LengthSquared() < 0.001f)
            return null;
        var ray = new CollisionRay(origin.Position, Vector2.Normalize(delta), (int) CollisionGroup.BulletImpassable);
        foreach (var hit in _squadPhysics.IntersectRayWithPredicate(origin.MapId, ray, delta.Length(),
                     predicate: entity => !HolographicProjectileBarrier(entity)).OrderBy(hit => hit.Distance))
            return HologramPrototype(hit.HitEntity, "HolosignBulletShield") ? hit.HitEntity : null;
        return null;
    }

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

    private void RepositionFromHologram(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        actor.FireBlockedUntil = _timing.CurTime + TimeSpan.FromSeconds(4);
        if (_timing.CurTime < actor.NextFireReposition ||
            actor.RunningSquadOrder?.Task is MilitiaSquadTask.Retreat or MilitiaSquadTask.Evade or MilitiaSquadTask.SeekMedic or MilitiaSquadTask.Separate)
            return;
        actor.NextFireReposition = _timing.CurTime + TimeSpan.FromSeconds(1);
        if (actor.SquadPosition.IsValid(EntityManager))
            actor.RejectedSquadPositions[actor.SquadPosition] = _timing.CurTime + TimeSpan.FromSeconds(2);
        actor.SquadOrder = null;
        _nextSquadUpdate = _timing.CurTime;
    }
}
