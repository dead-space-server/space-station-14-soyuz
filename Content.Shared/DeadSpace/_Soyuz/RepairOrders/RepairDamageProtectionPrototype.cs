// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

/// <summary>Destruction protection is independent of repair valuation and identity.</summary>
[Prototype]
public sealed partial class RepairDamageProtectionPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
    [DataField] public List<EntProtoId> Entities = new();
    [DataField] public List<EntProtoId> Parents = new();
    [DataField] public List<string> Components = new();
}
