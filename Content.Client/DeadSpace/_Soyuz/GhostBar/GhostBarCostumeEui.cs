// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Client.Eui;
using Content.Shared.DeadSpace._Soyuz.GhostBar;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client.DeadSpace._Soyuz.GhostBar;

[UsedImplicitly]
public sealed class GhostBarCostumeEui : BaseEui
{
    private readonly GhostBarWindow _window;

    public GhostBarCostumeEui()
    {
        _window = new GhostBarWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnConfirm += () => SendMessage(new GhostBarButtonPressedMessage());
        _window.OnCostumeToggle += id => SendMessage(new GhostBarCostumeToggleMessage { CostumeId = id });
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not GhostBarEuiState s)
            return;

        _window.UpdateState(s);
    }
}