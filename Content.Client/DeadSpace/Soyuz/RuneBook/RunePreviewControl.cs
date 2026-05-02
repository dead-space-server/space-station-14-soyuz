using System.Numerics;
using Content.Shared.DeadSpace.Soyuz.RuneBook;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client.DeadSpace.Soyuz.RuneBook;

public sealed class RunePreviewControl : Control
{
    private static readonly Color Ink = Color.FromHex("#2b1b12");
    private static readonly Color Gold = Color.FromHex("#b88a3b");

    public int RuneId = -1;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = new UIBox2(6, 6, PixelWidth - 6, PixelHeight - 6);
        handle.DrawRect(box, Color.FromHex("#bfa77e").WithAlpha(0.45f));
        handle.DrawRect(box, Ink.WithAlpha(0.72f), false);

        if (RuneId < 0 || RuneId >= RuneBookRuneLibrary.RuneCount)
            return;

        foreach (var segment in RuneBookRuneLibrary.GetRune(RuneId).Segments)
        {
            var start = SnapToPixels(NodeToPreview(box, segment.Start));
            var end = SnapToPixels(NodeToPreview(box, segment.End));

            // DS14-Soyuz: prevent any visible skew by snapping to integer pixels,
            // and keep thickness symmetric.
            var inkCore = Ink.WithAlpha(0.80f);
            var inkEdge = Ink.WithAlpha(0.55f);
            handle.DrawLine(start, end, inkCore);
            handle.DrawLine(start + new Vector2(1, 0), end + new Vector2(1, 0), inkEdge);
            handle.DrawLine(start + new Vector2(-1, 0), end + new Vector2(-1, 0), inkEdge);
            handle.DrawLine(start + new Vector2(0, 1), end + new Vector2(0, 1), inkEdge);
            handle.DrawLine(start + new Vector2(0, -1), end + new Vector2(0, -1), inkEdge);

            var gold = Gold.WithAlpha(0.28f);
            handle.DrawLine(start + new Vector2(1, 1), end + new Vector2(1, 1), gold);
            handle.DrawLine(start + new Vector2(-1, -1), end + new Vector2(-1, -1), gold);
        }
    }

    private static Vector2 NodeToPreview(UIBox2 box, Vector2i node)
    {
        var padding = 16f;
        var width = MathF.Max(box.Width - padding * 2f, 1f);
        var height = MathF.Max(box.Height - padding * 2f, 1f);
        var scale = MathF.Min(width, height) / (RuneBookRuneLibrary.GridSize - 1);
        var left = box.Left + (box.Width - scale * (RuneBookRuneLibrary.GridSize - 1)) / 2f;
        var top = box.Top + (box.Height - scale * (RuneBookRuneLibrary.GridSize - 1)) / 2f;
        return new Vector2(left + node.X * scale, top + node.Y * scale);
    }

    private static Vector2 SnapToPixels(Vector2 v)
    {
        return new Vector2(MathF.Round(v.X), MathF.Round(v.Y));
    }
}
