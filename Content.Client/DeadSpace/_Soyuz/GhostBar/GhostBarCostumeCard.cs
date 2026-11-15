// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.GhostBar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.DeadSpace._Soyuz.GhostBar;

public sealed class GhostBarCostumeCard : PanelContainer
{
    public event Action<string>? OnToggled;
    public string CostumeId { get; }

    private static readonly Color BgNormal = Color.FromHex("#1e212a");
    private static readonly Color BgHover = Color.FromHex("#272b37");
    private static readonly Color BgSelected = Color.FromHex("#1f3329");
    private static readonly Color BorderNormal = Color.FromHex("#323746");
    private static readonly Color BorderHover = Color.FromHex("#4a5168");
    private static readonly Color BorderSelected = Color.FromHex("#4caf7a");
    private static readonly Color PreviewBg = Color.FromHex("#14161c");
    private static readonly Color BtnSelect = Color.FromHex("#4a7bd0");
    private static readonly Color BtnRemove = Color.FromHex("#c4524f");

    private readonly Button _button;
    private readonly Label _label;
    private readonly Label _statusLabel;
    private readonly PanelContainer _accent;

    private bool _selected;
    private bool _hovered;

    public GhostBarCostumeCard(GhostBarCostumeOption option, bool selected)
    {
        CostumeId = option.Id;
        _selected = selected;

        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        MinHeight = 64;
        Margin = new Thickness(0, 2);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        _accent = new PanelContainer
        {
            MinWidth = 4,
            VerticalExpand = true,
        };
        root.AddChild(_accent);

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 10,
            Margin = new Thickness(8, 6, 10, 6),
        };
        root.AddChild(content);

        var previewFrame = new PanelContainer
        {
            VerticalAlignment = VAlignment.Center,
            MinSize = new Vector2(52, 52),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = PreviewBg,
                BorderColor = BorderNormal,
                BorderThickness = new Thickness(1),
            },
        };

        if (!string.IsNullOrEmpty(option.ClothingProto))
        {
            var sprite = new EntityPrototypeView
            {
                MinSize = new Vector2(48, 48),
                SetSize = new Vector2(48, 48),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
                OverrideDirection = Direction.South,
                Stretch = SpriteView.StretchMode.Fill,
            };
            sprite.SetPrototype(option.ClothingProto);
            previewFrame.AddChild(sprite);
        }

        content.AddChild(previewFrame);

        var textBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };

        _label = new Label
        {
            Text = Loc.GetString(option.Name),
            ClipText = true,
        };
        textBox.AddChild(_label);

        _statusLabel = new Label
        {
            FontColorOverride = Color.FromHex("#8a90a3"),
        };
        textBox.AddChild(_statusLabel);

        content.AddChild(textBox);

        _button = new Button
        {
            VerticalAlignment = VAlignment.Center,
            MinSize = new Vector2(100, 34),
        };
        _button.OnPressed += _ => Toggle();
        content.AddChild(_button);

        AddChild(root);

        UpdateVisuals();
    }

    /// <summary>
    /// Обновить спрайт персонажа после выбора другой шмотки
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (_selected == selected)
            return;

        _selected = selected;
        UpdateVisuals();
    }

    private void Toggle() => OnToggled?.Invoke(CostumeId);

    private void UpdateVisuals()
    {
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = _selected ? BgSelected : _hovered ? BgHover : BgNormal,
            BorderColor = _selected ? BorderSelected : _hovered ? BorderHover : BorderNormal,
            BorderThickness = new Thickness(1),
        };

        _accent.PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = _selected ? BorderSelected : Color.Transparent,
        };

        _button.Text = _selected
            ? Loc.GetString("ghost-bar-costume-remove")
            : Loc.GetString("ghost-bar-costume-select");
        _button.ModulateSelfOverride = _selected ? BtnRemove : BtnSelect;

        _label.FontColorOverride = _selected ? Color.White : Color.FromHex("#d8dbe6");

        _statusLabel.Text = _selected
            ? Loc.GetString("ghost-bar-costume-status-selected")
            : string.Empty;
        _statusLabel.Visible = _selected;
        _statusLabel.FontColorOverride = BorderSelected;
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();
        _hovered = true;
        UpdateVisuals();
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hovered = false;
        UpdateVisuals();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        Toggle();
        args.Handle();
    }
}