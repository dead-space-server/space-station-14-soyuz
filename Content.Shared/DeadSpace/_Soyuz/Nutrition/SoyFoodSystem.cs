// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Tag;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.Nutrition;

/// <summary>Tracks soy meat through processing without declaring an entire mixed meal vegan.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SoyFoodComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId? Ingredient;

    [DataField, AutoNetworkedField]
    public bool ContainsAnimalMeat;
}

public readonly record struct SoyFoodOrigin(bool ContainsSoy, bool ContainsAnimalMeat)
{
    public SoyFoodOrigin Merge(SoyFoodOrigin other) => new(
        ContainsSoy || other.ContainsSoy, ContainsAnimalMeat || other.ContainsAnimalMeat);
}

public sealed class SoyFoodSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly INetManager _net = default!;

    private static readonly ProtoId<TagPrototype> MeatTag = "Meat";
    private static readonly ProtoId<ReagentPrototype> RawProtein = "UncookedAnimalProteins";
    private static readonly ProtoId<ReagentPrototype> Protein = "Protein";
    private static readonly ProtoId<FlavorPrototype> SoyFlavor = "soy";
    private static readonly ProtoId<FlavorPrototype> MeatFlavor = "meaty";

    public string? GetIngredientId(EntityUid uid)
    {
        if (TryComp<SoyFoodComponent>(uid, out var soy) && soy.Ingredient is { } ingredient)
            return ingredient.Id;
        return MetaData(uid).EntityPrototype?.ID;
    }

    public SoyFoodOrigin GetOrigin(EntityUid uid)
    {
        if (TryComp<SoyFoodComponent>(uid, out var soy))
            return new(true, soy.ContainsAnimalMeat);

        var animalMeat = _tags.HasTag(uid, MeatTag);
        if (TryComp<EdibleComponent>(uid, out var edible) &&
            _solutions.TryGetSolution(uid, edible.Solution, out _, out var solution))
            animalMeat |= solution.GetTotalPrototypeQuantity(RawProtein.Id) > 0;
        return new(false, animalMeat);
    }

    /// <summary>Used for copying a food item, not for mixing in a new ingredient.</summary>
    public void Inherit(EntityUid source, EntityUid result, bool replacePrototypeProtein = false)
    {
        Apply(result, GetOrigin(source), replacePrototypeProtein);
    }

    /// <summary>Combine before the ingredient's tags and solutions are merged into the dish.</summary>
    public void AddIngredient(EntityUid dish, EntityUid ingredient)
    {
        Apply(dish, GetOrigin(dish).Merge(GetOrigin(ingredient)));
    }

    public void Apply(EntityUid result, SoyFoodOrigin origin, bool replacePrototypeProtein = false)
    {
        if (!origin.ContainsSoy)
            return;

        var soy = EnsureComp<SoyFoodComponent>(result);
        soy.ContainsAnimalMeat |= origin.ContainsAnimalMeat;
        Dirty(result, soy);

        var flavor = EnsureComp<FlavorProfileComponent>(result);
        flavor.Flavors.Add(SoyFlavor.Id);
        if (!soy.ContainsAnimalMeat)
            flavor.Flavors.Remove(MeatFlavor.Id);

        if (_net.IsServer && MetaData(result).EntityPrototype?.Components.ContainsKey("SoyFood") != true)
        {
            var name = MetaData(result).EntityName;
            var suffix = Loc.GetString("soy-food-name-suffix");
            if (!name.EndsWith(suffix, StringComparison.Ordinal))
                _metadata.SetEntityName(result, name + suffix);
        }

        // Only transform a new recipe/heat result's default composition. Slicing and assembling
        // transfer actual solutions and must preserve any injected substances, including poisons.
        if (!replacePrototypeProtein || soy.ContainsAnimalMeat ||
            !TryComp<EdibleComponent>(result, out var edible) ||
            !_solutions.TryGetSolution(result, edible.Solution, out var soln, out var solution))
            return;

        var raw = solution.GetTotalPrototypeQuantity(RawProtein.Id);
        if (raw <= 0)
            return;
        _solutions.RemoveReagent(soln.Value, RawProtein.Id, raw);
        _solutions.TryAddReagent(soln.Value, Protein.Id, raw);
    }
}
