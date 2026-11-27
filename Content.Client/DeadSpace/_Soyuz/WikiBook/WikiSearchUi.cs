// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared.DeadSpace._Soyuz.WikiBook;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.DeadSpace._Soyuz.WikiBook;

/// <summary>Связывает интерфейс программы КПК Руководства с серверо</summary>
[UsedImplicitly]
public sealed partial class WikiSearchUi : UIFragment
{
    private WikiSearchFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new WikiSearchFragment();

        _fragment.OnSearch += query =>
            userInterface.SendMessage(new CartridgeUiMessage(new WikiSearchUiMessageEvent(WikiSearchAction.Search, query, string.Empty)));

        _fragment.OnOpen += page =>
            userInterface.SendMessage(new CartridgeUiMessage(new WikiSearchUiMessageEvent(WikiSearchAction.Open, string.Empty, page)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        switch (state)
        {
            case WikiSearchUiState results:
                _fragment?.ShowResults(results);
                break;
            case WikiArticleUiState article:
                _fragment?.ShowArticle(article);
                break;
        }
    }
}
