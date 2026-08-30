using System.Linq;
using System.Numerics;
using Content.Server.Storage.EntitySystems;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Storage.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Materializes a completed reward snapshot into the pool's configured physical container.
/// </summary>
public sealed class RepairOrderRewardDeliverySystem : EntitySystem
{
    private const int SearchRadius = 2;

    [Dependency] private readonly EntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private List<Entity<MapGridComponent>> _intersectingGrids = new();
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("repair_orders");
    }

    /// <summary>
    /// Creates and fills one reward container beside the exact console used for submission.
    /// On failure every entity created by this attempt is queued for deletion.
    /// </summary>
    public bool TryDeliver(
        EntityUid console,
        RepairOrderPrototype order,
        IReadOnlyList<RepairOrderRewardResult> rewards,
        out EntityUid deliveryContainer)
    {
        deliveryContainer = EntityUid.Invalid;
        if (!_prototype.TryIndex<RepairRewardPoolPrototype>(order.RewardPool, out var pool))
        {
            _sawmill.Warning(
                $"Cannot deliver rewards for repair order {order.ID}: reward pool {order.RewardPool} is missing.");
            return false;
        }

        if (!_prototype.TryIndex<EntityPrototype>(pool.DeliveryContainer, out _))
        {
            _sawmill.Warning(
                $"Cannot deliver rewards for repair order {order.ID}: delivery container {pool.DeliveryContainer} is missing.");
            return false;
        }

        if (!TryFindDeliveryCoordinates(console, out var coordinates, out var usedFallback))
        {
            _sawmill.Warning(
                $"Cannot deliver rewards for repair order {order.ID}: console {ToPrettyString(console)} is not on a usable grid.");
            return false;
        }

        var created = new List<EntityUid>();
        try
        {
            deliveryContainer = Spawn(pool.DeliveryContainer, coordinates);
            created.Add(deliveryContainer);

            if (usedFallback)
            {
                // Use the engine's ordinary local drop behavior when every checked neighbor is obstructed.
                _transform.DropNextTo(deliveryContainer, console);
                _sawmill.Warning(
                    $"No unobstructed neighboring tile was found for reward container {deliveryContainer}; " +
                    $"used DropNextTo fallback at console {ToPrettyString(console)}.");
            }

            if (!TryComp<EntityStorageComponent>(deliveryContainer, out var storage))
                throw new InvalidOperationException($"Delivery container {pool.DeliveryContainer} has no EntityStorage component.");

            foreach (var result in rewards)
            {
                if (!_prototype.TryIndex<RepairRewardPrototype>(result.Reward, out var reward))
                    throw new InvalidOperationException($"Reward prototype {result.Reward} is missing during delivery.");

                if (!_prototype.TryIndex<EntityPrototype>(reward.Entity, out _))
                    throw new InvalidOperationException($"Reward entity prototype {reward.Entity} is missing during delivery.");

                for (var i = 0; i < result.Count; i++)
                {
                    var item = Spawn(reward.Entity, Transform(deliveryContainer).Coordinates);
                    created.Add(item);

                    if (_entityStorage.Insert(item, deliveryContainer, storage))
                        continue;

                    // Capacity and shape restrictions must never destroy an earned reward.
                    _transform.DropNextTo(item, deliveryContainer);
                    _sawmill.Warning(
                        $"Reward {reward.Entity} did not fit in delivery container {deliveryContainer}; dropped it beside the container.");
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            _sawmill.Error(
                $"Failed to create reward delivery for repair order {order.ID} at console {ToPrettyString(console)}: {exception}");

            // Delete contained rewards before their container so storage destruction cannot spill the failed attempt.
            for (var i = created.Count - 1; i >= 0; i--)
            {
                if (Exists(created[i]))
                    QueueDel(created[i]);
            }

            deliveryContainer = EntityUid.Invalid;
            return false;
        }
    }

    private bool TryFindDeliveryCoordinates(
        EntityUid console,
        out EntityCoordinates coordinates,
        out bool usedFallback)
    {
        coordinates = EntityCoordinates.Invalid;
        usedFallback = false;

        if (!TryComp(console, out TransformComponent? consoleTransform) ||
            consoleTransform.MapID == MapId.Nullspace ||
            consoleTransform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return false;
        }

        var origin = _map.TileIndicesFor(gridUid, grid, consoleTransform.Coordinates);
        foreach (var offset in EnumerateNearbyOffsets())
        {
            var indices = origin + offset;
            if (!_map.TryGetTileRef(gridUid, grid, indices, out var tile) ||
                tile.Tile.IsEmpty ||
                _turf.IsTileBlocked(tile, CollisionGroup.MobMask))
            {
                continue;
            }

            var candidate = _map.GridTileToLocal(gridUid, grid, indices);
            var mapCoordinates = _transform.ToMapCoordinates(candidate);
            var bounds = Box2.CenteredAround(mapCoordinates.Position, new Vector2(grid.TileSize * 0.8f));

            _intersectingGrids.Clear();
            _mapManager.FindGridsIntersecting(
                mapCoordinates.MapId,
                bounds,
                ref _intersectingGrids,
                approx: false,
                includeMap: false);

            if (_intersectingGrids.Any(other => other.Owner != gridUid))
                continue;

            coordinates = candidate;
            return true;
        }

        coordinates = _transform.GetMoverCoordinates(console, consoleTransform);
        usedFallback = true;
        return coordinates != EntityCoordinates.Invalid;
    }

    private static IEnumerable<Vector2i> EnumerateNearbyOffsets()
    {
        // Increasing square rings keep the chosen clear tile as close to the submitting console as possible.
        for (var radius = 1; radius <= SearchRadius; radius++)
        {
            for (var x = -radius; x <= radius; x++)
            {
                for (var y = -radius; y <= radius; y++)
                {
                    if (Math.Abs(x) != radius && Math.Abs(y) != radius)
                        continue;

                    yield return new Vector2i(x, y);
                }
            }
        }
    }
}
