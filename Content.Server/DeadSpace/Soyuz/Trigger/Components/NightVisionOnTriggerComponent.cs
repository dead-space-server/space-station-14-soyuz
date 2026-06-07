using Robust.Shared.GameStates;
using Content.Shared.Trigger.Components.Effects;

namespace Content.Server.Soyuz.Trigger.Components;

/// <summary>
/// Переключает ночное зрение, когда срабаывает триггер
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NightVisionOnTriggerComponent : BaseXOnTriggerComponent;
