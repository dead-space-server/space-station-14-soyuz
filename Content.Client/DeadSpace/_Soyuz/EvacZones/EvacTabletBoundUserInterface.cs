// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Content.Shared.DeadSpace._Soyuz.EvacZones;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Client.DeadSpace._Soyuz.EvacZones;

[UsedImplicitly]
public sealed class EvacTabletBoundUserInterface : BoundUserInterface
{
    private EvacTabletWindow? _window;

    public EvacTabletBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<EvacTabletWindow>();
        _window.OnSend += (strokes, announce) => SendMessage(new EvacTabletSendMessage(strokes, announce));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is EvacTabletBuiState cast)
            _window?.Update(cast);
    }
}
