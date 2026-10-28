// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

/// <summary>Repair-only value category, independent of gameplay tags.</summary>
[Prototype]
public sealed partial class RepairValueTagPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
    [DataField(required: true)] public int Value;
}

[Prototype]
public sealed partial class RepairValueGroupPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public ProtoId<RepairValueTagPrototype> Tag;
    [DataField(required: true)] public List<EntProtoId> Entities = new();
}

/// <summary>At most one exact override may supersede an entity's group value.</summary>
[Prototype]
public sealed partial class RepairValueOverridePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public EntProtoId Entity;
    [DataField(required: true)] public int Value;
}

[Prototype]
public sealed partial class RepairValueExclusionPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public List<EntProtoId> Entities = new();

    /// <summary>Match any gameplay tag on the fully inherited entity prototype, not runtime tag changes.</summary>
    [DataField] public List<ProtoId<TagPrototype>> Tags = new();
}
