// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Shared.NPC.Components;

namespace Content.Shared.NPC.Systems;

public sealed partial class NpcFactionSystem
{
    public bool IsEntityHostile(Entity<NpcFactionMemberComponent?, FactionExceptionComponent?> ent, EntityUid target)
    {
        if (IsIgnored((ent.Owner, ent.Comp2), target))
            return false;
        if (GetHostiles((ent.Owner, ent.Comp2)).Any(hostile => GetCombatTarget(hostile) == target))
            return true;
        var other = new Entity<NpcFactionMemberComponent?>(GetFactionOwner(target), null);
        return Resolve(ent, ref ent.Comp1, false) && Resolve(other, ref other.Comp, false) &&
               !IsEntityFriendly((ent.Owner, ent.Comp1), other) && ent.Comp1.HostileFactions.Overlaps(other.Comp.Factions);
    }
}
