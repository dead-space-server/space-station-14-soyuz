// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server.DeadSpace.TEMP_FOR_EVENT.Elfiry;

public enum EventSearchResult : byte { Pending, Found, Blocked }

public sealed class EventWalkSearch(EntityUid grid, MapId map, uint revision, EventMovementTarget target,
    MapCoordinates goal, Vector2 position, Angle rotation, Vector2i start, TimeSpan expires)
{
    public readonly EntityUid Grid = grid;
    public readonly MapId Map = map;
    public readonly uint Revision = revision;
    public readonly EventMovementTarget Target = target;
    public readonly MapCoordinates Goal = goal;
    public readonly Vector2 GridPosition = position;
    public readonly Angle GridRotation = rotation;
    public readonly Vector2i Start = start;
    public readonly TimeSpan Expires = expires;
    private readonly PriorityQueue<Vector2i, float> _frontier = CreateFrontier(start);
    private readonly Dictionary<Vector2i, float> _costs = new() { [start] = 0f };
    private readonly Dictionary<Vector2i, Vector2i> _previous = new();
    private readonly Dictionary<(Vector2i, Vector2i), bool> _edges = new();
    private Vector2i? _reached;
    private int _expanded;
    public List<EntityCoordinates>? Route;
    public int Cursor;
    private static readonly Vector2i[] Adjacent = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    public EventSearchResult Advance(Func<Vector2i, bool> reached, Func<Vector2i, Vector2i, bool> clear,
        Func<Vector2i, float> estimate, float step, int budget = 512, Func<bool>? withinBudget = null)
    {
        if (_reached != null)
            return EventSearchResult.Found;
        for (var visited = 0; visited < budget && _frontier.Count > 0; visited++)
        {
            if (visited > 0 && withinBudget?.Invoke() == false)
                break;
            if (++_expanded > 8192)
                return EventSearchResult.Blocked;
            var current = _frontier.Dequeue();
            if (reached(current))
            {
                _reached = current;
                return EventSearchResult.Found;
            }
            foreach (var offset in Adjacent)
            {
                var next = current + offset;
                var cost = _costs[current] + step;
                if (_costs.TryGetValue(next, out var known) && known <= cost)
                    continue;
                if (!_edges.TryGetValue((current, next), out var passable))
                {
                    passable = clear(current, next);
                    _edges[(current, next)] = passable;
                    _edges[(next, current)] = passable;
                }
                if (!passable)
                    continue;
                _previous[next] = current;
                _costs[next] = cost;
                _frontier.Enqueue(next, cost + estimate(next));
            }
        }
        return _frontier.Count > 0 ? EventSearchResult.Pending : EventSearchResult.Blocked;
    }

    public List<Vector2i>? Reconstruct()
    {
        if (_reached is not { } node)
            return null;
        var result = new List<Vector2i>();
        while (true)
        {
            result.Add(node);
            if (node == Start)
                break;
            node = _previous[node];
        }
        result.Reverse();
        return result;
    }

    private static PriorityQueue<Vector2i, float> CreateFrontier(Vector2i start)
    {
        var result = new PriorityQueue<Vector2i, float>();
        result.Enqueue(start, 0f);
        return result;
    }
}
