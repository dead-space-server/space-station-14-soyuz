using System.Numerics;
using Content.Shared.DeadSpace.Soyuz.RuneBook;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.DeadSpace.Soyuz.RuneBook;

public sealed class RuneDrawingControl : Control
{
    private static readonly Color Parchment = Color.FromHex("#bfa77e");
    private static readonly Color ParchmentDark = Color.FromHex("#4b3322");
    private static readonly Color GridLine = Color.FromHex("#6a5138").WithAlpha(0.32f);
    private static readonly Color GridNode = Color.FromHex("#ead8b9");
    private static readonly Color GuideLine = Color.FromHex("#b98d42").WithAlpha(0.28f);
    private static readonly Color DrawLine = Color.FromHex("#64c5ff");
    private static readonly Color DrawCore = Color.FromHex("#effbff");
    private static readonly Color RippedOverlay = Color.FromHex("#3a211f").WithAlpha(0.62f);

    private readonly List<RuneBookSegment> _segments = new();
    private readonly HashSet<RuneBookSegment> _segmentSet = new();

    private bool _drawing;
    private Vector2i _dragStart;
    private Vector2i _hoverNode;

    public int TargetRune = -1;
    public bool PageRipped;
    public bool HasSegments => _segments.Count > 0;

    public event Action? OnSegmentsChanged;

    public RuneDrawingControl()
    {
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
    }

    public RuneBookSegment[] GetSegments()
    {
        return _segments.ToArray();
    }

    public void Clear()
    {
        _segments.Clear();
        _segmentSet.Clear();
        _drawing = false;
        OnSegmentsChanged?.Invoke();
    }

    public void Undo()
    {
        if (_segments.Count == 0)
            return;

        var last = _segments[^1];
        _segments.RemoveAt(_segments.Count - 1);
        _segmentSet.Remove(last);
        OnSegmentsChanged?.Invoke();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (!IsDrawingClick(args) || PageRipped)
        {
            base.KeyBindDown(args);
            return;
        }

        if (!TrySnapNode(args.RelativePixelPosition, out var node))
            return;

        _drawing = true;
        _dragStart = node;
        _hoverNode = node;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        if (!IsDrawingClick(args))
        {
            base.KeyBindUp(args);
            return;
        }

        if (!_drawing)
            return;

        _drawing = false;

        if (!PageRipped && TrySnapNode(args.RelativePixelPosition, out var node) && node != _dragStart)
        {
            var segment = new RuneBookSegment(_dragStart, node);
            if (_segmentSet.Add(segment))
            {
                _segments.Add(segment);
                OnSegmentsChanged?.Invoke();
            }
        }

        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (!_drawing)
            return;

        if (TrySnapNode(args.RelativePixelPosition, out var node))
            _hoverNode = node;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, Parchment);
        DrawOrnaments(handle);

        var box = GetGridBox();
        handle.DrawRect(box, Color.FromHex("#d2bd92").WithAlpha(0.54f));
        handle.DrawRect(box, ParchmentDark, filled: false);

        DrawGrid(handle, box);
        DrawGuide(handle, box);
        DrawSegments(handle, box);
        DrawPreview(handle, box);

        if (!PageRipped)
            return;

        handle.DrawRect(PixelSizeBox, RippedOverlay);
        DrawTear(handle);
    }

    private void DrawOrnaments(DrawingHandleScreen handle)
    {
        var border = Color.FromHex("#7c5a35");
        var glow = Color.FromHex("#d3a64d").WithAlpha(0.22f);
        var inset = 8f;

        handle.DrawRect(new UIBox2(inset, inset, PixelWidth - inset, PixelHeight - inset), border, false);
        handle.DrawRect(new UIBox2(inset + 4, inset + 4, PixelWidth - inset - 4, PixelHeight - inset - 4), glow, false);

        var centerTop = new Vector2(PixelWidth / 2f, 17f);
        var centerBottom = new Vector2(PixelWidth / 2f, PixelHeight - 17f);
        handle.DrawLine(centerTop - new Vector2(34, 0), centerTop + new Vector2(34, 0), border);
        handle.DrawCircle(centerTop, 5f, glow, false);
        handle.DrawLine(centerBottom - new Vector2(34, 0), centerBottom + new Vector2(34, 0), border);
        handle.DrawCircle(centerBottom, 5f, glow, false);
    }

    private void DrawGrid(DrawingHandleScreen handle, UIBox2 box)
    {
        var spacing = box.Width / (RuneBookRuneLibrary.GridSize - 1);

        for (var i = 0; i < RuneBookRuneLibrary.GridSize; i++)
        {
            var x = box.Left + i * spacing;
            var y = box.Top + i * spacing;
            handle.DrawLine(new Vector2(x, box.Top), new Vector2(x, box.Bottom), GridLine);
            handle.DrawLine(new Vector2(box.Left, y), new Vector2(box.Right, y), GridLine);
        }

        for (var y = 0; y < RuneBookRuneLibrary.GridSize; y++)
        {
            for (var x = 0; x < RuneBookRuneLibrary.GridSize; x++)
            {
                var position = NodeToPixel(box, new Vector2i(x, y));
                var accent = x == 0 ||
                             y == 0 ||
                             x == RuneBookRuneLibrary.GridSize - 1 ||
                             y == RuneBookRuneLibrary.GridSize - 1 ||
                             x == 7 ||
                             y == 7;

                handle.DrawCircle(position, accent ? 2.7f : 2.1f, ParchmentDark.WithAlpha(0.7f));
                handle.DrawCircle(position, accent ? 1.6f : 1.2f, GridNode);
            }
        }
    }

    private void DrawGuide(DrawingHandleScreen handle, UIBox2 box)
    {
        if (TargetRune < 0 || TargetRune >= RuneBookRuneLibrary.RuneCount)
            return;

        foreach (var segment in RuneBookRuneLibrary.GetRune(TargetRune).Segments)
        {
            var start = NodeToPixel(box, segment.Start);
            var end = NodeToPixel(box, segment.End);
            DrawGlowLine(handle, start, end, GuideLine, 1);
        }
    }

    private void DrawSegments(DrawingHandleScreen handle, UIBox2 box)
    {
        foreach (var segment in _segments)
        {
            var start = NodeToPixel(box, segment.Start);
            var end = NodeToPixel(box, segment.End);
            DrawGlowLine(handle, start, end, DrawLine.WithAlpha(0.55f), 3);
            DrawGlowLine(handle, start, end, DrawCore, 1);
            handle.DrawCircle(start, 4f, DrawLine.WithAlpha(0.42f));
            handle.DrawCircle(end, 4f, DrawLine.WithAlpha(0.42f));
            handle.DrawCircle(start, 2f, DrawCore);
            handle.DrawCircle(end, 2f, DrawCore);
        }
    }

    private void DrawPreview(DrawingHandleScreen handle, UIBox2 box)
    {
        if (!_drawing || _hoverNode == _dragStart)
            return;

        var start = NodeToPixel(box, _dragStart);
        var end = NodeToPixel(box, _hoverNode);
        DrawGlowLine(handle, start, end, Color.FromHex("#ffffff").WithAlpha(0.88f), 2);
        handle.DrawCircle(start, 4f, DrawCore);
        handle.DrawCircle(end, 4f, DrawCore);
    }

    private void DrawTear(DrawingHandleScreen handle)
    {
        var paper = Color.FromHex("#e4d1ad");
        var shadow = Color.FromHex("#2b1715");
        var y = PixelHeight * 0.48f;
        var last = new Vector2(28, y);

        for (var i = 1; i < 12; i++)
        {
            var x = 28 + i * (PixelWidth - 56) / 11f;
            var next = new Vector2(x, y + (i % 2 == 0 ? 18 : -16));
            handle.DrawLine(last + new Vector2(0, 4), next + new Vector2(0, 4), shadow);
            handle.DrawLine(last, next, paper);
            last = next;
        }
    }

    private UIBox2 GetGridBox()
    {
        var size = MathF.Min(PixelWidth, PixelHeight) - 54f;
        size = MathF.Max(size, 64f);
        var left = (PixelWidth - size) / 2f;
        var top = (PixelHeight - size) / 2f;
        return new UIBox2(left, top, left + size, top + size);
    }

    private bool TrySnapNode(Vector2 pixel, out Vector2i node)
    {
        var box = GetGridBox();
        var spacing = box.Width / (RuneBookRuneLibrary.GridSize - 1);

        if (pixel.X < box.Left - spacing * 0.35f ||
            pixel.X > box.Right + spacing * 0.35f ||
            pixel.Y < box.Top - spacing * 0.35f ||
            pixel.Y > box.Bottom + spacing * 0.35f)
        {
            node = default;
            return false;
        }

        var x = (int) MathF.Round((pixel.X - box.Left) / spacing);
        var y = (int) MathF.Round((pixel.Y - box.Top) / spacing);
        x = Math.Clamp(x, 0, RuneBookRuneLibrary.GridSize - 1);
        y = Math.Clamp(y, 0, RuneBookRuneLibrary.GridSize - 1);

        node = new Vector2i(x, y);
        return true;
    }

    private static Vector2 NodeToPixel(UIBox2 box, Vector2i node)
    {
        var spacing = box.Width / (RuneBookRuneLibrary.GridSize - 1);
        return new Vector2(box.Left + node.X * spacing, box.Top + node.Y * spacing);
    }

    private static void DrawGlowLine(DrawingHandleScreen handle, Vector2 start, Vector2 end, Color color, int radius)
    {
        var delta = end - start;
        var length = delta.Length();
        var perpendicular = length > 0.01f
            ? new Vector2(-delta.Y / length, delta.X / length)
            : Vector2.Zero;

        for (var offset = radius; offset > 0; offset--)
        {
            var alpha = color.A * (0.22f + 0.16f * offset);
            var shade = color.WithAlpha(Math.Clamp(alpha, 0f, 1f));
            handle.DrawLine(start + perpendicular * offset, end + perpendicular * offset, shade);
            handle.DrawLine(start - perpendicular * offset, end - perpendicular * offset, shade);
        }

        handle.DrawLine(start, end, color);
    }

    private static bool IsDrawingClick(GUIBoundKeyEventArgs args)
    {
        return args.Function == EngineKeyFunctions.Use ||
               args.Function == EngineKeyFunctions.UIClick;
    }
}
