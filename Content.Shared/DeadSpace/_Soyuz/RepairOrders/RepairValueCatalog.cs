// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

// A placed upper tile includes its supporting layers; removing it exposes the next layer.
public enum RepairTileLayer : byte { None, Lattice, Plating, Floor }

public sealed class RepairValueCatalog
{
    public static readonly ProtoId<ContentTileDefinition> StandardFloor = "FloorSteel";
    public static readonly ProtoId<ContentTileDefinition> StandardPlating = "Plating";
    public static readonly ProtoId<ContentTileDefinition> StandardLattice = "Lattice";
    private readonly Dictionary<string, int> _values = new();
    private readonly Dictionary<string, string> _categories = new();
    private readonly HashSet<string> _excluded = new();
    private readonly bool _allowPartial;
    public readonly List<string> Errors = new();

    private RepairValueCatalog(bool allowPartial) => _allowPartial = allowPartial;

    public bool TryResolve(string entity, out int value)
    {
        value = 0;
        return (_allowPartial || Errors.Count == 0) && _values.TryGetValue(entity, out value);
    }

    public bool IsExcluded(string entity) => (_allowPartial || Errors.Count == 0) && _excluded.Contains(entity);

    public bool TryGetCategory(string entity, out string category)
    {
        category = string.Empty;
        return (_allowPartial || Errors.Count == 0) && _categories.TryGetValue(entity, out category!);
    }

    public static RepairTileLayer GetTileLayer(ContentTileDefinition tile)
        => tile.TileId == Tile.Empty.TypeId ? RepairTileLayer.None
            : !tile.IsSubFloor ? RepairTileLayer.Floor
            : tile.MapAtmosphere ? RepairTileLayer.Lattice : RepairTileLayer.Plating;

    public static int CanonicalizeTile(int tileId, ITileDefinitionManager tiles)
        => GetTileLayer((ContentTileDefinition) tiles[tileId]) switch
        {
            RepairTileLayer.Floor => tiles[StandardFloor.Id].TileId,
            RepairTileLayer.Plating => tiles[StandardPlating.Id].TileId,
            RepairTileLayer.Lattice => tiles[StandardLattice.Id].TileId,
            _ => Tile.Empty.TypeId,
        };

    /// <summary>Independent requirements represented by a grid tile, preserving original layer visuals.</summary>
    public static Dictionary<RepairTileLayer, ContentTileDefinition> GetTileLayers(int tileId, ITileDefinitionManager tiles)
    {
        var result = new Dictionary<RepairTileLayer, ContentTileDefinition>();
        var tile = (ContentTileDefinition) tiles[tileId];
        var top = GetTileLayer(tile);
        if (top == RepairTileLayer.None)
            return result;

        result[RepairTileLayer.Lattice] = (ContentTileDefinition) tiles[StandardLattice.Id];
        if (top >= RepairTileLayer.Plating)
            result[RepairTileLayer.Plating] = (ContentTileDefinition) tiles[StandardPlating.Id];
        result[top] = tile;

        // Prefer the target's actual underlying material when its base turf defines one.
        var visited = new HashSet<string> { tile.ID };
        while (tile.BaseTurf is { } baseId && visited.Add(baseId.Id) &&
               tiles.TryGetDefinition(baseId.Id, out var definition) && definition is ContentTileDefinition next)
        {
            tile = next;
            var layer = GetTileLayer(tile);
            if (layer != RepairTileLayer.None && layer < top)
            {
                result[layer] = tile;
                top = layer;
            }
        }
        return result;
    }

    public static RepairValueCatalog Build(IPrototypeManager prototypes, bool allowPartial = false)
        => Build(prototypes,
            prototypes.EnumeratePrototypes<RepairValueTagPrototype>(),
            prototypes.EnumeratePrototypes<RepairValueGroupPrototype>(),
            prototypes.EnumeratePrototypes<RepairValueOverridePrototype>(),
            prototypes.EnumeratePrototypes<RepairValueExclusionPrototype>(), allowPartial);

    public static RepairValueCatalog Build(
        IPrototypeManager prototypes,
        IEnumerable<RepairValueTagPrototype> tags,
        IEnumerable<RepairValueGroupPrototype> groups,
        IEnumerable<RepairValueOverridePrototype> overrides,
        IEnumerable<RepairValueExclusionPrototype> exclusions,
        bool allowPartial = false)
    {
        var result = new RepairValueCatalog(allowPartial);
        var tagValues = new Dictionary<string, int>();
        var invalidTags = new HashSet<string>();
        var invalidEntities = new HashSet<string>();
        foreach (var tag in tags)
        {
            if (tag.Value <= 0)
            {
                result.Errors.Add($"RepairValueTag {tag.ID}: value must be positive.");
                invalidTags.Add(tag.ID);
            }
            if (!tagValues.TryAdd(tag.ID, tag.Value))
            {
                result.Errors.Add($"Duplicate RepairValueTag {tag.ID}.");
                invalidTags.Add(tag.ID);
            }
        }
        foreach (var tag in invalidTags)
            tagValues.Remove(tag);

        bool CheckEntity(EntProtoId entity, string source)
        {
            if (prototypes.HasIndex(entity))
                return true;
            result.Errors.Add($"{source}: missing EntityPrototype {entity}.");
            return false;
        }

        foreach (var group in groups)
        {
            if (group.Entities.Count == 0)
                result.Errors.Add($"RepairValueGroup {group.ID}: empty group.");
            var validTag = tagValues.TryGetValue(group.Tag.Id, out var value);
            if (!validTag)
                result.Errors.Add($"RepairValueGroup {group.ID}: missing RepairValueTag {group.Tag}.");
            foreach (var entity in group.Entities)
            {
                if (!CheckEntity(entity, group.ID))
                    continue;
                if (!validTag)
                {
                    invalidEntities.Add(entity.Id);
                    continue;
                }
                if (!result._values.TryAdd(entity.Id, value))
                {
                    result.Errors.Add($"RepairValueGroup {group.ID}: duplicate classification for {entity}.");
                    invalidEntities.Add(entity.Id);
                }
                result._categories.TryAdd(entity.Id, group.Tag.Id);
            }
        }

        var overridden = new HashSet<string>();
        foreach (var entry in overrides)
        {
            var exists = CheckEntity(entry.Entity, entry.ID);
            if (entry.Value <= 0)
                result.Errors.Add($"RepairValueOverride {entry.ID}: value must be positive.");
            var unique = overridden.Add(entry.Entity.Id);
            if (!unique)
                result.Errors.Add($"RepairValueOverride {entry.ID}: duplicate override for {entry.Entity}.");
            if (!exists)
                continue;
            if (entry.Value <= 0 || !unique)
            {
                invalidEntities.Add(entry.Entity.Id);
                continue;
            }
            result._values[entry.Entity.Id] = entry.Value;
        }

        var excludedTags = new HashSet<ProtoId<TagPrototype>>();
        foreach (var exclusion in exclusions)
        {
            if (exclusion.Entities.Count == 0 && exclusion.Tags.Count == 0)
                result.Errors.Add($"RepairValueExclusion {exclusion.ID}: entities or tags are required.");
            foreach (var tag in exclusion.Tags)
            {
                if (!prototypes.HasIndex(tag))
                    result.Errors.Add($"RepairValueExclusion {exclusion.ID}: missing TagPrototype {tag}.");
                else
                    excludedTags.Add(tag);
            }
            foreach (var entity in exclusion.Entities)
            {
                if (!CheckEntity(entity, exclusion.ID))
                    continue;
                if (!result._excluded.Add(entity.Id))
                    result.Errors.Add($"RepairValueExclusion {exclusion.ID}: duplicate exclusion for {entity}.");
            }
        }
        if (excludedTags.Count > 0)
        {
            var tagSystem = IoCManager.Resolve<IEntityManager>().System<TagSystem>();
            foreach (var entity in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (entity.Components.TryGetComponent("Tag", out var component) && component is TagComponent tagsComponent &&
                    tagSystem.HasAnyTag(tagsComponent, excludedTags))
                    result._excluded.Add(entity.ID);
            }
        }
        foreach (var entity in result._excluded)
        {
            if (result._values.ContainsKey(entity))
                result.Errors.Add($"RepairValueExclusion: {entity} is classified AND excluded.");
        }
        result._excluded.UnionWith(invalidEntities);
        foreach (var entity in result._excluded)
        {
            result._values.Remove(entity);
            result._categories.Remove(entity);
        }
        result.Errors.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>
    /// Fully inherited anchoring capability, anchored transform, or static physical construction.
    /// Deliberately broad: technical false positives require explicit YAML exclusions.
    /// Actual anchored map children are checked separately, including map transform overrides.
    /// </summary>
    public static bool IsCandidate(EntityPrototype entity)
    {
        if (entity.Abstract)
            return false;
        return entity.Components.ContainsKey("Anchorable") ||
               entity.Components.TryGetComponent("Transform", out var transform) &&
               transform is TransformComponent { Anchored: true } ||
               entity.Components.TryGetComponent("Physics", out var physics) &&
               physics is PhysicsComponent { BodyType: BodyType.Static };
    }

    public RepairValueCoverage Scan(IEnumerable<EntityPrototype> prototypes)
    {
        var all = prototypes.ToArray();
        var candidates = all.Where(IsCandidate).OrderBy(p => p.ID, StringComparer.Ordinal).ToArray();
        return new RepairValueCoverage(all.Length, candidates.Length,
            candidates.Count(p => _values.ContainsKey(p.ID)),
            candidates.Count(p => _excluded.Contains(p.ID)),
            candidates.Where(p => !_values.ContainsKey(p.ID) && !_excluded.Contains(p.ID))
                .Select(p => p.ID).ToArray());
    }
}

public sealed record RepairValueCoverage(int Total, int Candidates, int Classified, int Excluded, string[] Unclassified)
{
    public override string ToString()
        => $"Repair Value Coverage\nTotal prototypes: {Total}\nTotal candidate prototypes: {Candidates}\n" +
           $"Classified: {Classified}\nExcluded: {Excluded}\nUnclassified: {Unclassified.Length}\n" +
           string.Join("\n", Unclassified.Select(id => $"- {id}"));
}
