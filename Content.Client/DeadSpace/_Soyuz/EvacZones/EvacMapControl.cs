// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.EvacZones;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Client.DeadSpace._Soyuz.EvacZones;

/// <summary>
/// Карта космоса планшета: сетки кораблей/станций, зоны, рисование (ЛКМ), ластик, сдвиг (ПКМ), масштаб (колесо).
/// </summary>
public sealed class EvacMapControl : Control
{
    [Dependency] private readonly IEntityManager _entMan = default!;

    private readonly SharedTransformSystem _xform;
    private readonly List<Vector2> _tris = new();

    public MapId Map;
    public Vector2 Origin;
    public Vector2 Center;
    public float Scale = 3f;
    public List<EvacZoneStroke> Strokes = new();
    public Color Brush = Color.Green;
    public Color MarkerColor = Color.White;
    public bool Eraser;

    public event Action? Changed;

    private List<Vector2>? _current;
    private bool _erasing;
    private bool _panning;
    private Vector2 _lastPan;

    public EvacMapControl()
    {
        IoCManager.InjectDependencies(this);
        _xform = _entMan.System<SharedTransformSystem>();

        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
        HorizontalExpand = true;
        VerticalExpand = true;
        MinSize = new Vector2(560, 420);
    }

    private Vector2 Mid => new Vector2(PixelSize.X, PixelSize.Y) / 2f;

    private Vector2 ToScreen(Vector2 world) => Mid + new Vector2(world.X - Center.X, Center.Y - world.Y) * Scale;

    private Vector2 ToWorld(Vector2 screen)
    {
        var d = (screen - Mid) / Scale;
        return new Vector2(Center.X + d.X, Center.Y - d.Y);
    }

    public void Undo()
    {
        if (Strokes.Count == 0)
            return;

        Strokes.RemoveAt(Strokes.Count - 1);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (Strokes.Count == 0)
            return;

        Strokes.Clear();
        Changed?.Invoke();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.UIClick)
        {
            var world = ToWorld(args.RelativePixelPosition);
            if (Eraser)
            {
                _erasing = true;
                EraseAt(world);
            }
            else
            {
                _current = new List<Vector2> { world };
            }

            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            _panning = true;
            _lastPan = args.RelativePixelPosition;
            args.Handle();
        }
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function == EngineKeyFunctions.UIClick)
        {
            _erasing = false;

            if (_current != null)
            {
                if (_current.Count == 1)
                    _current.Add(_current[0] + new Vector2(0.01f, 0f));

                if (Strokes.Count < EvacZoneConstants.MaxStrokes)
                {
                    Strokes.Add(new EvacZoneStroke(Brush, _current));
                    Changed?.Invoke();
                }

                _current = null;
            }

            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            _panning = false;
            args.Handle();
        }
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        var pos = args.RelativePixelPosition;

        if (_panning)
        {
            var delta = pos - _lastPan;
            Center -= new Vector2(delta.X, -delta.Y) / Scale;
            _lastPan = pos;
            return;
        }

        var world = ToWorld(pos);

        if (_erasing)
        {
            EraseAt(world);
        }
        else if (_current != null && _current.Count < EvacZoneConstants.MaxPointsPerStroke)
        {
            if ((world - _current[^1]).Length() >= EvacZoneConstants.MinPointStep)
                _current.Add(world);
        }
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        var factor = args.Delta.Y > 0 ? 1.15f : 1f / 1.15f;
        Scale = Math.Clamp(Scale * factor, 0.3f, 40f);
        args.Handle();
    }

    private void EraseAt(Vector2 world)
    {
        var threshold = 10f / Scale + EvacZoneConstants.Thickness / 2f;
        var removed = Strokes.RemoveAll(s => Distance(world, s.Points) <= threshold);
        if (removed > 0)
            Changed?.Invoke();
    }

    private static float Distance(Vector2 p, List<Vector2> points)
    {
        if (points.Count == 0)
            return float.MaxValue;

        if (points.Count == 1)
            return (p - points[0]).Length();

        var best = float.MaxValue;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var ab = b - a;
            var len2 = ab.LengthSquared();
            var t = len2 < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
            best = MathF.Min(best, (p - (a + ab * t)).Length());
        }

        return best;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = new Vector2(PixelSize.X, PixelSize.Y);
        handle.DrawRect(new UIBox2(Vector2.Zero, size), new Color(8, 12, 18));

        DrawReferenceGrid(handle, size);
        DrawMapGrids(handle);

        foreach (var stroke in Strokes)
            DrawStroke(handle, stroke.Points, stroke.Color.WithAlpha(0.85f));

        if (_current != null)
            DrawStroke(handle, _current, Brush);

        var o = ToScreen(Origin);
        handle.DrawCircle(o, 4f, MarkerColor);
        handle.DrawCircle(o, 8f, MarkerColor.WithAlpha(0.5f), false);
    }

    private void DrawStroke(DrawingHandleScreen handle, List<Vector2> worldPoints, Color color)
    {
        _tris.Clear();
        var screen = new List<Vector2>(worldPoints.Count);
        foreach (var p in worldPoints)
            screen.Add(ToScreen(p));

        var width = MathF.Max(2f, EvacZoneConstants.Thickness * Scale);
        EvacZoneGeometry.AppendStroke(_tris, screen, width / 2f, true);
        EvacZoneGeometry.Draw(handle, _tris, color);
    }

    private void DrawReferenceGrid(DrawingHandleScreen handle, Vector2 size)
    {
        var step = 10f;
        while (step * Scale < 40f)
            step *= 5f;

        var topLeft = ToWorld(Vector2.Zero);
        var bottomRight = ToWorld(size);
        var color = new Color(40, 60, 80, 120);

        for (var x = MathF.Floor(topLeft.X / step) * step; x <= bottomRight.X; x += step)
        {
            var sx = ToScreen(new Vector2(x, 0)).X;
            handle.DrawLine(new Vector2(sx, 0), new Vector2(sx, size.Y), color);
        }

        for (var y = MathF.Floor(bottomRight.Y / step) * step; y <= topLeft.Y; y += step)
        {
            var sy = ToScreen(new Vector2(0, y)).Y;
            handle.DrawLine(new Vector2(0, sy), new Vector2(size.X, sy), color);
        }
    }

    private void DrawMapGrids(DrawingHandleScreen handle)
    {
        var color = new Color(130, 160, 190, 220);
        var query = _entMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var grid, out var xform))
        {
            if (xform.MapID != Map)
                continue;

            var matrix = _xform.GetWorldMatrix(uid);
            var box = grid.LocalAABB;
            var c0 = ToScreen(Vector2.Transform(box.BottomLeft, matrix));
            var c1 = ToScreen(Vector2.Transform(box.TopLeft, matrix));
            var c2 = ToScreen(Vector2.Transform(box.TopRight, matrix));
            var c3 = ToScreen(Vector2.Transform(box.BottomRight, matrix));

            handle.DrawLine(c0, c1, color);
            handle.DrawLine(c1, c2, color);
            handle.DrawLine(c2, c3, color);
            handle.DrawLine(c3, c0, color);
        }
    }
}
