using System.Numerics;
using System.Linq;
using Content.Shared.Kitchen;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client.Kitchen.UI;

public sealed class CookingMenu : DefaultWindow
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IEntityManager _entities = default!;

    private readonly BoxContainer _recipeList;
    private readonly LineEdit _search;
    private readonly SpriteView _preview;
    private readonly Label _previewPlaceholder;
    private readonly Label _recipeName;
    private readonly Label _mode;
    private readonly Label _time;
    private readonly Label _ingredients;
    private CookingRecipeEntry[] _recipes = [];
    private string? _selected;
    private string? _previewResultId;
    private EntityUid? _previewEntity;
    private int _portionCount = 1;

    public event Action<string?, int>? RecipeSelected;

    public CookingMenu()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("cooking-ui-title");
        MinSize = SetSize = new Vector2(760, 520);
        OnClose += ClearPreview;

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 12,
            Margin = new Thickness(12),
        };
        Contents.AddChild(root);

        var listPanel = Card("#202a32");
        listPanel.MinWidth = 280;
        root.AddChild(listPanel);
        var left = Column(8);
        listPanel.AddChild(left);
        left.AddChild(Heading("cooking-ui-recipes"));
        _search = new LineEdit { PlaceHolder = Loc.GetString("cooking-ui-search"), HorizontalExpand = true };
        _search.OnTextChanged += _ => RebuildRecipeList();
        left.AddChild(_search);
        var recipeScroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true };
        left.AddChild(recipeScroll);
        _recipeList = Column(3);
        recipeScroll.AddChild(_recipeList);

        var right = Column(10);
        right.HorizontalExpand = true;
        root.AddChild(right);

        var previewCard = Card("#253138");
        right.AddChild(previewCard);
        var previewRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 12,
            HorizontalExpand = true,
        };
        previewCard.AddChild(previewRow);
        var imageCard = Card("#182127");
        imageCard.MinSize = new Vector2(132, 132);
        previewRow.AddChild(imageCard);
        _preview = new SpriteView(_entities)
        {
            MinSize = new Vector2(112, 112),
            Scale = new Vector2(3, 3),
            Stretch = SpriteView.StretchMode.Fit,
        };
        imageCard.AddChild(_preview);
        _previewPlaceholder = new Label
        {
            Text = Loc.GetString("cooking-ui-preview-empty"),
            Visible = true,
        };
        imageCard.AddChild(_previewPlaceholder);

        var previewText = Column(6);
        previewText.HorizontalExpand = true;
        previewRow.AddChild(previewText);
        previewText.AddChild(Heading("cooking-ui-preview"));
        _recipeName = new Label { StyleClasses = { "LabelHeading" } };
        previewText.AddChild(_recipeName);
        _mode = new Label { FontColorOverride = Color.FromHex("#b9c9d0") };
        previewText.AddChild(_mode);
        _time = new Label { FontColorOverride = Color.FromHex("#f0c77b") };
        previewText.AddChild(_time);

        var ingredientsCard = Card("#202a32");
        ingredientsCard.VerticalExpand = true;
        right.AddChild(ingredientsCard);
        var ingredientsLayout = Column(6);
        ingredientsCard.AddChild(ingredientsLayout);
        ingredientsLayout.AddChild(Heading("cooking-ui-ingredients"));
        var ingredientsScroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true };
        ingredientsLayout.AddChild(ingredientsScroll);
        _ingredients = new Label { HorizontalExpand = true };
        ingredientsScroll.AddChild(_ingredients);

        right.AddChild(new Label
        {
            Text = Loc.GetString("cooking-ui-world-hint"),
            FontColorOverride = Color.FromHex("#b9c9d0"),
        });
    }

    public void ApplyState(CookingUiState state)
    {
        var rebuild = _selected != state.SelectedRecipe || _recipes.Length != state.Recipes.Length;
        if (!rebuild)
        {
            for (var i = 0; i < _recipes.Length; i++)
            {
                if (_recipes[i].Id == state.Recipes[i].Id)
                    continue;

                rebuild = true;
                break;
            }
        }

        _recipes = state.Recipes;
        _selected = state.SelectedRecipe;
        _portionCount = state.Portions;

        var active = state.SelectedRecipe ?? state.PredictedRecipe;
        var recipe = Array.Find(_recipes, entry => entry.Id == active);
        _recipeName.Text = recipe is null ? Loc.GetString("cooking-ui-select-short") : DisplayName(recipe);
        _mode.Text = Loc.GetString(state.SelectedRecipe != null
            ? "cooking-ui-mode-guided"
            : state.PredictedRecipe != null
                ? "cooking-ui-mode-recognized"
                : "cooking-ui-mode-free");
        _time.Text = recipe is null ? string.Empty : Loc.GetString("cooking-ui-time", ("seconds", recipe.Seconds));
        _ingredients.Text = recipe?.Ingredients ?? Loc.GetString("cooking-ui-select");
        SetPreview(recipe?.ResultId);

        if (rebuild)
            RebuildRecipeList();
    }

    private void SetPreview(string? resultId)
    {
        if (_previewResultId == resultId)
            return;

        ClearPreview();
        _previewResultId = resultId;
        if (resultId != null && _prototypes.HasIndex<EntityPrototype>(resultId))
        {
            _previewEntity = _entities.SpawnEntity(resultId, MapCoordinates.Nullspace);
            _preview.SetEntity(_previewEntity);
        }
        _preview.Visible = _previewEntity != null;
        _previewPlaceholder.Visible = !_preview.Visible;
    }

    private void ClearPreview()
    {
        _preview.SetEntity((EntityUid?) null);
        if (_previewEntity is { } entity && _entities.EntityExists(entity))
            _entities.DeleteEntity(entity);
        _previewEntity = null;
        _previewResultId = null;
    }

    protected override void Dispose(bool disposing)
    {
        ClearPreview();
        base.Dispose(disposing);
    }

    private void RebuildRecipeList()
    {
        _recipeList.RemoveAllChildren();
        var free = new Button
        {
            Text = Loc.GetString("cooking-ui-native"),
            HorizontalExpand = true,
            Pressed = _selected == null,
            MinHeight = 34,
        };
        free.OnPressed += _ => RecipeSelected?.Invoke(null, _portionCount);
        _recipeList.AddChild(free);
        var filter = _search.Text.Trim();
        foreach (var recipe in _recipes.OrderBy(DisplayName))
        {
            var name = DisplayName(recipe);
            if (!name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                continue;

            var button = new Button
            {
                Text = name,
                HorizontalExpand = true,
                Pressed = recipe.Id == _selected,
                MinHeight = 34,
            };
            var id = recipe.Id;
            button.OnPressed += _ => RecipeSelected?.Invoke(id, _portionCount);
            _recipeList.AddChild(button);
        }
    }

    private string DisplayName(CookingRecipeEntry recipe) =>
        _prototypes.TryIndex<EntityPrototype>(recipe.ResultId, out var result) ? result.Name : recipe.Name;

    private static BoxContainer Column(int spacing) => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = spacing,
    };

    private static Label Heading(string key) => new()
    {
        Text = Loc.GetString(key),
        StyleClasses = { "LabelHeading" },
        FontColorOverride = Color.FromHex("#e5c68a"),
    };

    private static PanelContainer Card(string background) => new()
    {
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(background),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 10,
            ContentMarginBottomOverride = 10,
        },
    };
}
