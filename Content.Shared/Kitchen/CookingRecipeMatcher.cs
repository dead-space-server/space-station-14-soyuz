using System.Linq;
using Content.Shared.FixedPoint;

namespace Content.Shared.Kitchen;

/// <summary>
/// Narrows recipe candidates as ingredients are added to cookware. A recipe is
/// suggested only when all other recipes for that cookware have been ruled out.
/// </summary>
public static class CookingRecipeMatcher
{
    // The old microwave consumes these as ingredients. In the new workflow
    // plates and tins are used for cooking or serving, not put into the food.
    private static readonly HashSet<string> ServingContainers =
    [
        "FoodBowlBig",
        "FoodPlate",
        "FoodPlateSmall",
        "FoodPlateTin",
        "FoodPlateMuffinTin",
    ];

    public static bool IsServingContainer(string id) => ServingContainers.Contains(id);

    public static int MinimumCompatiblePortions(
        FoodRecipePrototype recipe,
        IReadOnlyDictionary<string, int> solids,
        IReadOnlyDictionary<string, FixedPoint2> reagents,
        int maxPortions)
    {
        for (var portions = 1; portions <= maxPortions; portions++)
        {
            if (CanStillMake(recipe, solids, reagents, portions))
                return portions;
        }

        return maxPortions;
    }

    public static int CompletePortions(
        FoodRecipePrototype recipe,
        IReadOnlyDictionary<string, int> solids,
        IReadOnlyDictionary<string, FixedPoint2> reagents,
        int maxPortions)
    {
        if (recipe.IngredientsReagents.Count == 0 &&
            recipe.IngredientsSolids.Keys.All(IsServingContainer))
            return 0;

        for (var portions = maxPortions; portions >= 1; portions--)
        {
            var complete = true;
            foreach (var (id, quantity) in recipe.IngredientsSolids)
            {
                if (IsServingContainer(id))
                    continue;

                solids.TryGetValue(id, out var present);
                if (present < quantity * portions)
                {
                    complete = false;
                    break;
                }
            }

            if (!complete)
                continue;

            foreach (var (id, quantity) in recipe.IngredientsReagents)
            {
                reagents.TryGetValue(id, out var present);
                if (present < quantity * portions)
                {
                    complete = false;
                    break;
                }
            }

            if (complete)
                return portions;
        }

        return 0;
    }

    public static FoodRecipePrototype? GetUniqueCandidate(
        IEnumerable<FoodRecipePrototype> recipes,
        IReadOnlyDictionary<string, int> solids,
        IReadOnlyDictionary<string, FixedPoint2> reagents,
        int maxPortions = 6)
    {
        if (maxPortions < 1)
            return null;

        var hasIngredient = reagents.Count > 0;
        foreach (var solid in solids.Keys)
            hasIngredient |= !ServingContainers.Contains(solid);

        if (!hasIngredient)
            return null;

        FoodRecipePrototype? only = null;
        foreach (var recipe in recipes)
        {
            if (!CanStillMake(recipe, solids, reagents, maxPortions))
                continue;

            if (only != null)
                return null;

            only = recipe;
        }

        return only;
    }

    public static bool CanStillMake(
        FoodRecipePrototype recipe,
        IReadOnlyDictionary<string, int> solids,
        IReadOnlyDictionary<string, FixedPoint2> reagents,
        int maxPortions = 6)
    {
        if (maxPortions < 1)
            return false;

        for (var portions = 1; portions <= maxPortions; portions++)
        {
            var compatible = true;
            foreach (var (id, quantity) in solids)
            {
                if (ServingContainers.Contains(id))
                    continue;

                if (!recipe.IngredientsSolids.TryGetValue(id, out var required) || quantity > required * portions)
                {
                    compatible = false;
                    break;
                }
            }

            if (!compatible)
                continue;

            foreach (var (id, quantity) in reagents)
            {
                if (!recipe.IngredientsReagents.TryGetValue(id, out var required) || quantity > required * portions)
                {
                    compatible = false;
                    break;
                }
            }

            if (compatible)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the first ingredient still missing from the current batch. Calling
    /// this after automatic recognition skips everything the chef added already.
    /// Method-specific actions (flip, drain, simmer) are separate cooking steps.
    /// </summary>
    public static CookingIngredientPrompt? NextIngredient(
        FoodRecipePrototype recipe,
        IReadOnlyDictionary<string, int> solids,
        IReadOnlyDictionary<string, FixedPoint2> reagents,
        int portions,
        bool liquidsFirst = false)
    {
        if (portions < 1)
            return null;

        if (liquidsFirst)
        {
            var liquid = NextReagent(recipe, reagents, portions);
            if (liquid != null)
                return liquid;
        }

        foreach (var (id, quantity) in recipe.IngredientsSolids)
        {
            if (ServingContainers.Contains(id))
                continue;

            solids.TryGetValue(id, out var present);
            var missing = quantity * portions - FixedPoint2.New(present);
            if (missing > 0)
                return new CookingIngredientPrompt(id, missing, false);
        }

        return NextReagent(recipe, reagents, portions);
    }

    private static CookingIngredientPrompt? NextReagent(
        FoodRecipePrototype recipe,
        IReadOnlyDictionary<string, FixedPoint2> reagents,
        int portions)
    {
        foreach (var (id, quantity) in recipe.IngredientsReagents)
        {
            reagents.TryGetValue(id, out var present);
            var missing = quantity * portions - present;
            if (missing > 0)
                return new CookingIngredientPrompt(id, missing, true);
        }

        return null;
    }
}

public readonly record struct CookingIngredientPrompt(string IngredientId, FixedPoint2 MissingQuantity, bool IsReagent);
