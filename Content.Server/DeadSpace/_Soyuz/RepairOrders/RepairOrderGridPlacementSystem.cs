// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Searches for an unoccupied position near the repair-orders console.
/// </summary>
public sealed class RepairOrderGridPlacementSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    /// <summary>
    /// Searches progressively farther from <paramref name="origin"/>. Every distance tries the
    /// center and both lateral offsets before moving outward, so a free nearby slot is not skipped by jitter.
    /// </summary>
    public bool TryFindPlacement(
        MapId mapId,
        Vector2 origin,
        Vector2 direction,
        Box2 localBounds,
        float spawnDistance,
        float lateralOffset,
        int attempts,
        out MapCoordinates coordinates,
        out Angle angle)
    {
        if (mapId == MapId.Nullspace ||
            direction.LengthSquared() <= float.Epsilon ||
            spawnDistance <= 0f ||
            attempts <= 0)
        {
            coordinates = MapCoordinates.Nullspace;
            angle = Angle.Zero;
            return false;
        }

        direction = Vector2.Normalize(direction);
        var lateralDirection = new Vector2(-direction.Y, direction.X);
        var offsets = new[] { 0f, lateralOffset, -lateralOffset };
        for (var i = 0; i < attempts; i++)
        {
            var distance = spawnDistance * (0.5f + i * 0.1f);
            foreach (var offset in offsets)
            {
                var position = origin + direction * distance + lateralDirection * offset;
                var translatedBounds = localBounds.Translated(position);
                var randomAngle = _random.NextAngle();
                foreach (var candidateAngle in new[] { randomAngle, Angle.Zero, Angle.FromDegrees(90) })
                {
                    var rotatedBounds = new Box2Rotated(translatedBounds, candidateAngle, position);
                    if (_mapManager.FindGridsIntersecting(mapId, rotatedBounds).Any())
                        continue;

                    coordinates = new MapCoordinates(position, mapId);
                    angle = candidateAngle;
                    return true;
                }
            }
        }

        coordinates = MapCoordinates.Nullspace;
        angle = Angle.Zero;
        return false;
    }
}
