using Content.Shared.Physics;

namespace Content.Server.DeadSpace.Soyuz.GardenShovel;

[RegisterComponent]
public sealed partial class GardenShovelComponent : Component
{
    [DataField]
    public List<string> AvailableTiles { get; set; } = new();

    [DataField]
    public CollisionGroup CollisionMask { get; private set; } = CollisionGroup.None;

    [ViewVariables]
    public Modes Mode = Modes.Dig;
}

public enum Modes : byte
{
    Dig,
    Bury
}
