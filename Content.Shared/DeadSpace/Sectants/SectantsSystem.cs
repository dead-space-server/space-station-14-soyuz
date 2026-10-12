using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.DeadSpace.Events.Roles.Components;
using Content.Shared.Standing;
using JetBrains.Annotations;
using Robust.Shared.Timing;

namespace Content.Shared.DeadSpace.Sectants;

/// <summary>Shared helpers for sectant alpha entities.</summary>
[UsedImplicitly]
public sealed class SectantSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    public bool IsAlive(EntityUid uid) => _mobState.IsAlive(uid);

    public bool IsDown(EntityUid uid) => _standing.IsDown(uid);

    public bool CanTeleport(EntityUid uid, SectantRandomTeleportOnDamageComponent comp)
        => _timing.CurTime >= comp.LastTeleport + TimeSpan.FromSeconds(comp.Cooldown);

    /// <summary>
    /// Союзник ли это сектантов: общая NPC-фракция ИЛИ EventRole (слуги культа).
    /// </summary>
    public bool IsAlly(EntityUid candidate)
    {
        // 1) Ивентная роль — слуга культа, ассистент, кто угодно, кому выдали EventRole.
        if (HasComp<EventRoleComponent>(candidate))
            return true;

        return false;
    }

    /// <summary>
    /// Общая фракция между двумя сущностями (ручная проверка, обходит RA0002).
    /// </summary>
    public bool AreInSameFaction(EntityUid a, EntityUid b)
    {
        if (!TryComp<NpcFactionMemberComponent>(a, out var ca)) return false;
        if (!TryComp<NpcFactionMemberComponent>(b, out var cb)) return false;

        foreach (var fa in ca.Factions)
        foreach (var fb in cb.Factions)
            if (fa == fb) return true;

        return false;
    }

    /// <summary>
    /// Союзник ли target для source: общая фракция ИЛИ у target есть EventRoleComponent.
    /// </summary>
    public bool IsAllyOf(EntityUid source, EntityUid target)
    {
        if (source == target)
            return true;

        if (HasComp<EventRoleComponent>(target))
            return true;

        return AreInSameFaction(source, target);
    }
}
