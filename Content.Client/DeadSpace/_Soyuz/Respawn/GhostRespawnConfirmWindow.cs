// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client.DeadSpace._Soyuz.Respawn;

/// <summary>
/// Окно подтверждения респавна с предупреждением не играть уже использованным в раунде персонажем
/// </summary>
public sealed class GhostRespawnConfirmWindow : DefaultWindow
{
    public event Action? OnConfirm;

    public GhostRespawnConfirmWindow()
    {
        Title = Loc.GetString("respawn-confirm-title");
        MinSize = new Vector2(460, 0);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 12,
        };

        var text = new RichTextLabel
        {
            MaxWidth = 440,
        };

        var markup = Loc.GetString("respawn-confirm-text")
                     + "\n\n[color=#e06a6a][bold]" + Loc.GetString("respawn-confirm-warning-title") + "[/bold][/color] "
                     + Loc.GetString("respawn-confirm-warning");
        text.SetMessage(FormattedMessage.FromMarkupPermissive(markup));
        root.AddChild(text);

        var buttons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };

        var confirm = new Button
        {
            Text = Loc.GetString("respawn-confirm-yes"),
            HorizontalExpand = true,
        };
        confirm.OnPressed += _ =>
        {
            OnConfirm?.Invoke();
            Close();
        };

        var cancel = new Button
        {
            Text = Loc.GetString("respawn-confirm-no"),
            HorizontalExpand = true,
        };
        cancel.OnPressed += _ => Close();

        buttons.AddChild(confirm);
        buttons.AddChild(cancel);
        root.AddChild(buttons);

        Contents.AddChild(root);
    }
}