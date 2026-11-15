// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.DeadSpace.TEMP_FOR_EVENT.Elfiry;
using Content.Server.NPC.HTN;
using Content.Shared.Doors.Components;
using Content.Shared.Physics;
using Content.Shared.Standing;
using Robust.Shared.Map;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    public void UpdateEventPassage(EntityUid uid)
    {
        var unit = Comp<EventSquadUnitComponent>(uid);
        var origin = _transform.GetMapCoordinates(uid);
        if (unit.Passage is not { } passage)
        {
            if (unit.TravelHistory.Count == 0 || !unit.TravelHistory[^1].IsValid(EntityManager) ||
                Vector2.DistanceSquared(origin.Position, _transform.ToMapCoordinates(unit.TravelHistory[^1]).Position) > 0.64f)
            {
                unit.TravelHistory.Add(Transform(uid).Coordinates);
                if (unit.TravelHistory.Count > 48)
                    unit.TravelHistory.RemoveAt(0);
            }
            return;
        }
        var valid = passage.Revision == unit.Revision && passage.Until > _timing.CurTime &&
                    AvailableActor(passage.Passer, out _) && passage.Destination.IsValid(EntityManager) &&
                    Transform(passage.Passer).MapID == origin.MapId &&
                    TryComp<EventSquadUnitComponent>(passage.Passer, out var passer) && passer.Revision == passage.PasserRevision &&
                    passer.Passage == null && EventMovementAllowed(uid);
        if (valid)
        {
            var delta = _transform.GetWorldPosition(passage.Passer) - passage.Origin.Position;
            valid = delta.LengthSquared() < 16f && (Vector2.Dot(delta, passage.Direction) < 1.3f ||
                    Vector2.DistanceSquared(origin.Position, _transform.GetWorldPosition(passage.Passer)) < 2.25f);
        }
        if (valid)
            return;
        unit.Passage = null;
        unit.WaitingFor = null;
        unit.NextPassageCheck = _timing.CurTime + TimeSpan.FromSeconds(2);
        if (!unit.Ordered)
            Comp<MilitiaOperationActorComponent>(uid).DefensivePosition = Transform(uid).Coordinates;
        if (unit.Path != null)
            EndEventCommandPath(uid);
    }

    public EventCommandPath? EventPassagePath(EntityUid uid, uint revision, EntityCoordinates destination)
    {
        if (!TryComp<EventSquadUnitComponent>(uid, out var unit) || unit.Passage is not { } passage ||
            passage.Revision != revision || !passage.Destination.Equals(destination) ||
            passage.Route.Any(point => !point.IsValid(EntityManager)))
            return null;
        return new EventCommandPath(revision, destination, passage.Route, passage.Doors);
    }

    private HTNOperatorStatus? EventTrafficWaiting(EntityUid uid, EventCommandPath path)
    {
        var unit = Comp<EventSquadUnitComponent>(uid);
        var origin = _transform.GetMapCoordinates(uid);
        if (unit.Passage is { } passage && EventDestinationReached(uid, passage.Destination, 0.4f))
        {
            StopEventTraffic(uid, unit);
            return HTNOperatorStatus.Continuing;
        }
        if (unit.RouteIndex >= path.Route.Length || Comp<MilitiaOperationActorComponent>(uid).DoorPry != null)
            return null;
        var direction = EventTravelDirection(uid, unit);
        if (direction == Vector2.Zero)
            return null;
        var allies = _squadLookup.GetEntitiesInRange<EventSquadUnitComponent>(origin, 1.4f);
        var blocker = allies.Where(other => other.Owner != uid && other.Comp.Team == unit.Team &&
                AvailableActor(other.Owner, out _) && !PassableEventBody(other.Owner) &&
                _interaction.InRangeUnobstructed(origin, _transform.GetMapCoordinates(other.Owner), 0f, CollisionGroup.MobMask,
                    predicate: entity => IgnoreEventObstacle(uid, entity) || HasComp<Content.Shared.Mobs.Components.MobStateComponent>(entity)))
            .Where(other =>
            {
                var offset = _transform.GetWorldPosition(other.Owner) - origin.Position;
                if (EventTrafficPolicy.OverlapPriority(uid.GetHashCode(), other.Owner.GetHashCode(), offset.LengthSquared()))
                    return false;
                return EventTrafficPolicy.Blocks(Vector2.Dot(offset, direction),
                    offset.X * direction.Y - offset.Y * direction.X, unit.WaitingFor == other.Owner);
            }).OrderBy(other => Vector2.DistanceSquared(origin.Position, _transform.GetWorldPosition(other.Owner)))
            .FirstOrDefault();
        if (!blocker.Owner.IsValid())
        {
            unit.WaitingFor = null;
            unit.WaitingStarted = null;
            if (unit.TrafficStopped)
            {
                unit.TrafficStopped = false;
                StartEventRoutePoint(uid, path.Route[unit.RouteIndex]);
                unit.LastProgress = _timing.CurTime;
            }
            return null;
        }
        var blockingPosition = _transform.GetWorldPosition(blocker.Owner);
        unit.WaitingStarted ??= _timing.CurTime;
        if (unit.WaitingFor != blocker.Owner || Vector2.DistanceSquared(unit.WaitingPosition, blockingPosition) > 0.25f)
        {
            unit.WaitingSince = _timing.CurTime;
            unit.WaitingPosition = blockingPosition;
        }
        unit.WaitingFor = blocker.Owner;
        if (_timing.CurTime - unit.WaitingSince > TimeSpan.FromSeconds(4) ||
            _timing.CurTime - unit.WaitingStarted.Value > TimeSpan.FromSeconds(12))
        {
            unit.Avoiding = blocker.Owner;
            unit.AvoidUntil = _timing.CurTime + TimeSpan.FromSeconds(6);
            unit.WalkSearch = null;
            unit.RetryAfter = _timing.CurTime + TimeSpan.FromSeconds(0.25);
            return HTNOperatorStatus.Failed;
        }
        if (_timing.CurTime >= unit.NextPassageCheck)
        {
            unit.NextPassageCheck = _timing.CurTime + TimeSpan.FromSeconds(1);
            var otherDirection = EventTravelDirection(blocker.Owner, blocker.Comp);
            var otherMoving = blocker.Comp.Path != null && otherDirection != Vector2.Zero;
            if (unit.Passage is { } yielding && blocker.Comp.Passage == null)
                TryEventYield(blocker.Owner, yielding.Passer, direction, uid);
            else if (unit.Passage == null && blocker.Comp.Passage == null &&
                     (!otherMoving || Vector2.Dot(otherDirection, direction) < 0.25f))
            {
                var yields = EventTrafficPolicy.Yields(uid.GetHashCode(), blocker.Owner.GetHashCode(), true, otherMoving);
                if (yields)
                    TryEventYield(uid, blocker.Owner, otherDirection, uid);
                else
                    TryEventYield(blocker.Owner, uid, direction, uid);
            }
        }
        StopEventTraffic(uid, unit);
        return HTNOperatorStatus.Continuing;
    }

    private Vector2 EventTravelDirection(EntityUid uid, EventSquadUnitComponent unit)
    {
        var origin = _transform.GetWorldPosition(uid);
        if (unit.Path is { } path && unit.RouteIndex < path.Route.Length)
        {
            var next = path.Route[Math.Min(unit.RouteIndex + 2, path.Route.Length - 1)];
            if (next.IsValid(EntityManager))
            {
                var delta = _transform.ToMapCoordinates(next).Position - origin;
                if (delta.LengthSquared() > 0.01f)
                    return Vector2.Normalize(delta);
            }
        }
        return Vector2.Zero;
    }

    private void StopEventTraffic(EntityUid uid, EventSquadUnitComponent unit)
    {
        if (!unit.TrafficStopped)
            _steering.Unregister(uid);
        unit.TrafficStopped = true;
        unit.LastProgress = _timing.CurTime;
    }

    private bool TryEventYield(EntityUid uid, EntityUid passer, Vector2 direction, EntityUid initiator)
    {
        if (uid == passer || direction == Vector2.Zero || !EventMovementAllowed(uid) ||
            !AvailableActor(passer, out _) || !TryComp<EventSquadUnitComponent>(uid, out var unit) ||
            !TryComp<EventSquadUnitComponent>(passer, out var leader) || unit.Passage != null || leader.Passage != null ||
            DirectContact(uid, Comp<MilitiaOperationActorComponent>(uid)))
            return false;
        var origin = _transform.GetMapCoordinates(uid);
        if (_squadLookup.GetEntitiesInRange<EventSquadUnitComponent>(origin, 16f)
            .Any(other => other.Comp.Passage?.Passer == uid))
            return false;
        var normal = new Vector2(-direction.Y, direction.X);
        var anchors = new List<EntityCoordinates> { Transform(uid).Coordinates };
        anchors.AddRange(unit.TravelHistory.AsEnumerable().Reverse().Where(point => point.IsValid(EntityManager) &&
            _transform.ToMapCoordinates(point).MapId == origin.MapId).Take(32));
        anchors.Add(default);
        for (var i = 1; i <= 16; i++)
            anchors.Add(_transform.ToCoordinates(new MapCoordinates(origin.Position + direction * i, origin.MapId)));
        var history = new List<EntityCoordinates>();
        foreach (var anchor in anchors)
        {
            if (!anchor.IsValid(EntityManager))
            {
                history.Clear();
                continue;
            }
            var point = _transform.ToMapCoordinates(anchor);
            if (Vector2.DistanceSquared(origin.Position, point.Position) > 1024f)
                continue;
            foreach (var side in new[] { 1f, -1f })
            {
                var target = new MapCoordinates(point.Position + normal * side * 1.1f, origin.MapId);
                if (!EventParkingAvailable(uid, target) ||
                    _turf.GetTileRef(_transform.ToCoordinates(target)) is not { } tile ||
                    !PrepareEventRoute(uid, _transform.ToCoordinates(tile.GridUid, target), history.Append(anchor), out var route, out var doors) ||
                    !EventRouteSafe(uid, route, Comp<EventSquadUnitComponent>(uid).Order) ||
                    route.Any(step => Vector2.DistanceSquared(_transform.ToMapCoordinates(step).Position,
                        _transform.GetWorldPosition(passer)) + 0.1f < MathF.Min(0.64f,
                        Vector2.DistanceSquared(origin.Position, _transform.GetWorldPosition(passer)))))
                    continue;
                var destination = route.Length > 0 ? route[^1] : _transform.ToCoordinates(target);
                if (uid != initiator)
                    InterruptActor(uid);
                if (!EventMovementAllowed(uid) || !AvailableActor(passer, out _) || leader.Passage != null ||
                    !destination.IsValid(EntityManager))
                    return false;
                unit.Passage = new EventPassageYield(passer, unit.Revision, leader.Revision, destination, route, doors,
                    origin, direction, _timing.CurTime + TimeSpan.FromSeconds(15));
                unit.WaitingFor = null;
                unit.TrafficStopped = false;
                unit.RetryAfter = TimeSpan.Zero;
                Comp<MilitiaOperationActorComponent>(uid).SquadOrder = null;
                return true;
            }
            history.Add(anchor);
        }
        return false;
    }

    private bool EventParkingAvailable(EntityUid uid, MapCoordinates target)
    {
        var coordinates = _transform.ToCoordinates(target);
        if (_turf.GetTileRef(coordinates) is not { } tile || tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile) ||
            _squadLookup.GetEntitiesInRange<DoorComponent>(target, 0.7f).Count > 0 ||
            _squadLookup.GetEntitiesInRange<StandingStateComponent>(target, 0.85f)
                .Any(other => other.Owner != uid && other.Comp.Standing) ||
            _squadLookup.GetEntitiesInRange<EventSquadUnitComponent>(target, 16f)
                .Any(other => other.Owner != uid && other.Comp.Passage is { } passage && passage.Destination.IsValid(EntityManager) &&
                    Vector2.DistanceSquared(target.Position, _transform.ToMapCoordinates(passage.Destination).Position) < 1.21f))
            return false;
        return EventStandingSpace(target);
    }

    public bool EventStandingSpace(MapCoordinates target, bool ignoreMobs = false)
    {
        foreach (var delta in new[] { new Vector2(0.33f, 0.33f), new Vector2(-0.33f, 0.33f) })
        {
            if (!_interaction.InRangeUnobstructed(new MapCoordinates(target.Position - delta, target.MapId),
                    new MapCoordinates(target.Position + delta, target.MapId), 0f, CollisionGroup.MobMask,
                    predicate: entity => IgnoreEventObstacle(EntityUid.Invalid, entity) ||
                        ignoreMobs && HasComp<Content.Shared.Mobs.Components.MobStateComponent>(entity)))
                return false;
        }
        return true;
    }
}
