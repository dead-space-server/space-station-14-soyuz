// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Numerics;
using Content.Server.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Ranged.Events;
using Microsoft.Extensions.ObjectPool;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    private const float RangedResponseRange = 40f;
    [Dependency] private readonly NPCSystem _actorNpcs = default!;
    private readonly ObjectPool<HashSet<Entity<MilitiaOperationActorComponent>>> _hitscanActors =
        new DefaultObjectPool<HashSet<Entity<MilitiaOperationActorComponent>>>(new SetPolicy<Entity<MilitiaOperationActorComponent>>());

    private MapCoordinates? DamageSourcePosition(EntityUid uid, EntityUid? source)
        => source is { } other && other != uid && Exists(other) && !TerminatingOrDeleted(other) &&
           Transform(other).MapID == Transform(uid).MapID && !_transform.ContainsEntity(uid, (other, Transform(other)))
            ? _transform.GetMapCoordinates(other) : null;

    private void OnActorProjectileHit(Entity<ProjectileComponent> projectile, ref ProjectileHitEvent args)
    {
        if (!TryComp<MilitiaOperationActorComponent>(args.Target, out var actor) || !args.Damage.AnyPositive())
            return;
        var threat = DamageSourcePosition(args.Target, projectile.Comp.Weapon) ?? DamageSourcePosition(args.Target, args.Shooter);
        if (threat == null)
        {
            var velocity = _squadPhysics.GetMapLinearVelocity(new EntityCoordinates(projectile.Owner, Vector2.Zero));
            if (velocity.LengthSquared() > 0.01f)
            {
                var position = _transform.GetMapCoordinates(args.Target);
                threat = new MapCoordinates(position.Position - Vector2.Normalize(velocity) * actor.ThreatRange, position.MapId);
            }
        }
        AlertRangedActor((args.Target, actor), threat, args.Shooter ?? projectile.Comp.Weapon, hit: true);
    }

    private void OnActorHitscanHit(Entity<MilitiaOperationActorComponent> actor, ref HitscanRaycastStrikeEvent args)
    {
        var threat = HitscanPath(args.Data, out var origin, out _) ? origin :
            DamageSourcePosition(actor.Owner, args.Data.Shooter) ?? DamageSourcePosition(actor.Owner, args.Data.Gun);
        if (threat == null && args.Data.ShotDirection.LengthSquared() > 0.01f)
        {
            var position = _transform.GetMapCoordinates(actor.Owner);
            threat = new MapCoordinates(position.Position - Vector2.Normalize(args.Data.ShotDirection) * actor.Comp.ThreatRange, position.MapId);
        }
        AlertRangedActor(actor, threat, args.Data.Shooter ?? args.Data.Gun, hit: true);
    }

    private void OnActorTargetedShot(Entity<MilitiaOperationActorComponent> actor, ref GunShotAtTargetEvent args)
    {
        if (!actor.Comp.Medic || actor.Comp.MedicWithdrawUntil > _timing.CurTime + TimeSpan.FromSeconds(3.75) ||
            !AvailableActor(actor.Owner, out _) || _factions.IsEntityFriendly(actor.Owner, args.Shooter) ||
            DamageSourcePosition(actor.Owner, args.Shooter) is not { } source)
            return;
        AlertRangedActor(actor, source, args.Shooter);
    }

    private bool HitscanPath(HitscanRaycastFiredData data, out MapCoordinates origin, out MapCoordinates end)
    {
        origin = end = default;
        if (data.OutputTrace is not { Count: > 0 } traces || data.ShotDirection.LengthSquared() < 0.001f)
            return false;
        var trace = traces[^1];
        var hit = GetNetEntity(data.HitEntity);
        for (var index = traces.Count - 1; index >= 0; index--)
        {
            if (traces[index].ImpactedEnt != hit)
                continue;
            trace = traces[index];
            break;
        }
        var coordinates = GetCoordinates(trace.ImpactCoordinates);
        if (!coordinates.IsValid(EntityManager))
            return false;
        end = _transform.ToMapCoordinates(coordinates);
        origin = new MapCoordinates(end.Position - Vector2.Normalize(data.ShotDirection) * trace.Distance, end.MapId);
        return true;
    }

    private void AlertRangedActor(Entity<MilitiaOperationActorComponent> actor, MapCoordinates? threat, EntityUid? attacker, bool hit = false)
    {
        var wasEscaping = actor.Comp.RangedDangerUntil > _timing.CurTime;
        AlertActor(actor, threat);
        if (!AvailableActor(actor.Owner, out _))
            return;
        if (actor.Comp.Medic && (hit || threat != null))
            actor.Comp.MedicWithdrawUntil = _timing.CurTime + TimeSpan.FromSeconds(4);
        if (threat is not { } source)
            return;
        var position = _transform.GetMapCoordinates(actor.Owner);
        if (position.MapId != source.MapId)
            return;
        if (attacker is { } hostile && SquadThreat(actor.Owner, hostile))
        {
            actor.Comp.RangedAttacker = hostile;
            actor.Comp.RangedAttackerUntil = _timing.CurTime + actor.Comp.ContactMemory;
        }
        var sameGrid = Transform(actor.Owner).GridUid is { } grid &&
            _turf.GetTileRef(_transform.ToCoordinates(source))?.GridUid == grid;
        // Being able to answer with a laser does not make a blocked primary firing lane safe.
        var weapon = UsingSecondary(actor.Comp) ? actor.Comp.PrimaryWeapon : actor.Comp.Weapon;
        if (Vector2.DistanceSquared(position.Position, source.Position) <= actor.Comp.SquadVision * actor.Comp.SquadVision &&
            sameGrid && !FindTransparentBarrier(position, source, includeGirders: true).IsValid() &&
            !SquadShotBlocked(actor.Owner, EntityUid.Invalid, position, source, GetShotCollision(weapon), 0f))
            return;
        RememberWindowThreat(actor.Owner, actor.Comp, source);
        if (hit)
        {
            actor.Comp.LastRangedImpact = Transform(actor.Owner).Coordinates;
            // Keep a confirmed impact unsafe beyond the movement-stall timeout (1.5 seconds).
            actor.Comp.LastRangedImpactUntil = _timing.CurTime + TimeSpan.FromSeconds(2);
        }
        actor.Comp.RangedDangerUntil = actor.Comp.WindowHoldUntil;
        actor.Comp.WindowExposed = true;
        actor.Comp.ExposedToThreat = true;
        if (!wasEscaping)
        {
            // Interrupt an idle/combat plan once on entering danger, not on every incoming laser tick.
            actor.Comp.NextRangedRetreatSearch = TimeSpan.Zero;
            actor.Comp.RetreatRetryAfter = TimeSpan.Zero;
            actor.Comp.NextBreakoutAttempt = TimeSpan.Zero;
            InterruptActor(actor.Owner);
        }
    }

    private void OnThreateningHitscan(ref HitscanRaycastFiredEvent args)
    {
        if (!EntityQueryEnumerator<MilitiaOperationActorComponent>().MoveNext(out _) ||
            !TryComp<HitscanBasicDamageComponent>(args.Data.Hitscan, out var damage) || !damage.Damage.AnyPositive() ||
            !HitscanPath(args.Data, out var origin, out var end))
            return;
        var delta = end.Position - origin.Position;
        var lengthSquared = delta.LengthSquared();
        if (lengthSquared < 0.001f)
            return;
        var center = (origin.Position + end.Position) / 2f;
        var bounds = new Box2Rotated(Box2.CenteredAround(center, new Vector2(MathF.Sqrt(lengthSquared) + 4.5f, 4.5f)),
            delta.ToAngle(), center);
        var candidates = _hitscanActors.Get();
        try
        {
            _squadLookup.GetEntitiesIntersecting(origin.MapId, bounds, candidates);
            var attacker = args.Data.Shooter ?? args.Data.Gun;
            foreach (var actor in candidates)
            {
                if (actor.Owner == args.Data.HitEntity || actor.Owner == args.Data.Shooter ||
                    TerminatingOrDeleted(actor.Owner) || MetaData(actor.Owner).EntityPaused)
                    continue;
                var position = _transform.GetMapCoordinates(actor.Owner);
                var along = Vector2.Dot(position.Position - origin.Position, delta) / lengthSquared;
                if (along < 0f || along > 1f)
                    continue;
                var closest = new MapCoordinates(origin.Position + delta * along, origin.MapId);
                if (Vector2.DistanceSquared(position.Position, closest.Position) <= 1.5625f &&
                    AvailableActor(actor.Owner, out _) && !_factions.IsEntityFriendly(actor.Owner, attacker) &&
                    CoverExposed(position, closest))
                    AlertRangedActor(actor, origin, attacker);
            }
        }
        finally
        {
            _hitscanActors.Return(candidates);
        }
    }
}
