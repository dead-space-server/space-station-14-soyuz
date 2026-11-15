// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using System.Diagnostics;
using Content.Server.DeadSpace.TEMP_FOR_EVENT.Elfiry;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Components;
using Content.Shared.Damage.Components;
using Content.Shared.DeadSpace.TEMP_FOR_EVENT.Elfiry;
using Content.Shared.Physics;
using Content.Shared.Standing;
using Content.Shared.Doors.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    private TimeSpan _nextEventWalkSearch;
    public bool EventUnitAvailable(EntityUid uid)
        => HasComp<EventSquadUnitComponent>(uid) && AvailableActor(uid, out _);

    public bool EventUnitInCombat(EntityUid uid)
        => TryComp<MilitiaOperationActorComponent>(uid, out var actor) && DirectContact(uid, actor);

    public bool EventUnitHasMind(EntityUid uid) => HasPlayerMind(uid);

    public bool EventDestinationReached(EntityUid uid, EntityCoordinates destination, float range)
        => destination.IsValid(EntityManager) && Transform(uid).MapID == _transform.ToMapCoordinates(destination).MapId &&
           _turf.GetTileRef(destination) is { } tile && (Transform(uid).GridUid ?? Transform(uid).MapUid) == tile.GridUid &&
           Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.ToMapCoordinates(destination).Position) <= range * range &&
           _interaction.InRangeUnobstructed(_transform.GetMapCoordinates(uid), _transform.ToMapCoordinates(destination),
               0f, CollisionGroup.MobMask, predicate: entity => IgnoreEventObstacle(uid, entity));

    public void SyncEventDefense(EntityUid uid)
    {
        if (!TryComp<EventSquadUnitComponent>(uid, out var unit) || !AvailableActor(uid, out var actor))
            return;
        var transform = Transform(uid);
        if (unit.DamageSource is { } source && Exists(source) && _mobs.IsIncapacitated(source) && !DirectContact(uid, actor))
        {
            unit.DamagePosition = null;
            unit.DamageUntil = TimeSpan.Zero;
            actor.SquadOrder = null;
            unit.DamageSource = null;
        }
        if (unit.DefensiveGrid != transform.GridUid || !actor.DefensivePosition.IsValid(EntityManager) ||
            Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.ToMapCoordinates(actor.DefensivePosition).Position) > 64f)
        {
            actor.DefensivePosition = transform.Coordinates;
            actor.SquadOrder = null;
            unit.DefensiveGrid = transform.GridUid;
        }
    }

    public bool EventMovementAllowed(EntityUid uid)
    {
        if (!TryComp<EventSquadUnitComponent>(uid, out var unit) || !SquadMember(uid) || !_blocker.CanMove(uid) ||
            !TryComp<MilitiaOperationActorComponent>(uid, out var actor) ||
            actor.Patient.IsValid() || ReceivingTreatment(uid) || actor.MedicalRefill != null ||
            actor.ReloadUntil != null || actor.Grenades.Length > 0 ||
            GetSquadOrder(uid, true) != null || _timing.CurTime < unit.RetryAfter)
            return false;
        return unit.Order is EventSquadOrder.Assault or EventSquadOrder.Retreat || !DirectContact(uid, actor);
    }

    public (EntityUid Subject, EntityCoordinates Position)? EventMedicalDestination(EntityUid uid)
    {
        if (!TryComp<EventSquadUnitComponent>(uid, out var unit) || !AvailableActor(uid, out var actor))
            return null;
        var origin = _transform.GetMapCoordinates(uid);
        var canSelfTreat = TryComp<DamageableComponent>(uid, out var damage) && HasTreatment(uid, actor, uid, damage);
        var seekingMedic = damage != null && NeedsMedic(uid, actor) && !canSelfTreat;
        if (!seekingMedic && (!actor.Medic || !SafeForTreatment(uid)))
            return null;
        (EntityUid, EntityCoordinates)? nearest = null;
        var distance = actor.TreatmentRange * actor.TreatmentRange;
        var candidates = EntityQueryEnumerator<EventSquadUnitComponent, MilitiaOperationActorComponent, DamageableComponent>();
        while (candidates.MoveNext(out var other, out var candidate, out var medic, out var injury))
        {
            if (other == uid || candidate.Team != unit.Team || !AvailableActor(other, out _) ||
                Transform(other).MapID != origin.MapId || Transform(other).GridUid == Transform(uid).GridUid)
                continue;
            if (seekingMedic)
            {
                if (!medic.Medic || !HasTreatment(other, medic, uid, damage!))
                    continue;
            }
            else if (injury.TotalDamage < actor.TreatmentThreshold || !SafeForTreatment(other) ||
                     !HasTreatment(uid, actor, other, injury))
                continue;
            var squared = Vector2.DistanceSquared(origin.Position, _transform.GetWorldPosition(other));
            if (squared >= distance)
                continue;
            distance = squared;
            nearest = (other, Transform(other).Coordinates);
        }
        return nearest;
    }

    public EventCommandPath? PrepareEventCommandPath(EntityUid uid, uint revision, EntityCoordinates destination, List<PathPoly> path)
    {
        if (!EventMovementAllowed(uid) || !TryComp<EventSquadUnitComponent>(uid, out var unit) || unit.Revision != revision ||
            !destination.IsValid(EntityManager) || Transform(uid).MapID != _transform.ToMapCoordinates(destination).MapId)
            return null;
        var segment = new List<PathPoly>();
        var from = _transform.GetMapCoordinates(uid);
        var length = 0f;
        var endpoint = destination;
        foreach (var poly in path)
        {
            if (!poly.Coordinates.IsValid(EntityManager))
                return null;
            var to = _transform.ToMapCoordinates(poly.Coordinates);
            var distance = Vector2.Distance(from.Position, to.Position);
            if (length + distance > 24f)
            {
                var point = new MapCoordinates(Vector2.Lerp(from.Position, to.Position, (24f - length) / distance), from.MapId);
                endpoint = _transform.ToCoordinates(poly.Coordinates.EntityId, point);
                break;
            }
            segment.Add(poly);
            length += distance;
            from = to;
        }
        if (!PrepareEventRoute(uid, endpoint, segment.Select(poly => poly.Coordinates), out var route, out var doors) || !EventRouteSafe(uid, route, unit.Order))
            return null;
        return new EventCommandPath(revision, destination, route, doors);
    }

    public EventCommandPath? DirectEventCommandPath(EntityUid uid, uint revision, EntityCoordinates destination)
    {
        if (!EventMovementAllowed(uid) || !destination.IsValid(EntityManager) ||
            !TryComp<EventSquadUnitComponent>(uid, out var unit) || unit.Revision != revision)
            return null;
        var from = _transform.GetMapCoordinates(uid);
        var to = _transform.ToMapCoordinates(destination);
        if (from.MapId != to.MapId)
            return null;
        var distance = Vector2.Distance(from.Position, to.Position);
        var endpoint = distance > 24f
            ? _transform.ToCoordinates(new MapCoordinates(Vector2.Lerp(from.Position, to.Position, 24f / distance), from.MapId))
            : destination;
        return PrepareEventRoute(uid, endpoint, Array.Empty<EntityCoordinates>(), out var route, out var doors) && EventRouteSafe(uid, route, unit.Order)
            ? new EventCommandPath(revision, destination, route, doors) : null;
    }

    private bool PrepareEventRoute(EntityUid uid, EntityCoordinates destination, IEnumerable<EntityCoordinates> path,
        out EntityCoordinates[] route, out EntityUid[] doors)
    {
        route = Array.Empty<EntityCoordinates>();
        doors = Array.Empty<EntityUid>();
        var points = new List<EntityCoordinates>();
        var encounteredDoors = new HashSet<EntityUid>();
        var from = _transform.GetMapCoordinates(uid);
        foreach (var coordinates in path.Append(destination))
        {
            if (!coordinates.IsValid(EntityManager))
                return false;
            var to = _transform.ToMapCoordinates(coordinates);
            if (to.MapId != from.MapId)
                return false;
            var distance = Vector2.Distance(from.Position, to.Position);
            if (distance < 0.1f)
                continue;
            var steps = Math.Max(1, (int) MathF.Ceiling(distance / 0.5f));
            if (steps > 64 || points.Count + steps > 128)
                return false;
            if (!EventSegmentClear(uid, from, to, encounteredDoors))
                return false;
            for (var step = 1; step <= steps; step++)
                points.Add(_transform.ToCoordinates(coordinates.EntityId,
                    new MapCoordinates(Vector2.Lerp(from.Position, to.Position, (float) step / steps), from.MapId)));
            from = to;
        }
        route = points.ToArray();
        doors = encounteredDoors.ToArray();
        return true;
    }

    public bool PassableEventBody(EntityUid uid)
        => HasComp<Content.Shared.Mobs.Components.MobStateComponent>(uid) &&
           TryComp<StandingStateComponent>(uid, out var standing) && !standing.Standing;

    private bool IgnoreEventObstacle(EntityUid uid, EntityUid obstacle)
        => obstacle == uid || PassableEventBody(obstacle) ||
           !TryComp<PhysicsComponent>(obstacle, out var body) || !body.CanCollide ||
           !TryComp<FixturesComponent>(obstacle, out var fixtures) ||
           !fixtures.Fixtures.Values.Any(fixture => fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.MobMask) != 0);

    private bool EventRouteDoor(EntityUid uid, EntityUid obstacle)
        => TryComp<DoorComponent>(obstacle, out var door) && (door.State == DoorState.Opening || RetreatDoor(uid, obstacle));

    private bool EventSegmentClear(EntityUid uid, MapCoordinates from, MapCoordinates to, HashSet<EntityUid>? doors = null)
    {
        if (from.MapId != to.MapId)
            return false;
        var delta = to.Position - from.Position;
        var distance = delta.Length();
        if (distance < 0.01f)
            return true;
        var normal = new Vector2(-delta.Y, delta.X) / distance * 0.3f;
        foreach (var offset in new[] { Vector2.Zero, normal, -normal })
        {
            if (!_interaction.InRangeUnobstructed(new MapCoordinates(from.Position + offset, from.MapId),
                    new MapCoordinates(to.Position + offset, to.MapId), 0f, CollisionGroup.MobMask, predicate: entity =>
                    {
                        if (IgnoreEventObstacle(uid, entity))
                            return true;
                        if (!EventRouteDoor(uid, entity))
                            return false;
                        doors?.Add(entity);
                        return true;
                    }))
                return false;
        }
        var steps = Math.Max(1, (int) MathF.Ceiling(distance / 0.5f));
        for (var step = 0; step <= steps; step++)
        {
            var point = _transform.ToCoordinates(new MapCoordinates(Vector2.Lerp(from.Position, to.Position, (float) step / steps), from.MapId));
            if (_turf.GetTileRef(point) is not { } tile || tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile))
                return false;
            if (TryComp<EventSquadUnitComponent>(uid, out var unit) && unit.AvoidUntil > _timing.CurTime &&
                unit.Avoiding is { } avoid && AvailableActor(avoid, out _) && Transform(avoid).MapID == from.MapId)
            {
                var blocker = _transform.GetWorldPosition(avoid);
                if (Vector2.DistanceSquared(_transform.ToMapCoordinates(point).Position, blocker) + 0.01f <
                    MathF.Min(0.81f, Vector2.DistanceSquared(_transform.GetWorldPosition(uid), blocker)))
                    return false;
            }
        }
        return true;
    }

    public EventCommandPath? FindEventWalkPath(EntityUid uid, uint revision, EventMovementTarget movement)
    {
        var destination = movement.Destination;
        if (!EventMovementAllowed(uid) || !TryComp<EventSquadUnitComponent>(uid, out var unit) ||
            unit.Revision != revision || !destination.IsValid(EntityManager) ||
            _turf.GetTileRef(Transform(uid).Coordinates) is not { } startTile)
            return null;
        var origin = _transform.GetMapCoordinates(uid);
        var target = _transform.ToMapCoordinates(destination);
        if (origin.MapId != target.MapId)
            return null;
        var search = unit.WalkSearch;
        if (search != null && (search.Revision != revision || !search.Target.Matches(movement) ||
            search.Expires <= _timing.CurTime || search.Map != origin.MapId || !Exists(search.Grid) ||
            Transform(search.Grid).MapID != origin.MapId ||
            Vector2.DistanceSquared(search.Goal.Position, target.Position) > 0.25f ||
            Vector2.DistanceSquared(search.GridPosition, _transform.GetWorldPosition(search.Grid)) > 0.01f ||
            !search.GridRotation.Equals(_transform.GetWorldRotation(search.Grid))))
            search = unit.WalkSearch = null;
        if (search == null)
        {
            if (!TryComp<MapGridComponent>(startTile.GridUid, out _))
                return null;
            search = new EventWalkSearch(startTile.GridUid, origin.MapId, revision, movement, target,
                _transform.GetWorldPosition(startTile.GridUid), _transform.GetWorldRotation(startTile.GridUid),
                startTile.GridIndices, _timing.CurTime + TimeSpan.FromMinutes(2));
            unit.WalkSearch = search;
        }
        if (!TryComp<MapGridComponent>(search.Grid, out var grid))
            return null;
        if (search.Route == null)
        {
            if (_timing.CurTime < _nextEventWalkSearch)
            {
                unit.RetryAfter = _nextEventWalkSearch + TimeSpan.FromMilliseconds(25 * (unit.FormationSlot % 3));
                return null;
            }
            _nextEventWalkSearch = _timing.CurTime + TimeSpan.FromSeconds(0.15);
            var watch = Stopwatch.StartNew();
            MapCoordinates Point(Vector2i tile) => _transform.ToMapCoordinates(_maps.GridTileToLocal(search.Grid, grid, tile));
            var result = search.Advance(
                node => Vector2.Distance(Point(node).Position, target.Position) <= grid.TileSize * 1.4f &&
                        EventSegmentClear(uid, Point(node), target),
                (from, to) => EventSegmentClear(uid, Point(from), Point(to)),
                node => Vector2.Distance(Point(node).Position, target.Position), grid.TileSize,
                withinBudget: () => watch.ElapsedMilliseconds < 8);
            if (result == EventSearchResult.Pending)
            {
                unit.RetryAfter = _timing.CurTime + TimeSpan.FromSeconds(0.2);
                return null;
            }
            if (result == EventSearchResult.Blocked || search.Reconstruct() is not { } nodes)
            {
                unit.WalkSearch = null;
                return null;
            }
            search.Route = nodes.Select(node => _maps.GridTileToLocal(search.Grid, grid, node)).Append(destination).ToList();
        }
        var points = search.Route;
        var closest = float.PositiveInfinity;
        var cursor = -1;
        for (var i = search.Cursor; i < points.Count; i++)
        {
            if (!points[i].IsValid(EntityManager))
            {
                unit.WalkSearch = null;
                return null;
            }
            var point = _transform.ToMapCoordinates(points[i]);
            var squared = Vector2.DistanceSquared(origin.Position, point.Position);
            if (squared < closest && EventSegmentClear(uid, origin, point))
            {
                closest = squared;
                cursor = i;
            }
        }
        if (cursor < 0)
        {
            unit.WalkSearch = null;
            return null;
        }
        search.Cursor = cursor;
        var segment = new List<EntityCoordinates>();
        var last = origin;
        var length = 0f;
        for (var i = cursor; i < points.Count; i++)
        {
            var point = _transform.ToMapCoordinates(points[i]);
            var distance = Vector2.Distance(last.Position, point.Position);
            if (length + distance > 24f)
                break;
            segment.Add(points[i]);
            length += distance;
            last = point;
        }
        if (segment.Count == 0 || !PrepareEventRoute(uid, segment[^1], segment.SkipLast(1), out var route, out var doors) ||
            !EventRouteSafe(uid, route, unit.Order))
        {
            unit.WalkSearch = null;
            return null;
        }
        return new EventCommandPath(revision, destination, route, doors);
    }
    private void StartEventRoutePoint(EntityUid uid, EntityCoordinates point)
    {
        var steering = _steering.Register(uid, point);
        steering.Range = 0.25f;
        steering.ArriveOnLineOfSight = false;
        steering.Status = SteeringStatus.Moving;
    }

    private HTNOperatorStatus AdvanceEventRoute(EntityUid uid, EventCommandPath path, EventSquadUnitComponent unit)
    {
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        if (actor.DoorPry is { } id)
        {
            unit.LastProgress = _timing.CurTime;
            var status = _doAfter.GetStatus(id);
            if (status == Content.Shared.DoAfter.DoAfterStatus.Running)
                return HTNOperatorStatus.Continuing;
            EndRoutePry(uid);
            if (status != Content.Shared.DoAfter.DoAfterStatus.Finished)
                return HTNOperatorStatus.Failed;
        }
        while (unit.RouteIndex < path.Route.Length && EventDestinationReached(uid, path.Route[unit.RouteIndex], 0.3f))
        {
            unit.RouteIndex++;
            unit.LastProgress = _timing.CurTime;
        }
        if (unit.RouteIndex >= path.Route.Length)
            return HTNOperatorStatus.Finished;
        var origin = _transform.GetMapCoordinates(uid);
        var next = _transform.ToMapCoordinates(path.Route[unit.RouteIndex]);
        EntityUid? blockedDoor = null;
        if (!_interaction.InRangeUnobstructed(origin, next, 0f, CollisionGroup.MobMask, predicate: entity =>
            {
                if (IgnoreEventObstacle(uid, entity))
                    return true;
                if (!EventRouteDoor(uid, entity))
                    return false;
                if (blockedDoor == null || Vector2.DistanceSquared(origin.Position, _transform.GetWorldPosition(entity)) <
                    Vector2.DistanceSquared(origin.Position, _transform.GetWorldPosition(blockedDoor.Value)))
                    blockedDoor = entity;
                return true;
            }))
            return HTNOperatorStatus.Failed;
        if (blockedDoor is { } doorUid && TryComp<DoorComponent>(doorUid, out var door) && door.State != DoorState.Open)
        {
            unit.LastProgress = _timing.CurTime;
            _steering.Unregister(uid);
            if (door.State == DoorState.Opening)
                return HTNOperatorStatus.Continuing;
            if (!_interaction.InRangeUnobstructed(uid, doorUid))
                return HTNOperatorStatus.Failed;
            if (_squadDoors.TryOpen(doorUid, door, DoorUser(uid, doorUid), quiet: true))
                return HTNOperatorStatus.Continuing;
            return CanPryRouteDoor(uid, doorUid) ? PryRouteDoor(uid, doorUid) : HTNOperatorStatus.Failed;
        }
        StartEventRoutePoint(uid, path.Route[unit.RouteIndex]);
        return HTNOperatorStatus.Continuing;
    }

    private bool EventRouteSafe(EntityUid uid, EntityCoordinates[] route, EventSquadOrder order)
    {
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        var origin = _transform.GetMapCoordinates(uid);
        var threats = actor.SquadContacts.Where(c => c.Expires > _timing.CurTime && c.Position.IsValid(EntityManager) && SquadThreat(uid, c.Target))
            .Select(c => _transform.ToMapCoordinates(c.Position)).ToList();
        if (TryComp<EventSquadUnitComponent>(uid, out var unit) && unit.DamageUntil > _timing.CurTime &&
            unit.DamagePosition is { } incoming && incoming.MapId == origin.MapId)
            threats.Add(incoming);
        foreach (var coordinates in route)
        {
            var point = _transform.ToMapCoordinates(coordinates);
            if (point.MapId != origin.MapId || _turf.GetTileRef(coordinates) is not { } tile || tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile))
                return false;
            if (threats.Any(threat => Vector2.Distance(point.Position, threat.Position) + 0.1f <
                    MathF.Min(1.5f, Vector2.Distance(origin.Position, threat.Position))))
                return false;
            if (order == EventSquadOrder.Retreat && threats.Any(threat =>
                    Vector2.Distance(point.Position, threat.Position) + 0.5f < Vector2.Distance(origin.Position, threat.Position)))
                return false;
        }
        return true;
    }

    public void BeginEventCommandPath(EntityUid uid, EventCommandPath path)
    {
        if (!EventMovementAllowed(uid) || !TryComp<EventSquadUnitComponent>(uid, out var unit) || unit.Revision != path.Revision)
            return;
        EndSquadOrder(uid);
        EndGuard(uid);
        if (path.Route.Length > 0)
            StartEventRoutePoint(uid, path.Route[0]);
        unit.TrafficStopped = false;
        unit.Path = path;
        unit.RouteIndex = 0;
        unit.LastProgress = _timing.CurTime;
        unit.ProgressPosition = Transform(uid).Coordinates;
    }

    public HTNOperatorStatus RunEventCommandPath(EntityUid uid, EventCommandPath path)
    {
        if (!EventMovementAllowed(uid) || !TryComp<EventSquadUnitComponent>(uid, out var unit) ||
            unit.Revision != path.Revision || !ReferenceEquals(unit.Path, path) ||
            path.Route.Any(point => !point.IsValid(EntityManager)))
            return HTNOperatorStatus.Failed;
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        var origin = _transform.GetMapCoordinates(uid);
        if (EventTrafficWaiting(uid, path) is { } traffic)
            return traffic;
        if (!unit.ProgressPosition.IsValid(EntityManager) ||
            Vector2.DistanceSquared(origin.Position, _transform.ToMapCoordinates(unit.ProgressPosition).Position) > 0.09f)
        {
            unit.ProgressPosition = Transform(uid).Coordinates;
            unit.LastProgress = _timing.CurTime;
        }
        if (_timing.CurTime - unit.LastProgress > TimeSpan.FromSeconds(8))
        {
            unit.RetryAfter = _timing.CurTime + TimeSpan.FromSeconds(2);
            return HTNOperatorStatus.Failed;
        }
        var result = AdvanceEventRoute(uid, path, unit);
        if (actor.DoorPry == null && DirectContact(uid, actor))
            FireAtTarget(uid, actor, actor.DirectTarget);
        if (result == HTNOperatorStatus.Finished)
            actor.DefensivePosition = Transform(uid).Coordinates;
        else if (result == HTNOperatorStatus.Failed)
            unit.RetryAfter = _timing.CurTime + TimeSpan.FromSeconds(2);
        return result;
    }

    public void EndEventCommandPath(EntityUid uid, EventCommandPath? path = null)
    {
        if (!TryComp<EventSquadUnitComponent>(uid, out var unit) || path != null && !ReferenceEquals(unit.Path, path))
            return;
        unit.Path = null;
        unit.RouteIndex = 0;
        unit.WaitingFor = null;
        unit.WaitingStarted = null;
        unit.TrafficStopped = false;
        try
        {
            EndRoutePry(uid);
        }
        finally
        {
            _steering.Unregister(uid);
        }
    }
}
