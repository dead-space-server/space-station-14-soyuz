// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using System.Linq;
using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.Paper;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client.DeadSpace._Soyuz.Paper;

public sealed class HandmadePosterDrawingBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private HandmadePosterDrawingWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<HandmadePosterDrawingWindow>();
        _window.CanEdit = () => PlayerManager.LocalEntity is { } user &&
                                EntMan.System<HandmadePosterSystem>().CanDraw(Owner, user);
        _window.Save += message => SendMessage(message);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is HandmadePosterDrawingState drawing)
            _window?.ApplyState(drawing.Pixels, drawing.Revision);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is HandmadePosterDrawingResult result)
            _window?.ApplyResult(result);
    }
}

public sealed class HandmadePosterDrawingWindow : DefaultWindow
{
    public Func<bool>? CanEdit;
    public event Action<HandmadePosterDrawingSaveMessage>? Save;

    private readonly HandmadePosterCanvas _canvas;
    private readonly Button _save;
    private readonly Button _undo;
    private readonly Button _clear;
    private readonly Button _reload;
    private readonly Button _eraser;
    private readonly List<Button> _colors = new();
    private readonly ColorSelectorSliders _rgb;
    private readonly RichTextLabel _status;
    private uint[] _serverPixels = Array.Empty<uint>();
    private int _serverRevision = -1;
    private int _editRevision = -1;
    private bool _dirty;
    private bool _saving;
    private Guid? _pendingRequest;
    private bool _conflict;
    private bool _hasPermission;
    private string? _error;
    private string? _statusKey;
    private float _permissionRefresh;

    public HandmadePosterDrawingWindow()
    {
        Title = Loc.GetString("handmade-poster-drawing-title");
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        Contents.AddChild(root);
        _canvas = new HandmadePosterCanvas();

        var palette = new BoxContainer { SeparationOverride = 2 };
        for (byte i = 1; i < HandmadePosterDrawing.Palette.Length; i++)
        {
            var colorIndex = i;
            var button = new Button { MinSize = new Vector2(24, 24), ToggleMode = true, Pressed = i == 1 };
            button.AddChild(new PanelContainer
            {
                MinSize = new Vector2(14, 14),
                MouseFilter = MouseFilterMode.Ignore,
                PanelOverride = new StyleBoxFlat { BackgroundColor = HandmadePosterDrawing.Palette[i] },
            });
            button.OnPressed += _ => SelectColor(colorIndex);
            _colors.Add(button);
            palette.AddChild(button);
        }
        root.AddChild(palette);
        _rgb = new ColorSelectorSliders
        {
            SelectorType = ColorSelectorSliders.ColorSelectorType.Rgb,
            IsAlphaVisible = false,
            Color = HandmadePosterDrawing.Palette[1],
            HorizontalExpand = true,
        };
        _rgb.OnColorChanged += color => SelectColor(HandmadePosterDrawing.Pack(color));
        root.AddChild(_rgb);

        var tools = new BoxContainer { SeparationOverride = 4 };
        tools.AddChild(new Label { Text = Loc.GetString("handmade-poster-drawing-brush") });
        var brush = new SpinBox { IsValid = value => value is 1 or 3 or 5 or 7 };
        brush.OverrideValue(1);
        brush.SetButtons(new List<int> { -2 }, new List<int> { 2 });
        brush.ValueChanged += args => _canvas.BrushSize = args.Value;
        tools.AddChild(brush);
        _eraser = new Button { Text = Loc.GetString("handmade-poster-drawing-eraser"), ToggleMode = true };
        _eraser.OnPressed += _ => SelectColor(0u);
        tools.AddChild(_eraser);
        _undo = new Button { Text = Loc.GetString("handmade-poster-drawing-undo") };
        _undo.OnPressed += _ => _canvas.Undo();
        tools.AddChild(_undo);
        _clear = new Button { Text = Loc.GetString("handmade-poster-drawing-clear") };
        _clear.OnPressed += _ => _canvas.Clear();
        tools.AddChild(_clear);
        root.AddChild(tools);
        root.AddChild(new Label { Text = Loc.GetString("handmade-poster-drawing-navigation") });
        root.AddChild(_canvas);
        _status = new RichTextLabel();
        root.AddChild(_status);

        var actions = new BoxContainer { SeparationOverride = 4 };
        _save = new Button { Text = Loc.GetString("handmade-poster-drawing-save"), HorizontalExpand = true, Disabled = true };
        _save.OnPressed += _ =>
        {
            UpdateControls(refreshPermission: true);
            if (_save.Disabled)
                return;
            _saving = true;
            var requestId = Guid.NewGuid();
            _pendingRequest = requestId;
            _canvas.StopStroke();
            UpdateControls();
            Save?.Invoke(new HandmadePosterDrawingSaveMessage((uint[]) _canvas.Pixels.Clone(), _editRevision, requestId));
        };
        actions.AddChild(_save);
        _reload = new Button { Text = Loc.GetString("handmade-poster-drawing-reload"), HorizontalExpand = true };
        _reload.OnPressed += _ => LoadServerDrawing();
        actions.AddChild(_reload);
        root.AddChild(actions);
        _canvas.Changed += () =>
        {
            _dirty = true;
            _error = null;
            UpdateControls();
        };
        UpdateControls();
    }

    private void SelectColor(byte index)
    {
        var color = HandmadePosterDrawing.Palette[index];
        _rgb.Color = color;
        SelectColor(HandmadePosterDrawing.Pack(color));
    }

    private void SelectColor(uint color)
    {
        _canvas.StopStroke();
        _canvas.SelectedColor = color;
        _eraser.Pressed = color == 0;
        for (var i = 0; i < _colors.Count; i++)
            _colors[i].Pressed = HandmadePosterDrawing.Pack(HandmadePosterDrawing.Palette[i + 1]) == color;
    }

    public void ApplyState(uint[] pixels, int revision)
    {
        if (revision <= _serverRevision || !HandmadePosterDrawing.IsValid(pixels))
            return;

        _serverPixels = (uint[]) pixels.Clone();
        _serverRevision = revision;
        var identical = pixels.Length == 0 ? !_canvas.Pixels.Any(p => p != 0) : pixels.SequenceEqual(_canvas.Pixels);
        if (!_dirty && !_saving || identical)
            LoadServerDrawing();
        else if (_editRevision != revision)
            _conflict = true;
        UpdateControls(refreshPermission: true);
    }

    public void ApplyResult(HandmadePosterDrawingResult result)
    {
        // An acknowledgement from a closed editor must never replace the new window's draft.
        if (_pendingRequest != result.RequestId)
            return;

        _pendingRequest = null;
        _saving = false;
        ApplyState(result.Pixels, result.Revision);
        if (result.Error == null)
            LoadServerDrawing();
        else
            _error = result.Error;
        UpdateControls();
    }

    private void LoadServerDrawing()
    {
        _canvas.SetPixels(_serverPixels);
        _editRevision = _serverRevision;
        _dirty = false;
        _conflict = false;
        _error = null;
        UpdateControls();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _permissionRefresh -= (float) args.DeltaSeconds;
        if (_permissionRefresh <= 0)
        {
            _permissionRefresh = 0.25f;
            UpdateControls(refreshPermission: true);
        }
    }

    private void UpdateControls(bool refreshPermission = false)
    {
        if (refreshPermission)
            _hasPermission = CanEdit?.Invoke() == true;
        var editable = _serverRevision >= 0 && !_saving && !_conflict && _hasPermission;
        if (editable && _error == "handmade-poster-drawing-readonly")
            _error = null;
        _canvas.Editable = editable;
        if (!editable)
            _canvas.StopStroke();
        _save.Disabled = !editable || !_dirty || _conflict;
        _undo.Disabled = !editable || !_canvas.HasUndo;
        _clear.Disabled = !editable;
        _reload.Disabled = _saving || _serverRevision < 0;
        var key = _error ?? (_saving ? "handmade-poster-drawing-saving" :
            _conflict ? "handmade-poster-drawing-conflict" :
            !editable ? "handmade-poster-drawing-readonly" :
            _dirty ? "handmade-poster-drawing-unsaved" : "handmade-poster-drawing-ready");
        if (_statusKey != key)
        {
            _statusKey = key;
            _status.SetMessage(Loc.GetString(key));
        }
    }
}

public sealed class HandmadePosterCanvas : Control
{
    [Dependency] private readonly IClyde _clyde = default!;
    public uint[] Pixels { get; private set; } = new uint[HandmadePosterDrawing.PixelCount];
    public bool Editable;
    public uint SelectedColor = HandmadePosterDrawing.Pack(HandmadePosterDrawing.Palette[1]);
    public int BrushSize = 1;
    public bool HasUndo => _undo.Count > 0;
    public event Action? Changed;

    private readonly List<uint[]> _undo = new();
    private OwnedTexture? _texture;
    private bool _textureDirty = true;
    private Vector2i? _lastPoint;
    private float _zoom = 1f;
    private Vector2 _pan;
    private Vector2? _panPoint;

    public HandmadePosterCanvas()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
        MinSize = new Vector2(320, 416);
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    public void SetPixels(uint[] pixels)
    {
        Pixels = pixels.Length == HandmadePosterDrawing.PixelCount
            ? (uint[]) pixels.Clone() : new uint[HandmadePosterDrawing.PixelCount];
        _undo.Clear();
        _textureDirty = true;
        StopStroke();
    }

    public void StopStroke() => _lastPoint = null;

    private void Remember()
    {
        if (_undo.Count == 32)
            _undo.RemoveAt(0);
        _undo.Add((uint[]) Pixels.Clone());
    }

    public void Undo()
    {
        if (!Editable || !HasUndo)
            return;
        Pixels = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        StopStroke();
        _textureDirty = true;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (!Editable || !Pixels.Any(p => p != 0))
            return;
        Remember();
        Array.Clear(Pixels, 0, Pixels.Length);
        StopStroke();
        _textureDirty = true;
        Changed?.Invoke();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_textureDirty)
        {
            using var image = HandmadePosterTextures.CreateImage(Pixels);
            if (_texture == null)
                _texture = _clyde.LoadTextureFromImage(image, "poster-drawing-editor");
            else
                _texture.SetSubImage(Vector2i.Zero, image);
            _textureDirty = false;
        }
        var rect = GetCanvasRect();
        handle.DrawRect(rect, Color.White);
        if (_texture != null)
            handle.DrawTextureRect(_texture, rect);
    }

    private UIBox2 GetCanvasRect()
    {
        var imageSize = new Vector2(HandmadePosterDrawing.Width, HandmadePosterDrawing.Height);
        var scale = Math.Min(PixelSize.X / imageSize.X, PixelSize.Y / imageSize.Y);
        var size = imageSize * Math.Max(0, scale) * _zoom;
        return UIBox2.FromDimensions(((Vector2) PixelSize - size) / 2 + _pan, size);
    }

    private void ClampPan()
    {
        var overflow = GetCanvasRect().Size - (Vector2) PixelSize;
        _pan = new Vector2(
            Math.Clamp(_pan.X, -Math.Max(0, overflow.X) / 2, Math.Max(0, overflow.X) / 2),
            Math.Clamp(_pan.Y, -Math.Max(0, overflow.Y) / 2, Math.Max(0, overflow.Y) / 2));
    }

    protected override void Resized()
    {
        base.Resized();
        ClampPan();
        StopStroke();
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        var rect = GetCanvasRect();
        if (args.Delta.Y == 0 || rect.Width <= 0 || rect.Height <= 0)
            return;

        var anchor = args.RelativePixelPosition;
        var imagePosition = (anchor - rect.TopLeft) / rect.Size;
        _zoom = Math.Clamp(_zoom * MathF.Pow(1.25f, args.Delta.Y), 1f, 16f);
        var size = GetCanvasRect().Size;
        _pan = anchor - imagePosition * size - ((Vector2) PixelSize - size) / 2;
        ClampPan();
        StopStroke();
        _panPoint = null;
        args.Handle();
    }

    private bool TryGetPoint(Vector2 position, out Vector2i point)
    {
        point = default;
        var rect = GetCanvasRect();
        if (rect.Width <= 0 || rect.Height <= 0 || !rect.Contains(position) ||
            !UIBox2.FromDimensions(Vector2.Zero, (Vector2) PixelSize).Contains(position))
            return false;
        var relative = (position - rect.TopLeft) / rect.Size;
        point = new Vector2i(Math.Clamp((int) (relative.X * HandmadePosterDrawing.Width), 0, HandmadePosterDrawing.Width - 1),
            Math.Clamp((int) (relative.Y * HandmadePosterDrawing.Height), 0, HandmadePosterDrawing.Height - 1));
        return true;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            StopStroke();
            _panPoint = args.RelativePixelPosition;
            args.Handle();
            return;
        }
        if (!Editable || args.Function != EngineKeyFunctions.UIClick || !TryGetPoint(args.RelativePixelPosition, out var point))
            return;
        Remember();
        PaintLine(point, point);
        _lastPoint = Editable ? point : null;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIRightClick)
            _panPoint = null;
        if (args.Function == EngineKeyFunctions.UIClick)
            StopStroke();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (_panPoint is { } panPoint)
        {
            _pan += args.RelativePixelPosition - panPoint;
            _panPoint = args.RelativePixelPosition;
            ClampPan();
            args.Handle();
            return;
        }
        if (!Editable || _lastPoint is not { } last || !TryGetPoint(args.RelativePixelPosition, out var point))
            return;
        PaintLine(last, point);
        _lastPoint = Editable ? point : null;
        args.Handle();
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        StopStroke();
        _panPoint = null;
    }

    private void PaintLine(Vector2i from, Vector2i to)
    {
        var steps = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
        var changed = false;
        var radius = BrushSize / 2;
        for (var step = 0; step <= steps; step++)
        {
            var fraction = steps == 0 ? 0 : (float) step / steps;
            var x = (int) MathF.Round(from.X + (to.X - from.X) * fraction);
            var y = (int) MathF.Round(from.Y + (to.Y - from.Y) * fraction);
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (x + dx < 0 || x + dx >= HandmadePosterDrawing.Width || y + dy < 0 || y + dy >= HandmadePosterDrawing.Height)
                    continue;
                var index = (y + dy) * HandmadePosterDrawing.Width + x + dx;
                if (Pixels[index] == SelectedColor)
                    continue;
                Pixels[index] = SelectedColor;
                changed = true;
            }
        }
        if (changed)
        {
            _textureDirty = true;
            Changed?.Invoke();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _texture?.Dispose();
        base.Dispose(disposing);
    }
}
