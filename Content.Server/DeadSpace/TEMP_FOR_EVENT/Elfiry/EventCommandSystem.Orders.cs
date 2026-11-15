// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Shared.DeadSpace.TEMP_FOR_EVENT.Elfiry;
using Robust.Shared.Map;

namespace Content.Server.DeadSpace.TEMP_FOR_EVENT.Elfiry;

public sealed partial class EventCommandSystem
{
    private TimeSpan _nextFollow;

    private bool TryApplyOrders(IReadOnlyList<EventOrderChange> changes, Func<bool>? allowed = null)
    {
        var accepted = EventOrderTransaction.Execute(changes,
            change =>
            {
                if (change.Unit.Comp.Path != null)
                    _combat.EndEventCommandPath(change.Unit);
            },
            () =>
            {
                if (allowed?.Invoke() == false || changes.Any(change => !_combat.EventUnitAvailable(change.Unit) ||
                    !change.After.Destination.IsValid(EntityManager) ||
                    Transform(change.Unit).MapID != _transform.ToMapCoordinates(change.After.Destination).MapId))
                    throw new InvalidOperationException("Event order participants or destinations changed during preparation.");
            },
            change => change.After.Apply(change.Unit.Comp),
            change =>
            {
                if (TerminatingOrDeleted(change.Unit) || !_combat.EventUnitAvailable(change.Unit))
                    throw new InvalidOperationException("Event order participant became unavailable during activation.");
                _combat.SyncEventDefense(change.Unit);
                _npcs.KeepEventCommandActive(change.Unit);
                Replan(change.Unit);
                if (!_combat.EventUnitAvailable(change.Unit))
                    throw new InvalidOperationException("Event order participant became unavailable after activation.");
            },
            change =>
            {
                if (TryComp<EventSquadUnitComponent>(change.Unit, out var current) && ReferenceEquals(current, change.Unit.Comp))
                    change.Before.Apply(current);
            },
            exception => Log.Error($"Event order recovery failed: {exception}"), out var error);
        if (!accepted)
            Log.Error($"Event order was rolled back: {error}");
        return accepted;
    }

    private void UpdateFollowers()
    {
        if (_timing.CurTime < _nextFollow)
            return;
        _nextFollow = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        var followers = new List<Entity<EventSquadUnitComponent>>();
        var query = EntityQueryEnumerator<EventSquadUnitComponent>();
        while (query.MoveNext(out var uid, out var unit))
        {
            if (unit.Ordered && unit.Order == EventSquadOrder.Follow && _combat.EventUnitAvailable(uid))
                followers.Add((uid, unit));
        }
        foreach (var group in followers.GroupBy(unit => (unit.Comp.Commander, unit.Comp.Team, Transform(unit).MapID)))
        {
            var selected = group.OrderBy(unit => unit.Comp.FormationSlot).ThenBy(unit => unit.Owner.GetHashCode()).ToArray();
            var commander = group.Key.Commander;
            var present = CommanderAvailable(commander, group.Key.Team) && Transform(commander).MapID == group.Key.MapID;
            if (present && !selected.Any(unit => !unit.Comp.FollowPosition.IsValid(EntityManager) ||
                Vector2.DistanceSquared(_transform.GetWorldPosition(commander), _transform.ToMapCoordinates(unit.Comp.FollowPosition).Position) > 16f))
                continue;
            var occupied = new List<EntityCoordinates>();
            var changes = new List<EventOrderChange>();
            for (var i = 0; i < selected.Length; i++)
            {
                var unit = selected[i];
                var before = EventOrderState.Capture(unit.Comp);
                if (!present)
                {
                    changes.Add(new EventOrderChange(unit, before, before with
                    {
                        Order = EventSquadOrder.Defend, Destination = Transform(unit).Coordinates, Arrived = true,
                    }));
                    continue;
                }
                if (FormationPosition(_transform.GetMapCoordinates(commander), i, occupied) is not { } destination)
                    break;
                occupied.Add(destination);
                changes.Add(new EventOrderChange(unit, before, before with
                {
                    Destination = destination, FormationSlot = i, FollowPosition = Transform(commander).Coordinates, Arrived = false,
                }));
            }
            if (changes.Count == selected.Length)
                TryApplyOrders(changes, present ? () => CommanderAvailable(commander, group.Key.Team) &&
                    Transform(commander).MapID == group.Key.MapID : null);
        }
    }

    private sealed record EventOrderChange(Entity<EventSquadUnitComponent> Unit, EventOrderState Before, EventOrderState After);

    private sealed record EventOrderState(EntityUid Commander, int Squad, EventSquadOrder Order, EntityCoordinates Destination,
        int FormationSlot, bool Ordered, bool Arrived, string Status, EntityCoordinates FollowPosition)
    {
        public static EventOrderState Capture(EventSquadUnitComponent unit)
            => new(unit.Commander, unit.Squad, unit.Order, unit.Destination, unit.FormationSlot, unit.Ordered,
                unit.Arrived, unit.Status, unit.FollowPosition);

        public void Apply(EventSquadUnitComponent unit)
        {
            unit.Commander = Commander;
            unit.Squad = Squad;
            unit.Order = Order;
            unit.Destination = Destination;
            unit.FormationSlot = FormationSlot;
            unit.Ordered = Ordered;
            unit.Arrived = Arrived;
            unit.Status = Status;
            unit.FollowPosition = FollowPosition;
            unit.Revision++;
            unit.Passage = null;
            unit.WaitingFor = null;
            unit.WaitingStarted = null;
            unit.TrafficStopped = false;
            unit.Avoiding = null;
            unit.WalkSearch = null;
            unit.RetryAfter = TimeSpan.Zero;
        }
    }
}

public static class EventOrderTransaction
{
    public static bool Execute<T>(IReadOnlyList<T> changes, Action<T> prepare, Action verify, Action<T> commit,
        Action<T> activate, Action<T> rollback, Action<Exception> recoveryError, out Exception? error)
    {
        try
        {
            foreach (var change in changes)
                prepare(change);
            verify();
            foreach (var change in changes)
                commit(change);
            foreach (var change in changes)
                activate(change);
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception;
            foreach (var change in changes)
            {
                try { rollback(change); }
                catch (Exception recovery) { recoveryError(recovery); }
            }
            foreach (var change in changes)
            {
                try { activate(change); }
                catch (Exception recovery) { recoveryError(recovery); }
            }
            return false;
        }
    }
}
