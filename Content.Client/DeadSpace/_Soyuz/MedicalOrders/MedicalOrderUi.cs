// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DeadSpace._Soyuz.MedicalOrders;
using Robust.Client.GameObjects;
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
    private readonly BoxContainer _shopPage;
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
        overviewText.AddChild(new Label
        {
            Text = Loc.GetString("medical-orders-window-title"),
            StyleClasses = { "LabelHeading" },
        });
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

        var activePage = Page(_tabs, "medical-orders-active");
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
        activePage.AddChild(activeScroll);

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
        if (_state == null && state.Kind == MedicalOrderMachineKind.PatientSender)
            _tabs.CurrentTab = 1;
        if (_state != null && _state.Kind != state.Kind)
            _cart.Clear();
        _state = state;
        TabContainer.SetTabVisible(_offersPage, state.Kind != MedicalOrderMachineKind.PatientSender);
        TabContainer.SetTabVisible(_shopPage, state.Kind != MedicalOrderMachineKind.PatientReceiver);
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

        if (state.Kind == MedicalOrderMachineKind.Reagent)
        {
            _active.AddChild(Info(state.ReagentBeakerName is { } name
                ? Loc.GetString("medical-orders-beaker-inserted", ("name", name))
                : Loc.GetString("medical-orders-beaker-empty")));
            var beakerButton = new Button
            {
                Text = Loc.GetString(state.ReagentBeakerName == null
                    ? "medical-orders-insert-beaker" : "medical-orders-eject-beaker"),
                HorizontalExpand = true,
            };
            beakerButton.OnPressed += _ => OnBeakerSlot?.Invoke();
            _active.AddChild(beakerButton);
        }

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

            if (state.Kind == MedicalOrderMachineKind.Reagent)
            {
                content.AddChild(new Label
                {
                    Text = Loc.GetString(active.Lines.All(l => l.Submitted >= l.Required)
                        ? "medical-orders-ready" : "medical-orders-in-progress"),
                    FontColorOverride = active.Lines.All(l => l.Submitted >= l.Required)
                        ? Color.LightGreen : Color.LightGray,
                });
                var transfer = new Button
                {
                    Text = Loc.GetString("medical-orders-transfer-beaker"),
                    Disabled = state.ReagentBeakerName == null ||
                        active.Lines.All(l => l.Submitted >= l.Required),
                    HorizontalExpand = true,
                };
                transfer.OnPressed += _ => OnRequest?.Invoke(
                    new MedicalOrderRequestMessage(MedicalOrderAction.TransferReagents, active.RuntimeId));
                content.AddChild(transfer);
            }

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
                if (state.Kind == MedicalOrderMachineKind.Reagent)
                    complete.Disabled = active.Lines.Any(l => l.Submitted < l.Required);
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
                    ("status", Loc.GetString(state.LastExpired
                        ? "medical-orders-expired" : "medical-orders-complete-status")))));
        else
            _completed.AddChild(Info(Loc.GetString("medical-orders-none")));

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
        ("score", order.MaximumScore), ("minutes", (int) order.TimeLimit.TotalMinutes));

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
                        ("level", item.MinimumShopLevel), ("max", item.MaxCount)),
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
