namespace Content.Server.Kitchen.Components;

/// <summary>
/// Ingredient container for a specific class of cookware. The same recipe can
/// be selected explicitly or recognized from the current contents.
/// </summary>
[RegisterComponent]
public sealed partial class CookingVesselComponent : Component
{
    [DataField]
    public string ContainerId = "cooking";

    [DataField]
    public int Capacity = 24;

    public Robust.Shared.Containers.Container Storage = default!;

    [DataField(required: true)]
    public string Method = default!;

    [DataField]
    public string SolutionId = "cooker";

    [DataField]
    public int MaxPortions = 6;

    [DataField]
    public float MinimumTemperature = 350f;

    public string? HeatingAppliance;
    public string? SelectedRecipe;
    public string? PredictedRecipe;
    public EntityUid? PromptViewer;
    public int SelectedPortions = 1;
    public int CookingPortions;
    public float CookProgress;
    public string? CookedRecipe;
    public int RemainingPortions;
    public string LastHint = string.Empty;
    public bool IsConsuming;
}
