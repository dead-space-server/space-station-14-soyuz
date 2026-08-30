using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Data-driven settings for a portable repair structural analyzer.
/// Its on/off state is provided by the standard ItemToggle component.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RepairStructuralAnalyzerComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Range = 7f;
}

/// <summary>
/// Server-authored visualization data attached only to a grid with a ready, active RepairBlueprint.
/// Correct tasks are deliberately omitted.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RepairAnalyzerDataComponent : Component
{
    [AutoNetworkedField]
    public List<RepairAnalyzerTaskData> Tasks = new();
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class RepairAnalyzerTaskData
{
    public RepairTaskType Type;
    public Vector2 LocalPosition;
    public Angle LocalRotation;
    public string ExpectedPrototype = string.Empty;
    public RepairTaskState State;

    public RepairAnalyzerTaskData(
        RepairTaskType type,
        Vector2 localPosition,
        Angle localRotation,
        string expectedPrototype,
        RepairTaskState state)
    {
        Type = type;
        LocalPosition = localPosition;
        LocalRotation = localRotation;
        ExpectedPrototype = expectedPrototype;
        State = state;
    }
}

[Serializable, NetSerializable]
public enum RepairTaskType : byte
{
    Tile,
    AnchoredEntity,
    RemoveAnchoredEntity,
}

[Serializable, NetSerializable]
public enum RepairTaskState : byte
{
    Missing,
    Wrong,
    Correct,
}
