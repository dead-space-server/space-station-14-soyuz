using System.Collections.Generic;

namespace Content.Shared.DeadSpace.Soyuz.Roadmap;

public sealed class RoadmapEntryData
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Order { get; set; }
    public List<string> Tags { get; set; } = new();
}
