using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared.Kitchen;

/// <summary>
/// Resolves the cookware for each existing recipe, including secret recipes.
/// Rejects missing or multiply assigned recipes before the cooking UI uses the catalog.
/// </summary>
public sealed class CookingRecipeCatalogSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private readonly Dictionary<string, CookingMethodPrototype> _methodByRecipe = new();

    public override void Initialize()
    {
        base.Initialize();
        Rebuild();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    public bool TryGetMethod(string recipeId, out CookingMethodPrototype method)
    {
        return _methodByRecipe.TryGetValue(recipeId, out method!);
    }

    public IEnumerable<FoodRecipePrototype> RecipesForMethod(string methodId)
    {
        if (!_prototypes.TryIndex<CookingMethodPrototype>(methodId, out var method))
            yield break;

        foreach (var recipeId in method.Recipes)
            yield return _prototypes.Index<FoodRecipePrototype>(recipeId);
    }

    /// <summary>
    /// Returns a recipe only after the ingredients eliminate every other
    /// recipe for this cookware. Choosing a recipe explicitly can bypass this.
    /// </summary>
    public FoodRecipePrototype? PredictRecipe(
        string methodId,
        IReadOnlyDictionary<string, int> solids,
        IReadOnlyDictionary<string, FixedPoint2> reagents,
        int maxPortions = 6)
    {
        return CookingRecipeMatcher.GetUniqueCandidate(
            RecipesForMethod(methodId), solids, reagents, maxPortions);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<CookingMethodPrototype>() || args.WasModified<FoodRecipePrototype>())
            Rebuild();
    }

    private void Rebuild()
    {
        var methods = new Dictionary<string, CookingMethodPrototype>();
        foreach (var method in _prototypes.EnumeratePrototypes<CookingMethodPrototype>())
        {
            foreach (var recipeId in method.Recipes)
            {
                if (!_prototypes.TryIndex<FoodRecipePrototype>(recipeId, out _))
                    throw new InvalidOperationException($"Cooking method {method.ID} references missing recipe {recipeId}.");

                if (!methods.TryAdd(recipeId, method))
                    throw new InvalidOperationException($"Cooking recipe {recipeId} is assigned to multiple methods.");
            }
        }

        foreach (var recipe in _prototypes.EnumeratePrototypes<FoodRecipePrototype>())
        {
            if (!methods.ContainsKey(recipe.ID))
                throw new InvalidOperationException($"Cooking recipe {recipe.ID} has no cooking method.");
        }

        _methodByRecipe.Clear();
        foreach (var (recipeId, method) in methods)
            _methodByRecipe.Add(recipeId, method);
    }
}
