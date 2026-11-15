// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.DeadSpace._Soyuz.MilitiaOperations;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.NPC.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.DeadSpace.TEMP_FOR_EVENT.Elfiry;

public sealed partial class EventCommandSystem
{
    [Dependency] private readonly PathfindingSystem _pathfinding = default!;
    [Dependency] private readonly SharedMapSystem _gridMaps = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    private readonly Dictionary<EntityUid, BoardingLink> _boarding = new();
    private TimeSpan _nextBoarding;
    private static readonly Vector2i[] BoardingOffsets = [Vector2i.Zero, new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    private void ClearBoarding()
    {
        foreach (var link in _boarding.Values)
            _pathfinding.RemovePortal(link.Handle);
        _boarding.Clear();
        _nextBoarding = TimeSpan.Zero;
    }

    public override void Shutdown()
    {
        ClearBoarding();
        base.Shutdown();
    }

    private void UpdateBoarding()
    {
        if (_timing.CurTime < _nextBoarding)
            return;
        _nextBoarding = _timing.CurTime + TimeSpan.FromSeconds(2);
        var maps = new HashSet<MapId>();
        var units = EntityQueryEnumerator<EventSquadUnitComponent, TransformComponent>();
        while (units.MoveNext(out _, out _, out var transform))
            maps.Add(transform.MapID);
        var retained = new HashSet<EntityUid>();
        var doors = EntityQueryEnumerator<DoorComponent, TransformComponent>();
        while (doors.MoveNext(out var uid, out _, out var transform))
        {
            if (!maps.Contains(transform.MapID) || transform.GridUid is not { } grid ||
                transform.MapUid is not { } planet || planet == grid || HasComp<MilitiaOperationGridComponent>(grid) ||
                !TryComp<MapGridComponent>(grid, out var gridComp) || !TryComp<MapGridComponent>(planet, out var planetComp))
                continue;
            var world = _transform.GetWorldPosition(uid);
            var rotation = _transform.GetWorldRotation(uid);
            if (_boarding.TryGetValue(uid, out var existing) && existing.Map == transform.MapID &&
                Vector2.DistanceSquared(existing.Position, world) < 0.01f && existing.Rotation.Equals(rotation))
            {
                retained.Add(uid);
                continue;
            }
            if (_boarding.Remove(uid, out var previous))
                _pathfinding.RemovePortal(previous.Handle);
            if (!FindBoardingSides(uid, grid, gridComp, planet, planetComp, out var inside, out var outside) ||
                !_pathfinding.TryCreatePortal(inside, outside, out var handle))
                continue;
            _boarding[uid] = new BoardingLink(handle, transform.MapID, world, rotation);
            retained.Add(uid);
        }
        foreach (var removed in _boarding.Keys.Where(uid => !retained.Contains(uid)).ToArray())
        {
            _pathfinding.RemovePortal(_boarding[removed].Handle);
            _boarding.Remove(removed);
        }
    }

    private bool FindBoardingSides(EntityUid door, EntityUid grid, MapGridComponent gridComp,
        EntityUid planet, MapGridComponent planetComp, out EntityCoordinates inside, out EntityCoordinates outside)
    {
        inside = default;
        outside = default;
        var doorCoordinates = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(door));
        var center = _gridMaps.CoordinatesToTile(grid, gridComp, doorCoordinates);
        foreach (var offset in BoardingOffsets)
        {
            var indices = center + offset;
            if (!_gridMaps.TryGetTileRef(grid, gridComp, indices, out var tile) || tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile))
                continue;
            var candidate = _gridMaps.GridTileToLocal(grid, gridComp, indices);
            var from = _transform.ToMapCoordinates(candidate);
            if (!_combat.EventStandingSpace(from, ignoreMobs: true))
                continue;
            foreach (var direction in BoardingOffsets.Skip(1))
            for (var step = 1; step <= 3; step++)
            {
                var outsideIndices = center + direction * step;
                if (_gridMaps.TryGetTileRef(grid, gridComp, outsideIndices, out var shuttleTile) &&
                    !shuttleTile.Tile.IsEmpty && !_turf.IsSpace(shuttleTile.Tile))
                    continue;
                var to = _transform.ToMapCoordinates(_gridMaps.GridTileToLocal(grid, gridComp, outsideIndices));
                if (Vector2.DistanceSquared(from.Position, to.Position) > 9f)
                    continue;
                var planetCoordinates = _transform.ToCoordinates(planet, to);
                var planetIndices = _gridMaps.CoordinatesToTile(planet, planetComp, planetCoordinates);
                if (!_gridMaps.TryGetTileRef(planet, planetComp, planetIndices, out var ground) || ground.Tile.IsEmpty ||
                    _turf.IsSpace(ground.Tile) || !_combat.EventStandingSpace(to, ignoreMobs: true) ||
                    !_interaction.InRangeUnobstructed(from, to, 3f, CollisionGroup.MobMask,
                        predicate: entity => entity == door || HasComp<Content.Shared.Mobs.Components.MobStateComponent>(entity)))
                    continue;
                inside = candidate;
                outside = _gridMaps.GridTileToLocal(planet, planetComp, planetIndices);
                return true;
            }
        }
        return false;
    }

    private sealed record BoardingLink(int Handle, MapId Map, Vector2 Position, Angle Rotation);
}
