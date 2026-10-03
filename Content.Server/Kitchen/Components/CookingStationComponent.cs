namespace Content.Server.Kitchen.Components;

/// <summary>
/// Connects heated cookware with the stove surface, oven compartment, or
/// self-contained tabletop appliance in one kitchen station.
/// </summary>
[RegisterComponent]
public sealed partial class CookingStationComponent : Component
{
    [DataField]
    public string? TopAppliance;

    [DataField]
    public string? OvenAppliance;

    [DataField]
    public string? SelfAppliance;

    [DataField]
    public string OvenContainerId = "oven";

    [DataField]
    public int OvenCapacity = 4;

    public Robust.Shared.Containers.Container? OvenStorage;
}
