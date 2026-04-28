using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace.MaterialMarketplace;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.DeadSpace.MaterialMarketplace;

public sealed class MaterialMarketplaceMenu : FancyWindow
{
    private readonly OptionButton _categoryFilter;
    private readonly BoxContainer _materialsList;
    [Dependency] private readonly IPrototypeManager _prototype = null!;
    private readonly LineEdit _searchBar;
    private readonly SpriteSystem _spriteSystem;
    [Dependency] private readonly IEntitySystemManager _sysMan = null!;
    private readonly List<MaterialMarketplaceCategoryPrototype> _categories;

    private MaterialMarketplaceState? _lastState;
    private string _searchFilter = string.Empty;
    private HashSet<string> _whitelist = new();
    private string? _currentCategory;

    public event Action<string, int>? OnBuyPressed;

    public MaterialMarketplaceMenu()
    {
        IoCManager.InjectDependencies(this);
        _spriteSystem = _sysMan.GetEntitySystem<SpriteSystem>();

        _categories = _prototype.EnumeratePrototypes<MaterialMarketplaceCategoryPrototype>()
            .OrderBy(c => c.Order)
            .ToList();

        Title = "Магазин материалов";
        SetSize = new Vector2(800, 800);
        Resizable = false;

        var mainContainer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            HorizontalExpand = true,
            Margin = new Thickness(10, 30, 10, 10),
        };

        var searchHBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 10,
        };

        _searchBar = new LineEdit
        {
            PlaceHolder = "Поиск материала...",
            HorizontalExpand = true,
        };
        _searchBar.OnTextChanged += OnSearchChanged;
        searchHBox.AddChild(_searchBar);

        _categoryFilter = new OptionButton { MinWidth = 150 };
        _categoryFilter.AddItem("Все");
        foreach (var cat in _categories)
            _categoryFilter.AddItem(cat.Name);

        _categoryFilter.OnItemSelected += args =>
        {
            _currentCategory = args.Id == 0 ? null : _categories[args.Id - 1].ID;
            _categoryFilter.SelectId(args.Id);
            UpdateMaterialsList();
        };
        searchHBox.AddChild(_categoryFilter);
        mainContainer.AddChild(searchHBox);

        var scroll = new ScrollContainer
        {
            HScrollEnabled = false,
            VerticalExpand = true,
            HorizontalExpand = true,
            Margin = new Thickness(0, 10, 0, 0),
        };

        _materialsList = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            HorizontalExpand = true,
            SeparationOverride = 12,
            Margin = new Thickness(0, 5, 0, 5),
        };

        scroll.AddChild(_materialsList);
        mainContainer.AddChild(scroll);
        AddChild(mainContainer);
    }

    public void SetWhitelist(IEnumerable<string> whitelist)
    {
        _whitelist = new HashSet<string>(whitelist);
    }

    public void UpdateState(MaterialMarketplaceState state)
    {
        _lastState = state;
        UpdateMaterialsList();
    }

    private void OnSearchChanged(LineEdit.LineEditEventArgs args)
    {
        _searchFilter = args.Text.Trim().ToLowerInvariant();
        UpdateMaterialsList();
    }

    private void UpdateMaterialsList()
    {
        _materialsList.RemoveAllChildren();
        if (_lastState == null)
            return;

        var materialIds = new HashSet<string>(_lastState.AvailableMaterials.Keys);
        materialIds.UnionWith(_whitelist);

        var materialsDict = new Dictionary<string, (MaterialPrototype proto, int logicalCount, double price)>();
        foreach (var matId in materialIds)
        {
            var logicalCount = _lastState.AvailableMaterials.GetValueOrDefault(matId, 0);
            var price = _lastState.Prices.GetValueOrDefault(matId, 0);

            if (!_prototype.TryIndex<MaterialPrototype>(matId, out var mproto))
                continue;

            materialsDict[matId] = (mproto, logicalCount, price);
        }

        if (_currentCategory != null)
        {
            var selectedCategory = _categories.FirstOrDefault(c => c.ID == _currentCategory);
            if (selectedCategory != null)
            {
                var categoryMaterials = selectedCategory.Materials
                    .Where(materialsDict.ContainsKey)
                    .Select(id =>
                    {
                        var data = materialsDict[id];
                        materialsDict.Remove(id);
                        return data;
                    })
                    .Where(m =>
                    {
                        var key = GetDisplayNameKey(m.proto);
                        return string.IsNullOrEmpty(_searchFilter) ||
                               ResolveLoc(key).ToLowerInvariant().Contains(_searchFilter);
                    })
                    .ToList();

                if (categoryMaterials.Any())
                {
                    _materialsList.AddChild(CreateCategoryLabel(selectedCategory.Name));
                    foreach (var (proto, logicalCount, price) in categoryMaterials)
                    {
                        _materialsList.AddChild(CreateMaterialCard(proto, logicalCount, price));
                    }
                }
            }

            return;
        }

        foreach (var category in _categories.Where(c => c.ID != "Other"))
        {
            var categoryMaterials = category.Materials
                .Where(materialsDict.ContainsKey)
                .Select(id =>
                {
                    var data = materialsDict[id];
                    materialsDict.Remove(id);
                    return data;
                })
                .Where(m =>
                {
                    var key = GetDisplayNameKey(m.proto);
                    return string.IsNullOrEmpty(_searchFilter) ||
                           ResolveLoc(key).ToLowerInvariant().Contains(_searchFilter);
                })
                .ToList();

            if (!categoryMaterials.Any())
                continue;

            _materialsList.AddChild(CreateCategoryLabel(category.Name));

            foreach (var (proto, logicalCount, price) in categoryMaterials)
            {
                _materialsList.AddChild(CreateMaterialCard(proto, logicalCount, price));
            }
        }

        if (!materialsDict.Any())
            return;

        var otherCat = _categories.FirstOrDefault(c => c.ID == "Other");
        if (otherCat == null)
            return;

        var otherMaterials = materialsDict.Values
            .Where(m =>
            {
                var key = GetDisplayNameKey(m.proto);
                return string.IsNullOrEmpty(_searchFilter) ||
                       ResolveLoc(key).ToLowerInvariant().Contains(_searchFilter);
            })
            .ToList();

        if (!otherMaterials.Any())
            return;

        _materialsList.AddChild(CreateCategoryLabel(otherCat.Name));

        foreach (var (proto, logicalCount, price) in otherMaterials)
        {
            _materialsList.AddChild(CreateMaterialCard(proto, logicalCount, price));
        }
    }

    private string GetDisplayNameKey(MaterialPrototype proto)
    {
        if (!string.IsNullOrEmpty(proto.StackEntity) &&
            _prototype.TryIndex<EntityPrototype>(proto.StackEntity, out var stackEntity))
            return stackEntity.Name;
        return proto.Name;
    }

    private static string ResolveLoc(string? key, string? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(key) && Loc.TryGetString(key, out var localized))
            return localized;

        return string.IsNullOrWhiteSpace(fallback) ? key ?? string.Empty : fallback;
    }

    private Label CreateCategoryLabel(string text)
    {
        var label = new Label
        {
            Text = text.ToUpper(),
            FontColorOverride = Color.White,
            Margin = new Thickness(12, 10, 0, 8),
            HorizontalExpand = true,
        };
        label.StyleClasses.Add("LabelHeading");
        return label;
    }

    private Control CreateMaterialCard(MaterialPrototype material, int logicalCount, double price)
    {
        StackPrototype? stackProto = null;
        if (!string.IsNullOrEmpty(material.StackEntity))
        {
            stackProto = _prototype.EnumeratePrototypes<StackPrototype>()
                .FirstOrDefault(sp => sp.Spawn == material.StackEntity);
        }

        var displayName = ResolveLoc(material.Name, material.ID);

        var countName = stackProto != null
            ? ResolveLoc(stackProto.Name, displayName)
            : displayName;

        string amountLocalized;
        if (!string.IsNullOrEmpty(material.StackEntity) && stackProto != null)
        {
            var key = stackProto.ID.ToLowerInvariant();
            amountLocalized = Loc.TryGetString(key, out var localizedAmount, ("amount", logicalCount))
                ? localizedAmount
                : string.Empty;

            if (string.IsNullOrWhiteSpace(amountLocalized) || amountLocalized == key)
                amountLocalized = $"{logicalCount} {countName}";
        }
        else
        {
            amountLocalized = $"{logicalCount} {countName}";
        }

        var amountText = $"В наличии: {amountLocalized}";

        var row = new PanelContainer
        {
            Margin = new Thickness(0, 0, 0, 8),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = new Color(30, 30, 35),
                BorderColor = material.Color,
                BorderThickness = new Thickness(2),
            },
        };

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 15,
            VerticalExpand = true,
            HorizontalExpand = true,
            Margin = new Thickness(12),
        };

        var icon = new TextureRect
        {
            MinSize = new Vector2(80, 80),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            Texture = _spriteSystem.Frame0(material.Icon),
            Margin = new Thickness(0, 0, 15, 0),
        };
        content.AddChild(icon);

        var infoVBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };

        infoVBox.AddChild(new Label
        {
            Text = displayName,
            FontColorOverride = Color.White,
            StyleClasses = { "LabelHeading" }
        });

        infoVBox.AddChild(new Label
        {
            Text = amountText,
            FontColorOverride = Color.LightGray,
            Margin = new Thickness(0, 2, 0, 0)
        });

        content.AddChild(infoVBox);

        var buttonsContainer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Align = BoxContainer.AlignMode.End,
            VerticalExpand = true,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };

        var buttonsBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true,
        };

        foreach (var buyAmount in new[] { 1, 5, 10, 30 })
        {
            var totalPrice = price * buyAmount;
            var buttonContainer = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 3,
                Margin = new Thickness(3, 0, 3, 0),
                VerticalAlignment = VAlignment.Center,
            };

            buttonContainer.AddChild(new Label
            {
                Text = $"{totalPrice:F0}¢",
                FontColorOverride = Color.LightGreen,
                HorizontalAlignment = HAlignment.Center,
            });

            var btn = new Button
            {
                Text = buyAmount.ToString(),
                Disabled = buyAmount > logicalCount,
                MinWidth = 50,
                MinHeight = 35,
            };
            btn.StyleClasses.Add("ButtonGray");
            btn.OnPressed += _ => OnBuyPressed?.Invoke(material.ID, buyAmount);
            buttonContainer.AddChild(btn);

            buttonsBox.AddChild(buttonContainer);
        }

        buttonsContainer.AddChild(buttonsBox);
        content.AddChild(buttonsContainer);

        row.AddChild(content);
        return row;
    }
}
