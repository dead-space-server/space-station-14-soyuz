// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DeadSpace._Soyuz.MedicalOrders;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.DeadSpace._Soyuz.MedicalOrders;

public sealed class MedicalOrderBoundUserInterface : BoundUserInterface
{
    private MedicalOrderWindow? _window;

    public MedicalOrderBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();
        _window = new MedicalOrderWindow();
        _window.OnRequest += request => SendMessage(request);
        _window.OnBeakerSlot += () => SendMessage(new ItemSlotButtonPressedEvent("beakerSlot"));
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is MedicalOrderUiState medical)
            _window?.UpdateState(medical);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is MedicalOrderResultMessage result)
            _window?.ShowResult(result);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _window?.Close();
            _window?.Dispose();
        }
    }
}

public sealed class MedicalOrderWindow : FancyWindow
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private readonly Label _header;
    private readonly Label _heading;
    private readonly Label _shopProgressLabel;
    private readonly ProgressBar _shopProgress;
    private readonly Label _timer;
    private readonly Label _notice;
    private readonly Label _cartSummary;
    private readonly BoxContainer _cartLines;
    private readonly Button _checkout;
    private readonly BoxContainer _offers;
    private readonly BoxContainer _active;
    private readonly BoxContainer _completed;
    private readonly BoxContainer _shop;
    private readonly TabContainer _tabs;
    private readonly BoxContainer _offersPage;
    private readonly BoxContainer _activePage;
    private readonly BoxContainer _shopPage;
    private readonly BoxContainer _marketPage;
    private readonly BoxContainer _market;
    private readonly LineEdit _marketSearch;
    private readonly Label _marketBeaker;
    private readonly Label _marketSummary;
    private readonly Label _marketRejected;
    private readonly Label _marketLast;
    private readonly Button _marketBeakerButton;
    private readonly Button _marketSell;
    private readonly EntityPrototypeView _machineIcon;
    private readonly Dictionary<string, int> _cart = new();
    private MedicalOrderUiState? _state;
    private string? _pendingPurchase;

    public event Action<MedicalOrderRequestMessage>? OnRequest;
    public event Action? OnBeakerSlot;

    public MedicalOrderWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("medical-orders-window-title");
        MinSize = new Vector2(680, 600);
        SetSize = new Vector2(720, 700);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 8,
            Margin = new Thickness(10),
        };

        var overview = new PanelContainer { HorizontalExpand = true };
        var overviewRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(10, 8),
            SeparationOverride = 12,
        };
        _machineIcon = new EntityPrototypeView
        {
            SetSize = new Vector2(48, 48),
            Stretch = SpriteView.StretchMode.Fit,
        };
        overviewRow.AddChild(_machineIcon);
        var overviewText = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 3,
        };
        _heading = new Label
        {
            Text = Loc.GetString("medical-orders-window-title"),
            StyleClasses = { "LabelHeading" },
        };
        overviewText.AddChild(_heading);
        _header = new Label { FontColorOverride = Color.LightGray };
        overviewText.AddChild(_header);
        overviewRow.AddChild(overviewText);
        _timer = new Label
        {
            VerticalAlignment = VAlignment.Center,
            FontColorOverride = Color.LightBlue,
        };
        overviewText.AddChild(_timer);
        overview.AddChild(overviewRow);
        root.AddChild(overview);

        _notice = new Label { Visible = false, Margin = new Thickness(4, 0) };
        root.AddChild(_notice);

        _tabs = new TabContainer { HorizontalExpand = true, VerticalExpand = true };
        _offersPage = Page(_tabs, "medical-orders-offers");
        var offersScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
        };
        var offersContent = Column();
        _offers = Section(offersContent, "medical-orders-offers");
        offersScroll.AddChild(offersContent);
        _offersPage.AddChild(offersScroll);

        _activePage = Page(_tabs, "medical-orders-active");
        var activeScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
        };
        var activeContent = Column();
        _active = Section(activeContent, "medical-orders-active");
        _completed = Section(activeContent, "medical-orders-completed");
        activeScroll.AddChild(activeContent);
        _activePage.AddChild(activeScroll);

        _shopPage = Page(_tabs, "medical-orders-shop");
        var progressPanel = new PanelContainer { HorizontalExpand = true };
        var progressContent = Column();
        progressContent.Margin = new Thickness(10, 8);
        progressContent.AddChild(new Label
        {
            Text = Loc.GetString("medical-orders-shop-level-heading"),
            StyleClasses = { "LabelHeading" },
        });
        _shopProgressLabel = new Label { FontColorOverride = Color.LightGray };
        progressContent.AddChild(_shopProgressLabel);
        _shopProgress = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            MinHeight = 14,
            HorizontalExpand = true,
        };
        progressContent.AddChild(_shopProgress);
        progressPanel.AddChild(progressContent);
        _shopPage.AddChild(progressPanel);

        var shopScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
        };
        var shopContent = Column();
        _shop = Section(shopContent, "medical-orders-catalog-heading");
        shopScroll.AddChild(shopContent);
        _shopPage.AddChild(shopScroll);

        var cartPanel = new PanelContainer { HorizontalExpand = true };
        var cartContent = Column();
        cartContent.Margin = new Thickness(10, 8);
        cartContent.AddChild(new Label
        {
            Text = Loc.GetString("medical-orders-cart-heading"),
            StyleClasses = { "LabelHeading" },
        });
        _cartLines = Column();
        var cartScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            HScrollEnabled = false,
            MaxHeight = 120,
        };
        cartScroll.AddChild(_cartLines);
        cartContent.AddChild(cartScroll);
        _cartSummary = new Label { FontColorOverride = Color.LightBlue };
        cartContent.AddChild(_cartSummary);
        _checkout = new Button { Text = Loc.GetString("medical-orders-checkout"), HorizontalExpand = true };
        _checkout.OnPressed += _ => SubmitCart();
        cartContent.AddChild(_checkout);
        cartPanel.AddChild(cartContent);
        _shopPage.AddChild(cartPanel);

        _marketPage = Page(_tabs, "medical-orders-market-title");
        _marketPage.SetPositionInParent(2);
        _marketSearch = new LineEdit
        {
            PlaceHolder = Loc.GetString("medical-orders-market-search"),
            HorizontalExpand = true,
        };
        _marketPage.AddChild(_marketSearch);
        var marketPanel = new PanelContainer { HorizontalExpand = true, VerticalExpand = true };
        var marketContent = Column();
        marketContent.Margin = new Thickness(8);
        marketContent.AddChild(MarketRow(new[] { "reagent", "price", "reputation", "volume" }
            .Select(heading => Loc.GetString($"medical-orders-market-column-{heading}")).ToArray(), true));
        var marketScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
        };
        _market = Column();
        _market.SeparationOverride = 2;
        marketScroll.AddChild(_market);
        marketContent.AddChild(marketScroll);
        marketPanel.AddChild(marketContent);
        _marketPage.AddChild(marketPanel);
        var salePanel = new PanelContainer { HorizontalExpand = true };
        var saleContent = Column();
        saleContent.Margin = new Thickness(10, 8);
        var beakerRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 8,
        };
        _marketBeaker = Info(string.Empty);
        _marketBeaker.ClipText = true;
        _marketBeaker.VerticalAlignment = VAlignment.Center;
        beakerRow.AddChild(_marketBeaker);
        _marketBeakerButton = new Button();
        _marketBeakerButton.OnPressed += _ => OnBeakerSlot?.Invoke();
        beakerRow.AddChild(_marketBeakerButton);
        saleContent.AddChild(beakerRow);
        _marketSummary = new Label { FontColorOverride = Color.LightBlue };
        saleContent.AddChild(_marketSummary);
        _marketRejected = Info(string.Empty);
        saleContent.AddChild(_marketRejected);
        _marketLast = Info(string.Empty);
        saleContent.AddChild(_marketLast);
        _marketSell = new Button
        {
            Text = Loc.GetString("medical-orders-market-sell"),
            HorizontalExpand = true,
        };
        _marketSell.OnPressed += _ => OnRequest?.Invoke(
            new MedicalOrderRequestMessage(MedicalOrderAction.SellReagents));
        saleContent.AddChild(_marketSell);
        salePanel.AddChild(saleContent);
        _marketPage.AddChild(salePanel);
        _marketSearch.OnTextChanged += _ => RebuildMarket();

        root.AddChild(_tabs);
        ContentsContainer.AddChild(root);
    }

    private static BoxContainer Page(TabContainer tabs, string title)
    {
        var page = Column();
        page.VerticalExpand = true;
        TabContainer.SetTabTitle(page, Loc.GetString(title));
        tabs.AddChild(page);
        return page;
    }

    private static BoxContainer Column() => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        HorizontalExpand = true,
        SeparationOverride = 7,
    };

    private static BoxContainer Section(BoxContainer parent, string title)
    {
        var panel = new PanelContainer { HorizontalExpand = true, Margin = new Thickness(2) };
        var content = Column();
        content.Margin = new Thickness(10, 8);
        content.AddChild(new Label
        {
            Text = Loc.GetString(title),
            StyleClasses = { "LabelHeading" },
        });
        var body = Column();
        content.AddChild(body);
        panel.AddChild(content);
        parent.AddChild(panel);
        return body;
    }

    public void UpdateState(MedicalOrderUiState state)
    {
        var previous = _state;
        var firstState = previous == null;
        if (_state != null && _state.Kind != state.Kind)
            _cart.Clear();
        _state = state;
        Title = Loc.GetString(state.Kind == MedicalOrderMachineKind.Reagent
            ? "medical-orders-market-window-title" : "medical-orders-window-title");
        _heading.Text = Title;
        TabContainer.SetTabVisible(_offersPage, state.Kind == MedicalOrderMachineKind.PatientReceiver);
        TabContainer.SetTabVisible(_activePage, state.Kind != MedicalOrderMachineKind.Reagent);
        TabContainer.SetTabVisible(_shopPage, state.Kind != MedicalOrderMachineKind.PatientReceiver);
        TabContainer.SetTabVisible(_marketPage, state.Kind == MedicalOrderMachineKind.Reagent);
        if (firstState)
            _tabs.CurrentTab = state.Kind switch
            {
                MedicalOrderMachineKind.Reagent => 2,
                MedicalOrderMachineKind.PatientSender => 1,
                _ => 0,
            };
        _machineIcon.SetPrototype(state.Kind switch
        {
            MedicalOrderMachineKind.Reagent => "SoyuzMedicalReagentOrderMachine",
            MedicalOrderMachineKind.PatientReceiver => "SoyuzMedicalPatientReceiver",
            _ => "SoyuzMedicalPatientSender",
        });
        _header.Text = Loc.GetString("medical-orders-economy",
            ("points", state.Points), ("reputation", state.Reputation),
            ("level", state.ShopLevel));
        if (state.NextShopLevelThreshold is { } next)
        {
            _shopProgress.Visible = true;
            _shopProgress.Value = next <= 0 ? 0f : Math.Clamp((float) state.Reputation / next, 0f, 1f);
            _shopProgressLabel.Text = Loc.GetString("medical-orders-next-level",
                ("reputation", state.Reputation), ("required", next));
        }
        else
        {
            _shopProgress.Visible = true;
            _shopProgress.Value = 1f;
            _shopProgressLabel.Text = Loc.GetString("medical-orders-max-level");
        }

        _offers.RemoveAllChildren();
        _active.RemoveAllChildren();
        _completed.RemoveAllChildren();

        if (state.Kind == MedicalOrderMachineKind.PatientSender)
            _active.AddChild(Info(Loc.GetString("medical-orders-sender-instructions")));
        else if (state.Offers.Length == 0)
            _offers.AddChild(Info(Loc.GetString("medical-orders-none")));
        else
        {
            foreach (var offer in state.Offers)
            {
                var content = OrderCard(_offers, offer, state.Kind);
                var id = offer.RuntimeId;
                var button = new Button
                {
                    Text = Loc.GetString("medical-orders-accept"),
                    Disabled = state.Active != null,
                    HorizontalExpand = true,
                };
                button.OnPressed += _ => OnRequest?.Invoke(new MedicalOrderRequestMessage(MedicalOrderAction.Accept, id));
                content.AddChild(button);
            }
        }

        if (state.Active is { } active)
        {
            var content = OrderCard(_active, active, state.Kind);
            content.AddChild(new Label
            {
                Text = Loc.GetString("medical-orders-score", ("score", active.CurrentScore),
                    ("max", active.MaximumScore)),
                FontColorOverride = Color.LightBlue,
            });

            if (state.Kind != MedicalOrderMachineKind.Reagent &&
                state.PatientInitialDamage is { } initial && state.PatientCurrentDamage is { } current)
            {
                content.AddChild(new Label
                {
                    Text = Loc.GetString("medical-orders-patient-health", ("initial", initial),
                        ("current", current), ("threshold", state.PatientCompletionThreshold)),
                });
                content.AddChild(new ProgressBar
                {
                    MinValue = 0f,
                    MaxValue = 1f,
                    Value = initial <= 0 ? 0f : Math.Clamp(1f - current / initial, 0f, 1f),
                    HorizontalExpand = true,
                    MinHeight = 12f,
                });
                if (state.Kind == MedicalOrderMachineKind.PatientSender)
                {
                    content.AddChild(new Label
                    {
                        Text = Loc.GetString(state.PatientInserted
                            ? "medical-orders-patient-inserted" : "medical-orders-patient-not-inserted"),
                        FontColorOverride = state.PatientInserted ? Color.LightGreen : Color.Orange,
                    });
                    content.AddChild(new Label
                    {
                        Text = Loc.GetString("medical-orders-patient-state",
                            ("alive", Loc.GetString(state.PatientAlive
                                ? "medical-orders-yes" : "medical-orders-no")),
                            ("critical", Loc.GetString(state.PatientCritical
                                ? "medical-orders-yes" : "medical-orders-no"))),
                    });
                }
            }

            if (state.Kind != MedicalOrderMachineKind.PatientReceiver)
            {
                var complete = new Button
                {
                    Text = Loc.GetString("medical-orders-complete"),
                    HorizontalExpand = true,
                };
                complete.OnPressed += _ => OnRequest?.Invoke(
                    new MedicalOrderRequestMessage(MedicalOrderAction.Complete, active.RuntimeId));
                content.AddChild(complete);
            }
            else
                content.AddChild(Info(Loc.GetString("medical-orders-receiver-instructions")));
        }
        else
            _active.AddChild(Info(Loc.GetString("medical-orders-none")));

        if (state.LastCompleted is { } completed)
            _completed.AddChild(Info(
                Loc.GetString("medical-orders-last-result", ("id", completed.RuntimeId),
                    ("score", completed.CurrentScore), ("points", state.LastAwardedPoints),
                    ("reputation", state.LastAwardedReputation),
                    ("status", Loc.GetString(state.LastPatientLost
                        ? "medical-orders-patient-lost"
                        : state.LastExpired ? "medical-orders-expired" : "medical-orders-complete-status")))));
        else
            _completed.AddChild(Info(Loc.GetString("medical-orders-none")));

        RebuildMarket();
        // Market quotes refresh every second; keep unchanged shop buttons and the cart in place.
        if (previous == null || previous.Points != state.Points || previous.ShopLevel != state.ShopLevel ||
            !previous.Shop.Select(i => (i.ID, i.Entity, i.Cost, i.MaxCount, i.MinimumShopLevel, i.Classified))
                .SequenceEqual(state.Shop.Select(i =>
                    (i.ID, i.Entity, i.Cost, i.MaxCount, i.MinimumShopLevel, i.Classified))))
            RebuildShop();
        UpdateTimer();
    }

    private static Label Info(string value) => new()
    {
        Text = value,
        HorizontalExpand = true,
        FontColorOverride = Color.LightGray,
    };

    private BoxContainer OrderCard(BoxContainer parent, MedicalOrderView order, MedicalOrderMachineKind kind)
    {
        var panel = new PanelContainer { HorizontalExpand = true, Margin = new Thickness(2) };
        var content = Column();
        content.Margin = new Thickness(8);
        var heading = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };
        var icon = new EntityPrototypeView
        {
            SetSize = new Vector2(36, 36),
            Stretch = SpriteView.StretchMode.Fit,
        };
        icon.SetPrototype(kind == MedicalOrderMachineKind.Reagent
            ? "SoyuzMedicalReagentOrderMachine" : "SoyuzMedicalPatientReceiver");
        heading.AddChild(icon);
        heading.AddChild(new Label
        {
            Text = Loc.GetString("medical-orders-order-number", ("id", order.RuntimeId)),
            StyleClasses = { "LabelHeading" },
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
        });
        content.AddChild(heading);
        content.AddChild(Info(OrderTitle(order)));
        foreach (var line in order.Lines)
        {
            content.AddChild(new Label
            {
                Text = LineText(line, kind),
                HorizontalExpand = true,
            });
            if (order.Deadline != null && kind == MedicalOrderMachineKind.Reagent)
                content.AddChild(new ProgressBar
                {
                    MinValue = 0f,
                    MaxValue = 1f,
                    Value = line.Required <= 0 ? 0f : Math.Clamp(line.Submitted / line.Required, 0f, 1f),
                    HorizontalExpand = true,
                    MinHeight = 10f,
                });
        }
        panel.AddChild(content);
        parent.AddChild(panel);
        return content;
    }

    private string OrderTitle(MedicalOrderView order) => Loc.GetString("medical-orders-order-title",
        ("difficulty", order.Difficulty + 1),
        ("score", order.MaximumScore), ("minutes", (int) order.TimeLimit.TotalMinutes),
        ("reputation", order.ReputationReward));

    private string LineText(MedicalOrderLineView line, MedicalOrderMachineKind kind)
    {
        var name = line.ID;
        if (kind == MedicalOrderMachineKind.Reagent &&
            _prototypes.TryIndex<ReagentPrototype>(line.ID, out var reagent))
            name = reagent.LocalizedName;
        else if (kind != MedicalOrderMachineKind.Reagent &&
                 _prototypes.TryIndex<DamageTypePrototype>(line.ID, out var damage))
            name = damage.LocalizedName;
        if (kind != MedicalOrderMachineKind.Reagent)
            return Loc.GetString("medical-orders-patient-line", ("name", name),
                ("amount", line.Required));
        return Loc.GetString("medical-orders-line", ("name", name),
            ("submitted", line.Submitted), ("required", line.Required),
            ("remaining", Math.Max(0, line.Required - line.Submitted)));
    }

    private void RebuildMarket()
    {
        if (_state == null || _state.Kind != MedicalOrderMachineKind.Reagent)
            return;

        _market.RemoveAllChildren();
        var index = 0;
        foreach (var entry in _state.Market)
        {
            var name = _prototypes.TryIndex<ReagentPrototype>(entry.Reagent, out var reagent)
                ? reagent.LocalizedName : entry.Reagent;
            if (!name.Contains(_marketSearch.Text, StringComparison.CurrentCultureIgnoreCase))
                continue;
            var panel = new PanelContainer
            {
                HorizontalExpand = true,
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.FromHex(entry.Available > 0
                        ? "#263C39" : index % 2 == 0 ? "#252A35" : "#2C3240"),
                },
            };
            panel.AddChild(MarketRow(new[]
            {
                name, entry.Price.ToString("0.###"), entry.Reputation.ToString("0.####"),
                entry.Available.ToString("0.##"),
            }, false, reagent?.SubstanceColor, entry.Available > 0));
            _market.AddChild(panel);
            index++;
        }
        if (index == 0)
            _market.AddChild(Info(Loc.GetString("medical-orders-none")));

        _marketBeaker.Text = _state.ReagentBeakerName is { } beakerName
            ? Loc.GetString("medical-orders-beaker-inserted", ("name", beakerName))
            : Loc.GetString("medical-orders-beaker-empty");
        _marketBeakerButton.Text = Loc.GetString(_state.ReagentBeakerName == null
            ? "medical-orders-insert-beaker" : "medical-orders-eject-beaker");
        _marketSummary.Text = Loc.GetString("medical-orders-market-preview",
            ("volume", _state.MarketAcceptedVolume.ToString("0.##")),
            ("points", _state.MarketPreviewPoints), ("reputation", _state.MarketPreviewReputation));
        _marketRejected.Visible = _state.MarketRejectedVolume > 0;
        _marketRejected.Text = Loc.GetString("medical-orders-market-rejected",
            ("volume", _state.MarketRejectedVolume.ToString("0.##")));
        _marketLast.Visible = _state.LastMarketPoints > 0 || _state.LastMarketReputation > 0;
        _marketLast.Text = Loc.GetString("medical-orders-market-last-sale",
            ("points", _state.LastMarketPoints), ("reputation", _state.LastMarketReputation));
        _marketSell.Disabled = _state.MarketAcceptedVolume <= 0;
    }

    private static BoxContainer MarketRow(string[] values, bool heading, Color? reagentColor = null,
        bool supplied = false)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 8,
            Margin = new Thickness(8, 6),
        };
        var nameCell = new BoxContainer { HorizontalExpand = true, SeparationOverride = 8 };
        nameCell.AddChild(new PanelContainer
        {
            SetWidth = 6,
            PanelOverride = new StyleBoxFlat { BackgroundColor = reagentColor ?? Color.Transparent },
        });
        var name = new Label { Text = values[0], HorizontalExpand = true, ClipText = true, ToolTip = values[0] };
        if (heading)
            name.StyleClasses.Add("LabelHeading");
        nameCell.AddChild(name);
        row.AddChild(nameCell);
        for (var i = 1; i < values.Length; i++)
        {
            var cell = new Label
            {
                Text = values[i],
                SetWidth = i == 2 ? 130 : 110,
                ClipText = true,
                ToolTip = values[i],
                FontColorOverride = supplied ? Color.LightGreen : Color.LightGray,
            };
            if (heading)
                cell.StyleClasses.Add("LabelHeading");
            row.AddChild(cell);
        }
        return row;
    }

    private void RebuildShop()
    {
        if (_state == null)
            return;

        _shop.RemoveAllChildren();
        foreach (var item in _state.Shop)
        {
            var locked = _state.ShopLevel < item.MinimumShopLevel;
            _cart.TryGetValue(item.ID, out var count);
            var name = item.Classified ? Loc.GetString("medical-orders-classified") : item.Entity;
            EntityPrototype? entity = null;
            if (!item.Classified && _prototypes.TryIndex<EntityPrototype>(item.Entity, out var indexed))
            {
                entity = indexed;
                name = entity.Name;
            }

            var panel = new PanelContainer { HorizontalExpand = true, Margin = new Thickness(2) };
            var row = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                Margin = new Thickness(8, 6),
                SeparationOverride = 10,
            };
            if (entity != null)
            {
                var icon = new EntityPrototypeView
                {
                    SetSize = new Vector2(40, 40),
                    Stretch = SpriteView.StretchMode.Fit,
                };
                icon.SetPrototype(entity.ID);
                row.AddChild(icon);
            }
            else
            {
                row.AddChild(new Label
                {
                    Text = "?",
                    MinSize = new Vector2(40, 40),
                    VerticalAlignment = VAlignment.Center,
                    FontColorOverride = Color.LightGray,
                });
            }

            var details = Column();
            details.AddChild(new Label
            {
                Text = name,
                StyleClasses = { "LabelHeading" },
                FontColorOverride = locked ? Color.LightGray : null,
            });
            details.AddChild(new Label
            {
                Text = locked
                    ? Loc.GetString("medical-orders-shop-locked", ("level", item.MinimumShopLevel))
                    : Loc.GetString("medical-orders-shop-price", ("cost", item.Cost),
                        ("level", item.MinimumShopLevel)),
                FontColorOverride = locked ? Color.Orange : Color.LightBlue,
            });
            row.AddChild(details);

            var controls = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                VerticalAlignment = VAlignment.Center,
                SeparationOverride = 5,
            };
            var remove = new Button { Text = "-", Disabled = count == 0 || _pendingPurchase != null };
            var add = new Button
            {
                Text = "+",
                Disabled = locked || count >= item.MaxCount || _pendingPurchase != null,
            };
            remove.OnPressed += _ => ChangeCount(item.ID, -1, item.MaxCount);
            add.OnPressed += _ => ChangeCount(item.ID, 1, item.MaxCount);
            controls.AddChild(remove);
            controls.AddChild(new Label
            {
                Text = count.ToString(),
                MinSize = new Vector2(25, 0),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
            });
            controls.AddChild(add);
            row.AddChild(controls);
            panel.AddChild(row);
            _shop.AddChild(panel);
        }
        UpdateCart();
    }

    private void ChangeCount(string id, int delta, int max)
    {
        _cart.TryGetValue(id, out var current);
        var next = Math.Clamp(current + delta, 0, max);
        if (next == 0)
            _cart.Remove(id);
        else
            _cart[id] = next;
        RebuildShop();
    }

    private void UpdateCart()
    {
        if (_state == null)
            return;
        long total = 0;
        _cartLines.RemoveAllChildren();
        foreach (var item in _state.Shop)
        {
            if (_cart.TryGetValue(item.ID, out var count) && count > 0)
            {
                total += (long) item.Cost * count;
                var name = item.Entity;
                if (_prototypes.TryIndex<EntityPrototype>(item.Entity, out var entity))
                    name = entity.Name;
                _cartLines.AddChild(new Label
                {
                    Text = Loc.GetString("medical-orders-cart-line", ("name", name),
                        ("count", count), ("total", item.Cost * count)),
                    HorizontalExpand = true,
                });
            }
        }
        if (_cartLines.ChildCount == 0)
            _cartLines.AddChild(Info(Loc.GetString("medical-orders-cart-empty")));
        _cartSummary.Text = Loc.GetString("medical-orders-cart-total", ("total", total));
        _checkout.Visible = _state.Kind != MedicalOrderMachineKind.PatientReceiver;
        _checkout.Disabled = total == 0 || _pendingPurchase != null || total > _state.Points;
    }

    private void SubmitCart()
    {
        if (_state == null || _checkout.Disabled || _pendingPurchase != null)
            return;
        var id = Guid.NewGuid().ToString();
        _pendingPurchase = id;
        OnRequest?.Invoke(new MedicalOrderRequestMessage(MedicalOrderAction.Purchase,
            requestId: id,
            cart: _cart.Select(pair => new MedicalOrderShopCartLine(pair.Key, pair.Value)).ToArray()));
        RebuildShop();
    }

    public void ShowResult(MedicalOrderResultMessage result)
    {
        if (result.RequestId != _pendingPurchase)
            return;
        _pendingPurchase = null;
        if (result.Success)
            _cart.Clear();
        _notice.Text = Loc.GetString(result.Text);
        _notice.Visible = true;
        _notice.FontColorOverride = result.Success ? Color.LightGreen : Color.Orange;
        RebuildShop();
    }

    private void UpdateTimer()
    {
        if (_state == null)
            return;
        if (_state.Kind == MedicalOrderMachineKind.Reagent)
        {
            _timer.Text = Loc.GetString("medical-orders-market-live-prices");
            return;
        }
        var target = _state.Active?.Deadline ?? _state.NextRefresh;
        var remaining = target - _timing.CurTime;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;
        _timer.Text = Loc.GetString(_state.Active == null ? "medical-orders-refresh-in" : "medical-orders-deadline-in",
            ("time", $"{(int) remaining.TotalMinutes:00}:{remaining.Seconds:00}"));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateTimer();
    }
}
