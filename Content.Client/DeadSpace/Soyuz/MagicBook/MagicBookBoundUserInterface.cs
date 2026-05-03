using Content.Shared.DeadSpace.Soyuz.MagicBook;

namespace Content.Client.DeadSpace.Soyuz.MagicBook;

public sealed class MagicBookBoundUserInterface : BoundUserInterface
{
    private MagicBookWindow? _window;

    public MagicBookBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new MagicBookWindow();
        _window.OnInsertPage += () => SendMessage(new MagicBookInsertPageMessage());
        _window.OnPageSelected += page => SendMessage(new MagicBookSelectPageMessage(page));
        _window.OnRuneSlotSet += (slot, rune) => SendMessage(new MagicBookSetRuneSlotMessage(slot, rune));
        _window.OnRuneSlotCleared += slot => SendMessage(new MagicBookClearRuneSlotMessage(slot));
        _window.OnSaveSpell += name => SendMessage(new MagicBookSaveSpellMessage(name));
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_window == null || state is not MagicBookBoundUserInterfaceState magicState)
            return;

        _window.UpdateState(magicState);
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

