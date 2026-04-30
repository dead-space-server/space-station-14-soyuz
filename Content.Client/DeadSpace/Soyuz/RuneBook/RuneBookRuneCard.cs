using System.Numerics;
using Content.Shared.DeadSpace.Soyuz.RuneBook;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.DeadSpace.Soyuz.RuneBook;

public sealed class RuneBookRuneCard : Control
{
    private static readonly Color Ink = Color.FromHex("#2b1b12");
    private static readonly Color Gold = Color.FromHex("#b88a3b");
    private static readonly Color Blue = Color.FromHex("#64c5ff");

    private readonly Font _font;

    public readonly int RuneId;
    public bool Selected;
    public bool Disabled;

    public event Action<int>? OnSelected;

    public RuneBookRuneCard(int runeId)
    {
        RuneId = runeId;
        MouseFilter = MouseFilterMode.Stop;
        ToolTip = Loc.GetString("rune-book-ui-rune-card-tooltip", ("rune", runeId + 1));

        var cache = IoCManager.Resolve<IResourceCache>();
        _font = new VectorFont(cache.GetResource<FontResource>("/EngineFonts/NotoSans/NotoSans-Regular.ttf"), 9);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        if (Disabled || !IsClick(args))
        {
            base.KeyBindUp(args);
            return;
        }

        OnSelected?.Invoke(RuneId);
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var fill = Disabled
            ? Color.FromHex("#745b48").WithAlpha(0.52f)
            : Selected
                ? Color.FromHex("#2d4961").WithAlpha(0.75f)
                : Color.FromHex("#bfa77e").WithAlpha(0.58f);

        var border = Selected ? Blue : Ink.WithAlpha(0.82f);
        handle.DrawRect(PixelSizeBox, fill);
        handle.DrawRect(Inset(PixelSizeBox, 1), border, false);
        handle.DrawRect(Inset(PixelSizeBox, 4), Gold.WithAlpha(Selected ? 0.9f : 0.45f), false);

        var preview = Inset(PixelSizeBox, 14);
        preview = new UIBox2(preview.Left, preview.Top + 8, preview.Right, preview.Bottom - 6);
        DrawRune(handle, preview);

        var number = (RuneId + 1).ToString("00");
        var textSize = handle.GetDimensions(_font, number, 1f);
        handle.DrawString(_font, new Vector2((PixelWidth - textSize.X) / 2f, 5), number, Selected ? Color.White : Ink);

        if (Disabled)
            handle.DrawRect(PixelSizeBox, Color.Black.WithAlpha(0.28f));
    }

    private void DrawRune(DrawingHandleScreen handle, UIBox2 box)
    {
        foreach (var segment in RuneBookRuneLibrary.GetRune(RuneId).Segments)
        {
            var start = NodeToCard(box, segment.Start);
            var end = NodeToCard(box, segment.End);
            handle.DrawLine(start, end, Selected ? Color.White : Ink);
            handle.DrawLine(start + new Vector2(1, 0), end + new Vector2(1, 0), Gold.WithAlpha(0.68f));
        }
    }

    private static Vector2 NodeToCard(UIBox2 box, Vector2i node)
    {
        var scale = MathF.Min(box.Width, box.Height) / (RuneBookRuneLibrary.GridSize - 1);
        var left = box.Left + (box.Width - scale * (RuneBookRuneLibrary.GridSize - 1)) / 2f;
        var top = box.Top + (box.Height - scale * (RuneBookRuneLibrary.GridSize - 1)) / 2f;
        return new Vector2(left + node.X * scale, top + node.Y * scale);
    }

    private static UIBox2 Inset(UIBox2 box, float amount)
    {
        return new UIBox2(box.Left + amount, box.Top + amount, box.Right - amount, box.Bottom - amount);
    }

    private static bool IsClick(GUIBoundKeyEventArgs args)
    {
        return args.Function == EngineKeyFunctions.Use ||
               args.Function == EngineKeyFunctions.UIClick;
    }
}
