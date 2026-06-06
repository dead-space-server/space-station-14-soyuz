// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT
using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Soyuz.Roadmap;

[Prototype]
[DataDefinition]
public sealed partial class RoadmapEntryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;
    
    /// <summary>
    /// Ключ локализации для названия карточки.
    /// Если указан — используется вместо Title.
    /// Пример: "roadmap-entry-feature-title"
    /// </summary>
    [DataField("locTitle")]
    public string? LocTitle { get; private set; }

    /// <summary>
    /// Название карточки.
    /// Используется, если LocTitle не задан.
    /// </summary>
    [DataField("title")]
    public string Title { get; private set; } = string.Empty;
    
    /// <summary>
    /// Ключ локализации для описания карточки.
    /// Если указан — используется вместо Description.
    /// Пример: "roadmap-entry-feature-desc"
    /// </summary>
    [DataField("locDescription")]
    public string? LocDescription { get; private set; }

    /// <summary>
    /// Описание карточки. Короткое описание отображается до 30 символов, далее троеточие. Полное описание требуется раскрыть.
    /// Используется, если LocDescription не задан.
    /// </summary>
    [DataField("description")]
    public string Description { get; private set; } = string.Empty;
    
    /// <summary>
    /// Категория карточки, в которой карточка будет отображаться.
    /// Completed, InProgress, Planned
    /// </summary>
    [DataField("category", required: true)]
    public RoadmapCategory Category { get; private set; } = RoadmapCategory.Planned;
    
    /// <summary>
    /// Порядок отображения карточек в колонке.
    /// Записи с меньшим значением отображаются выше.
    /// </summary>
    [DataField("order")]
    public int Order { get; private set; } = 0;
    
    /// <summary>
    /// Ключи локализации для тегов.
    /// Если указаны — используются вместо Tags.
    /// Пример: ["roadmap-tag-gameplay", "roadmap-tag-major"]
    /// </summary>
    [DataField("locTags")]
    public List<string>? LocTags { get; private set; }

    /// <summary>
    /// Теги, отображаемые снизу карточки. Сортировки по тегам нет :(
    /// Используются, если LocTags не задан.
    /// </summary>
    [DataField("tags")]
    public List<string> Tags { get; private set; } = new();
}

public enum RoadmapCategory
{
    Completed,
    InProgress,
    Planned
}
