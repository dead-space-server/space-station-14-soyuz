using Content.Shared.DeadSpace.Soyuz.RuneBook;

namespace Content.Client.DeadSpace.Soyuz.RuneBook;

public sealed class RuneBookBoundUserInterface : BoundUserInterface
{
    private RuneBookWindow? _window;

    public RuneBookBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new RuneBookWindow();
        _window.OnPageSelected += page => SendMessage(new RuneBookSetPageMessage(page));
        _window.OnCheckRune += (runeId, segments) => SendMessage(new RuneBookCheckMessage(runeId, segments));
        _window.OnRipPage += page => SendMessage(new RuneBookRipPageMessage(page));
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not RuneBookBoundUserInterfaceState runeBookState)
            return;

        _window.UpdateState(runeBookState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        _window?.Close();
        _window?.Dispose();
    }
}
