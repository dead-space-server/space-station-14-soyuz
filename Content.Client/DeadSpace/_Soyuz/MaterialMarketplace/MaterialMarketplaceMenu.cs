// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace._Soyuz.MaterialMarketplace;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.DeadSpace._Soyuz.MaterialMarketplace;

public sealed class MaterialMarketplaceMenu : FancyWindow
{
    private const string ItemsFilterKey = "__items";

    [Dependency] private readonly IPrototypeManager _prototype = null!;
    [Dependency] private readonly IEntitySystemManager _sysMan = null!;

    private readonly SpriteSystem _spriteSystem;
    private readonly OptionButton _categoryFilter;
    private readonly BoxContainer _materialsList;
    private readonly LineEdit _searchBar;
    private readonly List<MaterialMarketplaceCategoryPrototype> _categories;

    // Корзина
    private readonly List<MarketplaceCartLine> _cart = new();
    private readonly BoxContainer _cartList;
    private readonly Label _totalLabel;
    private readonly Label _cashLabel;
    private readonly Button _ejectCashButton;
    private readonly Button _clearButton;
    private readonly Button _checkoutButton;

    // Выбор способа оплаты
    private readonly BoxContainer _paymentBox;
    private readonly Button _payDepartment;
    private readonly Label _payDepartmentInfo;
    private readonly Button _payPersonal;
    private readonly Label _payPersonalInfo;
    private readonly Button _payCash;
    private readonly Label _payCashInfo;

    private MaterialMarketplaceState? _lastState;
    private MaterialMarketplacePaymentOptionsMessage? _options;
    private string _searchFilter = string.Empty;
    private HashSet<string> _whitelist = new();
    private string? _currentCategory;
    private bool _paymentMode;
    private bool _awaitingResult;

    public event Action<List<MarketplaceCartLine>, MarketplacePayment>? OnCheckout;
    public event Action? OnRequestPayment;
    public event Action? OnEjectCash;

    public MaterialMarketplaceMenu()
    {
        IoCManager.InjectDependencies(this);
        _spriteSystem = _sysMan.GetEntitySystem<SpriteSystem>();

        _categories = _prototype.EnumeratePrototypes<MaterialMarketplaceCategoryPrototype>()
            .OrderBy(c => c.Order)
            .ToList();

        Title = "Магазин материалов";
        SetSize = new Vector2(1150, 800);
        Resizable = false;

        var mainContainer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            VerticalExpand = true,
            HorizontalExpand = true,
            Margin = new Thickness(10, 30, 10, 10),
        };

        var leftBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            HorizontalExpand = true,
        };

        var searchHBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 10,
        };

        _searchBar = new LineEdit
        {
            PlaceHolder = "Поиск...",
            HorizontalExpand = true,
        };
        _searchBar.OnTextChanged += OnSearchChanged;
        searchHBox.AddChild(_searchBar);

        _categoryFilter = new OptionButton { MinWidth = 150 };
        _categoryFilter.AddItem("Все");
        foreach (var cat in _categories)
            _categoryFilter.AddItem(cat.Name);
        _categoryFilter.AddItem("Предметы");

        _categoryFilter.OnItemSelected += args =>
        {
            if (args.Id == 0)
                _currentCategory = null;
            else if (args.Id == _categories.Count + 1)
                _currentCategory = ItemsFilterKey;
            else
                _currentCategory = _categories[args.Id - 1].ID;

            _categoryFilter.SelectId(args.Id);
            UpdateMaterialsList();
        };
        searchHBox.AddChild(_categoryFilter);
        leftBox.AddChild(searchHBox);

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
        leftBox.AddChild(scroll);
        mainContainer.AddChild(leftBox);

        var cartPanel = new PanelContainer
        {
            MinWidth = 340,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = new Color(25, 25, 30) },
        };

        var cartBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            HorizontalExpand = true,
            SeparationOverride = 8,
            Margin = new Thickness(10),
        };

        var cartTitle = new Label { Text = "КОРЗИНА", FontColorOverride = Color.White };
        cartTitle.StyleClasses.Add("LabelHeading");
        cartBox.AddChild(cartTitle);

        var cartScroll = new ScrollContainer
        {
            HScrollEnabled = false,
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        _cartList = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        cartScroll.AddChild(_cartList);
        cartBox.AddChild(cartScroll);

        _totalLabel = new Label { FontColorOverride = Color.LightGreen };
        cartBox.AddChild(_totalLabel);

        var cashRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        _cashLabel = new Label { HorizontalExpand = true, FontColorOverride = Color.LightGray };
        _ejectCashButton = new Button { Text = "Вернуть" };
        _ejectCashButton.OnPressed += _ => OnEjectCash?.Invoke();
        cashRow.AddChild(_cashLabel);
        cashRow.AddChild(_ejectCashButton);
        cartBox.AddChild(cashRow);

        var buttonsRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        _clearButton = new Button { Text = "Очистить", HorizontalExpand = true };
        _clearButton.OnPressed += _ =>
        {
            _cart.Clear();
            ExitPayment();
            RefreshCart();
        };
        _checkoutButton = new Button { Text = "Оформить", HorizontalExpand = true };
        _checkoutButton.OnPressed += _ => EnterPayment();
        buttonsRow.AddChild(_clearButton);
        buttonsRow.AddChild(_checkoutButton);
        cartBox.AddChild(buttonsRow);

        _paymentBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6,
            Visible = false,
        };
        var payTitle = new Label { Text = "Способ оплаты", FontColorOverride = Color.White };
        payTitle.StyleClasses.Add("LabelHeading");
        _paymentBox.AddChild(payTitle);

        _payDepartment = CreatePaymentOption("Оплатить за счёт отдела", MarketplacePayment.Department, out _payDepartmentInfo);
        _payPersonal = CreatePaymentOption("Оплатить со своего счёта", MarketplacePayment.Personal, out _payPersonalInfo);
        _payCash = CreatePaymentOption("Оплатить наличкой", MarketplacePayment.Cash, out _payCashInfo);

        var backButton = new Button { Text = "Назад" };
        backButton.OnPressed += _ => ExitPayment();
        _paymentBox.AddChild(backButton);
        cartBox.AddChild(_paymentBox);

        cartPanel.AddChild(cartBox);
        mainContainer.AddChild(cartPanel);

        AddChild(mainContainer);
        RefreshCart();
    }

    public void SetWhitelist(IEnumerable<string> whitelist)
    {
        _whitelist = new HashSet<string>(whitelist);
    }

    public void UpdateState(MaterialMarketplaceState state)
    {
        _lastState = state;

        for (var i = _cart.Count - 1; i >= 0; i--)
        {
            var line = _cart[i];
            if (!TryGetLineInfo(line, out _, out _, out var stock) || stock <= 0)
            {
                _cart.RemoveAt(i);
                continue;
            }

            if (line.Amount > stock)
                _cart[i] = new MarketplaceCartLine(line.Kind, line.Id, stock);
        }

        UpdateMaterialsList();
        RefreshCart();
        RefreshPayment();
    }

    public void SetPaymentOptions(MaterialMarketplacePaymentOptionsMessage options)
    {
        _options = options;
        RefreshPayment();
    }

    public void OnCheckoutResult(bool success)
    {
        _awaitingResult = false;

        if (success)
        {
            _cart.Clear();
            ExitPayment();
        }

        RefreshCart();
        RefreshPayment();
    }

    private void OnSearchChanged(LineEdit.LineEditEventArgs args)
    {
        _searchFilter = args.Text.Trim().ToLowerInvariant();
        UpdateMaterialsList();
    }

    private bool MatchesSearch(string name)
    {
        return string.IsNullOrEmpty(_searchFilter) || name.ToLowerInvariant().Contains(_searchFilter);
    }

    #region Список товаров

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

        List<(MaterialPrototype proto, int logicalCount, double price)> TakeMaterials(IEnumerable<string> ids)
        {
            var list = new List<(MaterialPrototype proto, int logicalCount, double price)>();
            foreach (var id in ids)
            {
                if (!materialsDict.Remove(id, out var data))
                    continue;

                if (MatchesSearch(Loc.GetString(GetDisplayNameKey(data.proto))))
                    list.Add(data);
            }

            return list;
        }

        void AddMaterialSection(string title, List<(MaterialPrototype proto, int logicalCount, double price)> mats)
        {
            if (mats.Count == 0)
                return;

            _materialsList.AddChild(CreateCategoryLabel(title));
            foreach (var (proto, logicalCount, price) in mats)
            {
                _materialsList.AddChild(CreateMaterialCard(proto, logicalCount, price));
            }
        }

        if (_currentCategory == ItemsFilterKey)
        {
            AddItemsSection();
            return;
        }

        if (_currentCategory != null)
        {
            var selectedCategory = _categories.FirstOrDefault(c => c.ID == _currentCategory);
            if (selectedCategory != null)
                AddMaterialSection(selectedCategory.Name, TakeMaterials(selectedCategory.Materials));

            return;
        }

        foreach (var category in _categories.Where(c => c.ID != "Other"))
        {
            AddMaterialSection(category.Name, TakeMaterials(category.Materials));
        }

        var otherCat = _categories.FirstOrDefault(c => c.ID == "Other");
        if (otherCat != null && materialsDict.Count > 0)
        {
            var others = materialsDict.Values
                .Where(m => MatchesSearch(Loc.GetString(GetDisplayNameKey(m.proto))))
                .ToList();
            AddMaterialSection(otherCat.Name, others);
        }

        AddItemsSection();
    }

    private void AddItemsSection()
    {
        if (_lastState == null)
            return;

        var items = _lastState.Items
            .Where(i => MatchesSearch(i.Name))
            .OrderBy(i => i.Name)
            .ToList();

        if (items.Count == 0)
            return;

        _materialsList.AddChild(CreateCategoryLabel("Предметы"));
        foreach (var item in items)
        {
            _materialsList.AddChild(CreateItemCard(item));
        }
    }

    private string GetDisplayNameKey(MaterialPrototype proto)
    {
        if (!string.IsNullOrEmpty(proto.StackEntity) &&
            _prototype.TryIndex<EntityPrototype>(proto.StackEntity, out var stackEntity))
            return stackEntity.Name;
        return proto.Name;
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

        var displayName = Loc.GetString(material.Name);

        var countName = stackProto != null
            ? Loc.GetString(stackProto.Name)
            : Loc.GetString(material.Name);

        string amountLocalized;
        if (!string.IsNullOrEmpty(material.StackEntity) && stackProto != null)
        {
            var key = stackProto.ID.ToLowerInvariant();
            amountLocalized = Loc.GetString(key, ("amount", logicalCount));

            if (string.IsNullOrWhiteSpace(amountLocalized) || amountLocalized == key)
                amountLocalized = $"{logicalCount} {countName}";
        }
        else
        {
            amountLocalized = $"{logicalCount} {countName}";
        }

        return CreateCard(
            MarketplaceLineKind.Material,
            material.ID,
            displayName,
            $"В наличии: {amountLocalized}",
            material.Color,
            _spriteSystem.Frame0(material.Icon),
            price,
            logicalCount);
    }

    private Control CreateItemCard(MarketplaceItemEntry item)
    {
        Texture? texture = null;
        if (_prototype.TryIndex<EntityPrototype>(item.PrototypeId, out var proto))
            texture = _spriteSystem.Frame0(proto);

        return CreateCard(
            MarketplaceLineKind.Item,
            item.PrototypeId,
            item.Name,
            $"В наличии: {item.Count} шт.",
            new Color(110, 110, 120),
            texture,
            item.Price,
            item.Count);
    }

    private Control CreateCard(
        MarketplaceLineKind kind,
        string id,
        string displayName,
        string amountText,
        Color borderColor,
        Texture? texture,
        double? price,
        int stock)
    {
        var row = new PanelContainer
        {
            Margin = new Thickness(0, 0, 0, 8),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = new Color(30, 30, 35),
                BorderColor = borderColor,
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

        content.AddChild(new TextureRect
        {
            MinSize = new Vector2(80, 80),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            Texture = texture,
            Margin = new Thickness(0, 0, 15, 0),
        });

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

        if (price == null)
        {
            content.AddChild(new Label
            {
                Text = "Стоимость предмета не выставлена",
                FontColorOverride = Color.Orange,
                VerticalAlignment = VAlignment.Center,
            });

            row.AddChild(content);
            return row;
        }

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

        foreach (var addAmount in new[] { 1, 5, 10, 30 })
        {
            var totalPrice = price.Value * addAmount;
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
                Text = $"+{addAmount}",
                Disabled = stock <= 0,
                MinWidth = 50,
                MinHeight = 35,
            };
            btn.StyleClasses.Add("ButtonGray");
            btn.OnPressed += _ => AddToCart(kind, id, addAmount, stock);
            buttonContainer.AddChild(btn);

            buttonsBox.AddChild(buttonContainer);
        }

        buttonsContainer.AddChild(buttonsBox);
        content.AddChild(buttonsContainer);

        row.AddChild(content);
        return row;
    }

    #endregion

    #region Корзина

    private bool TryGetLineInfo(MarketplaceCartLine line, out string name, out double price, out int stock)
    {
        name = line.Id;
        price = 0;
        stock = 0;

        if (_lastState == null)
            return false;

        if (line.Kind == MarketplaceLineKind.Material)
        {
            if (!_lastState.AvailableMaterials.TryGetValue(line.Id, out stock))
                return false;

            price = _lastState.Prices.GetValueOrDefault(line.Id, 0);
            if (_prototype.TryIndex<MaterialPrototype>(line.Id, out var material))
                name = Loc.GetString(material.Name);

            return true;
        }

        var item = _lastState.Items.FirstOrDefault(i => i.PrototypeId == line.Id);
        if (item == null || item.Price == null)
            return false;

        name = item.Name;
        price = item.Price.Value;
        stock = item.Count;
        return true;
    }

    private void AddToCart(MarketplaceLineKind kind, string id, int amount, int stock)
    {
        var index = _cart.FindIndex(l => l.Kind == kind && l.Id == id);
        var current = index >= 0 ? _cart[index].Amount : 0;
        var newAmount = Math.Min(stock, current + amount);

        if (newAmount <= current)
            return;

        SetCartAmount(kind, id, newAmount);
    }

    private void SetCartAmount(MarketplaceLineKind kind, string id, int amount)
    {
        var index = _cart.FindIndex(l => l.Kind == kind && l.Id == id);

        if (amount <= 0)
        {
            if (index >= 0)
                _cart.RemoveAt(index);
        }
        else if (index >= 0)
        {
            _cart[index] = new MarketplaceCartLine(kind, id, amount);
        }
        else
        {
            _cart.Add(new MarketplaceCartLine(kind, id, amount));
        }

        RefreshCart();
        RefreshPayment();
    }

    private int CartTotal(out bool hasUnavailable)
    {
        hasUnavailable = false;
        var total = 0;

        foreach (var line in _cart)
        {
            if (!TryGetLineInfo(line, out _, out var price, out _))
            {
                hasUnavailable = true;
                continue;
            }

            total += (int)Math.Round(price * line.Amount);
        }

        return total;
    }

    private void RefreshCart()
    {
        _cartList.RemoveAllChildren();

        foreach (var line in _cart)
        {
            if (!TryGetLineInfo(line, out var name, out var price, out var stock))
                continue;

            _cartList.AddChild(CreateCartRow(line, name, price, stock));
        }

        if (_cart.Count == 0)
        {
            _cartList.AddChild(new Label
            {
                Text = "Корзина пуста",
                FontColorOverride = Color.Gray,
            });

            if (_paymentMode)
                ExitPayment();
        }

        var total = CartTotal(out _);
        _totalLabel.Text = $"Итого: {total}¢";

        var deposited = _lastState?.DepositedCash ?? 0;
        _cashLabel.Text = $"Внесено наличными: {deposited}¢";
        _ejectCashButton.Disabled = deposited <= 0;

        _clearButton.Disabled = _cart.Count == 0;
        _checkoutButton.Disabled = _cart.Count == 0 || _paymentMode;
    }

    private Control CreateCartRow(MarketplaceCartLine line, string name, double price, int stock)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };

        row.AddChild(new Label
        {
            Text = name,
            HorizontalExpand = true,
            ClipText = true,
        });

        var minus = new Button { Text = "-", MinWidth = 28 };
        minus.OnPressed += _ => SetCartAmount(line.Kind, line.Id, line.Amount - 1);
        row.AddChild(minus);

        row.AddChild(new Label
        {
            Text = line.Amount.ToString(),
            MinWidth = 28,
            HorizontalAlignment = HAlignment.Center,
        });

        var plus = new Button { Text = "+", MinWidth = 28, Disabled = line.Amount >= stock };
        plus.OnPressed += _ => SetCartAmount(line.Kind, line.Id, Math.Min(stock, line.Amount + 1));
        row.AddChild(plus);

        row.AddChild(new Label
        {
            Text = $"{Math.Round(price * line.Amount):F0}¢",
            FontColorOverride = Color.LightGreen,
            MinWidth = 60,
            HorizontalAlignment = HAlignment.Right,
        });

        var remove = new Button { Text = "x", MinWidth = 28 };
        remove.OnPressed += _ => SetCartAmount(line.Kind, line.Id, 0);
        row.AddChild(remove);

        return row;
    }

    #endregion

    #region Оплата

    private Button CreatePaymentOption(string text, MarketplacePayment payment, out Label info)
    {
        var button = new Button { Text = text, HorizontalExpand = true, MinHeight = 32, Disabled = true };
        button.OnPressed += _ => Pay(payment);

        info = new Label
        {
            FontColorOverride = Color.LightGray,
            Margin = new Thickness(4, 0, 0, 4),
        };

        _paymentBox.AddChild(button);
        _paymentBox.AddChild(info);
        return button;
    }

    private void EnterPayment()
    {
        if (_cart.Count == 0)
            return;

        _paymentMode = true;
        _options = null;
        _paymentBox.Visible = true;
        _checkoutButton.Disabled = true;

        OnRequestPayment?.Invoke();
        RefreshPayment();
    }

    private void ExitPayment()
    {
        _paymentMode = false;
        _awaitingResult = false;
        _paymentBox.Visible = false;
        _checkoutButton.Disabled = _cart.Count == 0;
    }

    private void RefreshPayment()
    {
        if (!_paymentMode)
            return;

        var total = CartTotal(out var hasUnavailable);
        var canPay = !_awaitingResult && !hasUnavailable && _cart.Count > 0;

        if (_options == null)
        {
            _payDepartment.Disabled = true;
            _payDepartmentInfo.Text = "Загрузка...";
        }
        else if (_options.DepartmentName == null || _options.DepartmentBalance == null)
        {
            _payDepartment.Disabled = true;
            _payDepartmentInfo.Text = "Нужен доступ главы отдела";
        }
        else
        {
            _payDepartment.Disabled = !canPay || _options.DepartmentBalance.Value < total;
            _payDepartmentInfo.Text = $"{_options.DepartmentName}: {_options.DepartmentBalance.Value}¢";
        }

        if (_options == null)
        {
            _payPersonal.Disabled = true;
            _payPersonalInfo.Text = "Загрузка...";
        }
        else if (_options.PersonalBalance == null)
        {
            _payPersonal.Disabled = true;
            _payPersonalInfo.Text = "Банковский счёт на ID-карте не найден";
        }
        else
        {
            _payPersonal.Disabled = !canPay || _options.PersonalBalance.Value < total;
            _payPersonalInfo.Text = $"На вашем счёте: {_options.PersonalBalance.Value}¢";
        }

        var deposited = _lastState?.DepositedCash ?? 0;
        _payCash.Disabled = !canPay || deposited < total;
        _payCashInfo.Text = deposited < total
            ? $"Внесено {deposited}¢. Вставьте наличные в торгомат"
            : $"Внесено {deposited}¢, сдача: {deposited - total}¢";
    }

    private void Pay(MarketplacePayment payment)
    {
        if (_cart.Count == 0 || _awaitingResult)
            return;

        _awaitingResult = true;
        RefreshPayment();
        OnCheckout?.Invoke(_cart.ToList(), payment);
    }

    #endregion
}
