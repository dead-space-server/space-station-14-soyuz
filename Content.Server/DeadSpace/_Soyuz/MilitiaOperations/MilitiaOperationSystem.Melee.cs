// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Map;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    private static readonly float[] MeleeApproachAngles = [0.65f, 1.15f, 0f, -0.65f, -1.15f];

    private bool MeleePursuit(EntityUid uid, MilitiaOperationActorComponent actor)
        => DirectContact(uid, actor) && !MedicSupportPriority(uid, actor) &&
           (!actor.Medic || actor.DamageThreatUntil > _timing.CurTime) &&
           OwnsItem(uid, actor.Backup) && HasComp<MeleeWeaponComponent>(actor.Backup) &&
           DesiredWeapon(uid, actor) == actor.Backup &&
           OnDefensiveGrid(uid, Transform(actor.DirectTarget).Coordinates) &&
           SquadLineClear(uid, actor.DirectTarget);

    private bool ValidMeleeOrder(EntityUid uid, MilitiaOperationActorComponent actor, MilitiaSquadOrder order)
        => MeleePursuit(uid, actor) && MeleeDestinationInReach(actor, order);

    private bool MeleeDestinationInReach(MilitiaOperationActorComponent actor, MilitiaSquadOrder order)
        => actor.DirectTarget == order.Target &&
           TryComp<MeleeWeaponComponent>(actor.Backup, out var melee) &&
           (ReferenceEquals(actor.RunningSquadOrder, order)
               ? actor.SquadPosition.IsValid(EntityManager) && Vector2.DistanceSquared(_transform.ToMapCoordinates(actor.SquadPosition).Position,
                   _transform.GetWorldPosition(order.Target)) <= melee.Range * melee.Range
               : order.Positions.Any(position => position.IsValid(EntityManager) &&
                   Vector2.DistanceSquared(_transform.ToMapCoordinates(position).Position,
                       _transform.GetWorldPosition(order.Target)) <= melee.Range * melee.Range));

    private EntityCoordinates[] MeleePositions(EntityUid uid, MilitiaOperationActorComponent actor, List<MapCoordinates> occupied)
    {
        if (!TryComp<MeleeWeaponComponent>(actor.Backup, out var melee) ||
            Transform(uid).GridUid is not { } grid)
            return Array.Empty<EntityCoordinates>();
        var origin = _transform.GetMapCoordinates(uid);
        var target = _transform.GetMapCoordinates(actor.DirectTarget);
        var axis = origin.Position - target.Position;
        axis = axis.LengthSquared() < 0.001f ? Vector2.UnitX : Vector2.Normalize(axis);
        var radius = Math.Clamp(melee.Range - 0.35f, 0.7f, 1.1f);
        var home = _transform.ToMapCoordinates(actor.DefensivePosition);
        var positions = new List<EntityCoordinates>(MeleeApproachAngles.Length);
        foreach (var angle in MeleeApproachAngles)
        {
            var position = new MapCoordinates(target.Position + new Angle(angle * actor.StrafeSide).RotateVec(axis) * radius, target.MapId);
            var coordinates = _transform.ToCoordinates(grid, position);
            if (Vector2.DistanceSquared(origin.Position, position.Position) < 0.0625f ||
                !OnDefensiveGrid(uid, coordinates) || _turf.GetTileRef(coordinates) is not { } tile || tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile) ||
                Vector2.DistanceSquared(position.Position, home.Position) > actor.DefensiveRange * actor.DefensiveRange ||
                actor.RejectedSquadPositions.GetValueOrDefault(coordinates) > _timing.CurTime ||
                occupied.Any(other => other.MapId == position.MapId && Vector2.DistanceSquared(other.Position, position.Position) < 0.81f) ||
                !SquadPositionAvailable(uid, coordinates) || !SquadPositionClear(uid, position) ||
                !SquadLineClear(position, target) ||
                !SquadRouteClear(uid, origin, position, entity => entity == actor.DirectTarget || CanCrossHolographicBarrier(uid, entity)))
                continue;
            positions.Add(coordinates);
        }
        return positions.ToArray();
    }
}
