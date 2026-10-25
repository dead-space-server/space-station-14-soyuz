using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.Sobr;

[Serializable, NetSerializable]
public enum SobrRemoteUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class SobrSquadInfo
{
    public ProtoId<SobrSquadPrototype> Id;
    public string Name;
    public Color Color;
    public int Total;
    public int Ready;

    public SobrSquadInfo(ProtoId<SobrSquadPrototype> id, string name, Color color, int total, int ready)
    {
        Id = id;
        Name = name;
        Color = color;
        Total = total;
        Ready = ready;
    }
}

[Serializable, NetSerializable]
public sealed class SobrRemoteBuiState : BoundUserInterfaceState
{
    public List<SobrSquadInfo> Squads;

    public SobrRemoteBuiState(List<SobrSquadInfo> squads)
    {
        Squads = squads;
    }
}

[Serializable, NetSerializable]
public sealed class SobrRemoteWakeMessage : BoundUserInterfaceMessage
{
    public ProtoId<SobrSquadPrototype> Squad;

    public SobrRemoteWakeMessage(ProtoId<SobrSquadPrototype> squad)
    {
        Squad = squad;
    }
}