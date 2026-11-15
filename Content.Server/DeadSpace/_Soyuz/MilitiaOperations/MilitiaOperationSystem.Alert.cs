// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Numerics;
using Content.Server.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.Map;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    [Dependency] private readonly NPCSystem _actorNpcs = default!;

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
        AlertActor((args.Target, actor), threat);
    }

    private void OnActorHitscanHit(Entity<MilitiaOperationActorComponent> actor, ref HitscanRaycastStrikeEvent args)
    {
        var threat = DamageSourcePosition(actor.Owner, args.Data.Gun) ?? DamageSourcePosition(actor.Owner, args.Data.Shooter);
        if (threat == null && args.Data.ShotDirection.LengthSquared() > 0.01f)
        {
            var position = _transform.GetMapCoordinates(actor.Owner);
            threat = new MapCoordinates(position.Position - Vector2.Normalize(args.Data.ShotDirection) * actor.Comp.ThreatRange, position.MapId);
        }
        AlertActor(actor, threat);
    }
}
