// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared.DeadSpace._Soyuz.StationSpecialization;

/// <summary>
/// Все числовые значения бонусов специализаций
/// </summary>
[CVarDefs]
public sealed class StationSpecializationCVars : CVars
{
    /// <summary>Главный выключатель системы специализаций</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("soyuz.station_spec.enabled", true, CVar.SERVER | CVar.REPLICATED,
            "Включить систему специализаций станций.");

    /// <summary>Добавлять ли приписку специализации к названию станции</summary>
    public static readonly CVarDef<bool> NamePrefixEnabled =
        CVarDef.Create("soyuz.station_spec.name_prefix_enabled", true, CVar.SERVER | CVar.REPLICATED,
            "Добавлять приписку специализации к названию станции.");

    /// <summary>
    /// Милицейская специализация: true = любой человек без позывного пишется в рации как «НЕИЗВЕСТНЫЙ»,
    /// false = только те, у кого в работе есть компонент Callsign, но позывной не указан
    /// </summary>
    public static readonly CVarDef<bool> CallsignUnknownForAll =
        CVarDef.Create("soyuz.station_spec.callsign_unknown_for_all", true, CVar.SERVER | CVar.REPLICATED,
            "Подписывать в рации «НЕИЗВЕСТНЫЙ» всех без позывного (иначе только должности с пустым позывным).");

    /// <summary>Как часто срабатывает пассивная генерация, в секундах</summary>
    public static readonly CVarDef<float> PassiveInterval =
        CVarDef.Create("soyuz.station_spec.passive_interval", 1f, CVar.SERVER | CVar.REPLICATED,
            "Период пассивной генерации (НИО, почта), секунды.");

    /// <summary>Инженерная: множитель выплаты маяка сдачи энергии</summary>
    public static readonly CVarDef<float> BeaconMultiplier =
        CVarDef.Create("soyuz.station_spec.beacon_multiplier", 1.5f, CVar.SERVER | CVar.REPLICATED,
            "Множитель выплаты маяка сдачи энергии (инженерная).");

    /// <summary>НИО: очки исследований за один период</summary>
    public static readonly CVarDef<int> ResearchPoints =
        CVarDef.Create("soyuz.station_spec.research_points", 25, CVar.SERVER | CVar.REPLICATED,
            "Очки исследований за период passive_interval (научная).");

    /// <summary>Почта: деньги на счёт карго за один период</summary>
    public static readonly CVarDef<int> CargoMoney =
        CVarDef.Create("soyuz.station_spec.cargo_money", 10, CVar.SERVER | CVar.REPLICATED,
            "Деньги на счёт карго за период passive_interval (почтовая).");

    /// <summary>Почта: скидка на заказы в карго, 0.15 = 15%</summary>
    public static readonly CVarDef<float> CargoDiscount =
        CVarDef.Create("soyuz.station_spec.cargo_discount", 0.15f, CVar.SERVER | CVar.REPLICATED,
            "Скидка на заказы в карго, 0.15 = 15% (почтовая).");

    /// <summary>Сервис: множитель скорости роста растений</summary>
    public static readonly CVarDef<float> PlantGrowthMultiplier =
        CVarDef.Create("soyuz.station_spec.plant_growth_multiplier", 1.5f, CVar.SERVER | CVar.REPLICATED,
            "Множитель скорости роста растений (сервисная).");

    /// <summary>Мед: сколько добавить к каждому типу канистр в химкомате</summary>
    public static readonly CVarDef<int> ChemVendBonus =
        CVarDef.Create("soyuz.station_spec.chem_vend_bonus", 3, CVar.SERVER | CVar.REPLICATED,
            "Прибавка к каждому типу канистр в химкомате (медицинская).");
}
