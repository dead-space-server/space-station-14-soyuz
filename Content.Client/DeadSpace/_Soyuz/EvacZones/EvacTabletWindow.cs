// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace._Soyuz.EvacZones;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.DeadSpace._Soyuz.EvacZones;

public sealed class EvacTabletWindow : FancyWindow
{
    public event Action<List<EvacZoneStroke>, bool>? OnSend;

    private readonly EvacMapControl _map = new();
    private readonly BoxContainer _colors = new()
    {
        Orientation = BoxContainer.LayoutOrientation.Horizontal,
        SeparationOverride = 4,
    };

    private readonly Button _eraser;
    private readonly CheckBox _announce;
    private readonly ButtonGroup _group = new(false);
    private bool _initialized;

    public EvacTabletWindow()
    {
        Title = Loc.GetString("evac-tablet-title");
        MinSize = new Vector2(600, 540);

        _eraser = new Button
        {
            Text = Loc.GetString("evac-eraser"),
            ToggleMode = true,
            Group = _group,
        };
        _eraser.OnPressed += _ => _map.Eraser = true;

        var undo = new Button { Text = Loc.GetString("evac-undo") };
        undo.OnPressed += _ => _map.Undo();

        var clear = new Button { Text = Loc.GetString("evac-clear") };
        clear.OnPressed += _ => _map.Clear();

        _announce = new CheckBox
        {
            Text = Loc.GetString("evac-announce"),
            Pressed = true,
            Visible = false,
        };

        var send = new Button
        {
            Text = Loc.GetString("evac-send"),
            HorizontalExpand = true,
        };
        send.OnPressed += _ => SendPressed();

        var tools = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
        };
        tools.AddChild(_colors);
        tools.AddChild(_eraser);
        tools.AddChild(undo);
        tools.AddChild(clear);

        var bottom = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };
        bottom.AddChild(_announce);
        bottom.AddChild(send);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        root.AddChild(_map);
        root.AddChild(new Label { Text = Loc.GetString("evac-hint") });
        root.AddChild(tools);
        root.AddChild(bottom);

        XamlChildren.Add(root);
    }

    public void Update(EvacTabletBuiState state)
    {
        _map.Map = state.Map;
        _map.Origin = state.Origin;
        _map.Strokes = state.Strokes;
        _announce.Visible = state.CanAnnounce;

        if (!_initialized)
        {
            _initialized = true;
            _map.Center = state.Origin;
        }

        _colors.RemoveAllChildren();
        var first = true;
        foreach (var entry in state.Palette)
        {
            var color = entry.Color;
            var button = new Button
            {
                Text = Loc.GetString(entry.Name),
                ToggleMode = true,
                Group = _group,
                ModulateSelfOverride = color,
            };
            button.OnPressed += _ =>
            {
                _map.Brush = color;
                _map.Eraser = false;
            };

            if (first)
            {
                first = false;
                button.Pressed = true;
                _map.Brush = color;
                _map.Eraser = false;
            }

            _colors.AddChild(button);
        }
    }

    private void SendPressed()
    {
        var copy = new List<EvacZoneStroke>();
        foreach (var stroke in _map.Strokes)
            copy.Add(stroke.Clone());

        OnSend?.Invoke(copy, _announce.Visible && _announce.Pressed);
    }
}
