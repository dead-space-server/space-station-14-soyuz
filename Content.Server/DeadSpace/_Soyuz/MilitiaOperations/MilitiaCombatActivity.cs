// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Server.DeadSpace._Soyuz.MilitiaOperations;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.NPC;
using Content.Shared.Mind.Components;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSystem
{
    public void KeepMilitiaCombatActive(EntityUid uid)
    {
        if (!Enabled || !TryComp<MilitiaOperationActorComponent>(uid, out var actor) || !actor.EquipmentReady ||
            actor.DamageThreatUntil <= _timing.CurTime || !TryComp<HTNComponent>(uid, out var htn) || !htn.Enabled ||
            HasComp<Robust.Shared.Player.ActorComponent>(uid) || _mobState.IsIncapacitated(uid) ||
            TryComp<MindContainerComponent>(uid, out var mind) && mind.Mind != null ||
            MetaData(uid).EntityPaused || TerminatingOrDeleted(uid))
            return;
        _lastPlayerNearby[uid] = _timing.CurTime;
        if (!HasComp<ActiveNPCComponent>(uid))
            WakeNPC(uid, htn);
    }
}
