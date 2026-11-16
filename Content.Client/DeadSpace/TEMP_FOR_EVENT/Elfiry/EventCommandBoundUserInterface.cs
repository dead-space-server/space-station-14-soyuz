// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace.TEMP_FOR_EVENT.Elfiry;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client.DeadSpace.TEMP_FOR_EVENT.Elfiry;

public sealed class EventCommandBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private EventCommandWindow? _window;

    protected override void Open()
    {
        if (IsOpened)
            return;
        base.Open();
        _window = this.CreateWindow<EventCommandWindow>();
        _window.OnMessage += message => SendMessage(message);
        if (State is EventCommandUiState state)
            _window.UpdateState(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is EventCommandUiState command)
            _window?.UpdateState(command);
    }

}

public sealed class EventCommandWindow : FancyWindow
{
    [Dependency] private readonly IGameTiming _timing = default!;
    private readonly EventCommandMap _map = new();
    private readonly BoxContainer _roster = Column();
    private readonly BoxContainer _points = Column();
    private readonly Label _position = new() { ClipText = true };
    private readonly Label _status = new() { ClipText = true, HorizontalExpand = true };
    private readonly Label _summary = new() { ClipText = true };
    private readonly Label _empty = new() { Text = Loc.GetString("elfiry-command-empty-squad"), ClipText = true };
    private readonly OptionButton _squads = new() { HorizontalExpand = true, Filterable = true, MinHeight = 32 };
    private readonly LineEdit _squadName = new() { HorizontalExpand = true };
    private readonly Button _create;
    private readonly Button _rename;
    private readonly Button _delete;
    private readonly Dictionary<NetEntity, UnitRow> _rows = new();
    private readonly List<(EventSquadOrder Order, Button Button)> _orders = new();
    private EventCommandUiState? _state;
    private Vector2? _selected;
    private int _mapId;
    private int _squad = -1;
    private string _squadKey = string.Empty;
    private string _pointKey = string.Empty;
    private string? _creating;
    private bool _updating;
    private bool _pending;
    private TimeSpan _pendingUntil;
    public event Action<BoundUserInterfaceMessage>? OnMessage;

    public EventCommandWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("elfiry-command-title");
        MinSize = new Vector2(900, 600);
        SetSize = new Vector2(1120, 720);
        var root = Column(8);
        root.Margin = new Thickness(10);
        var body = Row(10);
        body.VerticalExpand = true;
        var mapColumn = Column(6);
        mapColumn.HorizontalExpand = true;
        mapColumn.VerticalExpand = true;
        var toolbar = Row(5);
        AddToolbar(toolbar, "elfiry-command-center", () => _map.CenterCommander());
        AddToolbar(toolbar, "elfiry-command-fit", () => _map.FocusSquad());
        AddToolbar(toolbar, "elfiry-command-zoom-out", () => _map.Zoom(1.25f));
        AddToolbar(toolbar, "elfiry-command-zoom-in", () => _map.Zoom(0.8f));
        toolbar.AddChild(new Control { HorizontalExpand = true });
        _summary.FontColorOverride = Color.FromHex("#bea7aa");
        toolbar.AddChild(_summary);
        mapColumn.AddChild(toolbar);
        _map.MinSize = new Vector2(420, 300);
        _map.HorizontalExpand = true;
        _map.VerticalExpand = true;
        _map.OnPositionSelected += SelectPosition;
        _map.OnSquadSelected += SelectSquad;
        mapColumn.AddChild(Card(_map, "#19191c", 1, true));

        var sidebar = Column(8);
        sidebar.SetWidth = 320;
        sidebar.VerticalExpand = true;
        _squads.OnItemSelected += args =>
        {
            if (!_updating)
                SelectSquad(args.Id);
        };
        _squads.AddItem(Loc.GetString("elfiry-command-all-squads"), -1);
        _squads.AddItem(Loc.GetString("elfiry-command-unassigned"), 0);
        sidebar.AddChild(_squads);
        _squadName.PlaceHolder = Loc.GetString("elfiry-command-squad-name-placeholder");
        _squadName.OnTextChanged += _ =>
        {
            if (_squadName.Text.Length > EventSquadLimits.NameLength)
                _squadName.Text = _squadName.Text[..EventSquadLimits.NameLength];
            UpdateSelection();
        };
        sidebar.AddChild(_squadName);
        var management = Row(4);
        _create = ManageButton("elfiry-command-create", EventSquadManagement.Create);
        _rename = ManageButton("elfiry-command-rename", EventSquadManagement.Rename);
        _delete = ManageButton("elfiry-command-delete", EventSquadManagement.Delete);
        _delete.ToolTip = Loc.GetString("elfiry-command-delete-help");
        management.AddChild(_create);
        management.AddChild(_rename);
        management.AddChild(_delete);
        sidebar.AddChild(management);

        var tabs = new TabContainer { VerticalExpand = true, HorizontalExpand = true };
        var rosterScroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
        _roster.AddChild(_empty);
        rosterScroll.AddChild(_roster);
        tabs.AddChild(rosterScroll);
        TabContainer.SetTabTitle(rosterScroll, Loc.GetString("elfiry-command-roster"));
        var pointScroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
        pointScroll.AddChild(_points);
        tabs.AddChild(pointScroll);
        TabContainer.SetTabTitle(pointScroll, Loc.GetString("elfiry-command-waypoints"));
        sidebar.AddChild(tabs);

        var destination = Column(2);
        destination.AddChild(new Label { Text = Loc.GetString("elfiry-command-destination"), FontColorOverride = Color.FromHex("#d59691") });
        destination.AddChild(_position);
        sidebar.AddChild(Card(destination, "#302124", 6));
        var buttons = new GridContainer { Columns = 2, HorizontalExpand = true };
        foreach (var order in Enum.GetValues<EventSquadOrder>())
        {
            var button = new Button
            {
                Text = Loc.GetString("elfiry-command-order-" + order.ToString().ToLowerInvariant()),
                ToolTip = Loc.GetString("elfiry-command-help-" + order.ToString().ToLowerInvariant()),
                MinHeight = 34,
                HorizontalExpand = true,
            };
            button.OnPressed += _ => SendOrder(order);
            _orders.Add((order, button));
            buttons.AddChild(button);
        }
        var clear = new Button { Text = Loc.GetString("elfiry-command-clear"), MinHeight = 34, HorizontalExpand = true };
        clear.OnPressed += _ =>
        {
            _selected = null;
            _map.Selection = null;
            UpdateSelection();
        };
        buttons.AddChild(clear);
        sidebar.AddChild(buttons);
        body.AddChild(mapColumn);
        body.AddChild(sidebar);
        root.AddChild(body);
        _status.FontColorOverride = Color.FromHex("#d9a29b");
        _status.Visible = false;
        root.AddChild(_status);
        ContentsContainer.AddChild(Card(root, "#221b1e", 0, true));
        SelectSquad(-1);
    }

    private static BoxContainer Column(int spacing = 6)
        => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = spacing };

    private static BoxContainer Row(int spacing = 6)
        => new() { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = spacing };

    private static PanelContainer Card(Control child, string color, int padding, bool expand = false)
    {
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat(Color.FromHex(color)),
            HorizontalExpand = true,
            VerticalExpand = expand,
        };
        child.Margin = new Thickness(padding);
        panel.AddChild(child);
        return panel;
    }

    private static void AddToolbar(BoxContainer row, string text, Action action)
    {
        var button = new Button { Text = Loc.GetString(text), MinHeight = 28 };
        button.OnPressed += _ => action();
        row.AddChild(button);
    }

    private Button ManageButton(string text, EventSquadManagement action)
    {
        var button = new Button { Text = Loc.GetString(text), HorizontalExpand = true, MinHeight = 30 };
        button.OnPressed += _ =>
        {
            if (_pending || _state is not { Authorized: true } state)
                return;
            var name = _squadName.Text.Trim();
            if (action == EventSquadManagement.Create)
                _creating = name;
            Send(new EventSquadManageRequest(state.Token, action, _squad, name));
        };
        return button;
    }

    private void SelectSquad(int squad)
    {
        if (squad > 0 && _state?.Squads.All(item => item.Id != squad) != false)
            squad = -1;
        _squad = squad;
        _map.Squad = squad;
        _squads.SelectId(squad);
        _squads.ToolTip = _state?.Squads.FirstOrDefault(item => item.Id == squad)?.Name;
        _squadName.Text = _state?.Squads.FirstOrDefault(item => item.Id == squad)?.Name ?? string.Empty;
        foreach (var row in _rows.Values)
            row.Control.Visible = squad < 0 || row.Assigned == squad;
        _empty.Visible = !_rows.Values.Any(row => row.Control.Visible);
        UpdateSelection();
    }

    private void SelectPosition(Vector2 position)
    {
        _selected = position;
        _map.Selection = position;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        _position.Text = _selected is { } point
            ? Loc.GetString("elfiry-command-position", ("x", point.X.ToString("0.0", CultureInfo.InvariantCulture)),
                ("y", point.Y.ToString("0.0", CultureInfo.InvariantCulture)))
            : Loc.GetString("elfiry-command-select-position");
        var available = _state?.Units.Any(unit => unit.Alive && (_squad < 0 ? unit.Squad > 0 : _squad > 0 && unit.Squad == _squad)) == true;
        foreach (var (order, button) in _orders)
            button.Disabled = _state?.Authorized != true || _pending || !available ||
                              _selected == null && order != EventSquadOrder.Follow;
        var squad = _state?.Squads.FirstOrDefault(item => item.Id == _squad);
        var name = _squadName.Text.Trim();
        var allowed = _state?.Authorized == true && !_pending;
        var validName = name.Length > 0 && name.Length <= EventSquadLimits.NameLength && !name.Any(char.IsControl);
        _create.Disabled = !allowed || !validName || _state!.Squads.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        _rename.Disabled = !allowed || squad == null || !validName || _state!.Squads.Any(item => item.Id != _squad &&
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        _delete.Disabled = !allowed || squad == null || squad.Members != 0;
        foreach (var row in _rows.Values)
            row.Squad.Disabled = !allowed || !row.Available;
    }

    private void SendOrder(EventSquadOrder order)
    {
        if (_pending || _state is not { Authorized: true } state || _selected == null && order != EventSquadOrder.Follow)
            return;
        Send(new EventSquadRequest(state.Token, _squad, order, state.MapId, _selected ?? state.Commander));
    }

    private void Send(BoundUserInterfaceMessage message)
    {
        _pending = true;
        _pendingUntil = _timing.RealTime + TimeSpan.FromSeconds(10);
        _status.Text = Loc.GetString("elfiry-command-sending");
        _status.Visible = true;
        UpdateSelection();
        OnMessage?.Invoke(message);
    }

    public void UpdateState(EventCommandUiState state)
    {
        var previous = _state;
        _state = state;
        _pending = false;
        if (_creating is { } name && state.Squads.FirstOrDefault(squad => squad.Name == name) is { } created)
        {
            _squad = created.Id;
            _squadName.Text = name;
            _creating = null;
        }
        if (_squad > 0 && state.Squads.All(squad => squad.Id != _squad))
        {
            _squad = -1;
            _squadName.Text = string.Empty;
        }
        if (_mapId != state.MapId)
        {
            _mapId = state.MapId;
            _selected = null;
            _map.Selection = null;
        }
        _map.UpdateState(state);
        _status.Text = state.Status;
        _status.Visible = !string.IsNullOrEmpty(state.Status);
        _summary.Text = Loc.GetString("elfiry-command-count", ("ready", state.Units.Count(unit => unit.Alive)), ("total", state.Units.Length));
        var key = string.Join("|", state.Squads.Select(squad => $"{squad.Id}:{squad.Name}:{squad.Members}"));
        var squadsChanged = key != _squadKey || previous == null;
        _updating = true;
        if (squadsChanged)
        {
            _squadKey = key;
            _squads.Clear();
            _squads.AddItem(Loc.GetString("elfiry-command-all-squads"), -1);
            _squads.AddItem(Loc.GetString("elfiry-command-unassigned"), 0);
            foreach (var squad in state.Squads)
                _squads.AddItem(SquadLabel(squad), squad.Id);
        }
        foreach (var removed in _rows.Keys.Except(state.Units.Select(unit => unit.Entity)).ToArray())
        {
            _roster.RemoveChild(_rows[removed].Control);
            _rows.Remove(removed);
        }
        foreach (var unit in state.Units)
        {
            if (!_rows.TryGetValue(unit.Entity, out var row))
            {
                var column = Column(3);
                column.HorizontalExpand = true;
                var label = new Label { ClipText = true, HorizontalExpand = true };
                var status = new Label { ClipText = true, FontColorOverride = Color.FromHex("#baa8ab") };
                column.AddChild(label);
                column.AddChild(status);
                var selector = new OptionButton { HorizontalExpand = true, MinHeight = 28, Filterable = true };
                selector.ToolTip = Loc.GetString("elfiry-command-assign");
                selector.OnItemSelected += args =>
                {
                    if (!_updating && !_pending && _state is { Authorized: true } current)
                        Send(new EventSquadAssignRequest(current.Token, unit.Entity, args.Id));
                };
                column.AddChild(selector);
                var control = Card(column, unit.Medic ? "#302c2b" : "#312429", 7);
                _roster.AddChild(control);
                row = new UnitRow(control, label, status, selector);
                _rows.Add(unit.Entity, row);
                FillUnitSquads(row, state);
            }
            else if (squadsChanged)
                FillUnitSquads(row, state);
            row.Name.Text = unit.Medic ? Loc.GetString("elfiry-command-medic-name", ("name", unit.Name)) : unit.Name;
            row.Name.ToolTip = unit.Name;
            row.Status.Text = unit.Alive ? unit.Status : Loc.GetString("elfiry-command-unit-unavailable");
            row.Assigned = state.Squads.Any(squad => squad.Id == unit.Squad) ? unit.Squad : 0;
            row.Available = unit.Alive;
            row.Squad.SelectId(row.Assigned);
            row.Squad.ToolTip = state.Squads.FirstOrDefault(squad => squad.Id == row.Assigned)?.Name ?? Loc.GetString("elfiry-command-assign");
            foreach (var squad in state.Squads)
                row.Squad.SetItemDisabled(row.Squad.GetIdx(squad.Id), squad.Members >= EventSquadLimits.Members && squad.Id != row.Assigned);
            row.Control.Visible = _squad < 0 || row.Assigned == _squad;
        }
        _squads.SelectId(_squad);
        _map.Squad = _squad;
        _updating = false;
        _empty.Visible = !_rows.Values.Any(row => row.Control.Visible);
        var pointKey = string.Join("|", state.Points.Select(point => $"{point.Name}/{point.Position}"));
        if (_pointKey != pointKey || _points.ChildCount == 0)
        {
            _pointKey = pointKey;
            _points.RemoveAllChildren();
            foreach (var point in state.Points)
            {
                var button = new Button { Text = point.Name, ClipText = true, ToolTip = point.Name, MinHeight = 32 };
                button.OnPressed += _ =>
                {
                    SelectPosition(point.Position);
                    _map.Center(point.Position);
                };
                _points.AddChild(button);
            }
            if (state.Points.Length == 0)
                _points.AddChild(new Label { Text = Loc.GetString("elfiry-command-no-waypoints"), ClipText = true });
        }
        UpdateSelection();
    }

    private static string SquadLabel(EventCommandSquadState squad)
        => Loc.GetString("elfiry-command-squad-label", ("name", squad.Name.Length > 22 ? squad.Name[..22] + "…" : squad.Name),
            ("count", squad.Members), ("limit", EventSquadLimits.Members));

    private static void FillUnitSquads(UnitRow row, EventCommandUiState state)
    {
        row.Squad.Clear();
        row.Squad.AddItem(Loc.GetString("elfiry-command-unassigned"), 0);
        foreach (var squad in state.Squads)
            row.Squad.AddItem(SquadLabel(squad), squad.Id);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!_pending || _timing.RealTime < _pendingUntil)
            return;
        _pending = false;
        _creating = null;
        _status.Text = Loc.GetString("elfiry-command-request-timeout");
        _status.Visible = true;
        UpdateSelection();
    }

    private sealed class UnitRow(PanelContainer control, Label name, Label status, OptionButton squad)
    {
        public readonly PanelContainer Control = control;
        public readonly Label Name = name;
        public readonly Label Status = status;
        public readonly OptionButton Squad = squad;
        public int Assigned;
        public bool Available;
    }
}

public sealed class EventCommandMap : Control
{
    private readonly Font _font;
    private readonly List<Vector2>[] _surfaces = Enumerable.Range(0, 7).Select(_ => new List<Vector2>()).ToArray();
    private readonly List<(Vector2 From, Vector2 To, Color Color)> _lines = new();
    private static readonly Color[] SurfaceColors =
    [
        Color.FromHex("#1d2a30"), Color.FromHex("#35434e"), Color.FromHex("#9aa9b7"),
        Color.FromHex("#63bdca"), Color.FromHex("#e7b578"), Color.FromHex("#75b5a3"), Color.FromHex("#657688"),
    ];
    private EventCommandUiState? _state;
    private Vector2 _center;
    private Vector2 _pressPosition;
    private Vector2 _pressCenter;
    private bool _pressed;
    private float _range = 48f;
    public int Squad;
    public Vector2? Selection;
    public event Action<Vector2>? OnPositionSelected;
    private float Scale => MathF.Max(1f, Math.Min(PixelWidth, PixelHeight) - 24f * UIScale) / (2f * _range);
    private Vector2 Midpoint => new(PixelWidth / 2f, PixelHeight / 2f);

    public EventCommandMap()
    {
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
        _font = new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 11);
    }

    public void UpdateState(EventCommandUiState state)
    {
        if (_state?.MapId != state.MapId)
        {
            _center = state.Commander;
            _pressed = false;
        }
        _state = state;
    }

    public void Center(Vector2 point) => _center = point;
    public void CenterCommander()
    {
        if (_state != null)
            Center(_state.Commander);
    }

    public void FocusSquad()
    {
        if (_state == null)
            return;
        var positions = _state.Units.Where(unit => unit.Alive && (Squad < 0 || unit.Squad == Squad))
            .Select(unit => unit.Position).ToArray();
        if (positions.Length == 0)
        {
            CenterCommander();
            return;
        }
        var minimum = positions.Aggregate(Vector2.Min);
        var maximum = positions.Aggregate(Vector2.Max);
        Center((minimum + maximum) / 2f);
        _range = Math.Clamp(MathF.Max(maximum.X - minimum.X, maximum.Y - minimum.Y) * 0.65f + 8f, 12f, 512f);
    }

    public void Zoom(float factor) => _range = Math.Clamp(_range * factor, 12f, 512f);

    private Vector2 Project(Vector2 position)
    {
        var relative = position - _center;
        return Midpoint + new Vector2(relative.X, -relative.Y) * Scale;
    }

    private Vector2 Unproject(Vector2 position)
    {
        var relative = (position - Midpoint) / Scale;
        return _center + new Vector2(relative.X, -relative.Y);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if ((args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use) || _pressed)
            return;
        _pressed = true;
        _pressPosition = args.PointerLocation.Position;
        _pressCenter = _center;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (!_pressed || args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use)
            return;
        _pressed = false;
        if (_state != null && Vector2.DistanceSquared(_pressPosition, args.PointerLocation.Position) <= 25f * UIScale * UIScale)
        {
            var point = Unproject(args.RelativePixelPosition);
            var nearest = _state.Units.Where(unit => unit.Alive)
                .MinBy(unit => Vector2.DistanceSquared(point, unit.Position));
            if (nearest != null && Vector2.DistanceSquared(point, nearest.Position) * Scale * Scale < 81f * UIScale * UIScale)
            {
                Squad = nearest.Squad;
                OnSquadSelected?.Invoke(Squad);
            }
            else
                OnPositionSelected?.Invoke(point);
        }
        args.Handle();
    }

    public event Action<int>? OnSquadSelected;

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (_pressed)
        {
            var delta = args.GlobalPixelPosition.Position - _pressPosition;
            _center = _pressCenter - new Vector2(delta.X, -delta.Y) / Scale;
        }
        var point = Unproject(args.RelativePixelPosition);
        ToolTip = null;
        if (_state == null)
            return;
        foreach (var grid in _state.Grids)
        {
            var local = new Angle(-grid.Rotation).RotateVec(point - grid.Position);
            foreach (var surface in grid.Surfaces)
            {
                if (surface.Kind == EventMapKind.Floor)
                    continue;
                var hit = new Angle(-surface.Rotation).RotateVec(local - surface.Bounds.Center) + surface.Bounds.Center;
                if (surface.Bounds.Contains(hit))
                    ToolTip = surface.Name;
            }
        }
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _pressed = false;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        var before = Unproject(args.RelativePixelPosition);
        Zoom(MathF.Pow(0.82f, args.Delta.Y));
        _center += before - Unproject(args.RelativePixelPosition);
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(PixelSizeBox, Color.FromHex("#17171b"));
        if (_state == null)
            return;
        DrawGrid(handle);
        foreach (var grid in _state.Grids.OrderByDescending(grid => grid.Planet))
        {
            foreach (var batch in _surfaces)
                batch.Clear();
            _lines.Clear();
            var rotation = new Angle(grid.Rotation);
            foreach (var surface in grid.Surfaces)
            {
                var bounds = surface.Bounds;
                var category = surface.Kind == EventMapKind.Floor ? grid.Planet ? 0 : 1 : (int) surface.Kind + 1;
                var color = SurfaceColors[category];
                var localRotation = new Angle(surface.Rotation);
                var center = bounds.Center;
                var a = Project(grid.Position + rotation.RotateVec(center + localRotation.RotateVec(bounds.BottomLeft - center)));
                var b = Project(grid.Position + rotation.RotateVec(center + localRotation.RotateVec(bounds.BottomRight - center)));
                var c = Project(grid.Position + rotation.RotateVec(center + localRotation.RotateVec(bounds.TopRight - center)));
                var d = Project(grid.Position + rotation.RotateVec(center + localRotation.RotateVec(bounds.TopLeft - center)));
                var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
                var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
                if (max.X < 0 || max.Y < 0 || min.X > PixelWidth || min.Y > PixelHeight)
                    continue;
                if (surface.Kind == EventMapKind.OpenDoor)
                {
                    _lines.Add((a, b, color));
                    _lines.Add((d, c, color));
                }
                else
                {
                    var batch = _surfaces[category];
                    batch.Add(a);
                    batch.Add(b);
                    batch.Add(c);
                    batch.Add(a);
                    batch.Add(c);
                    batch.Add(d);
                }
                if (surface.Kind == EventMapKind.Equipment && Scale > 8f * UIScale)
                    _lines.Add((a, c, Color.FromHex("#8c9eae")));
            }
            for (var i = 0; i < _surfaces.Length; i++)
            {
                if (_surfaces[i].Count > 0)
                    handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, CollectionsMarshal.AsSpan(_surfaces[i]), SurfaceColors[i]);
            }
            foreach (var line in _lines)
                handle.DrawLine(line.From, line.To, line.Color);
        }
        foreach (var point in _state.Points)
        {
            var position = Project(point.Position);
            handle.DrawCircle(position, 4f * UIScale, Color.FromHex("#e7b578"), false);
            handle.DrawString(_font, position + new Vector2(7, -5) * UIScale, point.Name, UIScale, Color.FromHex("#ddc6a0"));
        }
        var labelledSquads = new HashSet<int>();
        foreach (var unit in _state.Units)
        {
            var position = Project(unit.Position);
            var selected = Squad < 0 || Squad == unit.Squad;
            var color = !unit.Alive ? Color.Gray : unit.Medic ? Color.FromHex("#7de3b9") : Color.FromHex("#ee8d7c");
            if (!selected)
                color = color.WithAlpha(0.45f);
            if (selected && unit.Alive)
                handle.DrawCircle(position, 7f * UIScale, color.WithAlpha(0.15f));
            handle.DrawCircle(position, (selected ? 3.5f : 2f) * UIScale, color);
            if (selected && unit.Alive && unit.Squad > 0 && labelledSquads.Add(unit.Squad) &&
                _state.Squads.FirstOrDefault(candidate => candidate.Id == unit.Squad) is { } labelledSquad)
                handle.DrawString(_font, position + new Vector2(6, -4) * UIScale, labelledSquad.Name, UIScale, color);
        }
        handle.DrawCircle(Project(_state.Commander), 6f * UIScale, Color.White, false);
        if (Selection is { } target)
        {
            var position = Project(target);
            var color = Color.FromHex("#f3ce92");
            handle.DrawCircle(position, 8f * UIScale, color, false);
            handle.DrawLine(position - new Vector2(12, 0) * UIScale, position + new Vector2(12, 0) * UIScale, color);
            handle.DrawLine(position - new Vector2(0, 12) * UIScale, position + new Vector2(0, 12) * UIScale, color);
        }
        var length = MathF.Pow(10f, MathF.Floor(MathF.Log10(_range / 2f)));
        var start = new Vector2(14, PixelHeight / UIScale - 18) * UIScale;
        handle.DrawLine(start, start + new Vector2(length * Scale, 0), Color.FromHex("#b2bec8"));
        handle.DrawString(_font, start + new Vector2(0, -5) * UIScale, $"{length:0} м", UIScale, Color.FromHex("#b2bec8"));
        handle.DrawString(_font, new Vector2(PixelWidth - 22f * UIScale, 22f * UIScale), "N", UIScale, Color.FromHex("#91a5b5"));
    }

    private void DrawGrid(DrawingHandleScreen handle)
    {
        var spacing = MathF.Pow(2f, MathF.Ceiling(MathF.Log2(_range / 8f)));
        var min = Unproject(new Vector2(0, PixelHeight));
        var max = Unproject(new Vector2(PixelWidth, 0));
        var color = Color.FromHex("#30262c");
        for (var x = MathF.Ceiling(min.X / spacing) * spacing; x <= max.X; x += spacing)
            handle.DrawLine(Project(new Vector2(x, min.Y)), Project(new Vector2(x, max.Y)), color);
        for (var y = MathF.Ceiling(min.Y / spacing) * spacing; y <= max.Y; y += spacing)
            handle.DrawLine(Project(new Vector2(min.X, y)), Project(new Vector2(max.X, y)), color);
    }
}
