using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared.DeadSpace.Soyuz.RuneBook;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.DeadSpace.Soyuz.RuneBook;

public sealed class RuneDrawingControl : Control
{
    public enum RuneDrawingTool : byte
    {
        Point,
        Line,
        Diagonal,
        Arc,
        Broken,
        Erase,
        Selection
    }

    private static readonly Color Parchment = Color.FromHex("#bfa77e");
    private static readonly Color ParchmentDark = Color.FromHex("#4b3322");
    private static readonly Color GridLine = Color.FromHex("#6a5138").WithAlpha(0.32f);
    private static readonly Color GridNode = Color.FromHex("#ead8b9");
    // DS14-Soyuz: guide (ghost) sample line. Darkened ~35% for better readability.
    private static readonly Color GuideLine = Color.FromHex("#785b2a").WithAlpha(0.28f);
    private static readonly Color DrawLine = Color.FromHex("#64c5ff");
    private static readonly Color DrawCore = Color.FromHex("#effbff");
    private static readonly Color EraseLine = Color.FromHex("#b53a36").WithAlpha(0.65f);
    private static readonly Color SelectLine = Color.FromHex("#f3cd77").WithAlpha(0.75f);
    private static readonly Color RippedOverlay = Color.FromHex("#3a211f").WithAlpha(0.62f);

    private readonly List<RuneBookSegment> _segments = new();
    private readonly HashSet<RuneBookSegment> _segmentSet = new();
    private readonly HashSet<Vector2i> _points = new();
    private readonly HashSet<RuneBookSegment> _selectedSegments = new();
    private readonly HashSet<Vector2i> _selectedPoints = new();
    private readonly List<Vector2i> _selectedNodes = new(2);
    private readonly Stack<EditAction> _history = new();

    private bool _drawing;
    private Vector2i _dragStart;
    private Vector2i _hoverNode;

    private RuneDrawingTool _tool = RuneDrawingTool.Line;

    private bool _chainActive;
    private Vector2i _chainStart;
    private Vector2i _chainCurrent;

    private bool _strokeActive;
    private Vector2i _strokeStart;
    private Vector2i _strokeCurrent;

    private byte _arcStage;
    private Vector2i _arcStart;
    private Vector2i _arcEnd;

    public int TargetRune = -1;
    public bool PageRipped;
    public bool HasSegments => _segments.Count > 0;
    public bool HasEdits => _segments.Count > 0 || _points.Count > 0;
    public RuneDrawingTool Tool => _tool;

    public bool SnapToNodes { get; set; } = true;
    public bool NodeLine { get; set; }
    public bool ShowGuide { get; set; } = true;
    public bool MultiStroke { get; set; }

    public event Action? OnSegmentsChanged;

    public RuneDrawingControl()
    {
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
    }

    public void SetTool(RuneDrawingTool tool)
    {
        if (_tool == tool)
            return;

        _tool = tool;
        CancelActiveToolState(clearSelection: tool != RuneDrawingTool.Selection);
    }

    public void CloseStroke()
    {
        if (PageRipped)
            return;

        if (_tool == RuneDrawingTool.Selection && _selectedNodes.Count == 2)
        {
            var action = new EditAction();
            AddSegment(action, new RuneBookSegment(_selectedNodes[0], _selectedNodes[1]));
            Commit(action);
            _selectedNodes.Clear();
            return;
        }

        if (_tool == RuneDrawingTool.Broken && _strokeActive && _strokeCurrent != _strokeStart)
        {
            var action = new EditAction();
            AddSegment(action, new RuneBookSegment(_strokeCurrent, _strokeStart));
            Commit(action);
            _strokeActive = false;
            return;
        }

        if ((_tool == RuneDrawingTool.Line ||
             _tool == RuneDrawingTool.Diagonal ||
             _tool == RuneDrawingTool.Erase) &&
            MultiStroke &&
            _chainActive &&
            _chainCurrent != _chainStart)
        {
            var action = new EditAction();
            var segment = new RuneBookSegment(_chainCurrent, _chainStart);

            if (_tool == RuneDrawingTool.Erase)
                RemoveSegment(action, segment);
            else
                AddSegment(action, segment);

            Commit(action);
            _chainActive = false;
            return;
        }

        if (_tool == RuneDrawingTool.Arc)
        {
            _arcStage = 0;
        }
    }

    public RuneBookSegment[] GetSegments()
    {
        return _segments.ToArray();
    }

    public void Clear()
    {
        _segments.Clear();
        _segmentSet.Clear();
        _points.Clear();
        _selectedSegments.Clear();
        _selectedPoints.Clear();
        _selectedNodes.Clear();
        _history.Clear();
        _drawing = false;
        _strokeActive = false;
        _arcStage = 0;
        _chainActive = false;
        OnSegmentsChanged?.Invoke();
    }

    public void Undo()
    {
        if (_history.Count == 0)
            return;

        var action = _history.Pop();

        foreach (var seg in action.AddedSegments)
            RemoveSegmentInternal(seg);
        foreach (var seg in action.RemovedSegments)
            AddSegmentInternal(seg);

        foreach (var point in action.AddedPoints)
            _points.Remove(point);
        foreach (var point in action.RemovedPoints)
            _points.Add(point);

        _selectedSegments.Clear();
        _selectedPoints.Clear();
        _selectedNodes.Clear();
        OnSegmentsChanged?.Invoke();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (PageRipped)
        {
            base.KeyBindDown(args);
            return;
        }

        if (IsAltClick(args))
        {
            HandleAltClick(args);
            return;
        }

        if (!IsDrawingClick(args))
        {
            base.KeyBindDown(args);
            return;
        }

        if (!TrySnapNode(args.RelativePixelPosition, out var node))
            return;

        _hoverNode = node;

        switch (_tool)
        {
            case RuneDrawingTool.Point:
                HandlePointClick(node, add: !_points.Contains(node), args);
                return;
            case RuneDrawingTool.Broken:
                HandleBrokenClick(node, args);
                return;
            case RuneDrawingTool.Arc:
                HandleArcClick(node, args);
                return;
            case RuneDrawingTool.Selection:
                HandleSelectClick(args);
                return;
            case RuneDrawingTool.Line:
            case RuneDrawingTool.Diagonal:
            case RuneDrawingTool.Erase:
                if (MultiStroke)
                    HandleChainClick(node, args);
                else
                {
                    _drawing = true;
                    _dragStart = node;
                    args.Handle();
                }

                return;
            default:
                base.KeyBindDown(args);
                return;
        }
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

        if (!TrySnapNode(args.RelativePixelPosition, out var node))
            return;

        if (node == _dragStart)
        {
            if (_tool == RuneDrawingTool.Erase)
            {
                var action = new EditAction();
                RemovePoint(action, node);
                Commit(action);
            }

            args.Handle();
            return;
        }

        var actionUp = new EditAction();

        if (_tool == RuneDrawingTool.Erase)
        {
            RemoveSegment(actionUp, new RuneBookSegment(_dragStart, node));
        }
        else
        {
            var end = _tool == RuneDrawingTool.Diagonal
                ? GetDiagonalEnd(_dragStart, node)
                : node;

            if (end != _dragStart)
                AddSegment(actionUp, new RuneBookSegment(_dragStart, end));
        }

        Commit(actionUp);

        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

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
        if (!ShowGuide)
            return;

        if (TargetRune < 0 || TargetRune >= RuneBookRuneLibrary.RuneCount)
            return;

        foreach (var segment in RuneBookRuneLibrary.GetRune(TargetRune).Segments)
        {
            var start = NodeToPixel(box, segment.Start);
            var end = NodeToPixel(box, segment.End);
            // DS14-Soyuz: 2x thickness for the ghost sample.
            DrawGlowLine(handle, start, end, GuideLine, 2);
        }
    }

    private void DrawSegments(DrawingHandleScreen handle, UIBox2 box)
    {
        foreach (var segment in _segments)
        {
            var start = NodeToPixel(box, segment.Start);
            var end = NodeToPixel(box, segment.End);
            var selected = _selectedSegments.Contains(segment);
            var line = selected ? SelectLine : DrawLine.WithAlpha(0.55f);
            DrawGlowLine(handle, start, end, line, 3);
            DrawGlowLine(handle, start, end, DrawCore, 1);
            handle.DrawCircle(start, 4f, DrawLine.WithAlpha(0.42f));
            handle.DrawCircle(end, 4f, DrawLine.WithAlpha(0.42f));
            handle.DrawCircle(start, 2f, DrawCore);
            handle.DrawCircle(end, 2f, DrawCore);
        }

        foreach (var point in _points)
        {
            var position = NodeToPixel(box, point);
            var selected = _selectedPoints.Contains(point);
            handle.DrawCircle(position, 6f, (selected ? SelectLine : DrawLine).WithAlpha(0.35f));
            handle.DrawCircle(position, 3f, DrawCore);
        }

        foreach (var node in _selectedNodes)
        {
            var position = NodeToPixel(box, node);
            handle.DrawCircle(position, 7f, SelectLine.WithAlpha(0.28f));
            handle.DrawCircle(position, 4f, SelectLine);
        }
    }

    private void DrawPreview(DrawingHandleScreen handle, UIBox2 box)
    {
        switch (_tool)
        {
            case RuneDrawingTool.Line:
            case RuneDrawingTool.Diagonal:
            case RuneDrawingTool.Erase:
                if (!_drawing)
                    return;

                var endNode = _tool == RuneDrawingTool.Diagonal
                    ? GetDiagonalEnd(_dragStart, _hoverNode)
                    : _hoverNode;

                if (endNode == _dragStart)
                    return;

                var start = NodeToPixel(box, _dragStart);
                var end = NodeToPixel(box, endNode);
                DrawGlowLine(handle, start, end, _tool == RuneDrawingTool.Erase ? EraseLine : DrawCore.WithAlpha(0.9f), 2);
                handle.DrawCircle(start, 4f, DrawCore);
                handle.DrawCircle(end, 4f, DrawCore);
                return;
            case RuneDrawingTool.Broken:
                if (!_strokeActive || _hoverNode == _strokeCurrent)
                    return;

                var s = NodeToPixel(box, _strokeCurrent);
                var e = NodeToPixel(box, _hoverNode);
                DrawGlowLine(handle, s, e, DrawCore.WithAlpha(0.9f), 2);
                handle.DrawCircle(s, 4f, DrawCore);
                handle.DrawCircle(e, 4f, DrawCore);
                return;
            case RuneDrawingTool.Arc:
                if (_arcStage == 0)
                    return;

                var arcFrom = _arcStage == 1 ? _arcStart : _arcEnd;
                if (arcFrom == _hoverNode)
                    return;

                var arcA = NodeToPixel(box, arcFrom);
                var arcB = NodeToPixel(box, _hoverNode);
                DrawGlowLine(handle, arcA, arcB, DrawCore.WithAlpha(0.9f), 2);
                handle.DrawCircle(arcA, 4f, DrawCore);
                handle.DrawCircle(arcB, 4f, DrawCore);
                return;
            case RuneDrawingTool.Selection:
                if (_selectedNodes.Count == 0)
                    return;

                if (_selectedNodes.Count == 2)
                {
                    var fromSel = NodeToPixel(box, _selectedNodes[0]);
                    var toSel = NodeToPixel(box, _selectedNodes[1]);
                    DrawGlowLine(handle, fromSel, toSel, SelectLine, 2);
                    return;
                }

                var selFrom = _selectedNodes[0];
                if (selFrom == _hoverNode)
                    return;

                var selA = NodeToPixel(box, selFrom);
                var selB = NodeToPixel(box, _hoverNode);
                DrawGlowLine(handle, selA, selB, SelectLine, 2);
                return;
            default:
                return;
        }
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

        if (SnapToNodes)
            return true;

        var snapped = NodeToPixel(box, node);
        var distSq = (snapped - pixel).LengthSquared();
        var threshold = spacing * 0.20f;
        return distSq <= threshold * threshold;
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

    private static bool IsAltClick(GUIBoundKeyEventArgs args)
    {
        return args.Function == EngineKeyFunctions.UseSecondary ||
               args.Function == EngineKeyFunctions.UIRightClick;
    }

    private void CancelActiveToolState(bool clearSelection)
    {
        _drawing = false;
        _arcStage = 0;
        _strokeActive = false;
        _chainActive = false;

        if (clearSelection)
        {
            _selectedSegments.Clear();
            _selectedPoints.Clear();
            _selectedNodes.Clear();
        }
    }

    private void HandleAltClick(GUIBoundKeyEventArgs args)
    {
        if (!TrySnapNode(args.RelativePixelPosition, out var node))
        {
            args.Handle();
            return;
        }

        _hoverNode = node;

        switch (_tool)
        {
            case RuneDrawingTool.Point:
                HandlePointClick(node, add: false, args);
                return;
            case RuneDrawingTool.Broken:
                _strokeActive = false;
                args.Handle();
                return;
            case RuneDrawingTool.Arc:
                _arcStage = 0;
                args.Handle();
                return;
            case RuneDrawingTool.Erase:
                HandleEraseNode(node, args);
                return;
            case RuneDrawingTool.Selection:
                _selectedNodes.Clear();
                _selectedSegments.Clear();
                _selectedPoints.Clear();
                args.Handle();
                return;
            case RuneDrawingTool.Line:
            case RuneDrawingTool.Diagonal:
                if (MultiStroke)
                {
                    _chainActive = false;
                    args.Handle();
                    return;
                }

                args.Handle();
                return;
            default:
                args.Handle();
                return;
        }
    }

    private void HandlePointClick(Vector2i node, bool add, GUIBoundKeyEventArgs args)
    {
        var action = new EditAction();
        if (add)
            AddPoint(action, node);
        else
            RemovePoint(action, node);

        Commit(action);
        args.Handle();
    }

    private void HandleBrokenClick(Vector2i node, GUIBoundKeyEventArgs args)
    {
        if (!_strokeActive)
        {
            _strokeActive = true;
            _strokeStart = node;
            _strokeCurrent = node;
            args.Handle();
            return;
        }

        if (node == _strokeCurrent)
        {
            _strokeActive = false;
            args.Handle();
            return;
        }

        var action = new EditAction();
        AddSegment(action, new RuneBookSegment(_strokeCurrent, node));
        Commit(action);

        _strokeCurrent = node;
        if (node == _strokeStart)
            _strokeActive = false;

        args.Handle();
    }

    private void HandleArcClick(Vector2i node, GUIBoundKeyEventArgs args)
    {
        if (_arcStage == 0)
        {
            _arcStart = node;
            _arcStage = 1;
            args.Handle();
            return;
        }

        if (_arcStage == 1)
        {
            if (node == _arcStart)
            {
                args.Handle();
                return;
            }

            _arcEnd = node;
            _arcStage = 2;
            args.Handle();
            return;
        }

        var action = new EditAction();
        AddBezierArc(action, _arcStart, _arcEnd, node);
        Commit(action);
        _arcStage = 0;
        args.Handle();
    }

    private void HandleEraseNode(Vector2i node, GUIBoundKeyEventArgs args)
    {
        var action = new EditAction();
        RemovePoint(action, node);

        var toRemove = _segments.Where(seg => seg.Start == node || seg.End == node).ToArray();
        foreach (var seg in toRemove)
            RemoveSegment(action, seg);

        Commit(action);
        args.Handle();
    }

    private void HandleSelectClick(GUIBoundKeyEventArgs args)
    {
        if (!TrySnapNode(args.RelativePixelPosition, out var node))
        {
            args.Handle();
            return;
        }

        if (_selectedNodes.Count > 0 && _selectedNodes.Contains(node))
        {
            _selectedNodes.Remove(node);
            args.Handle();
            return;
        }

        if (_selectedNodes.Count == 0)
        {
            _selectedNodes.Add(node);
            args.Handle();
            return;
        }

        if (_selectedNodes.Count == 1)
        {
            _selectedNodes.Add(node);
            args.Handle();
            return;
        }

        _selectedNodes.Clear();
        _selectedNodes.Add(node);
        args.Handle();
    }

    private void HandleChainClick(Vector2i node, GUIBoundKeyEventArgs args)
    {
        if (!_chainActive)
        {
            _chainActive = true;
            _chainStart = node;
            _chainCurrent = node;
            args.Handle();
            return;
        }

        if (node == _chainCurrent)
        {
            _chainActive = false;
            args.Handle();
            return;
        }

        var action = new EditAction();
        var end = _tool == RuneDrawingTool.Diagonal
            ? GetDiagonalEnd(_chainCurrent, node)
            : node;

        if (end != _chainCurrent)
        {
            var segment = new RuneBookSegment(_chainCurrent, end);
            if (_tool == RuneDrawingTool.Erase)
                RemoveSegment(action, segment);
            else
                AddSegment(action, segment);
        }

        Commit(action);
        _chainCurrent = end;

        if (_chainCurrent == _chainStart)
            _chainActive = false;

        args.Handle();
    }

    private static Vector2i GetDiagonalEnd(Vector2i start, Vector2i rawEnd)
    {
        var dx = rawEnd.X - start.X;
        var dy = rawEnd.Y - start.Y;
        if (dx == 0 && dy == 0)
            return start;

        var signX = dx >= 0 ? 1 : -1;
        var signY = dy >= 0 ? 1 : -1;
        var magnitude = Math.Max(Math.Abs(dx), Math.Abs(dy));

        var maxX = signX > 0 ? RuneBookRuneLibrary.GridSize - 1 - start.X : start.X;
        var maxY = signY > 0 ? RuneBookRuneLibrary.GridSize - 1 - start.Y : start.Y;
        magnitude = Math.Clamp(magnitude, 0, Math.Min(maxX, maxY));

        return new Vector2i(start.X + signX * magnitude, start.Y + signY * magnitude);
    }

    private void AddBezierArc(EditAction action, Vector2i start, Vector2i end, Vector2i control)
    {
        var s = new Vector2(start.X, start.Y);
        var e = new Vector2(end.X, end.Y);
        var c = new Vector2(control.X, control.Y);

        var steps = Math.Clamp((int) (Vector2.Distance(s, e) * 6f), 12, 64);
        Vector2i? last = null;

        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float) steps;
            var u = 1f - t;
            var point = u * u * s + 2f * u * t * c + t * t * e;
            var node = new Vector2i(
                Math.Clamp((int) MathF.Round(point.X), 0, RuneBookRuneLibrary.GridSize - 1),
                Math.Clamp((int) MathF.Round(point.Y), 0, RuneBookRuneLibrary.GridSize - 1));

            if (last != null && last.Value == node)
                continue;

            if (last != null)
                AddSegment(action, new RuneBookSegment(last.Value, node));

            last = node;
        }
    }

    private void Commit(EditAction action)
    {
        if (!action.HasChanges)
            return;

        _history.Push(action);
        OnSegmentsChanged?.Invoke();
    }

    private void AddSegment(EditAction action, RuneBookSegment segment)
    {
        if (!NodeLine)
        {
            AddSegmentSingle(action, segment);
            return;
        }

        foreach (var expanded in ExpandSegment(segment))
            AddSegmentSingle(action, expanded);
    }

    private void RemoveSegment(EditAction action, RuneBookSegment segment)
    {
        if (!NodeLine)
        {
            RemoveSegmentSingle(action, segment);
            return;
        }

        foreach (var expanded in ExpandSegment(segment))
            RemoveSegmentSingle(action, expanded);
    }

    private void AddSegmentSingle(EditAction action, RuneBookSegment segment)
    {
        if (_segmentSet.Add(segment))
        {
            _segments.Add(segment);
            action.AddedSegments.Add(segment);
        }
    }

    private void RemoveSegmentSingle(EditAction action, RuneBookSegment segment)
    {
        if (_segmentSet.Remove(segment))
        {
            _segments.Remove(segment);
            action.RemovedSegments.Add(segment);
            _selectedSegments.Remove(segment);
        }
    }

    private void AddPoint(EditAction action, Vector2i node)
    {
        if (_points.Add(node))
        {
            action.AddedPoints.Add(node);
            _selectedPoints.Remove(node);
        }
    }

    private void RemovePoint(EditAction action, Vector2i node)
    {
        if (_points.Remove(node))
        {
            action.RemovedPoints.Add(node);
            _selectedPoints.Remove(node);
        }
    }

    private void AddSegmentInternal(RuneBookSegment segment)
    {
        if (_segmentSet.Add(segment))
            _segments.Add(segment);
    }

    private void RemoveSegmentInternal(RuneBookSegment segment)
    {
        if (_segmentSet.Remove(segment))
            _segments.Remove(segment);
    }

    private static IEnumerable<RuneBookSegment> ExpandSegment(RuneBookSegment segment)
    {
        var start = segment.Start;
        var end = segment.End;
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var steps = GreatestCommonDivisor(Math.Abs(deltaX), Math.Abs(deltaY));

        if (steps <= 1)
        {
            yield return segment;
            yield break;
        }

        var step = new Vector2i(deltaX / steps, deltaY / steps);
        var current = start;
        for (var i = 0; i < steps; i++)
        {
            var next = current + step;
            yield return new RuneBookSegment(current, next);
            current = next;
        }
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            var remainder = left % right;
            left = right;
            right = remainder;
        }

        return Math.Max(left, 1);
    }

    private bool TryPickSegment(Vector2 pixel, out RuneBookSegment segment)
    {
        var box = GetGridBox();
        var best = float.MaxValue;
        var found = false;
        segment = default;

        foreach (var seg in _segments)
        {
            var a = NodeToPixel(box, seg.Start);
            var b = NodeToPixel(box, seg.End);
            var dist = DistanceToSegmentSquared(pixel, a, b);
            if (dist < best)
            {
                best = dist;
                segment = seg;
                found = true;
            }
        }

        return found && best <= 12f * 12f;
    }

    private static float DistanceToSegmentSquared(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var ap = point - a;
        var lenSq = ab.LengthSquared();
        if (lenSq <= 0.0001f)
            return ap.LengthSquared();

        var t = Math.Clamp(Vector2.Dot(ap, ab) / lenSq, 0f, 1f);
        var closest = a + ab * t;
        return (point - closest).LengthSquared();
    }

    private sealed class EditAction
    {
        public readonly List<RuneBookSegment> AddedSegments = new();
        public readonly List<RuneBookSegment> RemovedSegments = new();
        public readonly List<Vector2i> AddedPoints = new();
        public readonly List<Vector2i> RemovedPoints = new();

        public bool HasChanges =>
            AddedSegments.Count > 0 ||
            RemovedSegments.Count > 0 ||
            AddedPoints.Count > 0 ||
            RemovedPoints.Count > 0;
    }
}
