using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Input;
using Robust.Shared.Utility;
using Content.Client.UserInterface.ControlExtensions;

namespace Content.Client.Guidebook.RichText;

[UsedImplicitly]
public sealed class TextLinkTag : IMarkupTagHandler
{
    [Dependency] private readonly ILogManager _logManager = default!;

    private ISawmill Sawmill => _sawmill ??= _logManager.GetSawmill(Name);
    private ISawmill? _sawmill;

    public static Color LinkColor => Color.CornflowerBlue;

    public string Name => "textlink";

    /// <inheritdoc/>
    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        if (!node.Value.TryGetString(out var text)
            || !node.Attributes.TryGetValue("link", out var linkParameter)
            || !linkParameter.TryGetString(out var link))
        {
            control = null;
            return false;
        }

        var label = new Label();
        label.Text = text;
        var baseColor = GetLinkColor(node);
        var hoverColor = BlendTowardsWhite(baseColor, 0.2f);

        label.MouseFilter = Control.MouseFilterMode.Stop;
        label.FontColorOverride = baseColor;
        label.DefaultCursorShape = Control.CursorShape.Hand;

        label.OnMouseEntered += _ => label.FontColorOverride = hoverColor;
        label.OnMouseExited += _ => label.FontColorOverride = baseColor;
        label.OnKeyBindDown += args => OnKeybindDown(args, link, label);

        control = label;
        return true;
    }

    private static Color GetLinkColor(MarkupNode node)
    {
        if (node.Attributes.TryGetValue("color", out var colorParameter) &&
            colorParameter.TryGetString(out var rawColor))
        {
            if (Color.TryFromHex(rawColor) is { } hexColor)
                return hexColor;

            if (Color.TryFromName(rawColor, out var namedColor))
                return namedColor;
        }

        return LinkColor;
    }

    private static Color BlendTowardsWhite(Color color, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return new Color(
            color.R + (1f - color.R) * amount,
            color.G + (1f - color.G) * amount,
            color.B + (1f - color.B) * amount,
            color.A);
    }

    private void OnKeybindDown(GUIBoundKeyEventArgs args, string link, Control? control)
    {
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        if (control == null)
            return;

        if (control.TryGetParentHandler<ILinkClickHandler>(out var handler))
            handler.HandleClick(link);
        else
            Sawmill.Warning("Warning! No valid ILinkClickHandler found.");
    }
}

public interface ILinkClickHandler
{
    public void HandleClick(string link);
}
