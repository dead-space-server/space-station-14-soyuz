// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Client.Message;
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
        base.Open();
        _window = new MilitiaOperationWindow();
        _window.OnRequest += request => SendMessage(request);
        _window.OnDocumentSlot += () => SendMessage(new ItemSlotButtonPressedEvent("operationDocument"));
        _window.OnClose += Close;
        _window.OpenCentered();
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
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
    private readonly Dictionary<string, int> _cart = new();
    private readonly Dictionary<Guid, ContainerButton> _offerCards = new();
    private readonly List<ContainerButton> _navigation = new();
    private MilitiaOperationUiState? _state;
    private string _catalog = string.Empty;
    private string _activeKey = string.Empty;
    private string _historyKey = "\0";
    private string _cartKey = string.Empty;
    private Guid? _selected;
    private Guid? _abortConfirmation;
    private Guid? _purchase;
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
        heading.AddChild(Text(Loc.GetString("militia-operation-subtitle"), Muted));
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
        catalog.AddChild(Scroll(_shop));
        var catalogPanel = Surface(catalog, expand: true);
        catalogPanel.SizeFlagsStretchRatio = 3;
        shopRow.AddChild(catalogPanel);
        var cart = Column();
        cart.AddChild(Section(1, "militia-operation-cart"));
        cart.AddChild(Scroll(_cartLines));
        cart.AddChild(_cartTotal);
        _buy = Button("militia-operation-buy", () =>
        {
            if (_state?.ShopEnabled != true || _purchase != null || _cart.Count == 0)
                return;
            _purchase = Guid.NewGuid();
            OnRequest?.Invoke(new MilitiaOperationRequest(MilitiaOperationAction.Purchase, _purchase.Value,
                _cart.Select(p => new RepairOrderRewardBuiEntry(p.Key, p.Value)).ToList()));
            UpdateCart();
        });
        cart.AddChild(_buy);
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
        _accept = Button("militia-operation-accept", () => OnRequest?.Invoke(new(MilitiaOperationAction.Accept, offer.Token)));
        _accept.Disabled = _state.Active != null || _state.CleanupPending || _timing.CurTime < _state.AvailableAt;
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

    private void RenderShop()
    {
        _shop.RemoveAllChildren();
        if (_state?.ShopEnabled != true || !_prototypes.TryIndex<RepairRewardPoolPrototype>(_state.Shop, out var pool))
            return;
        foreach (var id in pool.Rewards.OrderBy(id => _prototypes.TryIndex(id, out var r) ? r.MinimumShopLevel : int.MaxValue))
        {
            if (!_prototypes.TryIndex(id, out var reward) || !_prototypes.TryIndex(reward.Entity, out var entity) ||
                reward.Classified && _state.Level < reward.MinimumShopLevel)
                continue;
            var row = new BoxContainer { SeparationOverride = 8, HorizontalExpand = true };
            var icon = new EntityPrototypeView { SetSize = new Vector2(40), Stretch = SpriteView.StretchMode.Fit };
            icon.SetPrototype(entity.ID);
            row.AddChild(icon);
            var description = Column();
            description.AddChild(Paragraph(entity.Name));
            description.AddChild(Text(Loc.GetString("militia-operation-price", ("cost", reward.Cost), ("level", reward.MinimumShopLevel))));
            row.AddChild(description);
            var add = Button("militia-operation-add", () =>
            {
                if (_purchase != null)
                    return;
                _cart.TryGetValue(reward.ID, out var count);
                _cart[reward.ID] = Math.Min(count + 1, reward.MaxCount);
                UpdateCart();
            });
            add.HorizontalExpand = false;
            add.MinWidth = 90;
            add.VerticalAlignment = VAlignment.Center;
            add.Disabled = _state.Level < reward.MinimumShopLevel;
            row.AddChild(add);
            var panel = Surface(row);
            panel.VerticalExpand = false;
            _shop.AddChild(panel);
        }
    }

    private void UpdateCart()
    {
        var key = $"{_state?.Points}:{_state?.Level}:{_purchase}:{string.Join(';', _cart.Select(p => $"{p.Key}:{p.Value}"))}";
        if (_cartKey == key)
            return;
        _cartKey = key;
        _cartLines.RemoveAllChildren();
        if (_cart.Count == 0)
            _cartLines.AddChild(Paragraph(Loc.GetString("militia-operation-cart-empty")));
        long total = 0;
        foreach (var (id, count) in _cart.ToArray())
        {
            if (!_prototypes.TryIndex<RepairRewardPrototype>(id, out var reward) || !_prototypes.TryIndex(reward.Entity, out var entity))
                continue;
            total += (long) count * reward.Cost;
            var row = Column();
            row.AddChild(Paragraph($"{entity.Name} × {count}"));
            row.AddChild(Text(Loc.GetString("militia-operation-total", ("cost", (long) count * reward.Cost)), Muted));
            var remove = Button("militia-operation-remove", () =>
            {
                if (count > 1) _cart[id] = count - 1;
                else _cart.Remove(id);
                UpdateCart();
            });
            remove.Disabled = _purchase != null;
            row.AddChild(remove);
            _cartLines.AddChild(Surface(row));
        }
        _cartTotal.Text = Loc.GetString("militia-operation-total", ("cost", total));
        _buy.Disabled = total == 0 || _state?.ShopEnabled != true || total > _state.Points || _purchase != null;
    }

    public void ShowResult(MilitiaOperationResult result)
    {
        _notice.Text = Loc.GetString(result.Message);
        _notice.FontColorOverride = result.Success ? Accent : Warning;
        _notice.Visible = true;
        if (_purchase == result.Token)
        {
            if (result.Success)
                _cart.Clear();
            _purchase = null;
        }
        UpdateCart();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        var deadline = _state?.Deadline ?? _state?.AvailableAt;
        var remaining = deadline - _timing.CurTime;
        _timer.Text = remaining is { } time && time > TimeSpan.Zero
            ? Loc.GetString(_state?.Active == null ? "militia-operation-cooldown" : "militia-operation-time",
                ("time", $"{(int) time.TotalMinutes:00}:{time.Seconds:00}")) : string.Empty;
        _availability.Visible = _state?.Active == null && _state?.AvailableAt > _timing.CurTime;
        _availability.Text = _timer.Text;
        if (_accept != null && _state != null)
            _accept.Disabled = _state.Active != null || _state.CleanupPending || _timing.CurTime < _state.AvailableAt;
    }
}
