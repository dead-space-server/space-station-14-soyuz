using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Complete replacement of the analyzer data currently authorized for one specific client.
/// An empty array revokes every previously supplied snapshot.
/// </summary>
[Serializable, NetSerializable]
public sealed class RepairAnalyzerSnapshotEvent : EntityEventArgs
{
    public RepairAnalyzerGridSnapshot[] Grids { get; }

    public RepairAnalyzerSnapshotEvent(RepairAnalyzerGridSnapshot[] grids)
    {
        Grids = grids;
    }
}

[Serializable, NetSerializable]
public readonly struct RepairAnalyzerGridSnapshot
{
    public readonly NetEntity Grid;
    public readonly RepairAnalyzerTaskData[] Tasks;

    public RepairAnalyzerGridSnapshot(NetEntity grid, RepairAnalyzerTaskData[] tasks)
    {
        Grid = grid;
        Tasks = tasks;
    }
}
