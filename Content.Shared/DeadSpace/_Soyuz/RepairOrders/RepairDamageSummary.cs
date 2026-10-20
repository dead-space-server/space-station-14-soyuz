// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

/// <summary>Localize immutable successful event IDs at presentation time, never infer damage from the live grid.</summary>
public static class RepairDamageSummary
{
    public static string Format(IPrototypeManager prototypes, IEnumerable<string> events, bool detailed)
    {
        var lines = new List<string>();
        foreach (var group in events.GroupBy(id => id).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var text = Loc.GetString("repair-orders-damage-unknown");
            if (prototypes.TryIndex<RepairDamageEventPrototype>(group.Key, out var ev) &&
                Loc.TryGetString(detailed ? ev.Description : ev.Name, out var localized))
                text = localized;
            lines.Add(Loc.GetString(group.Count() > 1 ? "repair-orders-damage-event-count" : "repair-orders-damage-event",
                ("event", text), ("count", group.Count())));
        }
        if (lines.Count == 0) return Loc.GetString("repair-orders-damage-unknown");
        return string.Join(detailed ? "\n" : " ", lines);
    }
}
