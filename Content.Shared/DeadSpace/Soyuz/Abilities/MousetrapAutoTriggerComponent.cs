using Robust.Shared.GameStates;

namespace Content.Shared.Abilities;

[RegisterComponent, ComponentProtoName("AlwaysTriggerMousetrap"), NetworkedComponent] // DS14: keep old prototype/component id
public sealed partial class MousetrapAutoTriggerComponent : Component
{
}
