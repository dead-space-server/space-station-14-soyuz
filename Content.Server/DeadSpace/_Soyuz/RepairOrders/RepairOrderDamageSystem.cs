// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>Accept-time damage only. Pricing and repair tasks remain owned by the existing repair systems.</summary>
public sealed class RepairOrderDamageSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly ITileDefinitionManager _tiles = default!;
    [Dependency] private readonly RepairDamageProtectionSystem _protection = default!;
    private static readonly Vector2i[] Neighbors = { new(1, 0), new(0, 1), new(-1, 0), new(0, -1) };

    public RepairDamageSnapshot Snapshot(Entity<MapGridComponent> grid, RepairOrderPrototype order)
    {
        _protection.ValidateConfiguration();
        var values = RepairValueCatalog.Build(_prototypes);
        if (values.Errors.Count != 0)
            throw new InvalidOperationException(string.Join("\n", values.Errors));
        var floorValue = _prototypes.Index(order.ScoreProfile).FloorTilePoints;
        if (floorValue <= 0) throw new InvalidOperationException($"{order.ID}: invalid floor value.");
        var floors = SortCells(_map.GetAllTiles(grid.Owner, grid.Comp).Where(t => !t.Tile.IsEmpty).Select(t => t.GridIndices));
        var entities = new List<RepairDamageEntity>();
        var protectedFloors = new HashSet<Vector2i>();
        var children = Transform(grid.Owner).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            var xform = Transform(child);
            if (!xform.Anchored || xform.ParentUid != grid.Owner) continue;
            var cell = new Vector2i((int) Math.Floor(xform.LocalPosition.X / grid.Comp.TileSize),
                (int) Math.Floor(xform.LocalPosition.Y / grid.Comp.TileSize));
            var id = MetaData(child).EntityPrototype?.ID;
            if (id == null || values.IsExcluded(id))
            {
                // A removed support tile must not strand an excluded helper or an untracked entity.
                protectedFloors.Add(cell);
                continue;
            }
            if (!values.TryResolve(id, out var value))
                throw new InvalidOperationException($"Repair value configuration error: order {order.ID}, grid {grid.Owner}, entity {id}, position {xform.LocalPosition}.");
            values.TryGetCategory(id, out var category);
            var protectedEntity = !_protection.CanProcedurallyDamage(child);
            if (protectedEntity) protectedFloors.Add(cell);
            entities.Add(new RepairDamageEntity(0, child, id, xform.LocalPosition, xform.LocalRotation, cell, category, value, protectedEntity));
        }
        var sorted = entities.OrderBy(e => e.Position.X).ThenBy(e => e.Position.Y)
            .ThenBy(e => e.Prototype, StringComparer.Ordinal).ThenBy(e => e.Rotation.Theta).ThenBy(e => e.Uid)
            .Select((entity, index) => entity with { Index = index }).ToImmutableArray();
        return new RepairDamageSnapshot(grid.Owner, floors, sorted, SortCells(protectedFloors), floorValue)
        {
            FloorLayerCounts = floors.ToImmutableDictionary(cell => cell,
                cell => RepairValueCatalog.GetTileLayers(_map.GetTileRef(grid.Owner, grid.Comp, cell).Tile.TypeId, _tiles).Count),
        };
    }

    public bool TryGeneratePlan(RepairDamageSnapshot snapshot, RepairDamageProfilePrototype profile, int seed,
        out RepairOrderDamagePlan plan, out string rejection)
    {
        plan = default!;
        var errors = RepairDamageConfiguration.Validate(_prototypes);
        if (errors.Count > 0) { rejection = string.Join("; ", errors); return false; }
        var events = profile.Events.Select(id => _prototypes.Index(id))
            .Where(ev => ev.Severity <= profile.MaxSeverity && CanApply(snapshot, ev))
            .OrderBy(ev => ev.ID, StringComparer.Ordinal).ToArray();
        rejection = "No applicable event combination meets the profile limits.";
        var floors = snapshot.Floors.ToHashSet();
        for (var attempt = 0; attempt < profile.MaxGenerationAttempts; attempt++)
        {
            var random = new RobustRandom();
            random.SetSeed(unchecked(seed + attempt * 0x1f123bb5));
            var tiles = new HashSet<Vector2i>();
            var entities = new HashSet<int>();
            var selected = new List<RepairDamageEventResult>();
            var desired = random.Next(profile.MinEvents, profile.MaxEvents + 1);
            for (var roll = 0; roll < profile.MaxEventRolls && selected.Count < profile.MaxEvents; roll++)
            {
                var pool = events.Where(ev => selected.Count(e => e.Event.Id == ev.ID) < (ev.MaxOccurrences ?? profile.MaxOccurrencesPerEvent) &&
                    (ev.Targets.Count == 0 || snapshot.Entities.Any(e => !e.Protected && !entities.Contains(e.Index) && Matches(ev.Targets, e.Category)))).ToList();
                if (pool.Count == 0) break;
                var pick = random.NextDouble() * pool.Sum(ev => (double) ev.Weight);
                var chosen = pool[^1];
                foreach (var ev in pool)
                {
                    pick -= ev.Weight;
                    if (pick > 0) continue;
                    chosen = ev;
                    break;
                }
                var nextTiles = new HashSet<Vector2i>(tiles);
                var nextEntities = new HashSet<int>(entities);
                if (!TryEvent(snapshot, floors, profile, chosen, selected, random, nextTiles, nextEntities, out var result))
                    continue;
                var candidate = MakePlan(snapshot, seed, attempt, nextTiles, nextEntities, selected.Append(result));
                // Reject excess and topology damage before accepting even a staged event. Never trim a heavy
                // event into a different semantic event merely to meet the remaining budget.
                if (!ValidatePlan(snapshot, profile, candidate, out rejection, requireMinimum: false)) continue;
                tiles = nextTiles;
                entities = nextEntities;
                selected.Add(result);
                if (selected.Count >= desired && ValidatePlan(snapshot, profile, candidate, out rejection))
                {
                    plan = candidate;
                    return true;
                }
            }
            var final = MakePlan(snapshot, seed, attempt, tiles, entities, selected);
            if (!ValidatePlan(snapshot, profile, final, out rejection)) continue;
            plan = final;
            return true;
        }
        return false;
    }

    public bool CanApply(RepairDamageSnapshot snapshot, RepairDamageEventPrototype ev)
        => CanApply(snapshot, ev, new HashSet<int>(), new HashSet<Vector2i>());

    private static bool CanApply(RepairDamageSnapshot snapshot, RepairDamageEventPrototype ev,
        HashSet<int> removedEntities, HashSet<Vector2i> removedTiles)
    {
        if (Centers(snapshot, ev, removedEntities, removedTiles).Count == 0) return false;
        return ev.Stages.SelectMany(s => s.Operations).Any(op => op.Type == RepairDamageOperation.RemoveTile
            ? snapshot.Floors.Any(c => !removedTiles.Contains(c) && !snapshot.ProtectedFloors.Contains(c))
            : snapshot.Entities.Any(e => !e.Protected && !removedEntities.Contains(e.Index) && Matches(op.Categories, e.Category)));
    }

    private static bool Matches(List<ProtoId<RepairValueTagPrototype>> categories, string category)
        => categories.Count == 0 || categories.Any(id => id.Id == category);

    private static List<Vector2i> Centers(RepairDamageSnapshot snapshot, RepairDamageEventPrototype ev,
        HashSet<int> removedEntities, HashSet<Vector2i> removedTiles)
    {
        var floors = snapshot.Floors.ToHashSet();
        var equipment = snapshot.Entities.Where(e => !e.Protected && !removedEntities.Contains(e.Index) && Matches(ev.Targets, e.Category)).ToArray();
        IEnumerable<Vector2i> candidates = ev.Targets.Count > 0 || ev.Placement == RepairDamagePlacement.RepairValueTarget
            ? equipment.Select(e => e.Cell)
            : snapshot.Floors.Where(c => !removedTiles.Contains(c));
        bool Boundary(Vector2i c) => Neighbors.Any(n => !floors.Contains(c + n));
        if (ev.BoundaryTargets) candidates = candidates.Where(Boundary);
        candidates = ev.Placement switch
        {
            RepairDamagePlacement.Interior => candidates.Where(c => !Boundary(c)),
            RepairDamagePlacement.Boundary or RepairDamagePlacement.BoundaryToInterior => candidates.Where(Boundary),
            RepairDamagePlacement.BoundaryCorner => candidates.Where(c => Neighbors.Count(n => !floors.Contains(c + n)) >= 2),
            _ => candidates,
        };
        var sorted = SortCells(candidates).ToList();
        if (ev.Placement == RepairDamagePlacement.DenseRepairableArea && sorted.Count > 0)
        {
            var densities = sorted.ToDictionary(c => c, c => equipment.Count(e => DistanceSquared(e.Cell, c) <= 4));
            var max = densities.Values.Max();
            sorted.RemoveAll(c => densities[c] < max || densities[c] == 0);
        }
        return sorted;
    }

    private static bool TryEvent(RepairDamageSnapshot snapshot, HashSet<Vector2i> floors,
        RepairDamageProfilePrototype profile, RepairDamageEventPrototype ev, List<RepairDamageEventResult> previous,
        IRobustRandom random, HashSet<Vector2i> removedTiles, HashSet<int> removedEntities, out RepairDamageEventResult result)
    {
        result = default!;
        var oldTiles = removedTiles.ToHashSet();
        var oldEntities = removedEntities.ToHashSet();
        var centers = new List<Vector2i>();
        var affected = new HashSet<Vector2i>();
        var count = random.Next(ev.MinCenters, ev.MaxCenters + 1);
        for (var point = 0; point < count; point++)
        {
            var candidates = Centers(snapshot, ev, removedEntities, removedTiles)
                .Where(c => previous.SelectMany(e => e.Centers).All(p => DistanceSquared(c, p) >= profile.MinimumEventSeparation * profile.MinimumEventSeparation) &&
                    centers.All(p => DistanceSquared(c, p) >= ev.MinimumCenterSeparation * ev.MinimumCenterSeparation)).ToList();
            if (candidates.Count == 0) return false;
            var center = candidates[random.Next(candidates.Count)];
            centers.Add(center);
            var direction = Neighbors[random.Next(Neighbors.Length)];
            if (ev.Placement is RepairDamagePlacement.Boundary or RepairDamagePlacement.BoundaryToInterior or RepairDamagePlacement.BoundaryCorner || ev.BoundaryTargets)
            {
                // Longest inward ray chooses a stable inward orientation; edge shapes use its tangent.
                direction = Neighbors.OrderByDescending(n => Enumerable.Range(1, floors.Count)
                    .TakeWhile(step => floors.Contains(center + n * step)).Count()).First();
            }
            var before = removedTiles.Count + removedEntities.Count;
            foreach (var stage in ev.Stages)
            {
                var area = Shape(stage, center, direction, floors, random).ToHashSet();
                foreach (var op in stage.Operations)
                {
                    var applied = 0;
                    if (op.Type == RepairDamageOperation.RemoveTile)
                    {
                        foreach (var cell in SortCells(area))
                        {
                            if (applied >= op.MaxTargets) break;
                            if (!floors.Contains(cell) || removedTiles.Contains(cell) || snapshot.ProtectedFloors.Contains(cell) || random.NextDouble() >= op.Chance) continue;
                            removedTiles.Add(cell);
                            affected.Add(cell);
                            applied++;
                            // Remove every tracked anchored child before removing its support, regardless of
                            // which optional equipment operation happened to roll successfully.
                            foreach (var entity in snapshot.Entities.Where(e => e.Cell == cell && !e.Protected)) removedEntities.Add(entity.Index);
                        }
                    }
                    else
                    {
                        var targets = area;
                        if (op.ConnectedTargets)
                        {
                            var eligible = snapshot.Entities.Where(e => !e.Protected && !removedEntities.Contains(e.Index) &&
                                area.Contains(e.Cell) && Matches(op.Categories, e.Category)).Select(e => e.Cell).ToHashSet();
                            targets = new HashSet<Vector2i>();
                            var pending = new Queue<Vector2i>();
                            if (eligible.Contains(center)) pending.Enqueue(center);
                            while (pending.TryDequeue(out var cell))
                            {
                                if (!targets.Add(cell)) continue;
                                foreach (var neighbor in Neighbors)
                                    if (eligible.Contains(cell + neighbor) && !targets.Contains(cell + neighbor))
                                        pending.Enqueue(cell + neighbor);
                            }
                        }
                        foreach (var entity in snapshot.Entities)
                        {
                            if (applied >= op.MaxTargets) break;
                            if (entity.Protected || removedEntities.Contains(entity.Index) || !targets.Contains(entity.Cell) || !Matches(op.Categories, entity.Category) || random.NextDouble() >= op.Chance) continue;
                            removedEntities.Add(entity.Index);
                            affected.Add(entity.Cell);
                            applied++;
                        }
                    }
                }
            }
            // Each composite impact must contribute real damage, not just a decorative center in a report.
            if (before == removedTiles.Count + removedEntities.Count) return false;
        }
        if (ev.Targets.Count > 0 && !snapshot.Entities.Any(e => removedEntities.Contains(e.Index) && !oldEntities.Contains(e.Index) && Matches(ev.Targets, e.Category))) return false;
        if (ev.RequiresFloorRemoval && removedTiles.Count == oldTiles.Count) return false;
        var changed = snapshot.CountTileRequirements(removedTiles.Except(oldTiles)) + removedEntities.Count - oldEntities.Count;
        if (changed == 0) return false;
        result = new RepairDamageEventResult(ev.ID, centers.ToImmutableArray(), SortCells(affected), changed);
        return true;
    }

    private static IEnumerable<Vector2i> Shape(RepairDamageStage stage, Vector2i center, Vector2i forward,
        HashSet<Vector2i> floors, IRobustRandom random)
    {
        var shape = stage.Shape == RepairDamageShape.MultiPoint ? stage.PointShape : stage.Shape;
        var radius = random.Next(stage.MinRadius, stage.MaxRadius + 1);
        var length = random.Next(stage.MinLength, stage.MaxLength + 1);
        var side = new Vector2i(-forward.Y, forward.X);
        if (shape == RepairDamageShape.IrregularBlob)
        {
            var blob = new HashSet<Vector2i> { center };
            var frontier = new List<Vector2i> { center };
            var limit = (radius * 2 + 1) * (radius + 1);
            while (blob.Count < limit && frontier.Count > 0)
            {
                var next = frontier[random.Next(frontier.Count)];
                var neighbors = Neighbors.Select(n => next + n).Where(c => floors.Contains(c) && !blob.Contains(c) && DistanceSquared(c, center) <= radius * radius).ToArray();
                if (neighbors.Length == 0) { frontier.Remove(next); continue; }
                var chosen = neighbors[random.Next(neighbors.Length)];
                blob.Add(chosen);
                frontier.Add(chosen);
            }
            return SortCells(blob);
        }
        var result = new List<Vector2i>();
        var extent = Math.Max(length, radius);
        for (var x = -extent; x <= extent; x++)
        for (var y = -extent; y <= extent; y++)
        {
            var inside = shape switch
            {
                RepairDamageShape.Disc => x * x + y * y <= radius * radius,
                RepairDamageShape.Ellipse => (double) (x * x) / (length * length) + (double) (y * y) / Math.Max(1, radius * radius) <= 1,
                RepairDamageShape.Line => x >= 0 && x < length && y == 0,
                RepairDamageShape.Strip => x >= 0 && x < length && Math.Abs(y) <= radius,
                RepairDamageShape.Rectangle => Math.Abs(x) <= length / 2 && Math.Abs(y) <= radius,
                RepairDamageShape.EdgeSegment => x >= 0 && x <= radius && Math.Abs(y) <= length / 2,
                _ => false,
            };
            if (inside) result.Add(center + forward * x + side * y);
        }
        return result;
    }

    public static RepairOrderDamagePlan MakePlan(RepairDamageSnapshot snapshot, int seed, int attempt,
        IEnumerable<Vector2i> tiles, IEnumerable<int> entities, IEnumerable<RepairDamageEventResult> events)
    {
        var removedTiles = SortCells(tiles);
        var removedEntities = entities.Distinct().OrderBy(i => i).ToImmutableArray();
        var value = snapshot.CountTileRequirements(removedTiles) * snapshot.FloorValue + removedEntities.Sum(i => snapshot.Entities[i].Value);
        return new RepairOrderDamagePlan(seed, attempt, removedTiles, removedEntities, events.ToImmutableArray(), value, snapshot.TotalValue);
    }

    public static bool ValidatePlan(RepairDamageSnapshot snapshot, RepairDamageProfilePrototype profile,
        RepairOrderDamagePlan plan, out string reason, bool requireMinimum = true)
    {
        reason = string.Empty;
        var removedTiles = plan.RemovedTiles.ToHashSet();
        var removedEntities = plan.RemovedEntities.ToHashSet();
        var floors = snapshot.Floors.ToHashSet();
        if (!removedTiles.IsSubsetOf(floors) || removedTiles.Overlaps(snapshot.ProtectedFloors) ||
            removedEntities.Any(i => i < 0 || i >= snapshot.Entities.Length || snapshot.Entities[i].Protected) ||
            removedTiles.Count != plan.RemovedTiles.Length || removedEntities.Count != plan.RemovedEntities.Length)
            reason = "Plan contains invalid, protected or duplicate targets.";
        else if (snapshot.Entities.Any(e => removedTiles.Contains(e.Cell) && !removedEntities.Contains(e.Index)))
            reason = "Plan strands anchored entities without supporting floor.";
        else if (plan.TotalValue != snapshot.TotalValue || plan.DamageValue != snapshot.CountTileRequirements(removedTiles) * snapshot.FloorValue + removedEntities.Sum(i => snapshot.Entities[i].Value))
            reason = "Plan damage metrics do not match the snapshot.";
        else if (removedTiles.Count > floors.Count * profile.MaxRemovedFloorFraction)
            reason = "Floor removal cap exceeded.";
        else if (removedEntities.Count > snapshot.Entities.Length * profile.MaxRemovedAnchoredEntityFraction)
            reason = "Anchored entity removal cap exceeded.";
        else if (plan.DamageFraction > profile.MaxDamageFraction)
            reason = "Maximum damage value exceeded.";
        else if (requireMinimum && (plan.DamageFraction < profile.MinDamageFraction || snapshot.CountTileRequirements(removedTiles) + removedEntities.Count < profile.MinChangedRequirements || plan.Events.Length < profile.MinEvents))
            reason = "Insufficient damage or events.";
        else if (plan.Events.Length > profile.MaxEvents || plan.Events.Any(e => e.ChangedRequirements <= 0 || e.Centers.IsEmpty || e.AffectedCells.IsEmpty || !profile.Events.Contains(e.Event)) ||
                 plan.Events.Sum(e => e.ChangedRequirements) != snapshot.CountTileRequirements(removedTiles) + removedEntities.Count)
            reason = "Invalid event count or no-op event.";
        else
        {
            floors.ExceptWith(removedTiles);
            if (floors.Count < profile.MinRemainingFloor) reason = "Too little floor remains.";
            else if (!IsConnected(floors)) reason = "Remaining floor would split the grid.";
        }
        return reason.Length == 0;
    }

    public static bool IsConnected(HashSet<Vector2i> floors)
    {
        if (floors.Count == 0) return false;
        var pending = new Queue<Vector2i>();
        var visited = new HashSet<Vector2i>();
        pending.Enqueue(floors.First());
        while (pending.TryDequeue(out var cell))
        {
            if (!visited.Add(cell)) continue;
            foreach (var direction in Neighbors)
                if (floors.Contains(cell + direction) && !visited.Contains(cell + direction)) pending.Enqueue(cell + direction);
        }
        return visited.Count == floors.Count;
    }

    public void ApplyPlan(Entity<MapGridComponent> grid, RepairDamageSnapshot snapshot,
        RepairDamageProfilePrototype profile, RepairOrderDamagePlan plan)
    {
        if (grid.Owner != snapshot.Grid)
            throw new InvalidOperationException("Repair damage snapshot belongs to another grid.");
        if (!ValidatePlan(snapshot, profile, plan, out var reason))
            throw new InvalidOperationException($"Cannot apply repair damage: {reason}");
        // Inspect every target before the first mutation. Snapshot and Apply run synchronously on a paused map.
        foreach (var index in plan.RemovedEntities)
        {
            var entity = snapshot.Entities[index];
            if (!Exists(entity.Uid) || !_protection.CanProcedurallyDamage(entity.Uid) || Transform(entity.Uid).ParentUid != grid.Owner || !Transform(entity.Uid).Anchored ||
                Transform(entity.Uid).LocalPosition != entity.Position || MetaData(entity.Uid).EntityPrototype?.ID != entity.Prototype)
                throw new InvalidOperationException("Repair damage snapshot became stale.");
        }
        foreach (var cell in plan.RemovedTiles)
            if (_map.GetTileRef(grid.Owner, grid.Comp, cell).Tile.IsEmpty)
                throw new InvalidOperationException("Repair damage floor snapshot became stale.");
        foreach (var index in plan.RemovedEntities) Del(snapshot.Entities[index].Uid);
        // One batch prevents transient splits from a different intermediate removal ordering.
        _map.SetTiles(grid.Owner, grid.Comp, plan.RemovedTiles.Select(c => (c, Tile.Empty)).ToList());
    }

    private static ImmutableArray<Vector2i> SortCells(IEnumerable<Vector2i> cells)
        => cells.Distinct().OrderBy(c => c.X).ThenBy(c => c.Y).ToImmutableArray();
    private static float DistanceSquared(Vector2i a, Vector2i b)
        => (float) (a.X - b.X) * (a.X - b.X) + (float) (a.Y - b.Y) * (a.Y - b.Y);
}
