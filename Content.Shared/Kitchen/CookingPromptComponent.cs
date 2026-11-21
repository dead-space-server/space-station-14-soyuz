using Robust.Shared.GameStates;

namespace Content.Shared.Kitchen;

/// <summary>
/// The current cooking step shown above a vessel in the world.
/// It is only populated after a recipe is selected or uniquely recognized.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CookingPromptComponent : Component
{
    [AutoNetworkedField]
    public EntityUid? Viewer;

    [AutoNetworkedField]
    public string Stage = string.Empty;

    [AutoNetworkedField]
    public string Detail = string.Empty;
}
