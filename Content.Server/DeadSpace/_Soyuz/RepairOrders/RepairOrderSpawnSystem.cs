using System.Numerics;
using Content.Server.Station.Systems;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

public enum RepairOrderSpawnFailure : byte
{
    None,
    NoStation,
    NoStationGrid,
    LoadFailed,
    InvalidGrid,
    NoSpace,
    TransferFailed,
}

/// <summary>
/// Loads and places damaged repair grids without exposing a partially completed activation.
/// </summary>
public sealed class RepairOrderSpawnSystem : EntitySystem
{
    private const float MinimumSpawnDistance = 32f;
    private const float LateralOffset = 8f;
    private const int PlacementAttempts = 20;

    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly RepairOrderGridPlacementSystem _placement = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("repair_orders");
    }

    public bool TrySpawnDamagedGrid(
        EntityUid console,
        RepairOrderPrototype order,
        out EntityUid spawnedGrid,
        out RepairOrderSpawnFailure failure)
    {
        spawnedGrid = EntityUid.Invalid;
        failure = RepairOrderSpawnFailure.None;

        var consoleXform = Transform(console);
        var stationUid = _station.GetOwningStation(console, consoleXform);
        if (stationUid == null)
        {
            failure = RepairOrderSpawnFailure.NoStation;
            return false;
        }

        var stationGridUid = consoleXform.GridUid;
        if (stationGridUid == null ||
            !TryComp<MapGridComponent>(stationGridUid.Value, out var stationGrid) ||
            _station.GetOwningStation(stationGridUid.Value) != stationUid)
        {
            failure = RepairOrderSpawnFailure.NoStationGrid;
            return false;
        }

        var stationGridXform = Transform(stationGridUid.Value);
        if (stationGridXform.MapID == MapId.Nullspace || stationGridXform.MapUid == null)
        {
            failure = RepairOrderSpawnFailure.NoStationGrid;
            return false;
        }

        MapId? temporaryMapId = null;
        EntityUid loadedGridUid = EntityUid.Invalid;
        var transferred = false;

        try
        {
            var temporaryMap = _map.CreateMap(out var mapId);
            temporaryMapId = mapId;
            _map.SetPaused(temporaryMap, true);

            bool loaded;
            Entity<MapGridComponent>? loadedGrid;
            try
            {
                loaded = _loader.TryLoadGrid(mapId, order.DamagedGridPath, out loadedGrid);
            }
            catch (Exception exception)
            {
                failure = RepairOrderSpawnFailure.LoadFailed;
                _sawmill.Error($"Failed to load damaged repair grid for order {order.ID} from {order.DamagedGridPath}: {exception}");
                return false;
            }

            if (!loaded || loadedGrid is not { } damagedGrid)
            {
                failure = RepairOrderSpawnFailure.LoadFailed;
                return false;
            }

            loadedGridUid = damagedGrid.Owner;
            var damagedBounds = damagedGrid.Comp.LocalAABB;
            if (damagedBounds.Size.X <= 0f || damagedBounds.Size.Y <= 0f)
            {
                failure = RepairOrderSpawnFailure.InvalidGrid;
                return false;
            }

            var (stationPosition, stationRotation) = _transform.GetWorldPositionRotation(stationGridXform);
            var stationBounds = new Box2Rotated(
                    stationGrid.LocalAABB.Translated(stationPosition),
                    stationRotation,
                    stationPosition)
                .CalcBoundingBox();

            var preferredAngle = _transform.GetWorldRotation(consoleXform) - MathF.PI / 2f;
            var spawnDistance = MathF.Max(MinimumSpawnDistance, damagedBounds.MaxDimension * 2f);

            MapCoordinates placementCoordinates = MapCoordinates.Nullspace;
            Angle placementAngle = Angle.Zero;
            var foundPlacement = false;

            for (var directionIndex = 0; directionIndex < 4; directionIndex++)
            {
                var directionAngle = preferredAngle + directionIndex * MathF.PI / 2f;
                var direction = directionAngle.ToVec();
                var outsidePoint = stationBounds.Center + direction * (stationBounds.MaxDimension * 2f);
                var origin = stationBounds.ClosestPoint(outsidePoint);

                if (!_placement.TryFindPlacement(
                        stationGridXform.MapID,
                        origin,
                        direction,
                        damagedBounds,
                        spawnDistance,
                        LateralOffset,
                        PlacementAttempts,
                        out placementCoordinates,
                        out placementAngle))
                {
                    continue;
                }

                foundPlacement = true;
                break;
            }

            if (!foundPlacement)
            {
                failure = RepairOrderSpawnFailure.NoSpace;
                return false;
            }

            var loadedXform = Transform(loadedGridUid);
            _transform.SetParent(loadedGridUid, loadedXform, stationGridXform.MapUid.Value);
            _transform.SetWorldPositionRotation(
                loadedGridUid,
                placementCoordinates.Position,
                placementAngle,
                loadedXform);
            transferred = true;

            _map.DeleteMap(mapId);
            temporaryMapId = null;

            spawnedGrid = loadedGridUid;
            return true;
        }
        catch (Exception exception)
        {
            failure = RepairOrderSpawnFailure.TransferFailed;
            _sawmill.Error($"Failed to spawn repair grid for order {order.ID}: {exception}");
            return false;
        }
        finally
        {
            if (temporaryMapId is { } mapId)
                _map.DeleteMap(mapId);

            if (spawnedGrid == EntityUid.Invalid &&
                loadedGridUid.IsValid() &&
                Exists(loadedGridUid) &&
                (transferred || temporaryMapId == null))
            {
                Del(loadedGridUid);
            }
        }
    }
}
