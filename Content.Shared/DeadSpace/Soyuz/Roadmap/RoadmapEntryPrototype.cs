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
    
    [DataField("title", required: true)]
    public string Title { get; private set; } = string.Empty;
    
    [DataField("description", required: true)]
    public string Description { get; private set; } = string.Empty;
    
    [DataField("category", required: true)]
    public RoadmapCategory Category { get; private set; } = RoadmapCategory.Planned;
    
    [DataField("order")]
    public int Order { get; private set; } = 0;
    
    [DataField("tags")]
    public List<string> Tags { get; private set; } = new();
}

public enum RoadmapCategory
{
    Completed,
    InProgress,
    Planned
}
