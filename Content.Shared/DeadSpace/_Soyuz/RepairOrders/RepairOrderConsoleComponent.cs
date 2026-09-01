using Robust.Shared.GameStates;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Marks a powered computer as a repair orders console.
/// The actual order pool is stored on the owning station.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RepairOrderConsoleComponent : Component
{
    /// <summary>
    /// Authoritative anti-spam delay between physical report printouts from this console.
    /// </summary>
    [DataField]
    public TimeSpan ReportPrintCooldown = TimeSpan.FromSeconds(5);

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextReportPrint;
}
