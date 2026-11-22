// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Content.Server.NPC.HTN;
using Content.Shared.DeadSpace._Soyuz.MilitiaOperations;
using Content.Shared.Maps;
using Content.Shared.Atmos;
using Content.Shared.Doors.Components;
using Content.Shared.Item;
using Content.Shared.Physics;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    private const float MinimumSpawnDistance = 150f;
    private const float MaximumSpawnDistance = 300f;

    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    private readonly HashSet<MapId> _preparationMaps = new();

    private bool TryPrepareOperation(Entity<MilitiaOperationStateComponent> controller, EntityUid console, MilitiaOperationConfigPrototype config,
        MilitiaOperationPrototype prototype, out MilitiaActiveOperation active)
    {
        active = default!;
        if (prototype.Points <= 0 || prototype.Reputation < 0 || prototype.MinimumCrew < 1 ||
            prototype.MaximumCrew < prototype.MinimumCrew || prototype.MaximumCrew > 24 ||
            prototype.Medics < 1 || prototype.Medics >= prototype.MinimumCrew || !_prototypes.HasIndex(config.Medic) || !_prototypes.HasIndex(config.DocumentGuard) ||
            prototype.Duration <= TimeSpan.Zero || config.Crew.Count == 0 || config.Crew.Count > 32 ||
            config.Cooldown < TimeSpan.Zero ||
            !TryComp(console, out TransformComponent? consoleXform) || consoleXform.GridUid is not { } sourceGrid ||
            consoleXform.MapUid is not { } sourceMap || consoleXform.MapID == MapId.Nullspace)
            return false;
        foreach (var id in config.Crew)
        {
            if (!_prototypes.HasIndex(id))
                return false;
        }

        MapId? temporaryMap = null;
        EntityUid gridUid = EntityUid.Invalid;
        var committed = false;
        var idOperation = Guid.NewGuid();
        var seed = _random.Next();
        var random = new Random(seed);
        try
        {
            var temporary = _maps.CreateMap(out var mapId);
            temporaryMap = mapId;
            _preparationMaps.Add(mapId);
            _maps.SetPaused(temporary, true);
            if (!_loader.TryLoadGrid(mapId, prototype.Map, out var loaded) || loaded is not { } grid)
                return false;
            gridUid = grid.Owner;
            MarkGrid(gridUid, controller.Owner, idOperation);
            RestrictOperationLoot(gridUid, config);
            if (!FindPlacement(sourceGrid, consoleXform.MapID, grid.Comp.LocalAABB, random, out var position, out var rotation))
                return false;

            var free = new List<Vector2i>();
            var interior = FindInteriorTiles(gridUid, grid.Comp);
            var tiles = _maps.GetAllTilesEnumerator(gridUid, grid.Comp);
            while (tiles.MoveNext(out var tileRef))
            {
                if (tileRef is not { } tile)
                    continue;
                if (interior.Contains(tile.GridIndices) && !_turf.IsTileBlocked(tile, CollisionGroup.MobMask) &&
                    HasFloor(gridUid, grid.Comp, tile.GridIndices + new Vector2i(1, 0)) &&
                    HasFloor(gridUid, grid.Comp, tile.GridIndices + new Vector2i(-1, 0)) &&
                    HasFloor(gridUid, grid.Comp, tile.GridIndices + new Vector2i(0, 1)) &&
                    HasFloor(gridUid, grid.Comp, tile.GridIndices + new Vector2i(0, -1)))
                    free.Add(tile.GridIndices);
            }
            if (free.Count < prototype.MinimumCrew + 1)
                return false;
            for (var i = free.Count - 1; i > 0; i--)
            {
                var other = random.Next(i + 1);
                (free[i], free[other]) = (free[other], free[i]);
            }

            var documentPosition = free[^1];
            free.RemoveAt(free.Count - 1);
            var crewCount = Math.Min(random.Next(prototype.MinimumCrew, prototype.MaximumCrew + 1), free.Count);
            var spacing = new int[free.Count];
            Array.Fill(spacing, int.MaxValue);
            for (var i = 1; i < crewCount; i++)
            {
                var best = i;
                for (var j = i; j < free.Count; j++)
                {
                    var delta = free[j] - free[i - 1];
                    spacing[j] = Math.Min(spacing[j], delta.X * delta.X + delta.Y * delta.Y);
                    if (spacing[j] > spacing[best])
                        best = j;
                }
                (free[i], free[best]) = (free[best], free[i]);
                (spacing[i], spacing[best]) = (spacing[best], spacing[i]);
            }
            var documentUid = Spawn(config.Document, TileCoordinates(gridUid, grid.Comp, documentPosition));
            var document = EnsureComp<MilitiaOperationDocumentComponent>(documentUid);
            document.Controller = controller.Owner;
            document.Operation = idOperation;
            var prepared = new MilitiaActiveOperation
            {
                Prototype = prototype.ID, Id = idOperation, Document = documentUid,
                Seed = seed, Deadline = _timing.CurTime + prototype.Duration, Name = prototype.Name,
                Points = prototype.Points, Reputation = prototype.Reputation, Cooldown = config.Cooldown,
            };
            prepared.Grids.Add(gridUid);
            var assigned = new Dictionary<string, int>();
            for (var i = 0; i < crewCount; i++)
            {
                var options = config.Crew.Where(id => assigned.GetValueOrDefault(id.Id) < (crewCount + 1) / 2).ToArray();
                if (options.Length == 0)
                    options = config.Crew.ToArray();
                var role = i == 0 ? config.DocumentGuard : i <= prototype.Medics ? config.Medic : options[random.Next(options.Length)];
                assigned[role.Id] = assigned.GetValueOrDefault(role.Id) + 1;
                var enemy = Spawn(role, TileCoordinates(gridUid, grid.Comp, free[i]));
                var marker = EnsureComp<MilitiaOperationEnemyComponent>(enemy);
                marker.Controller = controller.Owner;
                marker.Operation = idOperation;
                if (!TryComp<HTNComponent>(enemy, out var htn))
                    throw new InvalidOperationException($"Operation enemy {enemy} has no HTN.");
                _htn.SetHTNEnabled((enemy, htn), false);
                if (!Comp<MilitiaOperationActorComponent>(enemy).EquipmentReady)
                    throw new InvalidOperationException($"Militia actor {enemy} was not equipped during initialization.");
                var actor = Comp<MilitiaOperationActorComponent>(enemy);
                actor.DefensivePosition = TileCoordinates(gridUid, grid.Comp, free[i]);
                actor.DefensiveRange = grid.Comp.LocalAABB.Size.Length() + 1f;
                htn.Blackboard.SetValue("VisionRadius", MathF.Min(actor.DefensiveRange, 72f));
                htn.Blackboard.SetValue("AggroVisionRadius", MathF.Min(actor.DefensiveRange, 72f));
                if (i == 0 && (!_storage.Insert(actor.Backpack, documentUid, out _, playSound: false, stackAutomatically: false) ||
                    !OwnsItem(enemy, documentUid)))
                    throw new InvalidOperationException("Cannot secure operation evidence on its custodian.");
                prepared.Crew.Add(enemy);
            }

            var items = AllEntityQuery<ItemComponent, TransformComponent>();
            while (items.MoveNext(out var item, out _, out var itemXform))
            {
                if (item == documentUid)
                    continue;
                foreach (var enemy in prepared.Crew)
                {
                    if (!_transform.ContainsEntity(enemy, (item, itemXform)))
                        continue;
                    var equipment = EnsureComp<MilitiaOperationEquipmentComponent>(item);
                    equipment.Enemy = enemy;
                    equipment.Controller = controller.Owner;
                    equipment.Operation = idOperation;
                    break;
                }
            }

            if (!Exists(documentUid) || Terminating(documentUid) || EntityManager.IsQueuedForDeletion(documentUid))
                return false;
            foreach (var enemy in prepared.Crew)
            {
                if (!Exists(enemy) || Terminating(enemy) || _mobs.IsDead(enemy))
                    return false;
            }

            var xform = Transform(gridUid);
            _transform.SetParent(gridUid, xform, sourceMap);
            _transform.SetWorldPositionRotation(gridUid, position, rotation, xform);
            foreach (var enemy in prepared.Crew)
                _htn.SetHTNEnabled((enemy, Comp<HTNComponent>(enemy)), true);
            var defenses = EntityQueryEnumerator<HTNComponent, GunComponent, TransformComponent>();
            while (defenses.MoveNext(out var defense, out _, out _, out var defenseXform))
            {
                if (defenseXform.GridUid != gridUid || prepared.Crew.Contains(defense))
                    continue;
                var owner = EnsureComp<MilitiaOperationEnemyComponent>(defense);
                owner.Controller = controller.Owner;
                owner.Operation = idOperation;
            }
            _maps.DeleteMap(mapId);
            _preparationMaps.Remove(mapId);
            temporaryMap = null;
            active = prepared;
            committed = true;
            return true;
        }
        catch (Exception exception)
        {
            _log.Error($"Operation preparation failed: Console={console}, Prototype={prototype.ID}, Seed={seed}: {exception}");
            return false;
        }
        finally
        {
            try
            {
                if (!committed && Exists(gridUid))
                {
                    controller.Comp.Cleanup.Add(gridUid);
                    EnsureComp<RepairOrderCleanupComponent>(gridUid).Station = controller.Owner;
                    if (!_repair.TryFindPlayerOnRepairGrid(gridUid, out _))
                        Del(gridUid);
                }
            }
            catch (Exception exception) { _log.Error($"Operation rollback failed for {gridUid}: {exception}"); }
            finally
            {
                if (temporaryMap is { } cleanupMap)
                    CleanupPreparationMap(cleanupMap);
            }
        }
    }

    private void CleanupPreparationMap(MapId map)
    {
        try
        {
            if (_mapManager.MapExists(map))
            {
                if (_repair.TryFindPlayerInRepairEntity(_mapManager.GetMapEntityId(map), out _))
                    return;
                _maps.DeleteMap(map);
            }
            _preparationMaps.Remove(map);
        }
        catch (Exception exception) { _log.Error($"Operation preparation map cleanup failed for {map}: {exception}"); }
    }

    private bool HasFloor(EntityUid uid, MapGridComponent grid, Vector2i indices)
        => _maps.TryGetTileRef(uid, grid, indices, out var tile) && !tile.Tile.IsEmpty && !_turf.IsSpace(tile.Tile);

    private HashSet<Vector2i> FindInteriorTiles(EntityUid gridUid, MapGridComponent grid)
    {
        var floors = new HashSet<Vector2i>();
        var barriers = new Dictionary<Vector2i, AtmosDirection>();
        var tiles = _maps.GetAllTilesEnumerator(gridUid, grid);
        while (tiles.MoveNext(out var tileRef))
        {
            if (tileRef is not { } tile)
                continue;
            if (!tile.Tile.IsEmpty && !_turf.IsSpace(tile.Tile))
                floors.Add(tile.GridIndices);
            var entities = _maps.GetAnchoredEntitiesEnumerator(gridUid, grid, tile.GridIndices);
            var blocked = AtmosDirection.Invalid;
            while (entities.MoveNext(out var entity))
            {
                if (HasComp<DoorComponent>(entity))
                    blocked |= AtmosDirection.All;
                else if (TryComp<AirtightComponent>(entity, out var airtight) && airtight.AirBlocked)
                    blocked |= airtight.AirBlockedDirection;
            }
            barriers[tile.GridIndices] = blocked;
        }

        var remaining = new HashSet<Vector2i>(floors.Where(tile => barriers.GetValueOrDefault(tile) != AtmosDirection.All));
        var interior = new HashSet<Vector2i>();
        var directions = new (Vector2i Offset, AtmosDirection Direction)[]
        {
            (new(1, 0), AtmosDirection.East), (new(-1, 0), AtmosDirection.West),
            (new(0, 1), AtmosDirection.North), (new(0, -1), AtmosDirection.South),
        };
        var pending = new Queue<Vector2i>();
        while (remaining.Count > 0)
        {
            var start = remaining.First();
            remaining.Remove(start);
            pending.Enqueue(start);
            var room = new List<Vector2i>();
            var enclosed = true;
            while (pending.TryDequeue(out var tile))
            {
                room.Add(tile);
                foreach (var (offset, direction) in directions)
                {
                    var adjacent = tile + offset;
                    if ((barriers.GetValueOrDefault(tile) & direction) != 0 ||
                        (barriers.GetValueOrDefault(adjacent) & direction.GetOpposite()) != 0)
                        continue;
                    if (!floors.Contains(adjacent))
                        enclosed = false;
                    else if (remaining.Remove(adjacent))
                        pending.Enqueue(adjacent);
                }
            }
            if (enclosed)
                interior.UnionWith(room);
        }
        return interior;
    }

    private static EntityCoordinates TileCoordinates(EntityUid uid, MapGridComponent grid, Vector2i indices)
        => new(uid, new Vector2((indices.X + 0.5f) * grid.TileSize, (indices.Y + 0.5f) * grid.TileSize));

    private bool FindPlacement(EntityUid anchor, MapId map, Box2 localBounds, Random random,
        out Vector2 position, out Angle rotation)
    {
        position = default;
        rotation = Angle.Zero;
        if (!TryComp<MapGridComponent>(anchor, out var anchorGrid) || Transform(anchor).MapID != map)
            return false;
        var station = _stations.GetOwningStation(anchor);
        var center = _transform.GetWorldPosition(anchor);
        var bounds = new List<Box2>();
        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var grid, out var xform))
        {
            if (xform.MapID != map || uid != anchor && (station == null || _stations.GetOwningStation(uid) != station))
                continue;
            bounds.Add(new Box2Rotated(grid.LocalAABB.Translated(_transform.GetWorldPosition(xform)),
                _transform.GetWorldRotation(xform), _transform.GetWorldPosition(xform)).CalcBoundingBox());
        }
        if (bounds.Count == 0)
            return false;
        var anchorRadius = anchorGrid.LocalAABB.MaxDimension;
        var targetRadius = localBounds.Size.Length() / 2f;
        for (var attempt = 0; attempt < 96; attempt++)
        {
            var angle = random.NextDouble() * Math.Tau;
            var direction = new Vector2((float) Math.Cos(angle), (float) Math.Sin(angle));
            var clearance = MinimumSpawnDistance + (float) random.NextDouble() * (MaximumSpawnDistance - MinimumSpawnDistance);
            var candidate = center + direction * (anchorRadius + targetRadius + clearance);
            var candidateRotation = new Angle(random.NextDouble() * Math.Tau);
            for (var step = 0; step < 8; step++)
            {
                var targetBounds = new Box2Rotated(localBounds.Translated(candidate), candidateRotation, candidate).CalcBoundingBox();
                var distance = bounds.Min(b => DistanceBetweenBounds(targetBounds, b));
                if (distance is >= MinimumSpawnDistance and <= MaximumSpawnDistance)
                    break;
                candidate += direction * (clearance - distance);
            }
            var finalBounds = new Box2Rotated(localBounds.Translated(candidate), candidateRotation, candidate).CalcBoundingBox();
            var nearest = bounds.Min(b => DistanceBetweenBounds(finalBounds, b));
            if (nearest is < MinimumSpawnDistance or > MaximumSpawnDistance)
                continue;
            var footprint = new Box2Rotated(localBounds.Enlarged(8f).Translated(candidate), candidateRotation, candidate);
            if (_mapManager.FindGridsIntersecting(map, footprint).Any())
                continue;
            position = candidate;
            rotation = candidateRotation;
            return true;
        }
        return false;
    }

    private static float DistanceBetweenBounds(Box2 a, Box2 b)
    {
        var x = MathF.Max(MathF.Max(a.Left - b.Right, 0), b.Left - a.Right);
        var y = MathF.Max(MathF.Max(a.Bottom - b.Top, 0), b.Bottom - a.Top);
        return MathF.Sqrt(x * x + y * y);
    }
}
