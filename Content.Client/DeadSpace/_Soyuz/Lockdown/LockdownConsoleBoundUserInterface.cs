// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.DeadSpace._Soyuz.Lockdown;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Client.UserInterface;

namespace Content.Client.DeadSpace._Soyuz.Lockdown;

[UsedImplicitly]
public sealed class LockdownConsoleBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IPlayerManager _player = default!;

    private LockdownConsoleWindow? _window;

    public LockdownConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<LockdownConsoleWindow>();

        _window.OnStartDepartment += id => SendMessage(new LockdownStartDepartmentMessage(id));
        _window.OnCancelDepartment += id => SendMessage(new LockdownCancelDepartmentMessage(id));
        _window.OnConfirmStation += () => SendMessage(new LockdownConfirmStationMessage());
        _window.OnCancelStation += () => SendMessage(new LockdownCancelStationMessage());
        _window.OnOpenSection += id => SendMessage(new SecurityConsoleOpenSectionMessage(id));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not LockdownConsoleBoundUserInterfaceState cast)
            return;

        // Допуск есть, если сервер включил нашу сущность в список владельцев импланта.
        var hasAccess = _player.LocalEntity is { } player
                        && cast.AccessActors.Contains(EntMan.GetNetEntity(player));

        _window?.UpdateState(cast, hasAccess);
    }
}