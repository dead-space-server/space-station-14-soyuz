using System.Numerics;
using Content.Shared.DeadSpace.Soyuz.RuneBook;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client.DeadSpace.Soyuz.RuneBook;

public sealed class RuneBookVerifiedRuneCard : Control
{
    private static readonly Color Ink = Color.FromHex("#2b1b12");
    private static readonly Color Gold = Color.FromHex("#b88a3b");
    private static readonly Color Highlight = Color.FromHex("#3ba14b");

    private readonly Font _nameFont;
    private readonly Font _numberFont;
    private readonly string _name;
    private readonly bool _highlight;

    public readonly int RuneId;

    public event Action<int>? OnPressed;

    public RuneBookVerifiedRuneCard(int runeId, bool highlight)
    {
        RuneId = runeId;
        _highlight = highlight;
        MouseFilter = MouseFilterMode.Stop;

        var cache = IoCManager.Resolve<IResourceCache>();
        _nameFont = new VectorFont(cache.GetResource<FontResource>("/EngineFonts/NotoSans/NotoSans-Regular.ttf"), 11);
        _numberFont = new VectorFont(cache.GetResource<FontResource>("/EngineFonts/NotoSans/NotoSans-Regular.ttf"), 9);

        _name = ResolveRuneName(runeId);
        ToolTip = Loc.GetString("rune-book-ui-verified-card-tooltip", ("rune", runeId + 1), ("name", _name));
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        if (!IsClick(args))
        {
            base.KeyBindUp(args);
            return;
        }

        OnPressed?.Invoke(RuneId);
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var fill = Color.FromHex("#cdb48b").WithAlpha(0.72f);
        var border = (_highlight ? Highlight : Ink).WithAlpha(0.9f);

        handle.DrawRect(PixelSizeBox, fill);
        handle.DrawRect(Inset(PixelSizeBox, 1), border, false);
        handle.DrawRect(Inset(PixelSizeBox, 4), Gold.WithAlpha(0.45f), false);

        // Header: number + name.
        var number = (RuneId + 1).ToString("00");
        var numSize = handle.GetDimensions(_numberFont, number, 1f);
        handle.DrawString(_numberFont, new Vector2(10, 8), number, Ink);

        var nameStart = 10 + numSize.X + 8;
        var maxNameWidth = PixelWidth - nameStart - 10;
        var name = _name;
        if (maxNameWidth > 0)
            name = Truncate(handle, _nameFont, _name, maxNameWidth);

        handle.DrawString(_nameFont, new Vector2(nameStart, 5), name, Ink);

        // Preview area.
        var preview = Inset(PixelSizeBox, 12);
        preview = new UIBox2(preview.Left, preview.Top + 28, preview.Right, preview.Bottom - 10);
        DrawRune(handle, preview);

        if (_highlight)
            handle.DrawRect(Inset(PixelSizeBox, 2), Highlight.WithAlpha(0.10f));
    }

    private void DrawRune(DrawingHandleScreen handle, UIBox2 box)
    {
        foreach (var segment in RuneBookRuneLibrary.GetRune(RuneId).Segments)
        {
            var start = NodeToCard(box, segment.Start);
            var end = NodeToCard(box, segment.End);
            handle.DrawLine(start, end, Ink);
            handle.DrawLine(start + new Vector2(1, 0), end + new Vector2(1, 0), Gold.WithAlpha(0.55f));
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

    private static string ResolveRuneName(int runeId)
    {
        if (!RuneBookRuneLibrary.TryGetRunePrototypeId(runeId, out var protoId))
            return Loc.GetString("rune-book-ui-verified-card-unknown");

        if (IoCManager.Instance == null ||
            !IoCManager.Instance.TryResolveType(typeof(IPrototypeManager), out var protoObj) ||
            protoObj is not IPrototypeManager proto)
        {
            return protoId;
        }

        return proto.TryIndex<RuneBookRunePrototype>(protoId, out var runeProto)
            ? runeProto.Name
            : protoId;
    }

    private static string Truncate(DrawingHandleScreen handle, Font font, string text, float maxWidth)
    {
        if (handle.GetDimensions(font, text, 1f).X <= maxWidth)
            return text;

        const string suffix = "…";
        var low = 0;
        var high = text.Length;

        while (low < high)
        {
            var mid = (low + high) / 2;
            var candidate = text.Substring(0, Math.Max(mid, 0)) + suffix;
            if (handle.GetDimensions(font, candidate, 1f).X <= maxWidth)
                low = mid + 1;
            else
                high = mid;
        }

        var len = Math.Max(low - 1, 0);
        return text.Substring(0, len) + suffix;
    }
}
