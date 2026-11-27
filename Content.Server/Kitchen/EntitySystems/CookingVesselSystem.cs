using System.Linq;
using Content.Server.Hands.Systems;
using Content.Server.Kitchen.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Kitchen;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Temperature.Components;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;

namespace Content.Server.Kitchen.EntitySystems;

/// <summary>
/// Uses the existing meal results with vessel-specific recipes. The contents
/// determine the recipe in free mode; selected recipes always remain explicit.
/// </summary>
public sealed class CookingVesselSystem : EntitySystem
{
    [Dependency] private readonly CookingRecipeCatalogSystem _catalog = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedPopupSystem _popups = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    private float _updateTime;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CookingVesselComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<CookingVesselComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CookingVesselComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<CookingVesselComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<CookingVesselComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<CookingVesselComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<CookingVesselComponent, EntInsertedIntoContainerMessage>(OnContentsChanged);
        SubscribeLocalEvent<CookingVesselComponent, EntRemovedFromContainerMessage>(OnContentsChanged);
        SubscribeLocalEvent<CookingVesselComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<CookingVesselComponent, CookingSelectRecipeMessage>(OnSelectRecipe);
        SubscribeLocalEvent<CookingVesselComponent, CookingServeMessage>(OnServe);
    }

    private void OnInit(Entity<CookingVesselComponent> ent, ref ComponentInit args)
    {
        ent.Comp.Storage = _containers.EnsureContainer<Container>(ent, ent.Comp.ContainerId);
        EnsureComp<CookingPromptComponent>(ent);
    }

    private void OnMapInit(Entity<CookingVesselComponent> ent, ref MapInitEvent args)
    {
        Refresh(ent);
    }

    private void OnGetVerbs(Entity<CookingVesselComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("cooking-ui-open"),
            Act = () => _ui.OpenUi(ent.Owner, CookingUiKey.Key, user),
        });
        if (ent.Comp.RemainingPortions > 0)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("cooking-ui-serve"),
                Act = () => ServePortion(ent),
            });
        }
        else
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("cooking-world-portions", ("count", ent.Comp.SelectedPortions)),
                Act = () =>
                {
                    ent.Comp.SelectedPortions = ent.Comp.SelectedPortions % ent.Comp.MaxPortions + 1;
                    ent.Comp.CookProgress = 0;
                    Refresh(ent);
                },
            });
        }
        if (ent.Comp.Storage.Count > 0)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("cooking-eject-ingredients"),
                Act = () => _containers.EmptyContainer(ent.Comp.Storage),
            });
        }
    }

    private void OnInteractUsing(Entity<CookingVesselComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Filled bottles and beakers use the game's normal solution transfer
        // interaction. The vessel must receive liquid, not swallow the bottle.
        if (HasComp<DrainableSolutionComponent>(args.Used) &&
            _solutions.TryGetDrainableSolution(args.Used, out _, out var source) &&
            source.Volume > 0)
        {
            if (ent.Comp.PromptViewer == null && ent.Comp.SelectedRecipe == null)
                ent.Comp.PromptViewer = args.User;
            return;
        }

        if (!CanAcceptIngredient(ent, args.Used))
            return;

        args.Handled = true;
        if (ent.Comp.RemainingPortions > 0 || ent.Comp.Storage.Count >= ent.Comp.Capacity ||
            !_hands.TryDropIntoContainer(args.User, args.Used, ent.Comp.Storage))
        {
            _popups.PopupEntity(Loc.GetString("cooking-insert-failed"), ent, args.User);
            return;
        }

        if (ent.Comp.PromptViewer == null && ent.Comp.SelectedRecipe == null)
            ent.Comp.PromptViewer = args.User;
        Refresh(ent);
    }

    private void OnInsertAttempt(Entity<CookingVesselComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID != ent.Comp.ContainerId)
            return;

        if (ent.Comp.RemainingPortions > 0 || !CanAcceptIngredient(ent, args.EntityUid) ||
            (!args.AssumeEmpty && ent.Comp.Storage.Count >= ent.Comp.Capacity))
            args.Cancel();
    }

    private bool CanAcceptIngredient(Entity<CookingVesselComponent> ent, EntityUid item)
    {
        if (!HasComp<ItemComponent>(item) || HasComp<CookingVesselComponent>(item))
            return false;

        if (HasComp<DrainableSolutionComponent>(item) &&
            _solutions.TryGetDrainableSolution(item, out _, out var source) &&
            source.Volume > 0)
            return false;

        var id = TryComp<StackComponent>(item, out var stack)
            ? _prototypes.Index<StackPrototype>(stack.StackTypeId).Spawn.Id
            : MetaData(item).EntityPrototype?.ID;
        var recipes = _catalog.RecipesForMethod(ent.Comp.Method);
        if (ent.Comp.SelectedRecipe != null)
            recipes = recipes.Where(recipe => recipe.ID == ent.Comp.SelectedRecipe);

        foreach (var recipe in recipes)
        {
            if (id != null && !CookingRecipeMatcher.IsServingContainer(id) &&
                recipe.IngredientsSolids.ContainsKey(id))
                return true;
        }

        return false;
    }

    private void OnInteractHand(Entity<CookingVesselComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.RemainingPortions > 0)
        {
            ServePortion(ent);
            args.Handled = true;
            return;
        }

        if (ent.Comp.HeatingAppliance == null)
            return;

        _ui.OpenUi(ent.Owner, CookingUiKey.Key, args.User);
        args.Handled = true;
    }

    private void OnContentsChanged(Entity<CookingVesselComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!ent.Comp.IsConsuming && args.Container.ID == ent.Comp.ContainerId)
            Refresh(ent);
    }

    private void OnContentsChanged(Entity<CookingVesselComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!ent.Comp.IsConsuming && args.Container.ID == ent.Comp.ContainerId)
            Refresh(ent);
    }

    private void OnSolutionChanged(Entity<CookingVesselComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (!ent.Comp.IsConsuming)
            Refresh(ent);
    }

    private void OnSelectRecipe(Entity<CookingVesselComponent> ent, ref CookingSelectRecipeMessage args)
    {
        if (args.Portions < 1 || args.Portions > ent.Comp.MaxPortions || ent.Comp.RemainingPortions > 0)
            return;

        if (args.RecipeId != null)
        {
            if (!_catalog.TryGetMethod(args.RecipeId, out var method) || method.ID != ent.Comp.Method ||
                !_prototypes.TryIndex<FoodRecipePrototype>(args.RecipeId, out var selected))
                return;

            if (selected.SecretRecipe)
            {
                var (solids, reagents) = ReadIngredients(ent);
                if (_catalog.PredictRecipe(ent.Comp.Method, solids, reagents, ent.Comp.MaxPortions)?.ID != args.RecipeId)
                    return;
            }
        }

        ent.Comp.SelectedRecipe = args.RecipeId;
        ent.Comp.PromptViewer = args.Actor;
        ent.Comp.SelectedPortions = args.Portions;
        ent.Comp.CookProgress = 0;
        Refresh(ent);
    }

    private void OnServe(Entity<CookingVesselComponent> ent, ref CookingServeMessage args)
    {
        ServePortion(ent);
    }

    private void ServePortion(Entity<CookingVesselComponent> ent)
    {
        if (ent.Comp.RemainingPortions < 1 || ent.Comp.CookedRecipe == null ||
            !_prototypes.TryIndex<FoodRecipePrototype>(ent.Comp.CookedRecipe, out var recipe))
            return;

        Spawn(recipe.Result, _transform.GetMapCoordinates(ent.Owner));
        ent.Comp.RemainingPortions--;
        if (ent.Comp.RemainingPortions == 0)
            ent.Comp.CookedRecipe = null;
        Refresh(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _updateTime += frameTime;
        if (_updateTime < 0.5f)
            return;

        var elapsed = _updateTime;
        _updateTime = 0;
        var query = EntityQueryEnumerator<CookingVesselComponent>();
        while (query.MoveNext(out var uid, out var vessel))
            Process((uid, vessel), elapsed);
    }

    private void Process(Entity<CookingVesselComponent> ent, float elapsed)
    {
        var vessel = ent.Comp;
        if (vessel.RemainingPortions > 0)
            return;

        var (solids, reagents) = ReadIngredients(ent);
        var recipe = ResolveRecipe(ent, solids, reagents);
        var portions = recipe == null ? 0 : vessel.SelectedRecipe != null || vessel.SelectedPortions > 1
            ? vessel.SelectedPortions
            : CookingRecipeMatcher.CompletePortions(recipe, solids, reagents, vessel.MaxPortions);

        if (recipe == null || portions == 0 ||
            CookingRecipeMatcher.CompletePortions(recipe, solids, reagents, portions) != portions ||
            !CanHeat(ent))
        {
            var wasCooking = vessel.CookProgress > 0;
            vessel.CookProgress = 0;
            vessel.CookingPortions = 0;
            if (wasCooking || BuildHint(ent, recipe, solids, reagents) != vessel.LastHint)
                Refresh(ent, solids, reagents, recipe);
            return;
        }

        if (vessel.CookingPortions != portions)
        {
            vessel.CookProgress = 0;
            vessel.CookingPortions = portions;
        }

        // A batch takes more time than one serving, while sharing one heat-up cycle.
        var duration = Math.Max(1f, recipe.CookTime * (1f + 0.4f * (portions - 1)));
        vessel.CookProgress += elapsed;
        if (vessel.CookProgress < duration)
        {
            Refresh(ent, solids, reagents, recipe);
            return;
        }

        vessel.IsConsuming = true;
        try
        {
            Consume(ent, recipe, portions);
        }
        finally
        {
            vessel.IsConsuming = false;
        }
        vessel.CookedRecipe = recipe.ID;
        vessel.RemainingPortions = portions;
        vessel.CookProgress = 0;
        vessel.CookingPortions = 0;
        Refresh(ent);
    }

    private bool CanHeat(Entity<CookingVesselComponent> ent)
    {
        if (!_prototypes.TryIndex<CookingMethodPrototype>(ent.Comp.Method, out var method) ||
            ent.Comp.HeatingAppliance != method.Appliance)
            return false;

        if (method.Appliance == "PrepTable")
            return true;

        return TryComp<TemperatureComponent>(ent, out var temperature) &&
            temperature.CurrentTemperature >= ent.Comp.MinimumTemperature;
    }

    private FoodRecipePrototype? ResolveRecipe(
        Entity<CookingVesselComponent> ent,
        Dictionary<string, int> solids,
        Dictionary<string, FixedPoint2> reagents)
    {
        var vessel = ent.Comp;
        if (vessel.SelectedRecipe != null)
        {
            vessel.PredictedRecipe = null;
            if (!_prototypes.TryIndex<FoodRecipePrototype>(vessel.SelectedRecipe, out var selected) ||
                !CookingRecipeMatcher.CanStillMake(selected, solids, reagents, vessel.SelectedPortions))
                return null;

            return selected;
        }

        var predicted = _catalog.PredictRecipe(vessel.Method, solids, reagents, vessel.MaxPortions);
        vessel.PredictedRecipe = predicted?.ID;
        return predicted;
    }

    private (Dictionary<string, int> Solids, Dictionary<string, FixedPoint2> Reagents) ReadIngredients(Entity<CookingVesselComponent> ent)
    {
        var solids = new Dictionary<string, int>();
        var reagents = new Dictionary<string, FixedPoint2>();
        var recipeSolids = _catalog.RecipesForMethod(ent.Comp.Method)
            .SelectMany(recipe => recipe.IngredientsSolids.Keys)
            .ToHashSet();

        if (_solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionId, out _, out var ownSolution))
            AddReagents(ownSolution, reagents);

        foreach (var item in ent.Comp.Storage.ContainedEntities)
        {
            string? id = TryComp<StackComponent>(item, out var stack)
                ? _prototypes.Index<StackPrototype>(stack.StackTypeId).Spawn.Id
                : MetaData(item).EntityPrototype?.ID;

            // A beaker or bottle supplies liquid without becoming a food solid.
            // Edible solids also contain nutriment; their own solution is not an
            // extra ingredient that should disqualify their recipe.
            if ((id == null || !recipeSolids.Contains(id)) && HasComp<RefillableSolutionComponent>(item) &&
                _solutions.TryGetDrainableSolution(item, out _, out var liquid))
            {
                AddReagents(liquid, reagents);
                continue;
            }

            if (id != null)
            {
                solids.TryGetValue(id, out var present);
                solids[id] = present + (stack?.Count ?? 1);
            }
        }

        return (solids, reagents);
    }

    private static void AddReagents(Content.Shared.Chemistry.Components.Solution solution, Dictionary<string, FixedPoint2> reagents)
    {
        foreach (var (reagent, quantity) in solution.Contents)
        {
            reagents.TryGetValue(reagent.Prototype, out var present);
            reagents[reagent.Prototype] = present + quantity;
        }
    }

    private void Consume(Entity<CookingVesselComponent> ent, FoodRecipePrototype recipe, int portions)
    {
        foreach (var (id, quantity) in recipe.IngredientsReagents)
        {
            var left = quantity * portions;
            if (_solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionId, out var own, out var ownSolution) && own != null)
            {
                var remove = FixedPoint2.Min(left, ownSolution.GetTotalPrototypeQuantity(id));
                _solutions.RemoveReagent(own.Value, id, remove);
                left -= remove;
            }

            foreach (var item in ent.Comp.Storage.ContainedEntities.ToArray())
            {
                if (left <= 0)
                    break;
                if (!_solutions.TryGetDrainableSolution(item, out var drain, out var solution) || drain == null)
                    continue;
                var remove = FixedPoint2.Min(left, solution.GetTotalPrototypeQuantity(id));
                _solutions.RemoveReagent(drain.Value, id, remove);
                left -= remove;
            }
        }

        foreach (var (id, quantity) in recipe.IngredientsSolids)
        {
            if (CookingRecipeMatcher.IsServingContainer(id))
                continue;

            var left = (quantity * portions).Int();
            foreach (var item in ent.Comp.Storage.ContainedEntities.ToArray())
            {
                if (left <= 0)
                    break;
                var stack = CompOrNull<StackComponent>(item);
                string? itemId = stack != null
                    ? _prototypes.Index<StackPrototype>(stack.StackTypeId).Spawn.Id
                    : MetaData(item).EntityPrototype?.ID;
                if (itemId != id)
                    continue;

                if (stack != null)
                {
                    var take = Math.Min(left, stack.Count);
                    if (take == stack.Count)
                        _containers.Remove(item, ent.Comp.Storage);
                    _stacks.ReduceCount((item, stack), take);
                    left -= take;
                }
                else
                {
                    _containers.Remove(item, ent.Comp.Storage);
                    Del(item);
                    left--;
                }
            }
        }
    }

    private void Refresh(
        Entity<CookingVesselComponent> ent,
        Dictionary<string, int>? solids = null,
        Dictionary<string, FixedPoint2>? reagents = null,
        FoodRecipePrototype? recipe = null)
    {
        if (solids == null || reagents == null)
            (solids, reagents) = ReadIngredients(ent);
        recipe ??= ResolveRecipe(ent, solids, reagents);

        var hint = BuildHint(ent, recipe, solids, reagents);
        ent.Comp.LastHint = hint;

        var displayPortions = Math.Max(1, ent.Comp.CookingPortions > 0
            ? ent.Comp.CookingPortions
            : recipe != null && ent.Comp.SelectedRecipe == null
                ? Math.Max(ent.Comp.SelectedPortions,
                    CookingRecipeMatcher.MinimumCompatiblePortions(recipe, solids, reagents, ent.Comp.MaxPortions))
                : ent.Comp.SelectedPortions);
        var entries = _catalog.RecipesForMethod(ent.Comp.Method)
            .Where(r => !r.SecretRecipe || r.ID == ent.Comp.PredictedRecipe || r.ID == ent.Comp.SelectedRecipe)
            .OrderBy(r => _prototypes.Index<EntityPrototype>(r.Result).Name)
            .Select(r => new CookingRecipeEntry(
                r.ID,
                _prototypes.Index<EntityPrototype>(r.Result).Name,
                r.Result,
                (uint) Math.Ceiling(Math.Max(1f, r.CookTime * (1f + 0.4f * (displayPortions - 1)))),
                FormatIngredients(r, displayPortions)))
            .ToArray();
        var duration = recipe == null ? 1f : Math.Max(1f, recipe.CookTime *
            (1f + 0.4f * (Math.Max(1, ent.Comp.CookingPortions) - 1)));
        UpdateWorldPrompt(ent, recipe, solids, reagents, displayPortions, duration, hint);
        _ui.SetUiState(ent.Owner, CookingUiKey.Key, new CookingUiState(
            entries,
            ent.Comp.SelectedRecipe,
            ent.Comp.RemainingPortions > 0 ? ent.Comp.CookedRecipe : ent.Comp.PredictedRecipe,
            displayPortions,
            ent.Comp.MaxPortions,
            hint,
            Math.Clamp(ent.Comp.CookProgress / duration, 0f, 1f),
            ent.Comp.RemainingPortions));
    }

    private void UpdateWorldPrompt(
        Entity<CookingVesselComponent> ent,
        FoodRecipePrototype? recipe,
        Dictionary<string, int> solids,
        Dictionary<string, FixedPoint2> reagents,
        int portions,
        float duration,
        string hint)
    {
        var prompt = Comp<CookingPromptComponent>(ent);
        string stage;
        string detail;

        if (ent.Comp.RemainingPortions > 0)
        {
            stage = Loc.GetString("cooking-world-ready");
            detail = Loc.GetString("cooking-ui-ready", ("count", ent.Comp.RemainingPortions));
        }
        else if (recipe == null)
        {
            stage = ent.Comp.SelectedRecipe == null ? string.Empty : Loc.GetString("cooking-world-attention");
            detail = ent.Comp.SelectedRecipe == null ? string.Empty : hint;
        }
        else
        {
            var total = recipe.IngredientsSolids.Count(entry => !CookingRecipeMatcher.IsServingContainer(entry.Key)) +
                recipe.IngredientsReagents.Count;
            var complete = 0;
            foreach (var (id, amount) in recipe.IngredientsSolids)
            {
                if (CookingRecipeMatcher.IsServingContainer(id))
                    continue;
                solids.TryGetValue(id, out var present);
                if (FixedPoint2.New(present) >= amount * portions)
                    complete++;
            }
            foreach (var (id, amount) in recipe.IngredientsReagents)
            {
                reagents.TryGetValue(id, out var present);
                if (present >= amount * portions)
                    complete++;
            }

            stage = Loc.GetString("cooking-world-step", ("step", Math.Min(complete + 1, total + 1)),
                ("total", total + 1));
            detail = ent.Comp.CookProgress > 0
                ? Loc.GetString("cooking-world-cooking",
                    ("seconds", (int) Math.Ceiling(Math.Max(0, duration - ent.Comp.CookProgress))))
                : hint;
        }

        if (prompt.Viewer == ent.Comp.PromptViewer && prompt.Stage == stage && prompt.Detail == detail)
            return;

        prompt.Viewer = ent.Comp.PromptViewer;
        prompt.Stage = stage;
        prompt.Detail = detail;
        Dirty(ent.Owner, prompt);
    }

    private string BuildHint(
        Entity<CookingVesselComponent> ent,
        FoodRecipePrototype? recipe,
        Dictionary<string, int> solids,
        Dictionary<string, FixedPoint2> reagents)
    {
        if (ent.Comp.RemainingPortions > 0)
            return Loc.GetString("cooking-hint-done");
        if (recipe == null)
            return Loc.GetString(ent.Comp.SelectedRecipe == null ? "cooking-hint-unknown" : "cooking-hint-invalid");

        var portions = ent.Comp.SelectedRecipe == null
            ? Math.Max(ent.Comp.SelectedPortions,
                CookingRecipeMatcher.MinimumCompatiblePortions(recipe, solids, reagents, ent.Comp.MaxPortions))
            : ent.Comp.SelectedPortions;
        var missing = CookingRecipeMatcher.NextIngredient(recipe, solids, reagents, portions);
        if (missing != null)
            return Loc.GetString("cooking-hint-add",
                ("ingredient", IngredientName(missing.Value.IngredientId, missing.Value.IsReagent)),
                ("amount", missing.Value.MissingQuantity));

        return Loc.GetString(CanHeat(ent) ? "cooking-hint-ready" : "cooking-hint-wait");
    }

    private string FormatIngredients(FoodRecipePrototype recipe, int portions)
    {
        var ingredients = new List<string>();
        foreach (var (id, quantity) in recipe.IngredientsSolids)
        {
            if (!CookingRecipeMatcher.IsServingContainer(id))
                ingredients.Add($"{IngredientName(id, false)} × {quantity * portions}");
        }
        foreach (var (id, quantity) in recipe.IngredientsReagents)
            ingredients.Add($"{IngredientName(id, true)} × {quantity * portions}");
        return string.Join("\n", ingredients);
    }

    private string IngredientName(string id, bool reagent)
    {
        if (reagent)
            return _prototypes.TryIndex<ReagentPrototype>(id, out var chemical) ? chemical.LocalizedName : id;
        return _prototypes.TryIndex<EntityPrototype>(id, out var entity) ? entity.Name : id;
    }
}
