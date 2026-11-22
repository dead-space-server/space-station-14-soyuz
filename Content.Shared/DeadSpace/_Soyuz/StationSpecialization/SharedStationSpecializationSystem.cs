// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Diagnostics.CodeAnalysis;
using Content.Shared.Station;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.StationSpecialization;

/// <summary>
/// Общий API специализаций станции. Его можно вызывать и из shared-систем (например, растений)
/// </summary>
public abstract class SharedStationSpecializationSystem : EntitySystem
{
    [Dependency] protected readonly IConfigurationManager Cfg = default!;
    [Dependency] protected readonly IPrototypeManager Proto = default!;
    [Dependency] protected readonly SharedStationSystem Station = default!;

    public bool IsEnabled => Cfg.GetCVar(StationSpecializationCVars.Enabled);

    public bool TryGetStationSpecialization(
        EntityUid? entity,
        [NotNullWhen(true)] out Entity<StationSpecializationComponent>? station,
        [NotNullWhen(true)] out StationSpecializationPrototype? spec)
    {
        station = null;
        spec = null;

        if (!IsEnabled || entity is not { } uid)
            return false;

        var stationUid = HasComp<StationSpecializationComponent>(uid) ? uid : Station.GetOwningStation(uid);
        if (stationUid == null || !TryComp<StationSpecializationComponent>(stationUid, out var comp))
            return false;

        if (!Proto.TryIndex(comp.Specialization, out spec))
            return false;

        station = (stationUid.Value, comp);
        return true;
    }

    private bool HasBonus(EntityUid entity, StationSpecializationBonus bonus, [NotNullWhen(true)] out StationSpecializationPrototype? spec)
    {
        return TryGetStationSpecialization(entity, out _, out spec) && spec.Bonuses.Contains(bonus);
    }

    public float GetBeaconMultiplier(EntityUid entity)
    {
        return HasBonus(entity, StationSpecializationBonus.BeaconPayout, out _)
            ? Cfg.GetCVar(StationSpecializationCVars.BeaconMultiplier)
            : 1f;
    }

    public float GetPlantGrowthMultiplier(EntityUid entity)
    {
        return HasBonus(entity, StationSpecializationBonus.PlantGrowth, out _)
            ? Math.Max(0.01f, Cfg.GetCVar(StationSpecializationCVars.PlantGrowthMultiplier))
            : 1f;
    }

    public float GetCargoPriceMultiplier(EntityUid entity)
    {
        return HasBonus(entity, StationSpecializationBonus.CargoDiscount, out _)
            ? Math.Clamp(1f - Cfg.GetCVar(StationSpecializationCVars.CargoDiscount), 0f, 1f)
            : 1f;
    }

}
