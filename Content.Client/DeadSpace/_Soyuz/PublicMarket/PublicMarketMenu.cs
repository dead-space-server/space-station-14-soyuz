// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace._Soyuz.MaterialMarketplace;
using Content.Shared.DeadSpace._Soyuz.PublicMarket;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.DeadSpace._Soyuz.PublicMarket;

public sealed class PublicMarketMenu : FancyWindow
{
    [Dependency] private readonly IPrototypeManager _prototype = null!;
    [Dependency] private readonly IEntitySystemManager _sysMan = null!;
    [Dependency] private readonly IPlayerManager _player = null!;

    private readonly SpriteSystem _spriteSystem;
    private readonly Button _marketTab;
    private readonly Button _myTab;
    private readonly LineEdit _searchBar;
    private readonly Label _commissionLabel;
    private readonly BoxContainer _lotsList;
    private readonly List<NetEntity> _cart = new();
    private readonly BoxContainer _cartList;
    private readonly Label _totalLabel;
    private readonly Label _cashLabel;
    private readonly Button _ejectCashButton;
    private readonly Button _clearButton;
    private readonly Button _checkoutButton;

    private readonly BoxContainer _paymentBox;
    private readonly Button _payDepartment;
    private readonly Label _payDepartmentInfo;
    private readonly Button _payPersonal;
    private readonly Label _payPersonalInfo;
    private readonly Button _payCash;
    private readonly Label _payCashInfo;

    private PublicMarketState? _state;
    private MaterialMarketplacePaymentOptionsMessage? _options;
    private string _searchFilter = string.Empty;
    private bool _myTabActive;
    private bool _paymentMode;
    private bool _awaitingResult;

    public event Action<List<NetEntity>, MarketplacePayment>? OnBuy;
    public event Action? OnRequestPayment;
    public event Action? OnEjectCash;
    public event Action<NetEntity, int?>? OnSetPrice;
    public event Action<NetEntity>? OnWithdraw;

    public PublicMarketMenu()
    {
        IoCManager.InjectDependencies(this);
        _spriteSystem = _sysMan.GetEntitySystem<SpriteSystem>();

        Title = "Общий рынок";
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
            SeparationOverride = 8,
        };

        var tabGroup = new ButtonGroup();
        var tabsRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        _marketTab = new Button { Text = "Рынок", ToggleMode = true, Group = tabGroup, Pressed = true, HorizontalExpand = true };
        _myTab = new Button { Text = "Мои лоты", ToggleMode = true, Group = tabGroup, HorizontalExpand = true };
        _marketTab.OnPressed += _ =>
        {
            _myTabActive = false;
            UpdateLotsList();
        };
        _myTab.OnPressed += _ =>
        {
            _myTabActive = true;
            UpdateLotsList();
        };
        tabsRow.AddChild(_marketTab);
        tabsRow.AddChild(_myTab);
        leftBox.AddChild(tabsRow);

        _searchBar = new LineEdit { PlaceHolder = "Поиск...", HorizontalExpand = true };
        _searchBar.OnTextChanged += args =>
        {
            _searchFilter = args.Text.Trim().ToLowerInvariant();
            UpdateLotsList();
        };
        leftBox.AddChild(_searchBar);

        _commissionLabel = new Label { FontColorOverride = Color.LightGray };
        leftBox.AddChild(_commissionLabel);

        var scroll = new ScrollContainer
        {
            HScrollEnabled = false,
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        _lotsList = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            HorizontalExpand = true,
            SeparationOverride = 10,
        };
        scroll.AddChild(_lotsList);
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

        var cartScroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
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
            UpdateLotsList();
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

    public void UpdateState(PublicMarketState state)
    {
        _state = state;

        _cart.RemoveAll(id =>
        {
            var lot = FindLot(id);
            return lot == null || lot.Price == null;
        });

        _commissionLabel.Text = $"Комиссия рынка: {state.CommissionPercent}% (удерживается с продавца)";

        UpdateLotsList();
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

    private PublicMarketLotEntry? FindLot(NetEntity id)
    {
        return _state?.Lots.FirstOrDefault(l => l.Lot == id);
    }

    private string? MyUserId()
    {
        return _player.LocalUser?.UserId.ToString();
    }

    private Texture? GetIcon(string prototypeId)
    {
        return _prototype.TryIndex<EntityPrototype>(prototypeId, out var proto)
            ? _spriteSystem.Frame0(proto)
            : null;
    }

    #region Список лотов

    private void UpdateLotsList()
    {
        _lotsList.RemoveAllChildren();
        if (_state == null)
            return;

        var me = MyUserId();
        var lots = _state.Lots
            .Where(l => string.IsNullOrEmpty(_searchFilter) || l.Name.ToLowerInvariant().Contains(_searchFilter))
            .Where(l => _myTabActive ? l.SellerUserId == me : l.Price != null)
            .OrderBy(l => l.Name)
            .ToList();

        if (lots.Count == 0)
        {
            _lotsList.AddChild(new Label
            {
                Text = _myTabActive
                    ? "У вас нет лотов. Вставьте предмет в рынок, чтобы выставить его на продажу."
                    : "На рынке пока ничего нет.",
                FontColorOverride = Color.Gray,
                Margin = new Thickness(8),
            });
            return;
        }

        foreach (var lot in lots)
        {
            _lotsList.AddChild(_myTabActive ? CreateMyLotRow(lot) : CreateMarketRow(lot));
        }
    }

    private PanelContainer CreateRowPanel(Color border)
    {
        return new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = new Color(30, 30, 35),
                BorderColor = border,
                BorderThickness = new Thickness(2),
            },
        };
    }

    private Control CreateMarketRow(PublicMarketLotEntry lot)
    {
        var row = CreateRowPanel(new Color(110, 110, 120));
        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 15,
            HorizontalExpand = true,
            Margin = new Thickness(12),
        };

        content.AddChild(new TextureRect
        {
            MinSize = new Vector2(64, 64),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            Texture = GetIcon(lot.PrototypeId),
        });

        var info = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        var name = new Label { Text = lot.Name, FontColorOverride = Color.White };
        name.StyleClasses.Add("LabelHeading");
        info.AddChild(name);
        info.AddChild(new Label
        {
            Text = $"Продавец: {lot.SellerName} ({lot.DepartmentName})",
            FontColorOverride = Color.LightGray,
        });
        content.AddChild(info);

        var buy = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalAlignment = VAlignment.Center,
            SeparationOverride = 4,
        };
        buy.AddChild(new Label
        {
            Text = $"{lot.Price}¢",
            FontColorOverride = Color.LightGreen,
            HorizontalAlignment = HAlignment.Center,
        });

        var inCart = _cart.Contains(lot.Lot);
        var button = new Button
        {
            Text = inCart ? "В корзине" : "В корзину",
            Disabled = inCart,
            MinWidth = 110,
            MinHeight = 32,
        };
        button.OnPressed += _ =>
        {
            if (!_cart.Contains(lot.Lot))
                _cart.Add(lot.Lot);

            UpdateLotsList();
            RefreshCart();
            RefreshPayment();
        };
        buy.AddChild(button);
        content.AddChild(buy);

        row.AddChild(content);
        return row;
    }

    private Control CreateMyLotRow(PublicMarketLotEntry lot)
    {
        var row = CreateRowPanel(lot.Price == null ? Color.Orange : new Color(110, 110, 120));
        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 15,
            HorizontalExpand = true,
            Margin = new Thickness(12),
        };

        content.AddChild(new TextureRect
        {
            MinSize = new Vector2(64, 64),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            Texture = GetIcon(lot.PrototypeId),
        });

        var info = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        var name = new Label { Text = lot.Name, FontColorOverride = Color.White };
        name.StyleClasses.Add("LabelHeading");
        info.AddChild(name);

        if (lot.Price is { } price)
        {
            var commission = (int) Math.Round(price * (_state?.CommissionPercent ?? 0) / 100.0);
            info.AddChild(new Label { Text = $"Цена: {price}¢", FontColorOverride = Color.LightGreen });
            info.AddChild(new Label
            {
                Text = $"Отделу ({lot.DepartmentName}) достанется: {price - commission}¢",
                FontColorOverride = Color.LightGray,
            });
        }
        else
        {
            info.AddChild(new Label
            {
                Text = "Цена не задана - лот скрыт от покупателей",
                FontColorOverride = Color.Orange,
            });
        }

        content.AddChild(info);

        var controls = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalAlignment = VAlignment.Center,
            SeparationOverride = 4,
        };

        var priceRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
        };
        var edit = new LineEdit
        {
            MinWidth = 90,
            PlaceHolder = "Цена",
            Text = lot.Price?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        };
        var set = new Button { Text = "Задать" };
        set.OnPressed += _ =>
        {
            if (int.TryParse(edit.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                OnSetPrice?.Invoke(lot.Lot, value);
        };
        priceRow.AddChild(edit);
        priceRow.AddChild(set);
        controls.AddChild(priceRow);

        var buttonsRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
        };
        var hide = new Button
        {
            Text = "Скрыть",
            Disabled = lot.Price == null,
            HorizontalExpand = true,
        };
        hide.OnPressed += _ => OnSetPrice?.Invoke(lot.Lot, null);
        var withdraw = new Button { Text = "Забрать", HorizontalExpand = true };
        withdraw.OnPressed += _ => OnWithdraw?.Invoke(lot.Lot);
        buttonsRow.AddChild(hide);
        buttonsRow.AddChild(withdraw);
        controls.AddChild(buttonsRow);

        content.AddChild(controls);
        row.AddChild(content);
        return row;
    }

    #endregion

    #region Корзина

    private int CartTotal()
    {
        var total = 0;
        foreach (var id in _cart)
        {
            if (FindLot(id)?.Price is { } price)
                total += price;
        }

        return total;
    }

    private void RefreshCart()
    {
        _cartList.RemoveAllChildren();

        foreach (var id in _cart)
        {
            var lot = FindLot(id);
            if (lot?.Price == null)
                continue;

            var row = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 4,
            };
            row.AddChild(new Label { Text = lot.Name, HorizontalExpand = true, ClipText = true });
            row.AddChild(new Label
            {
                Text = $"{lot.Price}¢",
                FontColorOverride = Color.LightGreen,
                MinWidth = 60,
                HorizontalAlignment = HAlignment.Right,
            });

            var remove = new Button { Text = "x", MinWidth = 28 };
            var captured = id;
            remove.OnPressed += _ =>
            {
                _cart.Remove(captured);
                UpdateLotsList();
                RefreshCart();
                RefreshPayment();
            };
            row.AddChild(remove);
            _cartList.AddChild(row);
        }

        if (_cart.Count == 0)
        {
            _cartList.AddChild(new Label { Text = "Корзина пуста", FontColorOverride = Color.Gray });

            if (_paymentMode)
                ExitPayment();
        }

        _totalLabel.Text = $"Итого: {CartTotal()}¢";

        var deposited = _state?.DepositedCash ?? 0;
        _cashLabel.Text = $"Внесено наличными: {deposited}¢";
        _ejectCashButton.Disabled = deposited <= 0;

        _clearButton.Disabled = _cart.Count == 0;
        _checkoutButton.Disabled = _cart.Count == 0 || _paymentMode;
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

        var total = CartTotal();
        var canPay = !_awaitingResult && _cart.Count > 0;

        if (_options == null)
        {
            _payDepartment.Disabled = true;
            _payDepartmentInfo.Text = "Загрузка...";
            _payPersonal.Disabled = true;
            _payPersonalInfo.Text = "Загрузка...";
        }
        else
        {
            if (_options.DepartmentName == null || _options.DepartmentBalance == null)
            {
                _payDepartment.Disabled = true;
                _payDepartmentInfo.Text = "Нужен доступ главы отдела";
            }
            else
            {
                _payDepartment.Disabled = !canPay || _options.DepartmentBalance.Value < total;
                _payDepartmentInfo.Text = $"{_options.DepartmentName}: {_options.DepartmentBalance.Value}¢";
            }

            if (_options.PersonalBalance == null)
            {
                _payPersonal.Disabled = true;
                _payPersonalInfo.Text = "Банковский счёт на ID-карте не найден";
            }
            else
            {
                _payPersonal.Disabled = !canPay || _options.PersonalBalance.Value < total;
                _payPersonalInfo.Text = $"На вашем счёте: {_options.PersonalBalance.Value}¢";
            }
        }

        var deposited = _state?.DepositedCash ?? 0;
        _payCash.Disabled = !canPay || deposited < total;
        _payCashInfo.Text = deposited < total
            ? $"Внесено {deposited}¢. Вставьте наличные в рынок"
            : $"Внесено {deposited}¢, сдача: {deposited - total}¢";
    }

    private void Pay(MarketplacePayment payment)
    {
        if (_cart.Count == 0 || _awaitingResult)
            return;

        _awaitingResult = true;
        RefreshPayment();
        OnBuy?.Invoke(_cart.ToList(), payment);
    }

    #endregion
}