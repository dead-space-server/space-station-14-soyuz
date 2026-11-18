// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Client.Message;
using Content.Client.DeadSpace._Soyuz.RepairOrders;
using Content.Client.UserInterface.Controls;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeadSpace._Soyuz.MilitiaOperations;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.DeadSpace._Soyuz.MilitiaOperations;

public sealed class MilitiaOperationBoundUserInterface : BoundUserInterface
{
    private MilitiaOperationWindow? _window;
    public MilitiaOperationBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        if (IsOpened)
            return;
        base.Open();
        _window = this.CreateWindow<MilitiaOperationWindow>();
        _window.OnRequest += request => SendMessage(request);
        _window.OnDocumentSlot += () => SendMessage(new ItemSlotButtonPressedEvent("operationDocument"));
        _window.SetShopDraft(PlayerManager.LocalEntity is { } actor
            ? EntMan.System<RepairOrderShopSystem>().GetDraft(Owner, actor)
            : new RepairOrderShopDraft());
        if (State is MilitiaOperationUiState state)
            _window.UpdateState(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is MilitiaOperationUiState operation)
            _window?.UpdateState(operation);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is MilitiaOperationResult result)
            _window?.ShowResult(result);
    }

}

public sealed class MilitiaOperationWindow : FancyWindow
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IResourceCache _resources = default!;
    private static readonly Color Ink = Color.FromHex("#EADCE0");
    private static readonly Color Muted = Color.FromHex("#BFA7AE");
    private static readonly Color Accent = Color.FromHex("#E16E79");
    private static readonly Color Warning = Color.FromHex("#EDB274");
    private readonly Label _balance = new();
    private readonly Label _reputation = new();
    private readonly Label _clearance = new();
    private readonly Label _progressText = new();
    private readonly ProgressBar _progress = new() { MinValue = 0, MaxValue = 1, MinHeight = 5 };
    private readonly Label _notice = new();
    private readonly Label _status = new();
    private readonly Label _timer = new();
    private readonly Label _availability = new() { FontColorOverride = Muted, ClipText = true };
    private readonly BoxContainer _offers = Column();
    private readonly BoxContainer _details = Column();
    private readonly BoxContainer _active = Column();
    private readonly Label _coordinates = new();
    private readonly Label _remainingCrew = new();
    private readonly Label _evidence = new();
    private Button? _evidenceButton;
    private readonly BoxContainer _history = Column();
    private readonly BoxContainer _shop = Column();
    private readonly BoxContainer _shopContent;
    private readonly PanelContainer _shopMaintenance;
    private readonly BoxContainer _cartLines = Column();
    private readonly Label _cartTotal = new();
    private readonly Button _buy;
    private readonly LineEdit _search = new() { HorizontalExpand = true };
    private readonly OptionButton _category = new() { MinWidth = 150 };
    private readonly CheckBox _availableOnly = new();
    private readonly Button _clearCart;
    private readonly Dictionary<string, (Button Remove, Button Add, Label Count, RepairRewardPrototype Reward)> _quantities = new();
    private RepairOrderShopDraft _draft = new();
    private Dictionary<string, int> Cart => _draft.Cart;
    private readonly Dictionary<Guid, ContainerButton> _offerCards = new();
    private readonly List<ContainerButton> _navigation = new();
    private MilitiaOperationUiState? _state;
    private string _catalog = string.Empty;
    private string _shopKey = string.Empty;
    private string _activeKey = string.Empty;
    private string _historyKey = "\0";
    private string _cartKey = string.Empty;
    private Guid? _selected;
    private Guid? _abortConfirmation;
    private Guid? _purchase
    {
        get => Guid.TryParse(_draft.PendingRequestId, out var id) ? id : null;
        set => _draft.PendingRequestId = value?.ToString();
    }
    private TimeSpan _purchaseRetry;
    private Guid? _accepting;
    private TimeSpan _acceptTimeout;
    private Button? _accept;
    public event Action<MilitiaOperationRequest>? OnRequest;
    public event Action? OnDocumentSlot;

    public MilitiaOperationWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("militia-operation-title");
        MinSize = new Vector2(850, 580);
        SetSize = new Vector2(1080, 740);
        var background = new PanelContainer
        {
            HorizontalExpand = true, VerticalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#1E151C") },
        };
        var root = Column();
        root.Margin = new Thickness(10);
        var header = new BoxContainer { SeparationOverride = 14 };
        header.AddChild(Icon(0, 48));
        var heading = Column();
        var title = Text(Loc.GetString("militia-operation-title"));
        title.StyleClasses.Add("LabelHeading");
        heading.AddChild(title);
        header.AddChild(heading);
        root.AddChild(Surface(header, accent: true));
        var account = new BoxContainer { SeparationOverride = 8 };
        account.AddChild(Metric("militia-operation-points-label", _balance));
        account.AddChild(Metric("militia-operation-reputation-label", _reputation));
        account.AddChild(Metric("militia-operation-clearance-label", _clearance));
        root.AddChild(account);
        var progress = Column();
        _progressText.FontColorOverride = Muted;
        progress.AddChild(_progressText);
        _progress.BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#150F15") };
        _progress.ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Accent };
        progress.AddChild(_progress);
        root.AddChild(progress);
        _notice.Visible = false;
        _notice.ClipText = true;
        root.AddChild(_notice);
        var tabs = new TabContainer { HorizontalExpand = true, VerticalExpand = true, TabsVisible = false };
        tabs.PanelStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.Transparent };
        var navigation = new BoxContainer { SeparationOverride = 6 };
        var titles = new[] { "militia-operation-offers", "militia-operation-active", "militia-operation-archive", "militia-operation-supply-nav" };
        var icons = new[] { 1, 2, 4, 3 };
        for (var index = 0; index < titles.Length; index++)
        {
            var tab = index;
            var button = new ContainerButton { HorizontalExpand = true, MinHeight = 46 };
            var content = new BoxContainer { SeparationOverride = 6, Margin = new Thickness(8, 4), MouseFilter = MouseFilterMode.Ignore };
            content.AddChild(Icon(icons[index]));
            var label = Text(Loc.GetString(titles[index]));
            label.VerticalAlignment = VAlignment.Center;
            content.AddChild(label);
            button.AddChild(content);
            button.OnPressed += _ => tabs.CurrentTab = tab;
            navigation.AddChild(button);
            _navigation.Add(button);
        }
        tabs.OnTabChanged += SelectTab;
        SelectTab(0);
        root.AddChild(navigation);
        var offersPage = Page(tabs, "militia-operation-offers");
        var offerRow = new BoxContainer { HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 10 };
        var listColumn = Column();
        listColumn.AddChild(Section(1, "militia-operation-case-list"));
        listColumn.AddChild(_availability);
        listColumn.AddChild(Scroll(_offers));
        var list = Surface(listColumn, expand: true);
        list.MinWidth = 240;
        list.SizeFlagsStretchRatio = 2;
        offerRow.AddChild(list);
        var details = Surface(Scroll(_details), expand: true);
        details.SizeFlagsStretchRatio = 3;
        offerRow.AddChild(details);
        offersPage.AddChild(offerRow);
        var activePage = Page(tabs, "militia-operation-active");
        var situation = Column();
        situation.AddChild(Section(2, "militia-operation-situation"));
        _status.FontColorOverride = Accent;
        _status.ClipText = true;
        situation.AddChild(_status);
        _timer.FontColorOverride = Muted;
        situation.AddChild(_timer);
        activePage.AddChild(Surface(situation));
        activePage.AddChild(Scroll(_active));
        var archive = Page(tabs, "militia-operation-archive");
        archive.AddChild(Section(4, "militia-operation-archive-heading"));
        archive.AddChild(Scroll(_history));
        var shopPage = Page(tabs, "militia-operation-shop");
        var shopRow = new BoxContainer { HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 10, Visible = false };
        _shopContent = shopRow;
        var catalog = Column();
        catalog.AddChild(Section(3, "militia-operation-shop"));
        _search.PlaceHolder = Loc.GetString("militia-operation-shop-search");
        _search.OnTextChanged += args => { _draft.Search = args.Text; RenderShop(); };
        catalog.AddChild(_search);
        var filters = new BoxContainer { SeparationOverride = 8 };
        foreach (var key in new[] { "all", "weapons", "ammo", "equipment" })
            _category.AddItem(Loc.GetString($"militia-operation-shop-{key}"));
        _category.OnItemSelected += args => { _category.SelectId(args.Id); RenderShop(); };
        filters.AddChild(_category);
        _availableOnly.Text = Loc.GetString("militia-operation-shop-available");
        _availableOnly.OnToggled += _ => RenderShop();
        filters.AddChild(_availableOnly);
        catalog.AddChild(filters);
        catalog.AddChild(Scroll(_shop));
        var catalogPanel = Surface(catalog, expand: true);
        catalogPanel.SizeFlagsStretchRatio = 3;
        shopRow.AddChild(catalogPanel);
        var cart = Column();
        cart.AddChild(Section(1, "militia-operation-cart"));
        cart.AddChild(Scroll(_cartLines));
        cart.AddChild(_cartTotal);
        _buy = Button("militia-operation-buy", BeginPurchase);
        cart.AddChild(_buy);
        _clearCart = Button("militia-operation-cart-clear", () =>
        {
            if (_purchase != null)
                return;
            Cart.Clear();
            UpdateCart();
        });
        cart.AddChild(_clearCart);
        var cartPanel = Surface(cart, expand: true);
        cartPanel.MinWidth = 280;
        cartPanel.SizeFlagsStretchRatio = 2;
        shopRow.AddChild(cartPanel);
        shopPage.AddChild(shopRow);
        var maintenance = Column();
        maintenance.HorizontalAlignment = HAlignment.Center;
        maintenance.VerticalAlignment = VAlignment.Center;
        maintenance.MaxWidth = 520;
        maintenance.AddChild(Section(3, "militia-operation-shop-maintenance"));
        maintenance.AddChild(Paragraph(Loc.GetString("militia-operation-shop-maintenance-description")));
        _shopMaintenance = Surface(maintenance, expand: true, accent: true);
        shopPage.AddChild(_shopMaintenance);
        root.AddChild(tabs);
        background.AddChild(root);
        ContentsContainer.AddChild(background);
    }

    private static BoxContainer Column() => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 8,
    };

    private static Label Text(string text, Color? color = null) => new()
    {
        Text = text, FontColorOverride = color ?? Ink, ClipText = true,
        HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore,
    };

    private static RichTextLabel Paragraph(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true, Margin = new Thickness(4), MouseFilter = MouseFilterMode.Ignore };
        label.SetMessage(text);
        return label;
    }

    private static StyleBoxFlat PanelStyle(bool accent = false) => new()
    {
        BackgroundColor = Color.FromHex(accent ? "#3E252E" : "#2A1D26"),
        BorderColor = accent ? Accent : Color.FromHex("#68404D"), BorderThickness = new Thickness(1),
        ContentMarginLeftOverride = 10, ContentMarginRightOverride = 10,
        ContentMarginTopOverride = 10, ContentMarginBottomOverride = 10,
    };

    private static PanelContainer Surface(Control content, bool expand = false, bool accent = false)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true, VerticalExpand = expand, PanelOverride = PanelStyle(accent),
        };
        panel.AddChild(content);
        return panel;
    }

    private TextureRect Icon(int index, int size = 28) => new()
    {
        Texture = new AtlasTexture(_resources.GetResource<TextureResource>(
            "/Textures/_DeadSpace/_Soyuz/interface/MilitiaOperations/icons.png").Texture,
            UIBox2.FromDimensions(new Vector2(index * 32, 0), new Vector2(32))),
        SetSize = new Vector2(size), Stretch = TextureRect.StretchMode.KeepAspectCentered,
        MouseFilter = MouseFilterMode.Ignore, VerticalAlignment = VAlignment.Center,
    };

    private Control Section(int icon, string key)
    {
        var row = new BoxContainer { SeparationOverride = 8, HorizontalExpand = true };
        row.AddChild(Icon(icon));
        var title = Text(Loc.GetString(key), Accent);
        title.VerticalAlignment = VAlignment.Center;
        row.AddChild(title);
        return row;
    }

    private static Control Metric(string key, Label value)
    {
        var column = Column();
        column.AddChild(Text(Loc.GetString(key), Muted));
        value.FontColorOverride = Accent;
        value.StyleClasses.Add("LabelHeading");
        column.AddChild(value);
        return Surface(column);
    }

    private void SelectTab(int tab)
    {
        for (var index = 0; index < _navigation.Count; index++)
            _navigation[index].StyleBoxOverride = PanelStyle(index == tab);
    }

    private static ScrollContainer Scroll(Control content)
    {
        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(content);
        return scroll;
    }

    private static BoxContainer Page(TabContainer tabs, string title)
    {
        var page = Column();
        page.VerticalExpand = true;
        page.Margin = new Thickness(0, 4);
        tabs.AddChild(page);
        TabContainer.SetTabTitle(page, Loc.GetString(title));
        return page;
    }

    private static Button Button(string key, Action action)
    {
        var button = new Button { Text = Loc.GetString(key), HorizontalExpand = true, ClipText = true };
        button.OnPressed += _ => action();
        return button;
    }

    public void UpdateState(MilitiaOperationUiState state)
    {
        if (_state?.Status != state.Status)
            _notice.Visible = false;
        _state = state;
        if (_accepting is { } pending && (state.Active != null || state.Offers.All(offer => offer.Token != pending)))
            _accepting = null;
        _shopContent.Visible = state.ShopEnabled;
        _shopMaintenance.Visible = !state.ShopEnabled;
        _balance.Text = state.Points.ToString();
        _reputation.Text = state.Reputation.ToString();
        _clearance.Text = Loc.GetString("militia-operation-clearance-value", ("level", state.Level));
        if (_prototypes.TryIndex<RepairRewardPoolPrototype>(state.Shop, out var pool))
        {
            var thresholds = pool.ShopLevelThresholds;
            var next = state.Level < thresholds.Count ? thresholds[state.Level] : (int?) null;
            var previous = thresholds.Count == 0 ? 0 : thresholds[Math.Clamp(state.Level - 1, 0, thresholds.Count - 1)];
            _progress.Value = next is { } target ? (float) Math.Clamp((double) (state.Reputation - previous) / (target - previous), 0, 1) : 1;
            _progressText.Text = next is { } required
                ? Loc.GetString("militia-operation-clearance-progress", ("remaining", Math.Max(0, required - state.Reputation)))
                : Loc.GetString("militia-operation-clearance-max");
        }
        _status.Text = Loc.GetString(state.Status);
        var catalog = state.Shop + state.ShopEnabled + state.Level + string.Join(",", state.Offers.Select(o => o.Token)) +
                      (state.Active != null) + state.CleanupPending;
        if (catalog != _catalog)
        {
            _catalog = catalog;
            RenderOffers();
        }
        var shopKey = $"{state.Shop}:{state.ShopEnabled}:{state.Level}";
        if (shopKey != _shopKey)
        {
            if (_draft.RewardPoolId != state.Shop && _purchase == null)
                Cart.Clear();
            _draft.RewardPoolId = state.Shop;
            _shopKey = shopKey;
            RenderShop();
        }
        var activeKey = $"{state.Active?.Token}:{state.CleanupPending}";
        if (activeKey != _activeKey)
        {
            _activeKey = activeKey;
            RenderActive();
        }
        _coordinates.Text = state.Coordinates;
        _remainingCrew.Text = Loc.GetString("militia-operation-contact", ("count", state.Remaining));
        _evidence.Text = Loc.GetString(state.EvidenceReceived ? "militia-operation-evidence-received" :
            state.DocumentInserted ? "militia-operation-evidence-present" : "militia-operation-evidence-missing");
        if (_evidenceButton != null)
        {
            _evidenceButton.Text = Loc.GetString(state.DocumentInserted ? "militia-operation-eject" : "militia-operation-insert");
            _evidenceButton.Disabled = state.EvidenceReceived && !state.DocumentInserted;
        }
        var historyKey = string.Join('\n', state.History);
        if (historyKey != _historyKey)
        {
            _historyKey = historyKey;
            _history.RemoveAllChildren();
            if (state.History.Length == 0)
                _history.AddChild(Surface(Paragraph(Loc.GetString("militia-operation-archive-empty"))));
            foreach (var entry in state.History)
                _history.AddChild(Surface(Paragraph(entry)));
        }
        UpdateCart();
    }

    private void RenderOffers()
    {
        _accept = null;
        _offers.RemoveAllChildren();
        _offerCards.Clear();
        if (_state == null)
            return;
        var selected = _state.Offers.FirstOrDefault(o => o.Token == _selected) ?? _state.Offers.FirstOrDefault();
        _selected = selected?.Token;
        foreach (var offer in _state.Offers)
        {
            if (!_prototypes.TryIndex<MilitiaOperationPrototype>(offer.Prototype, out var prototype))
                continue;
            var button = new ContainerButton { HorizontalExpand = true };
            var card = Column();
            card.MouseFilter = MouseFilterMode.Ignore;
            card.AddChild(Paragraph(Loc.GetString(prototype.Name)));
            card.AddChild(Text(Loc.GetString("militia-operation-case-meta", ("points", prototype.Points),
                ("minutes", (int) prototype.Duration.TotalMinutes)), Muted));
            button.AddChild(card);
            button.OnPressed += _ => { _selected = offer.Token; ShowDetails(offer); };
            _offerCards[offer.Token] = button;
            _offers.AddChild(button);
        }
        _details.RemoveAllChildren();
        if (selected != null)
            ShowDetails(selected);
        else
            _details.AddChild(Paragraph(Loc.GetString("militia-operation-no-offers")));
    }

    private void ShowDetails(MilitiaOperationOffer offer)
    {
        _details.RemoveAllChildren();
        _accept = null;
        if (_state == null || !_prototypes.TryIndex<MilitiaOperationPrototype>(offer.Prototype, out var prototype))
            return;
        foreach (var (id, card) in _offerCards)
            card.StyleBoxOverride = PanelStyle(id == offer.Token);
        _details.AddChild(Section(1, "militia-operation-dossier"));
        var title = Paragraph(Loc.GetString(prototype.Name));
        title.StyleClasses.Add("LabelHeading");
        _details.AddChild(title);
        _details.AddChild(Paragraph(Loc.GetString(prototype.Description)));
        _details.AddChild(Section(2, "militia-operation-assignment"));
        _details.AddChild(Paragraph(Loc.GetString("militia-operation-objectives")));
        _details.AddChild(Surface(Paragraph(Loc.GetString("militia-operation-reward", ("points", prototype.Points),
            ("reputation", prototype.Reputation))), accent: true));
        _accept = Button("militia-operation-accept", () =>
        {
            var state = _state;
            if (_accepting != null || state == null || state.Active != null || state.CleanupPending ||
                _timing.CurTime < state.AvailableAt)
                return;
            _accepting = offer.Token;
            _acceptTimeout = _timing.RealTime + TimeSpan.FromSeconds(30);
            if (_accept != null)
                _accept.Disabled = true;
            _notice.Text = Loc.GetString("militia-operation-preparing");
            _notice.FontColorOverride = Muted;
            _notice.Visible = true;
            OnRequest?.Invoke(new(MilitiaOperationAction.Accept, offer.Token));
        });
        _accept.Disabled = _accepting != null || _state.Active != null || _state.CleanupPending || _timing.CurTime < _state.AvailableAt;
        _details.AddChild(_accept);
    }

    private void RenderActive()
    {
        _coordinates.Orphan();
        _remainingCrew.Orphan();
        _evidence.Orphan();
        _active.RemoveAllChildren();
        _abortConfirmation = null;
        _evidenceButton = null;
        if (_state?.Active is not { } active)
        {
            _active.AddChild(Surface(Paragraph(Loc.GetString(_state?.CleanupPending == true ? "militia-operation-cleanup" : "militia-operation-no-active"))));
            return;
        }
        if (_prototypes.TryIndex<MilitiaOperationPrototype>(active.Prototype, out var prototype))
            _active.AddChild(Surface(Paragraph(Loc.GetString(prototype.Name))));
        var contact = Column();
        contact.AddChild(Section(2, "militia-operation-target-position"));
        _coordinates.FontColorOverride = Accent;
        contact.AddChild(_coordinates);
        contact.AddChild(_remainingCrew);
        _active.AddChild(Surface(contact));
        var evidence = Column();
        evidence.AddChild(Section(4, "militia-operation-document-slot"));
        evidence.AddChild(_evidence);
        _evidenceButton = Button("militia-operation-insert", () => OnDocumentSlot?.Invoke());
        evidence.AddChild(_evidenceButton);
        _active.AddChild(Surface(evidence));
        var abort = Button("militia-operation-abort", () => { });
        abort.ModulateSelfOverride = Warning;
        abort.OnPressed += _ =>
        {
            if (_abortConfirmation == active.Token)
                OnRequest?.Invoke(new(MilitiaOperationAction.Abort, active.Token));
            else
            {
                _abortConfirmation = active.Token;
                abort.Text = Loc.GetString("militia-operation-abort-confirm");
            }
        };
        _active.AddChild(abort);
    }

    public void SetShopDraft(RepairOrderShopDraft draft)
    {
        _draft = draft;
        _search.Text = draft.Search;
    }

    private string ProductName(RepairRewardPrototype reward, EntityPrototype entity)
        => reward.ShopName is { } name ? Loc.GetString(name) : entity.Name;

    private static int ProductCategory(EntityPrototype entity)
        => entity.Components.ContainsKey("Gun") ? 1 : entity.Components.ContainsKey("BallisticAmmoProvider") ? 2 : 3;

    private void RenderShop()
    {
        _shop.RemoveAllChildren();
        _quantities.Clear();
        if (_state?.ShopEnabled != true || !_prototypes.TryIndex<RepairRewardPoolPrototype>(_state.Shop, out var pool))
            return;
        var search = _search.Text.Trim();
        foreach (var id in pool.Rewards.OrderBy(id => _prototypes.TryIndex(id, out var r) ? r.MinimumShopLevel : int.MaxValue))
        {
            if (!_prototypes.TryIndex(id, out var reward) || !_prototypes.TryIndex(reward.Entity, out var entity) ||
                reward.EmagOnly || reward.Classified && _state.Level < reward.MinimumShopLevel)
                continue;
            var name = ProductName(reward, entity);
            if (search.Length > 0 && !name.Contains(search, StringComparison.CurrentCultureIgnoreCase) &&
                !entity.ID.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                _category.SelectedId != 0 && ProductCategory(entity) != _category.SelectedId ||
                _availableOnly.Pressed && _state.Level < reward.MinimumShopLevel)
                continue;
            var row = new BoxContainer { SeparationOverride = 8, HorizontalExpand = true };
            var icon = new EntityPrototypeView { SetSize = new Vector2(40), Stretch = SpriteView.StretchMode.Fit };
            icon.SetPrototype(entity.ID);
            row.AddChild(icon);
            var description = Column();
            description.ToolTip = entity.Description;
            description.AddChild(Paragraph(name));
            description.AddChild(Text(Loc.GetString("militia-operation-price", ("cost", reward.Cost), ("level", reward.MinimumShopLevel)),
                _state.Level < reward.MinimumShopLevel ? Muted : Accent));
            row.AddChild(description);
            var controls = QuantityControls(reward);
            controls.VerticalAlignment = VAlignment.Center;
            row.AddChild(controls);
            _shop.AddChild(Surface(row));
        }
        if (_shop.ChildCount == 0)
            _shop.AddChild(Paragraph(Loc.GetString("militia-operation-shop-no-results")));
        UpdateQuantities();
    }

    private BoxContainer QuantityControls(RepairRewardPrototype reward)
    {
        var row = new BoxContainer { SeparationOverride = 4 };
        var remove = new Button { Text = "-", MinWidth = 28, ToolTip = Loc.GetString("militia-operation-remove") };
        var count = new Label { MinWidth = 24, HorizontalAlignment = HAlignment.Center, VerticalAlignment = VAlignment.Center };
        var add = new Button { Text = "+", MinWidth = 28, ToolTip = Loc.GetString("militia-operation-add") };
        remove.OnPressed += _ => ChangeQuantity(reward, -1);
        add.OnPressed += _ => ChangeQuantity(reward, 1);
        row.AddChild(remove);
        row.AddChild(count);
        row.AddChild(add);
        _quantities[reward.ID] = (remove, add, count, reward);
        return row;
    }

    private void ChangeQuantity(RepairRewardPrototype reward, int change)
    {
        if (_purchase != null || _state?.ShopEnabled != true || _state.Level < reward.MinimumShopLevel)
            return;
        var count = Math.Clamp(Cart.GetValueOrDefault(reward.ID) + change, 0, reward.MaxCount);
        if (count == 0)
            Cart.Remove(reward.ID);
        else
            Cart[reward.ID] = count;
        UpdateCart();
    }

    private void UpdateQuantities()
    {
        foreach (var (id, controls) in _quantities)
        {
            var count = Cart.GetValueOrDefault(id);
            controls.Count.Text = count.ToString();
            controls.Remove.Disabled = _purchase != null || count == 0;
            controls.Add.Disabled = _purchase != null || _state?.ShopEnabled != true ||
                                    _state.Level < controls.Reward.MinimumShopLevel || count >= controls.Reward.MaxCount;
        }
    }

    private void UpdateCart()
    {
        UpdateQuantities();
        var key = $"{_state?.Shop}:{_state?.ShopEnabled}:{_state?.Points}:{_state?.Level}:{_purchase}:{string.Join(';', Cart.Select(p => $"{p.Key}:{p.Value}"))}";
        if (_cartKey == key)
            return;
        _cartKey = key;
        _cartLines.RemoveAllChildren();
        _prototypes.TryIndex<RepairRewardPoolPrototype>(_state?.Shop ?? string.Empty, out var pool);
        long total = 0;
        var valid = pool != null;
        foreach (var (id, count) in Cart.ToArray())
        {
            if (pool == null || !pool.Rewards.Any(entry => entry.Id == id) ||
                !_prototypes.TryIndex<RepairRewardPrototype>(id, out var reward) ||
                !_prototypes.TryIndex(reward.Entity, out var entity) || reward.EmagOnly ||
                count <= 0 || count > reward.MaxCount || _state!.Level < reward.MinimumShopLevel)
            {
                if (_purchase == null)
                    Cart.Remove(id);
                else
                    valid = false;
                continue;
            }
            total += (long) count * reward.Cost;
            var row = Column();
            row.AddChild(Paragraph($"{ProductName(reward, entity)} × {count}"));
            row.AddChild(Text(Loc.GetString("militia-operation-total", ("cost", (long) count * reward.Cost)), Muted));
            var controls = new BoxContainer { SeparationOverride = 6 };
            var remove = new Button { Text = "-", MinWidth = 32, Disabled = _purchase != null };
            remove.OnPressed += _ => ChangeQuantity(reward, -1);
            var add = new Button { Text = "+", MinWidth = 32, Disabled = _purchase != null || count >= reward.MaxCount };
            add.OnPressed += _ => ChangeQuantity(reward, 1);
            controls.AddChild(remove);
            controls.AddChild(add);
            row.AddChild(controls);
            _cartLines.AddChild(Surface(row));
        }
        if (Cart.Count == 0)
            _cartLines.AddChild(Paragraph(Loc.GetString("militia-operation-cart-empty")));
        _cartTotal.Text = Loc.GetString("militia-operation-total", ("cost", total));
        _buy.Text = Loc.GetString(_purchase != null ? "militia-operation-purchase-pending" : "militia-operation-buy");
        _buy.Disabled = !valid || total == 0 || _state?.ShopEnabled != true || total > _state.Points || _purchase != null;
        _clearCart.Disabled = Cart.Count == 0 || _purchase != null;
        UpdateQuantities();
    }

    private void BeginPurchase()
    {
        if (_buy.Disabled || _state?.ShopEnabled != true || _purchase != null || Cart.Count == 0)
            return;
        _purchase = Guid.NewGuid();
        _draft.PendingLines = Cart.Select(p => new RepairOrderRewardBuiEntry(p.Key, p.Value)).ToList();
        SendPurchase();
        UpdateCart();
    }

    private void SendPurchase()
    {
        if (_purchase is not { } id || _draft.PendingLines is not { } lines)
            return;
        _purchaseRetry = _timing.RealTime + TimeSpan.FromSeconds(5);
        OnRequest?.Invoke(new MilitiaOperationRequest(MilitiaOperationAction.Purchase, id, lines));
    }

    public void ShowResult(MilitiaOperationResult result)
    {
        if (_accepting == result.Token)
            _accepting = null;
        _notice.Text = Loc.GetString(result.Message);
        _notice.FontColorOverride = result.Success ? Accent : Warning;
        _notice.Visible = true;
        if (_purchase == result.Token && result.Message != "militia-operation-busy")
        {
            if (result.Success)
                Cart.Clear();
            _purchase = null;
            _draft.PendingLines = null;
        }
        UpdateCart();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_state != null && _purchase != null && _timing.RealTime >= _purchaseRetry)
            SendPurchase();
        if (_accepting != null && _timing.RealTime >= _acceptTimeout)
        {
            _accepting = null;
            _notice.Text = Loc.GetString("militia-operation-request-timeout");
            _notice.FontColorOverride = Warning;
            _notice.Visible = true;
        }
        var deadline = _state?.Deadline ?? _state?.AvailableAt;
        var remaining = deadline - _timing.CurTime;
        _timer.Text = remaining is { } time && time > TimeSpan.Zero
            ? Loc.GetString(_state?.Active == null ? "militia-operation-cooldown" : "militia-operation-time",
                ("time", $"{(int) time.TotalMinutes:00}:{time.Seconds:00}")) : string.Empty;
        _availability.Visible = _state?.Active == null && _state?.AvailableAt > _timing.CurTime;
        _availability.Text = _timer.Text;
        if (_accept != null && _state != null)
            _accept.Disabled = _accepting != null || _state.Active != null || _state.CleanupPending || _timing.CurTime < _state.AvailableAt;
    }
}
