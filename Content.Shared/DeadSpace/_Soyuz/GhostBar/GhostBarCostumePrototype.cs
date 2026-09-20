// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.GhostBar;

[Prototype]
public sealed partial class GhostBarCostumePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public string Name { get; private set; } = string.Empty;

    [DataField]
    public string Category { get; private set; } = string.Empty;

    [DataField]
    public EntProtoId ClothingProto { get; private set; } = default!;

    [DataField]
    public string Slot { get; private set; } = string.Empty;

    [DataField]
    public bool Default { get; private set; }
}