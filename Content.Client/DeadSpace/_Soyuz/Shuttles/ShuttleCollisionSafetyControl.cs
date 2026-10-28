// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Globalization;
using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.Shuttles;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.DeadSpace._Soyuz.Shuttles;

public sealed class ShuttleCollisionSafetyControl : PanelContainer
{
    [Dependency] private readonly IResourceCache _resources = default!;
    private readonly Label _count;
    private readonly RichTextLabel _status;
    private readonly Button _activation;
    private readonly OptionButton _mode;
    private ShuttleCollisionMode _selected = ShuttleCollisionMode.Automatic;
    private bool _active;
    public event Action<ShuttleCollisionMode, bool>? OnModeSelected;

    public ShuttleCollisionSafetyControl()
    {
        IoCManager.InjectDependencies(this);
        Visible = false;
        Margin = new Thickness(0, 6, 0, 0);
        PanelOverride = new StyleBoxFlat(Color.FromHex("#242C35"))
        {
            ContentMarginLeftOverride = 8, ContentMarginRightOverride = 8,
            ContentMarginTopOverride = 6, ContentMarginBottomOverride = 6,
        };
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        AddChild(content);
        var row = new BoxContainer { SeparationOverride = 8 };
        content.AddChild(row);
        row.AddChild(new TextureRect
        {
            Texture = new AtlasTexture(_resources.GetResource<TextureResource>(
                "/Textures/_DeadSpace/_Soyuz/Structures/Machines/rubezh_sensor.rsi/on.png").Texture,
                UIBox2.FromDimensions(new Vector2(32, 0), new Vector2(32))),
            SetSize = new Vector2(32), Stretch = TextureRect.StretchMode.KeepCentered,
            MouseFilter = MouseFilterMode.Ignore,
        });
        row.AddChild(new Label { Text = Loc.GetString("shuttle-collision-title"), FontColorOverride = Color.FromHex("#E4BE78") });
        _count = new Label { HorizontalExpand = true };
        row.AddChild(_count);
        _activation = new Button { Text = Loc.GetString("shuttle-collision-activate"), ToggleMode = true, HorizontalExpand = true };
        _activation.OnPressed += _ =>
        {
            _active = _activation.Pressed;
            _activation.Text = Loc.GetString(_active ? "shuttle-collision-deactivate" : "shuttle-collision-activate");
            OnModeSelected?.Invoke(_selected, _active);
        };
        content.AddChild(_activation);
        _mode = new OptionButton { HorizontalExpand = true };
        foreach (var mode in Enum.GetValues<ShuttleCollisionMode>())
        {
            if (mode != ShuttleCollisionMode.Off)
                _mode.AddItem(Loc.GetString($"shuttle-collision-mode-{mode.ToString().ToLowerInvariant()}"), (int) mode);
        }
        _mode.SelectId((int) _selected);
        _mode.OnItemSelected += args =>
        {
            _selected = (ShuttleCollisionMode) args.Id;
            _mode.SelectId(args.Id);
            OnModeSelected?.Invoke(_selected, _active);
        };
        content.AddChild(_mode);
        _status = new RichTextLabel();
        content.AddChild(_status);
    }

    public void UpdateStatus(ShuttleCollisionStatus? state)
    {
        Visible = state != null;
        if (state == null)
            return;
        _active = state.Mode != ShuttleCollisionMode.Off;
        _selected = state.PreferredMode;
        _mode.SelectId((int) _selected);
        _activation.Pressed = _active;
        _activation.Text = Loc.GetString(_active ? "shuttle-collision-deactivate" : "shuttle-collision-activate");
        _count.Text = Loc.GetString("shuttle-collision-sensors", ("powered", state.Powered), ("installed", state.Installed));
        string text;
        if (state.Mode == ShuttleCollisionMode.Off)
            text = Loc.GetString("shuttle-collision-disabled");
        else if (state.Suspended)
            text = Loc.GetString("shuttle-collision-suspended");
        else if (state.Powered == 0)
            text = Loc.GetString("shuttle-collision-unpowered");
        else if (state.Distance is { } distance)
            text = Loc.GetString("shuttle-collision-threat", ("distance", distance.ToString("0.#", CultureInfo.InvariantCulture)),
                ("direction", Loc.GetString($"shuttle-collision-direction-{state.Direction.ToString().ToLowerInvariant()}")));
        else
            text = string.Empty;
        if (state.Mode == ShuttleCollisionMode.Automatic && state.InsufficientThrust)
            text += " " + Loc.GetString("shuttle-collision-thrust");
        _status.Visible = !string.IsNullOrWhiteSpace(text);
        _status.SetMessage(FormattedMessage.FromUnformatted(text.Trim()));
    }
}
