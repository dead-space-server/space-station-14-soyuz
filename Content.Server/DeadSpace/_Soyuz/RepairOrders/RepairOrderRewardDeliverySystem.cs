// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.Storage.EntitySystems;
using Content.Server.Station.Systems;
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
/// Materializes a shop purchase into the pool's configured physical container.
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
    [Dependency] private readonly StationSystem _station = default!;
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
    /// Creates as many protected reward containers as necessary beside the purchasing console.
    /// On failure every entity created by this attempt is queued for deletion.
    /// </summary>
    public bool TryDeliver(
        EntityUid station,
        Guid purchaseId,
        EntityUid console,
        RepairRewardPoolPrototype pool,
        IReadOnlyList<RepairOrderRewardResult> rewards,
        out RepairOrderDelivery delivery)
    {
        if (!TryComp(console, out TransformComponent? consoleTransform) ||
            consoleTransform.MapID == MapId.Nullspace ||
            consoleTransform.GridUid == null)
        {
            delivery = default!;
            _sawmill.Warning(
                $"Cannot deliver shop purchase {purchaseId}: console {ToPrettyString(console)} " +
                "is not on a usable grid.");
            return false;
        }

        var anchor = new RepairOrderDeliveryAnchor(
            consoleTransform.Coordinates,
            console,
            $"console {ToPrettyString(console)}",
            IncludeOrigin: false);
        return TryDeliver(station, purchaseId, anchor, pool, rewards, out delivery);
    }

    /// <summary>
    /// Uses a safe tile on the station's largest grid when no submitting console exists, while retaining the same
    /// protected-container insertion, rollback, reservation and idempotency path as manual delivery.
    /// </summary>
    public bool TryDeliverAtStation(
        EntityUid station,
        Guid purchaseId,
        RepairRewardPoolPrototype pool,
        IReadOnlyList<RepairOrderRewardResult> rewards,
        out RepairOrderDelivery delivery)
    {
        delivery = default!;
        var stationGrid = _station.GetLargestGrid(station);
        if (stationGrid == null ||
            !TryComp<MapGridComponent>(stationGrid.Value, out var grid) ||
            !TryFindStationDeliveryOrigin((stationGrid.Value, grid), out var origin))
        {
            _sawmill.Warning(
                $"Cannot deliver shop purchase {purchaseId}: station {station} has no safe delivery tile.");
            return false;
        }

        var anchor = new RepairOrderDeliveryAnchor(
            origin,
            null,
            $"station grid {stationGrid.Value}",
            IncludeOrigin: true);
        return TryDeliver(station, purchaseId, anchor, pool, rewards, out delivery);
    }

    /// <summary>
    /// Delivers around a previously remembered grid-local console position through the same protected placement,
    /// reservation, oversized-reward, rollback, and idempotency path as every other repair-order delivery.
    /// </summary>
    public bool TryDeliverAtCoordinates(
        EntityUid station,
        Guid purchaseId,
        EntityCoordinates coordinates,
        RepairRewardPoolPrototype pool,
        IReadOnlyList<RepairOrderRewardResult> rewards,
        out RepairOrderDelivery delivery)
    {
        var anchor = new RepairOrderDeliveryAnchor(
            coordinates,
            null,
            $"last known repair-orders console position {coordinates}",
            IncludeOrigin: true);
        return TryDeliver(station, purchaseId, anchor, pool, rewards, out delivery);
    }

    // DS14-Soyuz-start: medical orders reuse the same protected placement and delivery handle.
    public bool TryDeliverMedical(
        EntityUid station,
        Guid purchaseId,
        EntityUid console,
        EntProtoId containerPrototype,
        IReadOnlyList<EntProtoId> items,
        out RepairOrderDelivery delivery)
    {
        delivery = default!;
        if (items.Count == 0 ||
            !_prototype.TryIndex<EntityPrototype>(containerPrototype, out _) ||
            !TryComp(console, out TransformComponent? consoleTransform) ||
            consoleTransform.MapID == MapId.Nullspace || consoleTransform.GridUid == null)
            return false;

        foreach (var item in items)
        {
            if (!_prototype.TryIndex<EntityPrototype>(item, out _))
                return false;
        }

        var key = new RepairOrderDeliveryKey(station, RepairOrderDeliveryKind.MedicalShopPurchase, purchaseId);
        if (_deliveries.ContainsKey(key))
            return false;

        var anchor = new RepairOrderDeliveryAnchor(consoleTransform.Coordinates, console,
            $"medical orders console {ToPrettyString(console)}", IncludeOrigin: false);
        var attempt = new RepairOrderDelivery(key);
        try
        {
            if (!TryCreateDeliveryContainer(anchor, containerPrototype, "medical orders", attempt,
                    out var currentContainer, out var storage))
                throw new InvalidOperationException("Cannot prepare the medical delivery container.");

            foreach (var entityId in items)
            {
                if (storage.Contents.ContainedEntities.Count >= storage.Capacity)
                {
                    if (!TryCreateDeliveryContainer(anchor, containerPrototype, "medical orders", attempt,
                            out currentContainer, out storage))
                        throw new InvalidOperationException("Cannot prepare an additional medical delivery container.");
                }

                var item = Spawn(entityId, Transform(currentContainer).Coordinates);
                attempt.RewardEntities.Add(item);
                if (storage.Open || !_entityStorage.CanInsert(item, currentContainer, storage))
                {
                    if (storage.Contents.ContainedEntities.Count == 0 ||
                        !TryCreateDeliveryContainer(anchor, containerPrototype, "medical orders", attempt,
                            out currentContainer, out storage) ||
                        storage.Open || !_entityStorage.CanInsert(item, currentContainer, storage))
                        throw new InvalidOperationException($"Medical delivery container cannot hold {entityId}.");

                    _transform.SetCoordinates(item, Transform(currentContainer).Coordinates);
                }

                if (!_entityStorage.Insert(item, currentContainer, storage))
                    throw new InvalidOperationException($"Cannot insert {entityId} into medical delivery.");
            }

            _deliveries.Add(key, attempt);
            delivery = attempt;
            return true;
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Medical shop delivery {purchaseId} failed: {exception}");
            Rollback(attempt);
            return false;
        }
    }
    // DS14-Soyuz-end

    private bool TryDeliver(
        EntityUid station,
        Guid purchaseId,
        RepairOrderDeliveryAnchor anchor,
        RepairRewardPoolPrototype pool,
        IReadOnlyList<RepairOrderRewardResult> rewards,
        out RepairOrderDelivery delivery)
    {
        var key = new RepairOrderDeliveryKey(station, RepairOrderDeliveryKind.ShopPurchase, purchaseId);
        if (_deliveries.TryGetValue(key, out var existingDelivery))
        {
            if (existingDelivery.Committed)
            {
                delivery = default!;
                _sawmill.Warning($"Shop delivery {purchaseId} on station {station} was already committed.");
                return false;
            }

            delivery = existingDelivery;
            _sawmill.Debug(
                $"Reused existing shop delivery {purchaseId} on station {station}; " +
                "no additional rewards or containers were spawned.");
            return true;
        }

        delivery = default!;
        if (!_prototype.TryIndex<EntityPrototype>(pool.DeliveryContainer, out _))
        {
            _sawmill.Warning(
                $"Cannot deliver shop purchase {purchaseId}: delivery container {pool.DeliveryContainer} is missing.");
            return false;
        }

        var attempt = new RepairOrderDelivery(key);
        try
        {
            if (!TryCreateDeliveryContainer(anchor, pool, attempt, out var currentContainer, out var currentStorage))
                throw new InvalidOperationException("The first protected reward container could not be created.");

            foreach (var result in rewards)
            {
                if (!_prototype.TryIndex<RepairRewardPrototype>(result.Reward, out var reward))
                    throw new InvalidOperationException($"Reward prototype {result.Reward} is missing during delivery.");

                if (!_prototype.TryIndex<EntityPrototype>(reward.Entity, out _))
                    throw new InvalidOperationException($"Reward entity prototype {reward.Entity} is missing during delivery.");

                for (var i = 0; i < result.Count; i++)
                {
                    if (IsAtCapacity(currentStorage) && currentStorage.Contents.ContainedEntities.Count > 0)
                    {
                        if (!TryCreateDeliveryContainer(
                                anchor,
                                pool,
                                attempt,
                                out currentContainer,
                                out currentStorage))
                        {
                            throw new InvalidOperationException(
                                $"A full reward container could not be followed by another protected container for {reward.Entity}.");
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
                        // A non-empty container may reject an otherwise compatible reward because its remaining
                        // capacity is insufficient. A single fresh container distinguishes that from an entity
                        // which fundamentally cannot be stored in the configured delivery container.
                        if (currentStorage.Contents.ContainedEntities.Count > 0)
                        {
                            if (!TryCreateDeliveryContainer(
                                    anchor,
                                    pool,
                                    attempt,
                                    out currentContainer,
                                    out currentStorage))
                            {
                                throw new InvalidOperationException(
                                    $"A reward container which rejected {reward.Entity} could not be followed by another protected container.");
                            }

                            _transform.SetCoordinates(item, Transform(currentContainer).Coordinates);
                            if (currentStorage.Open)
                            {
                                throw new InvalidOperationException(
                                    $"Delivery container {pool.DeliveryContainer} spawned open and cannot securely contain reward {reward.Entity}.");
                            }
                        }

                        if (!_entityStorage.CanInsert(item, currentContainer, currentStorage))
                        {
                            if (!TryPlaceOversizedReward(item, anchor, attempt))
                            {
                                throw new InvalidOperationException(
                                    $"Oversized reward entity {reward.Entity} could not be placed at {anchor.Description}.");
                            }

                            _sawmill.Info(
                                $"Reward {reward.Entity} cannot fit into an empty delivery container " +
                                $"{pool.DeliveryContainer} and was delivered as an oversized reward.");
                            continue;
                        }
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
            try
            {
                _sawmill.Error(
                    $"Failed to create shop delivery {purchaseId} at {anchor.Description}: {exception}");
            }
            finally
            {
                Rollback(attempt);
            }
            return false;
        }
    }

    /// <summary>
    /// Idempotently removes an uncommitted physical delivery. Rewards are deleted before their containers so
    /// destroying an EntityStorage cannot spill items from a failed purchase into the world.
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
    /// Transfers ownership of a prepared delivery to a successful purchase. Once committed, even a
    /// repeated rollback request cannot delete or duplicate the delivered rewards.
    /// </summary>
    public void Commit(RepairOrderDelivery delivery)
    {
        delivery.Committed = true;
    }

    private bool TryCreateDeliveryContainer(
        RepairOrderDeliveryAnchor anchor,
        RepairRewardPoolPrototype pool,
        RepairOrderDelivery attempt,
        out EntityUid deliveryContainer,
        out EntityStorageComponent storage)
    {
        return TryCreateDeliveryContainer(anchor, pool.DeliveryContainer, pool.ID, attempt,
            out deliveryContainer, out storage);
    }

    // DS14-Soyuz-start: let other Soyuz shops reuse repair-order placement and rollback.
    private bool TryCreateDeliveryContainer(
        RepairOrderDeliveryAnchor anchor,
        EntProtoId containerPrototype,
        string source,
        RepairOrderDelivery attempt,
        out EntityUid deliveryContainer,
        out EntityStorageComponent storage)
    {
        deliveryContainer = EntityUid.Invalid;
        storage = default!;
        if (!TryFindDeliveryCoordinates(
                anchor,
                attempt,
                out var coordinates,
                out var usedDropFallback,
                out var reusedFirstPosition))
        {
            _sawmill.Warning(
                $"Cannot deliver rewards from {source}: {anchor.Description} has no usable placement.");
            return false;
        }

        deliveryContainer = Spawn(containerPrototype, coordinates);
        attempt.ContainersInternal.Add(deliveryContainer);

        if (usedDropFallback)
        {
            // This fallback moves the protected crate, never an individual reward entity.
            _transform.DropNextTo(deliveryContainer, anchor.DropFallbackEntity!.Value);
            _sawmill.Warning(
                $"No unobstructed neighboring tile was found for protected reward container {deliveryContainer}; " +
                $"used DropNextTo placement at {anchor.Description}.");
        }

        if (!TryRememberDeliveryPlacement(deliveryContainer, attempt, rememberAsFirstContainer: true))
        {
            _sawmill.Error(
                $"Cannot remember the actual placement of protected reward container {deliveryContainer} " +
                $"for {source}.");
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
                $"Cannot deliver rewards from {source}: delivery container " +
                $"{containerPrototype} has no EntityStorage component.");
            return false;
        }

        storage = foundStorage;
        return true;
    }
    // DS14-Soyuz-end

    private bool TryPlaceOversizedReward(
        EntityUid reward,
        RepairOrderDeliveryAnchor anchor,
        RepairOrderDelivery attempt)
    {
        if (!TryFindDeliveryCoordinates(
                anchor,
                attempt,
                out var coordinates,
                out var usedDropFallback,
                out _))
        {
            return false;
        }

        _transform.SetCoordinates(reward, coordinates);
        if (usedDropFallback)
            _transform.DropNextTo(reward, anchor.DropFallbackEntity!.Value);

        return TryRememberDeliveryPlacement(reward, attempt, rememberAsFirstContainer: false);
    }

    private static bool IsAtCapacity(EntityStorageComponent storage)
    {
        return storage.Contents.ContainedEntities.Count >= storage.Capacity;
    }

    private bool TryFindDeliveryCoordinates(
        RepairOrderDeliveryAnchor anchor,
        RepairOrderDelivery attempt,
        out EntityCoordinates coordinates,
        out bool usedDropFallback,
        out bool reusedFirstPosition)
    {
        coordinates = EntityCoordinates.Invalid;
        usedDropFallback = false;
        reusedFirstPosition = false;

        if (anchor.Coordinates == EntityCoordinates.Invalid)
            return false;

        var gridUid = anchor.Coordinates.EntityId;
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return false;
        }

        var origin = _map.TileIndicesFor(gridUid, grid, anchor.Coordinates);
        foreach (var offset in EnumerateNearbyOffsets(anchor.IncludeOrigin))
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

        if (anchor.DropFallbackEntity is not { } fallbackEntity ||
            !TryComp(fallbackEntity, out TransformComponent? fallbackTransform))
        {
            return false;
        }

        // Preserve the original console fallback. Its actual post-DropNextTo coordinates are captured below.
        coordinates = _transform.GetMoverCoordinates(fallbackEntity, fallbackTransform);
        usedDropFallback = true;
        return coordinates != EntityCoordinates.Invalid;
    }

    private bool TryFindStationDeliveryOrigin(
        Entity<MapGridComponent> stationGrid,
        out EntityCoordinates coordinates)
    {
        foreach (var tile in _map.GetAllTiles(stationGrid.Owner, stationGrid.Comp))
        {
            if (tile.Tile.IsEmpty || _turf.IsTileBlocked(tile, CollisionGroup.MobMask))
                continue;

            var candidate = _map.GridTileToLocal(stationGrid.Owner, stationGrid.Comp, tile.GridIndices);
            var mapCoordinates = _transform.ToMapCoordinates(candidate);
            var bounds = Box2.CenteredAround(
                mapCoordinates.Position,
                new Vector2(stationGrid.Comp.TileSize * 0.8f));

            _intersectingGrids.Clear();
            _mapManager.FindGridsIntersecting(
                mapCoordinates.MapId,
                bounds,
                ref _intersectingGrids,
                approx: false,
                includeMap: false);

            if (_intersectingGrids.Any(other => other.Owner != stationGrid.Owner))
                continue;

            coordinates = candidate;
            return true;
        }

        coordinates = EntityCoordinates.Invalid;
        return false;
    }

    private bool TryRememberDeliveryPlacement(
        EntityUid entity,
        RepairOrderDelivery attempt,
        bool rememberAsFirstContainer)
    {
        if (!TryComp(entity, out TransformComponent? transform) ||
            transform.Coordinates == EntityCoordinates.Invalid)
        {
            return false;
        }

        // This is deliberately read after DropNextTo, so a fallback first crate records where it really ended up.
        if (rememberAsFirstContainer)
            attempt.FirstContainerCoordinates ??= transform.Coordinates;

        if (transform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return true;
        }

        var indices = _map.TileIndicesFor(gridUid, grid, transform.Coordinates);
        attempt.ReservedGridCells.Add(new RepairOrderDeliveryCell(gridUid, indices));
        return true;
    }

    private static IEnumerable<Vector2i> EnumerateNearbyOffsets(bool includeOrigin)
    {
        if (includeOrigin)
            yield return Vector2i.Zero;

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
/// The shop either commits the listed protected containers or rolls the whole handle back.
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

internal enum RepairOrderDeliveryKind : byte
{
    RepairOrder,
    ShopPurchase,
    MedicalShopPurchase, // DS14-Soyuz
}

internal readonly record struct RepairOrderDeliveryKey(EntityUid Station, RepairOrderDeliveryKind Kind, Guid PurchaseId);

internal readonly record struct RepairOrderDeliveryCell(EntityUid Grid, Vector2i Indices);

internal readonly record struct RepairOrderDeliveryAnchor(
    EntityCoordinates Coordinates,
    EntityUid? DropFallbackEntity,
    string Description,
    bool IncludeOrigin);
