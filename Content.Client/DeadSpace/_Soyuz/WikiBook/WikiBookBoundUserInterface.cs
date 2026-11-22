// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Content.Shared.DeadSpace._Soyuz.WikiBook;
using Robust.Client.UserInterface;

namespace Content.Client.DeadSpace._Soyuz.WikiBook;

public sealed class WikiBookBoundUserInterface : BoundUserInterface
{
    private WikiBookWindow? _window;

    public WikiBookBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<WikiBookWindow>();
        _window.OnNavigate += page => SendMessage(new WikiBookNavigateMessage(page));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is WikiBookState wikiState)
            _window?.UpdateState(wikiState);
    }
}
