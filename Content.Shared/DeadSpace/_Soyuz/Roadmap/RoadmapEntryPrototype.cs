// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT
using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.Roadmap;

[Prototype]
[DataDefinition]
public sealed partial class RoadmapEntryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;
    
    /// <summary>
    /// Название карточка
    /// </summary>
    [DataField("title", required: true)]
    public string Title { get; private set; } = string.Empty;
    
    /// <summary>
    /// Описание карточки. Короткое описание отображается до 30 символов, далее троеточие. Полное описание требуется раскрыть
    /// </summary>
    [DataField("description", required: true)]
    public string Description { get; private set; } = string.Empty;
    
    /// <summary>
    /// Категория карточки, в которой карточка будет отображаться
    /// Completed, InProgress, Planned
    /// </summary>
    [DataField("category", required: true)]
    public RoadmapCategory Category { get; private set; } = RoadmapCategory.Planned;
    
    /// <summary>
    /// Порядок отображения карточек в колонке
    /// Записи с меньшим значением отображаются выше
    /// </summary>
    [DataField("order")]
    public int Order { get; private set; } = 0;
    
    /// <summary>
    /// Теги, отображаемые снизу карточки. Сортировки по тегам нет :(
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
