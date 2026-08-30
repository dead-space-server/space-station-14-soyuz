using Robust.Shared.GameStates;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Marks a powered computer as a repair orders console.
/// The actual order pool is stored on the owning station.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RepairOrderConsoleComponent : Component;
