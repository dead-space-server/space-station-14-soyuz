// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RepairReconstructorComponent : Component
{
    [DataField] public float Range = 7f;
    [DataField] public float Delay = 5f;
    [AutoNetworkedField] public NetEntity? SelectionGrid;
    [AutoNetworkedField] public NetEntity? SelectionUser;
    [AutoNetworkedField] public Vector2i? SelectionStart;
    [AutoNetworkedField] public Vector2i? SelectionEnd;
    [AutoNetworkedField] public Vector2i[] BlockedCells = Array.Empty<Vector2i>();
    public int SelectionRuntimeId;
    public int? QuotedCost;
    public int[] QuotedRequirements = Array.Empty<int>();
    // Server-only: the engine owns cancellation, while Applying remains set until rollback completes.
    public DoAfterId? DoAfter;
    public bool Applying;
    public bool Busy => Applying || DoAfter != null;
}

/// <summary>Only this cartridge may refill a reconstruction tablet.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RepairReconstructionCartridgeComponent : Component;

[Serializable, NetSerializable]
public sealed partial class RepairReconstructionDoAfterEvent : DoAfterEvent
{
    [DataField] public NetEntity Grid;
    [DataField] public int RuntimeId;
    [DataField] public Vector2i Start;
    [DataField] public Vector2i End;
    [DataField] public int[] Requirements = Array.Empty<int>();
    [DataField] public int QuotedCost;

    public override DoAfterEvent Clone() => new RepairReconstructionDoAfterEvent
    {
        Grid = Grid, RuntimeId = RuntimeId, Start = Start, End = End,
        Requirements = (int[]) Requirements.Clone(), QuotedCost = QuotedCost,
    };
}
