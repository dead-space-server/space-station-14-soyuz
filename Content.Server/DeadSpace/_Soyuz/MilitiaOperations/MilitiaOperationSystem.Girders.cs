// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Numerics;
using Content.Server.Destructible;
using Content.Shared.Damage.Components;
using Content.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    private bool BreachableGirder(EntityUid uid, EntityUid girder)
        => HologramPrototype(girder, "Girder") && HasComp<DestructibleComponent>(girder) &&
           HasComp<DamageableComponent>(girder) && _squadPhysics.IsCurrentlyHardCollidable(uid, girder) &&
           OnDefensiveGrid(uid, Transform(girder).Coordinates);

    private bool GirderAssault(MilitiaOperationActorComponent actor, EntityUid enemy)
        => actor.GirderEnemy == enemy && actor.GirderPushUntil > _timing.CurTime;

    private void RejectGirderShot(MilitiaOperationActorComponent actor, EntityUid target)
    {
        if (target != actor.GirderTarget)
            return;
        actor.GirderTarget = EntityUid.Invalid;
        actor.NextGirderScan = _timing.CurTime + TimeSpan.FromSeconds(0.5);
    }

    private void RefreshGirderBreach(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid enemy)
    {
        if (!actor.GirderTarget.IsValid())
            return;
        if (actor.GirderEnemy != enemy)
        {
            actor.GirderTarget = EntityUid.Invalid;
            actor.GirderPushUntil = TimeSpan.Zero;
            return;
        }
        if (!BreachableGirder(uid, actor.GirderTarget))
        {
            actor.GirderTarget = EntityUid.Invalid;
            // Reuse the existing validated assault routes and grenade priorities after opening the passage.
            actor.GirderPushUntil = _timing.CurTime + TimeSpan.FromSeconds(8);
            actor.NextBreakoutAttempt = TimeSpan.Zero;
            actor.RetreatRetryAfter = TimeSpan.Zero;
            _urgentSquadOrders.Add(uid);
        }
        else if (actor.GirderBreachUntil <= _timing.CurTime)
        {
            actor.GirderTarget = EntityUid.Invalid;
            actor.NextGirderScan = _timing.CurTime + TimeSpan.FromSeconds(3);
        }
    }

    private bool ValidGirderShot(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid target)
    {
        if (target != actor.GirderTarget || actor.GirderBreachUntil <= _timing.CurTime ||
            actor.Grenades.Length > 0 || !BreachableGirder(uid, target) ||
            !BreakoutTargetInArea(uid, actor, actor.GirderEnemy) ||
            !SquadVisible(uid, actor.GirderEnemy, CombatVision(actor, actor.GirderEnemy)))
            return false;
        var origin = _transform.GetWorldPosition(uid);
        var enemy = _transform.GetWorldPosition(actor.GirderEnemy) - origin;
        var girder = _transform.GetWorldPosition(target) - origin;
        var lengthSquared = enemy.LengthSquared();
        if (lengthSquared < 0.01f)
            return false;
        var fraction = Vector2.Dot(girder, enemy) / lengthSquared;
        return fraction > 0f && fraction < 1f && Vector2.DistanceSquared(girder, enemy * fraction) <= 0.81f;
    }

    private bool TryGirderFireTarget(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid enemy, out EntityUid girder)
    {
        girder = EntityUid.Invalid;
        if (actor.Grenades.Length > 0 || !BreakoutTargetInArea(uid, actor, enemy))
            return false;
        if (ValidGirderShot(uid, actor, actor.GirderTarget))
        {
            girder = actor.GirderTarget;
            return true;
        }
        if (_timing.CurTime < actor.NextGirderScan)
            return false;
        actor.NextGirderScan = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        var collision = GetShotCollision(actor.Weapon);
        if (collision.Hitscan)
            return false;
        var origin = _transform.GetMapCoordinates(uid);
        var destination = _transform.GetMapCoordinates(enemy);
        var delta = destination.Position - origin.Position;
        if (origin.MapId != destination.MapId || delta.LengthSquared() < 0.01f || delta.LengthSquared() > 144f)
            return false;
        // Only the first actual obstruction on this firing lane is eligible, never a wall behind it.
        var hit = _squadRayCast.CastShape(origin.MapId, ShotPathShape, _squadPhysics.GetPhysicsTransform(uid), delta,
            new QueryFilter
            {
                MaskBits = (uint) (collision.Mask | (int) CollisionGroup.MobMask),
                IsIgnored = entity => entity == uid || entity == enemy ||
                    !TryComp<PhysicsComponent>(entity, out var body) || !body.CanCollide,
            }, RayCastSystem.RayCastClosestCallback);
        if (!hit.Hit || !BreachableGirder(uid, hit.Entity))
            return false;
        var position = _transform.GetMapCoordinates(hit.Entity);
        var spread = HolographicFireSpread(actor);
        if (SquadShotBlocked(uid, hit.Entity, origin, position, collision, spread) ||
            SquadFireLaneBlocked(uid, hit.Entity, origin, position, spread, out _))
            return false;
        actor.GirderTarget = hit.Entity;
        actor.GirderEnemy = enemy;
        actor.GirderBreachUntil = _timing.CurTime + TimeSpan.FromSeconds(5);
        actor.GirderPushUntil = TimeSpan.Zero;
        girder = hit.Entity;
        return true;
    }
}
