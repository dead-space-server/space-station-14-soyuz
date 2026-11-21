using Robust.Shared.Serialization;

namespace Content.Shared.Kitchen;

[Serializable, NetSerializable]
public enum CookingUiKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class CookingSelectRecipeMessage(string? recipeId, int portions) : BoundUserInterfaceMessage
{
    public string? RecipeId = recipeId;
    public int Portions = portions;
}

[Serializable, NetSerializable]
public sealed class CookingServeMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class CookingRecipeEntry(string id, string name, string resultId, uint seconds, string ingredients)
{
    public string Id = id;
    public string Name = name;
    public string ResultId = resultId;
    public uint Seconds = seconds;
    public string Ingredients = ingredients;
}

[Serializable, NetSerializable]
public sealed class CookingUiState(
    CookingRecipeEntry[] recipes,
    string? selectedRecipe,
    string? predictedRecipe,
    int portions,
    int maxPortions,
    string hint,
    float progress,
    int remainingPortions) : BoundUserInterfaceState
{
    public CookingRecipeEntry[] Recipes = recipes;
    public string? SelectedRecipe = selectedRecipe;
    public string? PredictedRecipe = predictedRecipe;
    public int Portions = portions;
    public int MaxPortions = maxPortions;
    public string Hint = hint;
    public float Progress = progress;
    public int RemainingPortions = remainingPortions;
}
