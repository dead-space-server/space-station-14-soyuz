// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.DeadSpace._Soyuz.PharmaceuticalResearch;

namespace Content.Client.DeadSpace._Soyuz.PharmaceuticalResearch;

public sealed class PharmaceuticalBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PharmaceuticalWindow? _window;
    private PharmaceuticalState? _state;

    protected override void Open()
    {
        base.Open();
        _window = new PharmaceuticalWindow();
        _window.OnCommand += command => SendMessage(command);
        _window.OnClose += Close;
        if (_state != null)
            _window.UpdateState(_state);
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not PharmaceuticalState laboratory)
            return;
        _state = laboratory;
        _window?.UpdateState(laboratory);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _window?.Dispose();
            _window = null;
        }
    }
}
