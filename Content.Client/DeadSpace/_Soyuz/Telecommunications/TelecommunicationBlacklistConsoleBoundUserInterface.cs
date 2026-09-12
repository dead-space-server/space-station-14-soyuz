// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using Content.Shared.DeadSpace._Soyuz.Telecommunications;
using Robust.Client.UserInterface;

namespace Content.Client.DeadSpace._Soyuz.Telecommunications;

public sealed class TelecommunicationBlacklistConsoleBoundUserInterface(EntityUid owner, Enum uiKey)
    : BoundUserInterface(owner, uiKey)
{
    private TelecommunicationBlacklistConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<TelecommunicationBlacklistConsoleWindow>();
        _window.OnTargetSelected += target =>
            SendMessage(new SelectTelecommunicationBlacklistTargetMessage(target));
        _window.OnApply += (target, channels) =>
            SendMessage(new ApplyTelecommunicationBlacklistMessage(target, channels));
        _window.OnRefresh += () =>
            SendMessage(new RefreshTelecommunicationBlacklistConsoleMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is TelecommunicationBlacklistConsoleState consoleState)
            _window?.UpdateState(consoleState);
    }
}
