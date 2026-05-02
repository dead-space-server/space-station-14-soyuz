using System;
using System.Numerics;
using Content.Shared.DeadSpace.Soyuz.RuneBook;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.DeadSpace.Soyuz.RuneBook;

/// <summary>
/// Non-interactive rune "how to draw" preview that animates segments in order.
/// </summary>
public sealed class RuneInstructionControl : Control
{
    private static readonly Color Parchment = Color.FromHex("#bfa77e");
    private static readonly Color ParchmentBorder = Color.FromHex("#2b1b12").WithAlpha(0.72f);
    private static readonly Color GridLine = Color.FromHex("#6a5138").WithAlpha(0.22f);
    private static readonly Color GridNode = Color.FromHex("#ead8b9").WithAlpha(0.95f);
    private static readonly Color Ink = Color.FromHex("#0f0a07");
    private static readonly Color InkShadow = Color.FromHex("#b88a3b").WithAlpha(0.35f);

    private RuneBookSegment[] _segments = Array.Empty<RuneBookSegment>();
    private float _timer;
    private int _step;

    public int RuneId
    {
        get => _runeId;
        set
        {
            if (_runeId == value)
                return;

            _runeId = value;
            ReloadRune();
        }
    }

    private int _runeId = -1;

    /// <summary>Seconds per segment.</summary>
    public float StepDuration { get; set; } = 0.55f;

    /// <summary>If true, keeps animating from the start.</summary>
    public bool Loop { get; set; } = true;

    public void Restart()
    {
        _timer = 0f;
        _step = 0;
    }

    private void ReloadRune()
    {
        if (_runeId < 0 || _runeId >= RuneBookRuneLibrary.RuneCount)
        {
            _segments = Array.Empty<RuneBookSegment>();
            Restart();
            return;
        }

        _segments = RuneBookRuneLibrary.GetRune(_runeId).Segments;
        Restart();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_segments.Length == 0)
            return;

        var duration = MathF.Max(StepDuration, 0.05f);
        _timer += args.DeltaSeconds;

        while (_timer >= duration)
        {
            _timer -= duration;
            _step++;

            if (_step > _segments.Length)
            {
                if (Loop)
                    _step = 0;
                else
                    _step = _segments.Length;
            }
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = new UIBox2(8, 8, PixelWidth - 8, PixelHeight - 8);
        handle.DrawRect(box, Parchment.WithAlpha(0.58f));
        handle.DrawRect(box, ParchmentBorder, false);

        DrawGrid(handle, box);

        if (_segments.Length == 0)
            return;

        // Draw already "learned" segments in ink.
        var count = Math.Clamp(_step, 0, _segments.Length);
        for (var i = 0; i < count; i++)
            DrawSegment(handle, box, _segments[i], Ink, thick: true);

        // Highlight current segment being shown.
        if (count < _segments.Length)
        {
            var seg = _segments[count];
            DrawSegment(handle, box, seg, InkShadow, thick: true);
            DrawSegment(handle, box, seg, Ink, thick: false);
        }
    }

    private static void DrawGrid(DrawingHandleScreen handle, UIBox2 box)
    {
        var grid = Inset(box, 22);
        var scale = MathF.Min(grid.Width, grid.Height) / (RuneBookRuneLibrary.GridSize - 1);
        var left = grid.Left + (grid.Width - scale * (RuneBookRuneLibrary.GridSize - 1)) / 2f;
        var top = grid.Top + (grid.Height - scale * (RuneBookRuneLibrary.GridSize - 1)) / 2f;

        // Lines.
        for (var i = 0; i < RuneBookRuneLibrary.GridSize; i++)
        {
            var x = left + i * scale;
            var y = top + i * scale;
            handle.DrawLine(new Vector2(left, y), new Vector2(left + (RuneBookRuneLibrary.GridSize - 1) * scale, y), GridLine);
            handle.DrawLine(new Vector2(x, top), new Vector2(x, top + (RuneBookRuneLibrary.GridSize - 1) * scale), GridLine);
        }

        // Nodes.
        for (var y = 0; y < RuneBookRuneLibrary.GridSize; y++)
        {
            for (var x = 0; x < RuneBookRuneLibrary.GridSize; x++)
            {
                var pos = new Vector2(left + x * scale, top + y * scale);
                handle.DrawCircle(pos, 1.35f, GridNode);
            }
        }
    }

    private static void DrawSegment(DrawingHandleScreen handle, UIBox2 box, RuneBookSegment segment, Color color, bool thick)
    {
        var start = NodeToBox(box, segment.Start);
        var end = NodeToBox(box, segment.End);

        handle.DrawLine(start, end, color);
        handle.DrawLine(start + new Vector2(1, 0), end + new Vector2(1, 0), color);

        if (!thick)
            return;

        handle.DrawLine(start + new Vector2(0, 1), end + new Vector2(0, 1), color);
        handle.DrawLine(start + new Vector2(-1, 0), end + new Vector2(-1, 0), color);
    }

    private static Vector2 NodeToBox(UIBox2 box, Vector2i node)
    {
        var padding = 42f;
        var width = MathF.Max(box.Width - padding * 2f, 1f);
        var height = MathF.Max(box.Height - padding * 2f, 1f);
        var scale = MathF.Min(width, height) / (RuneBookRuneLibrary.GridSize - 1);
        var left = box.Left + (box.Width - scale * (RuneBookRuneLibrary.GridSize - 1)) / 2f;
        var top = box.Top + (box.Height - scale * (RuneBookRuneLibrary.GridSize - 1)) / 2f;
        return new Vector2(left + node.X * scale, top + node.Y * scale);
    }

    private static UIBox2 Inset(UIBox2 box, float amount)
    {
        return new UIBox2(box.Left + amount, box.Top + amount, box.Right - amount, box.Bottom - amount);
    }
}
