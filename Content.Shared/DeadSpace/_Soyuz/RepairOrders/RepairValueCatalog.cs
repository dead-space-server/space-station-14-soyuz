// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Validated exact-ID lookup. Candidate detection is used only by developer validation,
/// never to assign a runtime price. Invalid catalogs cannot resolve any values.
/// </summary>
public sealed class RepairValueCatalog
{
    public static readonly ProtoId<ContentTileDefinition> StandardFloor = "FloorSteel";
    private readonly Dictionary<string, int> _values = new();
    private readonly Dictionary<string, string> _categories = new();
    private readonly HashSet<string> _excluded = new();
    public readonly List<string> Errors = new();

    public bool TryResolve(string entity, out int value)
    {
        value = 0;
        return Errors.Count == 0 && _values.TryGetValue(entity, out value);
    }

    public bool IsExcluded(string entity) => Errors.Count == 0 && _excluded.Contains(entity);

    public bool TryGetCategory(string entity, out string category)
    {
        category = string.Empty;
        return Errors.Count == 0 && _categories.TryGetValue(entity, out category!);
    }

    public static int CanonicalizeTile(int tileId, int steelTileId)
        => tileId == Tile.Empty.TypeId ? Tile.Empty.TypeId : steelTileId;

    public static RepairValueCatalog Build(IPrototypeManager prototypes)
        => Build(prototypes,
            prototypes.EnumeratePrototypes<RepairValueTagPrototype>(),
            prototypes.EnumeratePrototypes<RepairValueGroupPrototype>(),
            prototypes.EnumeratePrototypes<RepairValueOverridePrototype>(),
            prototypes.EnumeratePrototypes<RepairValueExclusionPrototype>());

    public static RepairValueCatalog Build(
        IPrototypeManager prototypes,
        IEnumerable<RepairValueTagPrototype> tags,
        IEnumerable<RepairValueGroupPrototype> groups,
        IEnumerable<RepairValueOverridePrototype> overrides,
        IEnumerable<RepairValueExclusionPrototype> exclusions)
    {
        var result = new RepairValueCatalog();
        var tagValues = new Dictionary<string, int>();
        foreach (var tag in tags)
        {
            if (tag.Value <= 0)
                result.Errors.Add($"RepairValueTag {tag.ID}: value must be positive.");
            if (!tagValues.TryAdd(tag.ID, tag.Value))
                result.Errors.Add($"Duplicate RepairValueTag {tag.ID}.");
        }

        void CheckEntity(EntProtoId entity, string source)
        {
            if (!prototypes.HasIndex(entity))
                result.Errors.Add($"{source}: missing EntityPrototype {entity}.");
        }

        foreach (var group in groups)
        {
            if (group.Entities.Count == 0)
                result.Errors.Add($"RepairValueGroup {group.ID}: empty group.");
            if (!tagValues.TryGetValue(group.Tag.Id, out var value))
                result.Errors.Add($"RepairValueGroup {group.ID}: missing RepairValueTag {group.Tag}.");
            foreach (var entity in group.Entities)
            {
                CheckEntity(entity, group.ID);
                if (!result._values.TryAdd(entity.Id, value))
                    result.Errors.Add($"RepairValueGroup {group.ID}: duplicate classification for {entity}.");
                result._categories.TryAdd(entity.Id, group.Tag.Id);
            }
        }

        var overridden = new HashSet<string>();
        foreach (var entry in overrides)
        {
            CheckEntity(entry.Entity, entry.ID);
            if (entry.Value <= 0)
                result.Errors.Add($"RepairValueOverride {entry.ID}: value must be positive.");
            if (!overridden.Add(entry.Entity.Id))
                result.Errors.Add($"RepairValueOverride {entry.ID}: duplicate override for {entry.Entity}.");
            result._values[entry.Entity.Id] = entry.Value;
        }

        foreach (var exclusion in exclusions)
        {
            if (exclusion.Entities.Count == 0 || string.IsNullOrWhiteSpace(exclusion.Reason))
                result.Errors.Add($"RepairValueExclusion {exclusion.ID}: entities and reason are required.");
            foreach (var entity in exclusion.Entities)
            {
                CheckEntity(entity, exclusion.ID);
                if (!result._excluded.Add(entity.Id))
                    result.Errors.Add($"RepairValueExclusion {exclusion.ID}: duplicate exclusion for {entity}.");
                if (result._values.ContainsKey(entity.Id))
                    result.Errors.Add($"RepairValueExclusion {exclusion.ID}: {entity} is classified AND excluded.");
            }
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
