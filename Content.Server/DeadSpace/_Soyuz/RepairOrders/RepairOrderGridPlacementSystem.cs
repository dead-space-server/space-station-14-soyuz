using System.Linq;
using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Repair-order-local copy of the salvage magnet placement search.
/// Keeping it local avoids changing the existing magnet implementation or behavior.
/// </summary>
public sealed class RepairOrderGridPlacementSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    /// <summary>
    /// Searches progressively farther from <paramref name="origin"/>, with randomized lateral offset and rotation.
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
        var fraction = 0.5f;

        // Match the salvage magnet's progressive distance, lateral jitter and random rotation.
        for (var i = 0; i < attempts; i++)
        {
            var position = origin +
                           direction * (spawnDistance * fraction) +
                           lateralDirection * _random.NextFloat(-lateralOffset, lateralOffset);

            angle = _random.NextAngle();
            var translatedBounds = localBounds.Translated(position);
            var rotatedBounds = new Box2Rotated(translatedBounds, angle, position);

            if (_mapManager.FindGridsIntersecting(mapId, rotatedBounds).Any())
            {
                fraction += 0.1f;
                continue;
            }

            coordinates = new MapCoordinates(position, mapId);
            return true;
        }

        coordinates = MapCoordinates.Nullspace;
        angle = Angle.Zero;
        return false;
    }
}
