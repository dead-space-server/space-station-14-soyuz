// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.DeadSpace._Soyuz.RepairOrders;

public sealed partial class RepairOrderWindow
{
    private const string InterfaceTextures = "/Textures/_DeadSpace/_Soyuz/interface/RepairOrders";
    private static readonly Color Amber = Color.FromHex("#ECAF4F");
    private static readonly Color Muted = Color.FromHex("#94A6AD");

    [Dependency] private readonly IResourceCache _resources = default!;

    private readonly List<ContainerButton> _navigation = new();
    private int? _selectedOfferId;
    private FancyWindow? _referenceDialog;

    private static StyleBoxFlat SurfaceStyle(bool accent = false, bool background = false)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(background ? "#131C22" : accent ? "#2B3030" : "#202B33"),
            BorderColor = accent ? Amber : Color.FromHex("#35444D"),
            BorderThickness = new Thickness(background ? 0 : 1),
        };
    }

    private static TextureRect InterfaceIcon(string icon, int size = 28)
    {
        return new TextureRect
        {
            TexturePath = $"{InterfaceTextures}/{icon}.png",
            SetSize = new Vector2(size, size),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            MouseFilter = MouseFilterMode.Ignore,
            VerticalAlignment = VAlignment.Center,
        };
    }

    private static Control SectionHeading(string icon, string text)
    {
        var heading = new BoxContainer { SeparationOverride = 10, HorizontalExpand = true };
        heading.AddChild(InterfaceIcon(icon));
        var label = new RichTextLabel { HorizontalExpand = true, StyleClasses = { "LabelHeading" } };
        label.SetMessage(text, Amber);
        heading.AddChild(label);
        return heading;
    }

    private void InitializePresentation()
    {
        BackgroundPanel.PanelOverride = SurfaceStyle(background: true);
        HeaderPanel.PanelOverride = SurfaceStyle(accent: true);
        foreach (var panel in new[] { OffersPanel, OfferDetailsPanel, WorklistPanel, ShopAccountPanel, CartPanel })
            panel.PanelOverride = SurfaceStyle();
        OrderTabs.PanelStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.Transparent };
        ShopProgressBar.BackgroundStyleBoxOverride = SurfaceStyle(background: true);
        ShopProgressBar.ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Amber };
        OnClose += () =>
        {
            _referenceDialog?.Dispose();
            _referenceDialog = null;
        };

        var icons = new[] { "orders", "active", "worklist", "shop" };
        var titles = new[] { "repair-orders-nav-offers", "repair-orders-active-heading",
            "repair-orders-worklist-heading", "repair-orders-shop-heading" };
        for (var index = 0; index < icons.Length; index++)
        {
            var tab = index;
            var button = new ContainerButton { HorizontalExpand = true, MinHeight = 44 };
            var content = new BoxContainer
            {
                SeparationOverride = 8,
                Margin = new Thickness(10, 4),
                MouseFilter = MouseFilterMode.Ignore,
            };
            content.AddChild(InterfaceIcon(icons[index]));
            content.AddChild(new Label
            {
                Text = Loc.GetString(titles[index]),
                MouseFilter = MouseFilterMode.Ignore,
                VerticalAlignment = VAlignment.Center,
            });
            button.AddChild(content);
            button.OnPressed += _ => OrderTabs.CurrentTab = tab;
            _navigation.Add(button);
            NavigationContainer.AddChild(button);
        }
        OrderTabs.OnTabChanged += UpdateNavigation;
        UpdateNavigation(OrderTabs.CurrentTab);
        AcceptOrderButton.OnPressed += _ =>
        {
            if (CanAcceptSelected() && _selectedOfferId is { } id)
                OnAccept?.Invoke(id);
        };
    }

    private void UpdateNavigation(int tab)
    {
        for (var index = 0; index < _navigation.Count; index++)
            _navigation[index].StyleBoxOverride = SurfaceStyle(accent: index == tab);
    }

    private Control CreateOfferCard(RepairOrderBuiEntry entry)
    {
        var card = new ContainerButton { HorizontalExpand = true };
        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(10),
            HorizontalExpand = true,
            MouseFilter = MouseFilterMode.Ignore,
        };
        card.AddChild(content);
        var title = new RichTextLabel { HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
        var meta = new Label
        {
            Text = "—",
            FontColorOverride = Muted,
            MouseFilter = MouseFilterMode.Ignore,
        };
        if (_prototype.TryIndex<RepairOrderPrototype>(entry.PrototypeId, out var order))
        {
            title.SetMessage(Loc.GetString(order.ObjectName));
            meta.Text = Loc.GetString("repair-orders-offer-meta", ("level", order.Difficulty),
                ("minutes", (int) order.RepairTime.TotalMinutes));
        }
        else
        {
            title.SetMessage(entry.PrototypeId);
        }
        content.AddChild(title);
        content.AddChild(meta);
        card.OnPressed += _ =>
        {
            _selectedOfferId = entry.RuntimeId;
            UpdateOfferDetails();
        };
        _offerControls[entry.RuntimeId] = card;
        return card;
    }

    private void UpdateOfferDetails()
    {
        if (_state == null)
            return;
        var entry = _state.Available.FirstOrDefault(offer => offer.RuntimeId == _selectedOfferId)
            ?? _state.Available.FirstOrDefault();
        _selectedOfferId = entry?.RuntimeId;
        OfferCountLabel.Text = Loc.GetString("repair-orders-offer-count", ("count", _state.Available.Count));
        foreach (var (id, card) in _offerControls)
            card.StyleBoxOverride = SurfaceStyle(accent: id == _selectedOfferId);

        OfferDetailsContainer.RemoveAllChildren();
        if (entry == null || !_prototype.TryIndex<RepairOrderPrototype>(entry.PrototypeId, out var order))
        {
            var empty = new RichTextLabel { HorizontalExpand = true };
            empty.SetMessage(Loc.GetString(entry == null ? "repair-orders-none-available" : "repair-orders-missing-prototype",
                ("prototype", entry?.PrototypeId ?? string.Empty)), Muted);
            OfferDetailsContainer.AddChild(empty);
            UpdateOfferAvailability();
            return;
        }

        var content = OfferDetailsContainer;
        content.AddChild(SectionHeading("orders", Loc.GetString(order.Name)));
        var identity = new RichTextLabel { HorizontalExpand = true };
        identity.SetMessage(Loc.GetString("repair-orders-object", ("type", Loc.GetString(order.ObjectType)),
            ("name", Loc.GetString(order.ObjectName))), Muted);
        content.AddChild(identity);
        AddReferencePreview(content, order);
        AddDifficulty(content, order.Difficulty);
        var duration = new Label { FontColorOverride = Amber };
        duration.Text = Loc.GetString("repair-orders-offer-duration", ("minutes", (int) order.RepairTime.TotalMinutes));
        content.AddChild(duration);
        var description = new RichTextLabel { HorizontalExpand = true };
        description.SetMessage(Loc.GetString(order.Description));
        content.AddChild(description);
        if (order.Difficulty >= 7)
        {
            var retention = new RichTextLabel { HorizontalExpand = true };
            retention.SetMessage(Loc.GetString("repair-orders-offer-retention"), Amber);
            content.AddChild(retention);
        }
        UpdateOfferAvailability();
    }

    private void AddReferencePreview(BoxContainer content, RepairOrderPrototype order)
    {
        // A static reference image, never a snapshot of the damaged or currently active grid.
        var path = $"{InterfaceTextures}/Previews/{order.TargetGridPath.FilenameWithoutExtension}.png";
        if (!_resources.TryGetResource<TextureResource>(path, out var preview))
            return;
        var frame = new ContainerButton
        {
            HorizontalExpand = true,
            StyleBoxOverride = SurfaceStyle(background: true),
            ToolTip = Loc.GetString("repair-orders-reference-open"),
        };
        frame.AddChild(new TextureRect
        {
            Texture = preview.Texture,
            CanShrink = true,
            SetHeight = 220,
            HorizontalExpand = true,
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            MouseFilter = MouseFilterMode.Ignore,
        });
        frame.OnPressed += _ => OpenReferencePreview(order, preview.Texture);
        content.AddChild(frame);
    }

    private void OpenReferencePreview(RepairOrderPrototype order, Texture texture)
    {
        _referenceDialog?.Dispose();
        var dialog = new FancyWindow
        {
            Title = Loc.GetString("repair-orders-reference-title"),
            SetSize = new Vector2(820, 600),
            MinSize = new Vector2(360, 300),
        };
        _referenceDialog = dialog;
        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(10),
            SeparationOverride = 10,
        };
        content.AddChild(SectionHeading("orders", Loc.GetString(order.ObjectName)));
        content.AddChild(new TextureRect
        {
            Texture = texture,
            CanShrink = true,
            VerticalExpand = true,
            HorizontalExpand = true,
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
        });
        dialog.ContentsContainer.AddChild(content);
        dialog.OnClose += () =>
        {
            if (ReferenceEquals(_referenceDialog, dialog))
                _referenceDialog = null;
            dialog.Dispose();
        };
        dialog.OpenCentered();
    }

    private bool CanAcceptSelected()
    {
        return _state != null && _state.Active == null && !_state.Accepting && !_state.Completing &&
               _state.NextOffer > _timing.CurTime && _state.Available.Any(entry =>
                   entry.RuntimeId == _selectedOfferId && _prototype.HasIndex<RepairOrderPrototype>(entry.PrototypeId));
    }

    private void UpdateOfferAvailability()
    {
        AcceptOrderButton.Disabled = !CanAcceptSelected();
        var status = _state?.Active != null ? "repair-orders-offer-busy"
            : _state?.Accepting == true || _state?.Completing == true ? "repair-orders-offer-processing"
            : _state == null || _state.NextOffer <= _timing.CurTime ? "repair-orders-offer-refreshing"
            : "repair-orders-offer-ready";
        var text = Loc.GetString(status);
        if (OfferAvailabilityLabel.Text != text)
        {
            OfferAvailabilityLabel.Text = text;
            OfferAvailabilityLabel.ToolTip = text;
        }
    }
}
