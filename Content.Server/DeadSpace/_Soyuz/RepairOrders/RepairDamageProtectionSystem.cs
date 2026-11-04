// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using System.Linq;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>The single gate for every procedural destruction path, including supporting tiles.</summary>
public sealed class RepairDamageProtectionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IComponentFactory _components = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    private readonly HashSet<string> _configurationWarnings = new();

    public bool CanProcedurallyDamage(EntityUid uid)
    {
        if (MetaData(uid).EntityPrototype is not { } prototype) return false;
        return CanProcedurallyDamage(prototype) && !_prototypes.EnumeratePrototypes<RepairDamageProtectionPrototype>()
            .SelectMany(p => p.Components).Any(name => _components.TryGetRegistration(name, out var registration) && HasComp(uid, registration.Type));
    }

    public bool CanProcedurallyDamage(EntityPrototype prototype)
    {
        var parents = _prototypes.EnumerateAllParents<EntityPrototype>(prototype.ID, includeSelf: true)
            .Select(p => p.id).ToHashSet();
        return !_prototypes.EnumeratePrototypes<RepairDamageProtectionPrototype>().Any(rule =>
            rule.Entities.Any(id => id.Id == prototype.ID) || rule.Parents.Any(id => parents.Contains(id.Id)) ||
            rule.Components.Any(prototype.Components.ContainsKey));
    }

    public void ValidateConfiguration()
    {
        void Warn(string diagnostic)
        {
            if (_configurationWarnings.Add(diagnostic))
                _logManager.GetSawmill("repair_orders").Warning(diagnostic);
        }

        foreach (var rule in _prototypes.EnumeratePrototypes<RepairDamageProtectionPrototype>())
        {
            foreach (var id in rule.Entities)
                if (!_prototypes.HasIndex(id)) Warn($"{rule.ID}: unknown protected prototype {id}; entry ignored.");
            foreach (var id in rule.Parents)
                if (!_prototypes.HasMapping<EntityPrototype>(id.Id)) Warn($"{rule.ID}: unknown protected family {id}; entry ignored.");
            foreach (var name in rule.Components)
                if (!_components.TryGetRegistration(name, out _)) Warn($"{rule.ID}: unknown protection component {name}; entry ignored.");
        }
    }
}
