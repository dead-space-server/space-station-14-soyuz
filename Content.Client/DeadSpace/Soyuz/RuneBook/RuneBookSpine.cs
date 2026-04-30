using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client.DeadSpace.Soyuz.RuneBook;

public sealed class RuneBookSpine : Control
{
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var leather = Color.FromHex("#241712");
        var edge = Color.FromHex("#7c5a35");
        var glow = Color.FromHex("#b88a3b").WithAlpha(0.28f);

        handle.DrawRect(PixelSizeBox, leather);
        handle.DrawLine(new Vector2(3, 0), new Vector2(3, PixelHeight), edge);
        handle.DrawLine(new Vector2(PixelWidth - 3, 0), new Vector2(PixelWidth - 3, PixelHeight), edge);

        for (var y = 48f; y < PixelHeight; y += 112f)
        {
            handle.DrawCircle(new Vector2(PixelWidth / 2f, y), 8f, glow);
            handle.DrawCircle(new Vector2(PixelWidth / 2f, y), 4f, edge);
        }
    }
}
