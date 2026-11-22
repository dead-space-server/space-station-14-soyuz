// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.StationSpecialization;

/// <summary>
/// Какие эффекты включает специализация. Сами значения лежат в <see cref="StationSpecializationCVars"/>
/// </summary>
[Serializable, NetSerializable]
public enum StationSpecializationBonus : byte
{
    /// <summary>Выплата маяка сдачи энергии умножается</summary>
    BeaconPayout,

    /// <summary>Пассивные очки исследований</summary>
    PassiveResearch,

    /// <summary>Пассивные деньги на счёт карго</summary>
    PassiveCargoMoney,

    /// <summary>Скидка на заказы в карго</summary>
    CargoDiscount,

    /// <summary>Ускоренный рост растений</summary>
    PlantGrowth,

    /// <summary>Больше канистр в химкомате</summary>
    ChemVend,

    /// <summary>Радиопозывные вместо имён в рации</summary>
    Callsigns,
}
