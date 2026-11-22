// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.StationSpecialization;

/// <summary>
/// Профиль специализации станции. Здесь только список включённых эффектов
/// </summary>
[Prototype]
public sealed partial class StationSpecializationPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Название: ключ локализации или просто готовый текст</summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>Описание: ключ локализации или просто готовый текст</summary>
    [DataField]
    public string? Description;

    /// <summary>Какие эффекты даёт специализация</summary>
    [DataField]
    public HashSet<StationSpecializationBonus> Bonuses = new();

    /// <summary>Прототипы химкоматов, к которым применяется бонус ChemVend</summary>
    [DataField]
    public HashSet<EntProtoId> ChemVendMachines = new();

    /// <summary>
    /// Приписка к названию станции: ключ локализации или готовый текст
    /// </summary>
    [DataField]
    public string? NamePrefix;

    /// <summary>Отделы, чьи должности получают сообщение о навыке при заходе в раунд</summary>
    [DataField]
    public HashSet<ProtoId<DepartmentPrototype>> SkillDepartments = new();

    /// <summary>
    /// Навык: сообщение в чат при заходе в раунд, как у профессии (ключ локализации или готовый текст)
    /// </summary>
    [DataField]
    public string? SkillMessage;

    /// <summary>
    /// Суффикс варианта страницы вики для этой специализации
    /// </summary>
    [DataField]
    public string? WikiSuffix;
}
