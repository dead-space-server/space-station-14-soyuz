// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Server.DeadSpace.TEMP_FOR_EVENT.Elfiry;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.NPC;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSystem
{
    public void KeepEventCommandActive(EntityUid uid)
    {
        if (!Enabled || !HasComp<EventSquadUnitComponent>(uid) || !TryComp<HTNComponent>(uid, out var htn) ||
            !htn.Enabled || _mobState.IsIncapacitated(uid) || TerminatingOrDeleted(uid))
            return;
        _lastPlayerNearby[uid] = _timing.CurTime;
        if (!HasComp<ActiveNPCComponent>(uid))
            WakeNPC(uid, htn);
    }
}
