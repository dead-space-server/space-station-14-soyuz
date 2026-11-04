// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.Shuttles;

[RegisterComponent]
public sealed partial class ShuttleCollisionSensorComponent : Component
{
    [DataField]
    public float Range = 30f;
}

[RegisterComponent]
public sealed partial class ShuttleCollisionControllerComponent : Component
{
    [DataField]
    public ShuttleCollisionMode Mode = ShuttleCollisionMode.Automatic;

    [DataField]
    public ShuttleCollisionMode PreferredMode = ShuttleCollisionMode.Automatic;
}

[Serializable, NetSerializable]
public enum ShuttleCollisionMode : byte
{
    Off,
    Warning,
    Automatic,
}

[Serializable, NetSerializable]
public enum ShuttleCollisionVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum ShuttleCollisionSensorState : byte
{
    Off,
    On,
    Warning,
}

[Serializable, NetSerializable]
public sealed class ShuttleCollisionModeMessage(ShuttleCollisionMode mode, bool enabled = true) : BoundUserInterfaceMessage
{
    public readonly ShuttleCollisionMode Mode = mode;
    public readonly bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class ShuttleCollisionStatus
{
    public ShuttleCollisionMode Mode;
    public ShuttleCollisionMode PreferredMode;
    public int Installed;
    public int Powered;
    public float? Distance;
    public Direction Direction;
    public bool Suspended;
    public bool InsufficientThrust;
}
