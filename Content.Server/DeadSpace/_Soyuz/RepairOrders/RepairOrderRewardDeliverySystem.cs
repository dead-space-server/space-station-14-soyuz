using System.Linq;
using System.Numerics;
using Content.Server.Storage.EntitySystems;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.GameTicking;
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

    private readonly Dictionary<RepairOrderDeliveryKey, RepairOrderDelivery> _deliveries = new();
    private List<Entity<MapGridComponent>> _intersectingGrids = new();
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("repair_orders");
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _deliveries.Clear());
    }

    /// <summary>
    /// Creates as many protected reward containers as necessary beside the exact console used for submission.
    /// On failure every entity created by this attempt is queued for deletion.
    /// </summary>
    public bool TryDeliver(
        EntityUid station,
        int runtimeId,
        EntityUid console,
        RepairOrderPrototype order,
        IReadOnlyList<RepairOrderRewardResult> rewards,
        out RepairOrderDelivery delivery)
    {
        var key = new RepairOrderDeliveryKey(station, runtimeId);
        if (_deliveries.TryGetValue(key, out var existingDelivery))
        {
            delivery = existingDelivery;
            _sawmill.Debug(
                $"Reused existing reward delivery for repair order {runtimeId} on station {station}; " +
                "no additional rewards or containers were spawned.");
            return true;
        }

        delivery = default!;
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

        var attempt = new RepairOrderDelivery(key);
        try
        {
            if (!TryCreateDeliveryContainer(console, order, pool, attempt, out var currentContainer, out var currentStorage))
                throw new InvalidOperationException("The first protected reward container could not be created.");

            foreach (var result in rewards)
            {
                if (!_prototype.TryIndex<RepairRewardPrototype>(result.Reward, out var reward))
                    throw new InvalidOperationException($"Reward prototype {result.Reward} is missing during delivery.");

                if (!_prototype.TryIndex<EntityPrototype>(reward.Entity, out _))
                    throw new InvalidOperationException($"Reward entity prototype {reward.Entity} is missing during delivery.");

                for (var i = 0; i < result.Count; i++)
                {
                    if (IsAtCapacity(currentStorage))
                    {
                        if (!TryCreateDeliveryContainer(
                                console,
                                order,
                                pool,
                                attempt,
                                out currentContainer,
                                out currentStorage))
                        {
                            throw new InvalidOperationException(
                                $"A full reward container could not be followed by another protected container for {reward.Entity}.");
                        }

                        if (IsAtCapacity(currentStorage))
                        {
                            throw new InvalidOperationException(
                                $"A fresh delivery container {pool.DeliveryContainer} has no usable capacity for {reward.Entity}.");
                        }
                    }

                    var item = Spawn(reward.Entity, Transform(currentContainer).Coordinates);
                    attempt.RewardEntities.Add(item);

                    if (currentStorage.Open)
                    {
                        throw new InvalidOperationException(
                            $"Delivery container {pool.DeliveryContainer} spawned open and cannot securely contain reward {reward.Entity}.");
                    }

                    if (!_entityStorage.CanInsert(item, currentContainer, currentStorage))
                    {
                        _sawmill.Error(
                            $"Cannot deliver reward {reward.Entity} for repair order {order.ID}: protected container " +
                            $"{pool.DeliveryContainer} rejects the entity while not full " +
                            $"({currentStorage.Contents.ContainedEntities.Count}/{currentStorage.Capacity}). " +
                            "No unsecured world-drop fallback will be used.");
                        throw new InvalidOperationException(
                            $"Reward entity {reward.Entity} is incompatible with delivery container {pool.DeliveryContainer}.");
                    }

                    if (!_entityStorage.Insert(item, currentContainer, currentStorage))
                    {
                        throw new InvalidOperationException(
                            $"Reward entity {reward.Entity} passed storage checks but insertion into {currentContainer} failed.");
                    }
                }
            }

            _deliveries.Add(key, attempt);
            delivery = attempt;
            return true;
        }
        catch (Exception exception)
        {
            _sawmill.Error(
                $"Failed to create reward delivery for repair order {order.ID} at console {ToPrettyString(console)}: {exception}");

            Rollback(attempt);
            return false;
        }
    }

    /// <summary>
    /// Idempotently removes an uncommitted physical delivery. Rewards are deleted before their containers so
    /// destroying an EntityStorage cannot spill payment from a failed completion attempt into the world.
    /// </summary>
    public void Rollback(RepairOrderDelivery? delivery)
    {
        if (delivery == null || delivery.Committed)
            return;

        if (_deliveries.TryGetValue(delivery.Key, out var tracked) && ReferenceEquals(tracked, delivery))
            _deliveries.Remove(delivery.Key);

        for (var i = delivery.RewardEntities.Count - 1; i >= 0; i--)
        {
            if (Exists(delivery.RewardEntities[i]))
                QueueDel(delivery.RewardEntities[i]);
        }

        for (var i = delivery.ContainersInternal.Count - 1; i >= 0; i--)
        {
            if (Exists(delivery.ContainersInternal[i]))
                QueueDel(delivery.ContainersInternal[i]);
        }

        delivery.RewardEntities.Clear();
        delivery.ContainersInternal.Clear();
    }

    /// <summary>
    /// Transfers ownership of a prepared delivery to a successfully completed order. Once committed, even a
    /// repeated rollback request cannot delete or duplicate the delivered rewards.
    /// </summary>
    public void Commit(RepairOrderDelivery delivery)
    {
        delivery.Committed = true;
    }

    private bool TryCreateDeliveryContainer(
        EntityUid console,
        RepairOrderPrototype order,
        RepairRewardPoolPrototype pool,
        RepairOrderDelivery attempt,
        out EntityUid deliveryContainer,
        out EntityStorageComponent storage)
    {
        deliveryContainer = EntityUid.Invalid;
        storage = default!;
        if (!TryFindDeliveryCoordinates(
                console,
                attempt,
                out var coordinates,
                out var usedDropFallback,
                out var reusedFirstPosition))
        {
            _sawmill.Warning(
                $"Cannot deliver rewards for repair order {order.ID}: console {ToPrettyString(console)} is not on a usable grid.");
            return false;
        }

        deliveryContainer = Spawn(pool.DeliveryContainer, coordinates);
        attempt.ContainersInternal.Add(deliveryContainer);

        if (usedDropFallback)
        {
            // This fallback moves the protected crate, never an individual reward entity.
            _transform.DropNextTo(deliveryContainer, console);
            _sawmill.Warning(
                $"No unobstructed neighboring tile was found for protected reward container {deliveryContainer}; " +
                $"used DropNextTo placement at console {ToPrettyString(console)}.");
        }

        if (!TryRememberContainerPlacement(deliveryContainer, attempt))
        {
            _sawmill.Error(
                $"Cannot remember the actual placement of protected reward container {deliveryContainer} " +
                $"for repair order {order.ID}.");
            return false;
        }

        if (reusedFirstPosition)
        {
            _sawmill.Debug(
                $"Every unreserved reward-crate cell within radius {SearchRadius} is unavailable; " +
                $"placed protected container {deliveryContainer} at the actual position of the first container.");
        }

        if (!TryComp(deliveryContainer, out EntityStorageComponent? foundStorage))
        {
            _sawmill.Error(
                $"Cannot deliver rewards for repair order {order.ID}: delivery container " +
                $"{pool.DeliveryContainer} has no EntityStorage component.");
            return false;
        }

        storage = foundStorage;
        return true;
    }

    private static bool IsAtCapacity(EntityStorageComponent storage)
    {
        return storage.Contents.ContainedEntities.Count >= storage.Capacity;
    }

    private bool TryFindDeliveryCoordinates(
        EntityUid console,
        RepairOrderDelivery attempt,
        out EntityCoordinates coordinates,
        out bool usedDropFallback,
        out bool reusedFirstPosition)
    {
        coordinates = EntityCoordinates.Invalid;
        usedDropFallback = false;
        reusedFirstPosition = false;

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
            if (attempt.ReservedGridCells.Contains(new RepairOrderDeliveryCell(gridUid, indices)) ||
                !_map.TryGetTileRef(gridUid, grid, indices, out var tile) ||
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

        if (attempt.FirstContainerCoordinates is { } firstContainerCoordinates)
        {
            // Additional crates intentionally stack at the first crate only after all ordinary cells are exhausted.
            coordinates = firstContainerCoordinates;
            reusedFirstPosition = true;
            return coordinates != EntityCoordinates.Invalid;
        }

        // Preserve the original first-crate fallback. Its actual post-DropNextTo coordinates are captured below.
        coordinates = _transform.GetMoverCoordinates(console, consoleTransform);
        usedDropFallback = true;
        return coordinates != EntityCoordinates.Invalid;
    }

    private bool TryRememberContainerPlacement(EntityUid container, RepairOrderDelivery attempt)
    {
        if (!TryComp(container, out TransformComponent? containerTransform) ||
            containerTransform.Coordinates == EntityCoordinates.Invalid)
        {
            return false;
        }

        // This is deliberately read after DropNextTo, so a fallback first crate records where it really ended up.
        attempt.FirstContainerCoordinates ??= containerTransform.Coordinates;

        if (containerTransform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return true;
        }

        var indices = _map.TileIndicesFor(gridUid, grid, containerTransform.Coordinates);
        attempt.ReservedGridCells.Add(new RepairOrderDeliveryCell(gridUid, indices));
        return true;
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

/// <summary>
/// Ownership handle for physical entities created by one delivery attempt.
/// The completion system either commits the listed protected containers or rolls the whole handle back.
/// </summary>
public sealed class RepairOrderDelivery
{
    internal readonly RepairOrderDeliveryKey Key;
    internal readonly List<EntityUid> ContainersInternal = new();
    internal readonly List<EntityUid> RewardEntities = new();
    internal readonly HashSet<RepairOrderDeliveryCell> ReservedGridCells = new();
    internal EntityCoordinates? FirstContainerCoordinates;
    internal bool Committed;

    public IReadOnlyList<EntityUid> Containers => ContainersInternal;

    internal RepairOrderDelivery(RepairOrderDeliveryKey key)
    {
        Key = key;
    }
}

internal readonly record struct RepairOrderDeliveryKey(EntityUid Station, int RuntimeId);

internal readonly record struct RepairOrderDeliveryCell(EntityUid Grid, Vector2i Indices);
