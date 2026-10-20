// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Marks a powered computer as a repair orders console.
/// Order offers are station-scoped; the shop catalog comes from this console's configured reward pool.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RepairOrderConsoleComponent : Component
{
    [DataField(required: true)]
    public ProtoId<RepairRewardPoolPrototype> ShopRewardPool;

    /// <summary>
    /// Authoritative anti-spam delay between physical report printouts from this console.
    /// </summary>
    [DataField]
    public TimeSpan ReportPrintCooldown = TimeSpan.FromSeconds(5);

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextReportPrint;
}
