using Robust.Shared.GameStates;

namespace Content.Shared._NF.Clothing.Components;
/// <summary>
/// To be used with Harpy to replace the tag
/// </summary>
[RegisterComponent, ComponentProtoName("HarpyHideWings"), NetworkedComponent] // DS14: keep old prototype/component id
public sealed partial class HarpyWingOccluderComponent : Component
{
}
