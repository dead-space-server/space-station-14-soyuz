// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Content.Shared.DeadSpace._Soyuz.Sobr;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client.DeadSpace._Soyuz.Sobr;

[UsedImplicitly]
public sealed class SobrRemoteBoundUserInterface : BoundUserInterface
{
    private SobrRemoteWindow? _window;

    public SobrRemoteBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<SobrRemoteWindow>();
        _window.OnWake += squad => SendMessage(new SobrRemoteWakeMessage(squad));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is SobrRemoteBuiState cast)
            _window?.Update(cast);
    }
}