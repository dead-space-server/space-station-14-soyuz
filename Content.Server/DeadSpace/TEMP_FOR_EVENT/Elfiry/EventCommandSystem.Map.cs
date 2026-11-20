// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.DeadSpace.TEMP_FOR_EVENT.Elfiry;
using Content.Shared.Doors.Components;
using Content.Shared.Physics;
using Content.Shared.Pinpointer;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Server.DeadSpace.TEMP_FOR_EVENT.Elfiry;

public sealed partial class EventCommandSystem
{
    [Dependency] private readonly SharedNavMapSystem _navMap = default!;
    private readonly Dictionary<MapId, MapSnapshot> _mapViews = new();

    private EventCommandGridState[] MapView(MapCoordinates origin, float commandRange)
    {
        var radius = Math.Clamp(commandRange, 8f, 512f) + 32f;
        if (_mapViews.TryGetValue(origin.MapId, out var cached) && cached.Expires > _timing.CurTime &&
            cached.Radius >= radius && Vector2.DistanceSquared(cached.Center, origin.Position) < 32f * 32f)
            return cached.Grids;

        var bounds = Box2.CenteredAround(origin.Position, new Vector2(radius * 2f));
        var grids = _maps.GetAllGrids(origin.MapId)
            .Where(grid => _transform.GetWorldMatrix(grid).TransformBox(grid.Comp.LocalAABB).Intersects(bounds))
            .OrderByDescending(grid => HasComp<MapComponent>(grid))
            .ThenBy(grid => Vector2.DistanceSquared(origin.Position, _transform.GetWorldPosition(grid)))
            .Take(32).ToArray();
        var surfaces = grids.ToDictionary(grid => grid.Owner, _ => new List<EventCommandMapRect>());
        foreach (var grid in grids)
        {
            var rows = new Dictionary<int, List<int>>();
            var tiles = _gridMaps.GetTilesEnumerator(grid, grid.Comp, bounds);
            while (tiles.MoveNext(out var tile))
            {
                if (_turf.IsSpace(tile.Tile))
                    continue;
                var indices = tile.GridIndices;
                if (!rows.TryGetValue(indices.Y, out var row))
                    rows[indices.Y] = row = new List<int>();
                row.Add(indices.X);
            }
            AddFloorRects(rows, grid.Comp.TileSize, surfaces[grid]);
        }

        var entities = EntityQueryEnumerator<TransformComponent, FixturesComponent>();
        while (entities.MoveNext(out var uid, out var transform, out var fixtures))
        {
            if (!transform.Anchored || transform.MapID != origin.MapId || TerminatingOrDeleted(uid) ||
                !surfaces.TryGetValue(transform.GridUid ?? transform.MapUid ?? EntityUid.Invalid, out var list) ||
                !bounds.Contains(_transform.GetWorldPosition(uid)))
                continue;
            var category = _navMap.GetEntityType(uid);
            var door = CompOrNull<DoorComponent>(uid);
            var kind = door != null ? door.State == DoorState.Open ? EventMapKind.OpenDoor : EventMapKind.ClosedDoor
                : category == NavMapChunkType.Wall ? HasComp<Content.Shared.Wall.WallComponent>(uid) ? EventMapKind.Wall : EventMapKind.Window
                : EventMapKind.Equipment;
            if (kind == EventMapKind.Equipment && !fixtures.Fixtures.Values.Any(fixture => fixture.Hard &&
                    (fixture.CollisionLayer & (int) CollisionGroup.MobMask) != 0))
                continue;
            var position = transform.LocalPosition;
            if (kind is EventMapKind.Wall or EventMapKind.Window &&
                TryComp<AirtightComponent>(uid, out var airtight) && airtight.AirBlockedDirection is not (AtmosDirection.All or AtmosDirection.Invalid))
            {
                foreach (var direction in new[] { AtmosDirection.North, AtmosDirection.South, AtmosDirection.East, AtmosDirection.West })
                {
                    if ((airtight.AirBlockedDirection & direction) == 0)
                        continue;
                    var normal = direction.ToDirection().ToVec();
                    var size = direction is AtmosDirection.North or AtmosDirection.South ? new Vector2(1f, 0.14f) : new Vector2(0.14f, 1f);
                    list.Add(new EventCommandMapRect(Box2.CenteredAround(position + normal * 0.43f, size), kind, name: Name(uid)));
                }
                continue;
            }
            var dimensions = kind switch
            {
                EventMapKind.ClosedDoor or EventMapKind.OpenDoor => new Vector2(0.86f, 0.24f),
                EventMapKind.Equipment => new Vector2(0.64f),
                _ => new Vector2(0.94f),
            };
            list.Add(new EventCommandMapRect(Box2.CenteredAround(position, dimensions), kind,
                (float) transform.LocalRotation.Theta, Name(uid)));
        }

        var result = grids.Select(grid => new EventCommandGridState(_transform.GetWorldPosition(grid),
            (float) _transform.GetWorldRotation(grid).Theta, HasComp<MapComponent>(grid), surfaces[grid].ToArray())).ToArray();
        _mapViews[origin.MapId] = new MapSnapshot(origin.Position, radius, _timing.CurTime + TimeSpan.FromSeconds(3), result);
        return result;
    }

    private static void AddFloorRects(Dictionary<int, List<int>> rows, float tileSize, List<EventCommandMapRect> output)
    {
        var active = new Dictionary<(int Left, int Right), (int Bottom, int Top)>();
        foreach (var (y, row) in rows.OrderBy(pair => pair.Key))
        {
            row.Sort();
            var spans = new HashSet<(int Left, int Right)>();
            for (var i = 0; i < row.Count; i++)
            {
                var start = row[i];
                var end = start + 1;
                while (i + 1 < row.Count && row[i + 1] == end)
                    end = row[++i] + 1;
                spans.Add((start, end));
            }
            foreach (var (span, vertical) in active.ToArray())
            {
                if (vertical.Top == y && spans.Contains(span))
                    continue;
                Flush(span, vertical);
                active.Remove(span);
            }
            foreach (var span in spans)
                active[span] = active.TryGetValue(span, out var vertical) ? (vertical.Bottom, y + 1) : (y, y + 1);
        }
        foreach (var (span, vertical) in active)
            Flush(span, vertical);
        return;

        void Flush((int Left, int Right) span, (int Bottom, int Top) vertical)
            => output.Add(new EventCommandMapRect(new Box2(span.Left * tileSize, vertical.Bottom * tileSize,
                span.Right * tileSize, vertical.Top * tileSize), EventMapKind.Floor));
    }

    private sealed record MapSnapshot(Vector2 Center, float Radius, TimeSpan Expires, EventCommandGridState[] Grids);
}
