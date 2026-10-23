// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DeadSpace._Soyuz.MedicalOrders;
using Content.Shared.FixedPoint;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.DeadSpace._Soyuz.MedicalOrders;

public sealed partial class MedicalOrderWindow
{
    private readonly List<PanelContainer> _surfaces = new();
    private readonly List<(Label Label, bool Muted)> _themeLabels = new();
    private readonly List<(TextureRect Icon, string Name)> _themeIcons = new();
    private readonly List<ContainerButton> _navigation = new();
    private readonly List<(string Reagent, ContainerButton Button, bool Supplied, int Index)> _marketRows = new();
    private string? _selectedReagent;
    private string? _displayedReagent;
    private bool _reagentDetailsInitialized;
    private PanelContainer _background = default!;

    private bool IsExchange => _state?.Kind == MedicalOrderMachineKind.Reagent;
    private Color Ink => Color.FromHex(IsExchange ? "#E9E7F5" : "#D8E5E6");
    private Color Muted => Color.FromHex(IsExchange ? "#AAA6C2" : "#A8BFC2");
    private Color Accent => Color.FromHex(IsExchange ? "#B9A0FF" : "#73C8C3");
    private Color Warning => Color.FromHex(IsExchange ? "#FFB6A8" : "#F0A89D");

    private StyleBoxFlat SheetStyle(bool emphasis = false) => new()
    {
        BackgroundColor = Color.FromHex(IsExchange
            ? emphasis ? "#302741" : "#211E30"
            : emphasis ? "#304E54" : "#293E45"),
        BorderColor = emphasis ? Accent : Color.FromHex(IsExchange ? "#453C59" : "#456269"),
        BorderThickness = new Thickness(1),
    };

    // Only permanent controls are registered; frequently rebuilt cards are styled directly.
    private PanelContainer Surface()
    {
        var panel = new PanelContainer { HorizontalExpand = true, PanelOverride = SheetStyle() };
        _surfaces.Add(panel);
        return panel;
    }

    private Label ThemeLabel(string text, bool muted = false)
    {
        var label = new Label { Text = text, FontColorOverride = muted ? Muted : Ink };
        _themeLabels.Add((label, muted));
        return label;
    }

    private string IconPath(string name) =>
        $"/Textures/_DeadSpace/_Soyuz/interface/{(IsExchange ? "MedicalExchange" : "MedicalOrders")}/{name}.png";

    private TextureRect InterfaceIcon(string name, int size = 32, bool themed = false)
    {
        var icon = new TextureRect
        {
            TexturePath = IconPath(name),
            SetSize = new Vector2(size, size),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            MouseFilter = MouseFilterMode.Ignore,
            VerticalAlignment = VAlignment.Center,
        };
        if (themed)
            _themeIcons.Add((icon, name));
        return icon;
    }

    private Control SectionHeading(string icon, string title)
    {
        var row = new BoxContainer { HorizontalExpand = true, SeparationOverride = 8 };
        row.AddChild(InterfaceIcon(icon, themed: true));
        var label = ThemeLabel(Loc.GetString(title));
        label.StyleClasses.Add("LabelHeading");
        label.VerticalAlignment = VAlignment.Center;
        label.ClipText = true;
        label.HorizontalExpand = true;
        label.ToolTip = label.Text;
        row.AddChild(label);
        return row;
    }

    private void InitializePresentation(BoxContainer root)
    {
        var body = new BoxContainer { HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 12 };
        var navigation = Column();
        navigation.HorizontalExpand = false;
        navigation.SetWidth = 120;
        var icons = new[] { "orders", "patient", "quotes", "supplies", "emergency" };
        var titles = new[] { "medical-orders-nav-dispatch", "medical-orders-nav-treatment",
            "medical-orders-nav-exchange", "medical-orders-nav-supply", "medical-orders-nav-emergency" };
        for (var index = 0; index < icons.Length; index++)
        {
            var tab = index;
            var button = new ContainerButton { MinHeight = 74, HorizontalExpand = true };
            var content = Column();
            content.Margin = new Thickness(8);
            content.MouseFilter = MouseFilterMode.Ignore;
            var icon = InterfaceIcon(icons[index], 32, themed: true);
            icon.HorizontalAlignment = HAlignment.Center;
            content.AddChild(icon);
            var label = new Label
            {
                Text = Loc.GetString(titles[index]),
                FontColorOverride = Color.FromHex("#E9F3F2"),
            };
            label.HorizontalAlignment = HAlignment.Center;
            label.MouseFilter = MouseFilterMode.Ignore;
            content.AddChild(label);
            button.AddChild(content);
            button.OnPressed += _ => _tabs.CurrentTab = tab;
            _navigation.Add(button);
            navigation.AddChild(button);
        }
        body.AddChild(navigation);
        body.AddChild(_tabs);
        root.AddChild(body);
        _background = new PanelContainer { HorizontalExpand = true, VerticalExpand = true };
        _background.AddChild(root);
        ContentsContainer.AddChild(_background);
        _tabs.OnTabChanged += _ => UpdateNavigation();
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        _background.PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(IsExchange ? "#14111F" : "#16292F"),
        };
        foreach (var panel in _surfaces)
            panel.PanelOverride = SheetStyle();
        foreach (var (label, muted) in _themeLabels)
            label.FontColorOverride = muted ? Muted : Ink;
        foreach (var (icon, name) in _themeIcons)
            icon.TexturePath = IconPath(name);
        _serviceIcon.TexturePath = IconPath("service");
        _shopProgress.BackgroundStyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(IsExchange ? "#14111F" : "#1B3036"),
        };
        _shopProgress.ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Accent };
        UpdateNavigation();
    }

    private void UpdateNavigation()
    {
        for (var index = 0; index < _navigation.Count; index++)
        {
            var button = _navigation[index];
            button.Visible = _tabs.GetTabVisible(index);
            button.StyleBoxOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex(index == _tabs.CurrentTab
                    ? IsExchange ? "#493369" : "#17636B"
                    : IsExchange ? "#211B30" : "#214B54"),
                BorderColor = Color.FromHex(IsExchange ? "#B9A0FF" : "#7CDBD2"),
                BorderThickness = new Thickness(index == _tabs.CurrentTab ? 3 : 0, 0, 0, 0),
            };
        }
    }

    private StyleBoxFlat MarketRowStyle(bool selected, bool supplied, int index) => new()
    {
        BackgroundColor = Color.FromHex(selected ? "#403353" : supplied
            ? "#35304D" : index % 2 == 0 ? "#252135" : "#2B263D"),
        BorderColor = selected ? Accent : Color.Transparent,
        BorderThickness = new Thickness(1),
    };

    private void SelectReagent(string? id)
    {
        _selectedReagent = id;
        foreach (var (reagent, button, supplied, index) in _marketRows)
            button.StyleBoxOverride = MarketRowStyle(reagent == id, supplied, index);
        // Quotes change every second; keep the recipe and its scroll position while reading it.
        if (_reagentDetailsInitialized && _displayedReagent == id)
            return;
        _reagentDetailsInitialized = true;
        _displayedReagent = id;
        _reagentDetailsScroll.SetScrollValue(Vector2.Zero);
        _reagentDetails.DisposeAllChildren();
        if (id == null || !_prototypes.TryIndex<ReagentPrototype>(id, out var prototype))
        {
            _reagentDetails.AddChild(Info(Loc.GetString("medical-orders-reagent-empty")));
            return;
        }

        _reagentDetails.AddChild(ReagentCard(id, Loc.GetString("medical-orders-reagent-physical",
            ("description", prototype.LocalizedPhysicalDescription)), emphasis: true));
        _reagentDetails.AddChild(WrappedText(prototype.LocalizedDescription));
        var recipes = _prototypes.EnumeratePrototypes<ReactionPrototype>()
            .Where(reaction => reaction.Products.TryGetValue(id, out var amount) && amount > FixedPoint2.Zero)
            .OrderBy(reaction => reaction.Source)
            .ThenBy(reaction => reaction.Priority)
            .ThenBy(reaction => reaction.Products.Count)
            .ThenBy(reaction => reaction.ID, StringComparer.Ordinal)
            .ToArray();
        if (recipes.Length == 0)
            _reagentDetails.AddChild(Info(Loc.GetString("medical-orders-reagent-no-recipe")));
        for (var index = 0; index < recipes.Length; index++)
            _reagentDetails.AddChild(RecipeCard(recipes[index], index + 1));
    }

    private Control RecipeCard(ReactionPrototype reaction, int number)
    {
        var panel = new PanelContainer { HorizontalExpand = true, PanelOverride = SheetStyle() };
        var content = Column();
        content.Margin = new Thickness(8);
        var title = WrappedText(Loc.GetString("medical-orders-recipe-number", ("number", number)));
        title.StyleClasses.Add("LabelHeading");
        content.AddChild(title);
        content.AddChild(Info(Loc.GetString("medical-orders-recipe-inputs")));
        foreach (var (reagent, reactant) in reaction.Reactants.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var amount = Loc.GetString(reactant.Catalyst
                ? "medical-orders-recipe-catalyst" : "medical-orders-recipe-amount", ("amount", reactant.Amount));
            content.AddChild(ReagentCard(reagent, amount));
        }

        var arrow = new Label
        {
            Text = "↓",
            FontColorOverride = Accent,
            HorizontalAlignment = HAlignment.Center,
            StyleClasses = { "LabelHeading" },
        };
        content.AddChild(arrow);
        var methods = new List<string>();
        if (reaction.MixingCategories != null)
        {
            foreach (var category in reaction.MixingCategories)
            {
                if (_prototypes.TryIndex(category, out var mixer))
                    methods.Add(Loc.GetString(mixer.VerbText));
            }
        }
        content.AddChild(Info(methods.Count == 0
            ? Loc.GetString("medical-orders-recipe-mix-passive")
            : Loc.GetString("medical-orders-recipe-mixing", ("methods", string.Join(", ", methods)))));
        if (reaction.MinimumTemperature > 0)
            content.AddChild(Info(Loc.GetString("medical-orders-recipe-temperature-min",
                ("temperature", reaction.MinimumTemperature))));
        if (!float.IsPositiveInfinity(reaction.MaximumTemperature))
            content.AddChild(Info(Loc.GetString("medical-orders-recipe-temperature-max",
                ("temperature", reaction.MaximumTemperature))));
        if (reaction.Quantized)
            content.AddChild(Info(Loc.GetString("medical-orders-recipe-whole-batches")));
        content.AddChild(Info(Loc.GetString("medical-orders-recipe-outputs")));
        foreach (var (reagent, amount) in reaction.Products.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            content.AddChild(ReagentCard(reagent, Loc.GetString("medical-orders-recipe-amount", ("amount", amount)),
                emphasis: reagent == _selectedReagent));
        }
        panel.AddChild(content);
        return panel;
    }

    private Control ReagentCard(string id, string detail, bool emphasis = false)
    {
        var reagent = _prototypes.TryIndex<ReagentPrototype>(id, out var prototype) ? prototype : null;
        var panel = new PanelContainer { HorizontalExpand = true, PanelOverride = SheetStyle(emphasis) };
        var row = new BoxContainer { HorizontalExpand = true, SeparationOverride = 8, Margin = new Thickness(6) };
        row.AddChild(new PanelContainer
        {
            SetWidth = 5,
            PanelOverride = new StyleBoxFlat { BackgroundColor = (reagent?.SubstanceColor ?? Accent).WithAlpha(1f) },
        });
        row.AddChild(InterfaceIcon("beaker"));
        var text = Column();
        text.SeparationOverride = 2;
        text.AddChild(WrappedText(reagent?.LocalizedName ?? id));
        text.AddChild(Info(detail));
        row.AddChild(text);
        panel.AddChild(row);
        return panel;
    }

    private Control CartLine(MedicalOrderShopItemView item, string name, int count)
    {
        var panel = new PanelContainer { HorizontalExpand = true, PanelOverride = SheetStyle() };
        var content = Column();
        content.Margin = new Thickness(8);
        content.AddChild(WrappedText(name));
        var footer = new BoxContainer { HorizontalExpand = true, SeparationOverride = 8 };
        var remove = new Button { Text = "−", Disabled = _pendingPurchase != null };
        var add = new Button { Text = "+", Disabled = count >= item.MaxCount || _pendingPurchase != null };
        remove.OnPressed += _ => ChangeCount(item.ID, -1, item.MaxCount);
        add.OnPressed += _ => ChangeCount(item.ID, 1, item.MaxCount);
        footer.AddChild(remove);
        footer.AddChild(new Label
        {
            Text = count.ToString(),
            FontColorOverride = Ink,
            VerticalAlignment = VAlignment.Center,
        });
        footer.AddChild(add);
        var cost = WrappedText(Loc.GetString("medical-orders-cart-item-total", ("total", (long) item.Cost * count)));
        cost.VerticalAlignment = VAlignment.Center;
        footer.AddChild(cost);
        content.AddChild(footer);
        panel.AddChild(content);
        return panel;
    }
}
