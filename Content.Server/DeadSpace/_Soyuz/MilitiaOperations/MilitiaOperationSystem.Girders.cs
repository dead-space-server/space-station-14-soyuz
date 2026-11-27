// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.Destructible;
using Content.Server.NPC.HTN;
using Content.Shared.Damage.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.EnergySword;
using Robust.Shared.Map;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    private readonly Dictionary<EntityUid, EntityUid[]> _girdersNearBarrier = new();

    private bool BreachableGirder(EntityUid uid, EntityUid girder)
        => HasPrototypeAncestor(girder, "Girder") && HasComp<DestructibleComponent>(girder) &&
           HasComp<DamageableComponent>(girder) && _squadPhysics.IsCurrentlyHardCollidable(uid, girder) &&
           OnDefensiveGrid(uid, Transform(girder).Coordinates);

    private bool GirderAssault(MilitiaOperationActorComponent actor, EntityUid enemy)
        => actor.GirderEnemy == enemy && actor.GirderPushUntil > _timing.CurTime;

    private void MarkGirderAssault(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid enemy)
    {
        var continuing = GirderAssault(actor, enemy);
        actor.GirderEnemy = enemy;
        actor.GirderPushUntil = _timing.CurTime + TimeSpan.FromSeconds(8);
        if (continuing)
            return;

        actor.GirderPushExit = default;
        actor.NextBreakoutAttempt = TimeSpan.Zero;
        actor.RetreatRetryAfter = TimeSpan.Zero;
        if (actor.SquadOrder?.Task is MilitiaSquadTask.Advance or MilitiaSquadTask.Cover or MilitiaSquadTask.Peek)
            actor.SquadOrder = null;
        _urgentSquadOrders.Add(uid);
    }

    private bool ValidGirderBreach(EntityUid uid, MilitiaOperationActorComponent actor, MilitiaSquadOrder order)
        => order.Until > _timing.CurTime && actor.Grenades.Length == 0 && !actor.TriageRequired &&
           !MedicSupportPriority(actor) && OwnsItem(uid, actor.Backup) && HasComp<EnergySwordComponent>(actor.Backup) &&
           BreakoutTargetInArea(uid, actor, order.Target) && BreachableGirder(uid, order.Girder) &&
           (DirectContact(uid, actor)
               ? actor.DirectTarget == order.Target && (order.GirderExit.IsValid(EntityManager)
                   ? actor.TransparentBarrier.IsValid() : actor.TransparentBarrier == order.Girder)
               : RecentContact(actor, order.Target));

    private MilitiaSquadOrder? PlanGirderBreach(EntityUid uid, MilitiaOperationActorComponent actor,
        HashSet<EntityUid> assigned, List<MapCoordinates> occupied, MilitiaSquadContact[] contacts)
    {
        if (actor.SquadOrder is { Task: MilitiaSquadTask.BreachGirder } current &&
            !assigned.Contains(current.Girder) && ValidGirderBreach(uid, actor, current))
            return current;

        var barrier = actor.TransparentBarrier;
        if (!barrier.IsValid() || assigned.Contains(barrier) || _timing.CurTime < actor.NextGirderBreach ||
            actor.TriageRequired || !DirectContact(uid, actor) || !OwnsItem(uid, actor.Backup) ||
            !HasComp<EnergySwordComponent>(actor.Backup) || !TryComp<MeleeWeaponComponent>(actor.Backup, out var melee) ||
            !BreakoutTargetInArea(uid, actor, actor.DirectTarget) ||
            Transform(uid).GridUid is not { } grid)
            return null;

        var origin = _transform.GetMapCoordinates(uid);
        var reach = MathF.Min(melee.Range - 0.15f, 1.25f);
        if (!BreachableGirder(uid, barrier))
        {
            actor.NextGirderBreach = _timing.CurTime + TimeSpan.FromSeconds(2);
            return PlanSideGirderBreach(uid, actor, barrier, grid, origin, reach, assigned, occupied, contacts);
        }

        var girder = barrier;
        var target = _transform.GetMapCoordinates(girder);
        var delta = origin.Position - target.Position;
        var axis = delta.LengthSquared() > 0.001f ? Vector2.Normalize(delta) : Vector2.UnitX;
        var positions = new List<EntityCoordinates>(MeleeApproachAngles.Length);
        var standing = Transform(uid).Coordinates;
        if (GirderInMeleeReach(uid, girder, origin, target, reach) && SquadPositionAvailable(uid, standing))
            positions.Add(standing);
        else
        {
            foreach (var angle in MeleeApproachAngles)
            {
                var position = new MapCoordinates(target.Position + new Angle(angle).RotateVec(axis) * reach, target.MapId);
                var coordinates = _transform.ToCoordinates(grid, position);
                if (!OnDefensiveGrid(uid, coordinates) || _turf.GetTileRef(coordinates) is not { } tile ||
                    tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile) || !SquadPositionClear(uid, position) ||
                    occupied.Any(other => other.MapId == position.MapId && Vector2.DistanceSquared(other.Position, position.Position) < 0.81f) ||
                    !SquadPositionAvailable(uid, coordinates) ||
                    !SquadLineClear(position, target, ignored: entity => entity == uid || entity == girder))
                    continue;
                positions.Add(coordinates);
            }
        }

        if (positions.Count == 0)
        {
            actor.NextGirderBreach = _timing.CurTime + TimeSpan.FromSeconds(3);
            return null;
        }

        return new MilitiaSquadOrder(actor.DirectTarget, positions.ToArray(), MilitiaSquadTask.BreachGirder,
            _timing.CurTime + TimeSpan.FromSeconds(14), contacts) { Girder = girder };
    }

    private MilitiaSquadOrder? PlanSideGirderBreach(EntityUid uid, MilitiaOperationActorComponent actor,
        EntityUid barrier, EntityUid grid, MapCoordinates origin, float reach, HashSet<EntityUid> assigned,
        List<MapCoordinates> occupied, MilitiaSquadContact[] contacts)
    {
        if (!_squadPhysics.IsCurrentlyHardCollidable(uid, barrier))
            return null;

        var enemy = actor.DirectTarget;
        var target = _transform.GetMapCoordinates(enemy);
        var rotation = _transform.GetWorldRotation(grid);
        var shotCollision = GetShotCollision(actor.PrimaryWeapon);
        var home = _transform.ToMapCoordinates(actor.DefensivePosition);
        var maxDistanceSquared = actor.DefensiveRange * actor.DefensiveRange;
        var bestDistance = 144f;
        EntityUid bestGirder = EntityUid.Invalid;
        EntityCoordinates bestEntry = default;
        EntityCoordinates bestExit = default;
        // Only inspect nearby openings during squad planning. The normal path planner still validates the chosen route.
        if (!_girdersNearBarrier.TryGetValue(barrier, out var nearbyGirders))
        {
            var center = _transform.GetMapCoordinates(barrier);
            nearbyGirders = _squadLookup.GetEntitiesInRange<DestructibleComponent>(center, 12f, LookupFlags.Static | LookupFlags.Dynamic)
                .Select(candidate => candidate.Owner).Where(girder => HasPrototypeAncestor(girder, "Girder")).ToArray();
            _girdersNearBarrier.Add(barrier, nearbyGirders);
        }

        foreach (var girder in nearbyGirders)
        {
            if (assigned.Contains(girder) || !BreachableGirder(uid, girder))
                continue;

            var position = _transform.GetMapCoordinates(girder);
            if (!HasFloor(_transform.ToCoordinates(grid, position)))
                continue;

            foreach (var offset in SquadAdjacent)
            {
                var axis = rotation.RotateVec(new Vector2(offset.X, offset.Y));
                if (Vector2.Dot(axis, position.Position - origin.Position) < -0.25f)
                    continue;

                var entry = new MapCoordinates(position.Position - axis * reach, position.MapId);
                var exit = new MapCoordinates(position.Position + axis * reach, position.MapId);
                var travel = Vector2.DistanceSquared(origin.Position, entry.Position);
                if (travel >= bestDistance || Vector2.DistanceSquared(exit.Position, home.Position) > maxDistanceSquared)
                    continue;

                var entryCoordinates = _transform.ToCoordinates(grid, entry);
                var exitCoordinates = _transform.ToCoordinates(grid, exit);
                if (!HasFloor(entryCoordinates) || !HasFloor(exitCoordinates) ||
                    occupied.Any(other => other.MapId == entry.MapId && Vector2.DistanceSquared(other.Position, entry.Position) < 0.81f) ||
                    !SquadPositionAvailable(uid, entryCoordinates) ||
                    !SquadPositionClear(uid, entry) || !SquadPositionClear(uid, exit) ||
                    !SquadRouteClear(uid, origin, entry, entity => CanCrossHolographicBarrier(uid, entity) || RetreatDoor(uid, entity)) ||
                    !SquadRouteClear(uid, entry, exit, entity => entity == girder) ||
                    FiringPositionBeyondGirder(exit, axis) is not { } firingPosition)
                    continue;

                bestDistance = travel;
                bestGirder = girder;
                bestEntry = entryCoordinates;
                bestExit = firingPosition;
            }
        }

        return bestGirder.IsValid()
            ? new MilitiaSquadOrder(enemy, [bestEntry], MilitiaSquadTask.BreachGirder,
                _timing.CurTime + TimeSpan.FromSeconds(14), contacts) { Girder = bestGirder, GirderExit = bestExit }
            : null;

        bool HasFloor(EntityCoordinates coordinates)
            => OnDefensiveGrid(uid, coordinates) && _turf.GetTileRef(coordinates) is { } tile &&
               !tile.Tile.IsEmpty && !_turf.IsSpace(tile.Tile);

        EntityCoordinates? FiringPositionBeyondGirder(MapCoordinates exit, Vector2 axis)
        {
            if (!ShotPathBlocked(uid, enemy, exit, target, shotCollision))
                return _transform.ToCoordinates(grid, exit);

            // The first tile beyond the opening may still be behind the end of the grille line.
            foreach (var offset in SquadAdjacent)
            {
                var direction = rotation.RotateVec(new Vector2(offset.X, offset.Y));
                if (Vector2.Dot(direction, axis) < -0.5f)
                    continue;

                var position = new MapCoordinates(exit.Position + direction * 1.75f, exit.MapId);
                var coordinates = _transform.ToCoordinates(grid, position);
                var midpoint = _transform.ToCoordinates(grid, new MapCoordinates((exit.Position + position.Position) / 2f, exit.MapId));
                if (Vector2.DistanceSquared(position.Position, home.Position) <= maxDistanceSquared && HasFloor(coordinates) && HasFloor(midpoint) &&
                    !ShotPathBlocked(uid, enemy, position, target, shotCollision) && SquadPositionClear(uid, position) &&
                    SquadRouteClear(uid, exit, position, entity => CanCrossHolographicBarrier(uid, entity)))
                    return coordinates;
            }

            return null;
        }
    }

    private bool GirderInMeleeReach(EntityUid uid, EntityUid girder, MapCoordinates origin, MapCoordinates target, float reach)
        => reach > 0f && origin.MapId == target.MapId && Vector2.DistanceSquared(origin.Position, target.Position) <= reach * reach &&
           SquadLineClear(origin, target, ignored: entity => entity == uid || entity == girder);

    private HTNOperatorStatus StrikeGirder(EntityUid uid, MilitiaOperationActorComponent actor, MilitiaSquadOrder order)
    {
        if (!TryComp<MeleeWeaponComponent>(actor.Backup, out var melee) ||
            (!IsActiveEnergySword(actor.Backup) || _hands.GetActiveItem(uid) != actor.Backup) &&
            !EquipOwnedItem(uid, actor, actor.Backup, wield: false, freeHands: true))
        {
            RejectUnreachableGirder(uid, order);
            return HTNOperatorStatus.Failed;
        }

        if (melee.NextAttack > _timing.CurTime)
            return HTNOperatorStatus.Continuing;

        _combat.SetInCombatMode(uid, true);
        if (!GirderInMeleeReach(uid, order.Girder, _transform.GetMapCoordinates(uid), _transform.GetMapCoordinates(order.Girder), melee.Range) ||
            !_squadMelee.AttemptLightAttack(uid, actor.Backup, melee, order.Girder))
        {
            RejectUnreachableGirder(uid, order);
            return HTNOperatorStatus.Failed;
        }

        return BreachableGirder(uid, order.Girder) ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Finished;
    }

    public void RejectUnreachableGirder(EntityUid uid, MilitiaSquadOrder order)
    {
        if (order.Task != MilitiaSquadTask.BreachGirder || !TryComp<MilitiaOperationActorComponent>(uid, out var actor) ||
            !ReferenceEquals(actor.SquadOrder, order))
            return;

        actor.NextGirderBreach = _timing.CurTime + TimeSpan.FromSeconds(3);
        actor.SquadOrder = null;
        _urgentSquadOrders.Add(uid);
    }

    private void EndGirderBreach(EntityUid uid, MilitiaOperationActorComponent actor, MilitiaSquadOrder order)
    {
        actor.NextGirderBreach = _timing.CurTime + TimeSpan.FromSeconds(3);
        if (!AvailableActor(uid, out _))
            return;

        if (!BreachableGirder(uid, order.Girder) && BreakoutTargetInArea(uid, actor, order.Target))
        {
            actor.FireBlockedUntil = TimeSpan.Zero;
            actor.GirderPushUntil = TimeSpan.Zero;
            MarkGirderAssault(uid, actor, order.Target);
            actor.GirderPushExit = order.GirderExit;
        }

        if (_hands.GetActiveItem(uid) == actor.Backup && OwnsItem(uid, actor.PrimaryWeapon) &&
            EquipOwnedItem(uid, actor, actor.PrimaryWeapon, wield: true, freeHands: true))
            actor.Weapon = actor.PrimaryWeapon;
    }
}
