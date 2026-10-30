// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCCombatSystem
{
    private bool TryGetNextHitscanAmmo(Entity<GunComponent> gun, out bool hitscan)
    {
        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(gun, ref ammo);
        hitscan = ammo.NextAmmoEntity is { } entity ? IsHitscanAmmoEntity(entity)
            : ammo.NextAmmoPrototype is { } prototype && IsHitscanAmmoPrototype(prototype);
        return ammo.NextAmmoEntity != null || ammo.NextAmmoPrototype != null;
    }
}
