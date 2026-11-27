// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Content.Shared.DeadSpace._Soyuz.StationSpecialization;
using Content.Shared.VendingMachines.Components;

namespace Content.Server.DeadSpace._Soyuz.StationSpecialization;

public sealed partial class StationSpecializationSystem
{
    /// <summary>Прибавляет бонус к каждому типу канистр в химкоматах станции (один раз в начале раунда)</summary>
    partial void ApplyChemVendBonus(EntityUid station, StationSpecializationPrototype spec)
    {
        var bonus = Cfg.GetCVar(StationSpecializationCVars.ChemVendBonus);
        if (!spec.Bonuses.Contains(StationSpecializationBonus.ChemVend) || bonus <= 0 || spec.ChemVendMachines.Count == 0)
            return;

        var machines = EntityQueryEnumerator<VendingMachineComponent>();
        while (machines.MoveNext(out var uid, out var vending))
        {
            if (Station.GetOwningStation(uid) != station)
                continue;

            if (MetaData(uid).EntityPrototype?.ID is not { } protoId || !spec.ChemVendMachines.Contains(protoId))
                continue;

            foreach (var key in new List<string>(vending.Inventory.Keys))
            {
                var entry = vending.Inventory[key];
                entry.Amount += (uint)bonus;
                vending.Inventory[key] = entry;
            }
        }
    }
}
