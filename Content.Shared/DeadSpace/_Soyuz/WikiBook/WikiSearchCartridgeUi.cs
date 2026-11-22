// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.WikiBook;

[Serializable, NetSerializable]
public enum WikiSearchAction : byte
{
    Search,
    Open,
}

/// <summary>Сообщение клиента программе: поиск или открытие статьи</summary>
[Serializable, NetSerializable]
public sealed class WikiSearchUiMessageEvent : CartridgeMessageEvent
{
    public readonly WikiSearchAction Action;
    public readonly string Query;
    public readonly string Page;

    public WikiSearchUiMessageEvent(WikiSearchAction action, string query, string page)
    {
        Action = action;
        Query = query;
        Page = page;
    }
}

[Serializable, NetSerializable]
public sealed class WikiSearchResult
{
    public string Page = string.Empty;
    public string Title = string.Empty;
    public string? Category;

    /// <summary>Найдено по тексту статьи, а не по названию и ключевым словам</summary>
    public bool TextMatch;
}

[Serializable, NetSerializable]
public sealed class WikiSearchUiState : BoundUserInterfaceState
{
    public readonly string Query;
    public readonly List<WikiSearchResult> Results;

    public WikiSearchUiState(string query, List<WikiSearchResult> results)
    {
        Query = query;
        Results = results;
    }
}

[Serializable, NetSerializable]
public sealed class WikiArticleUiState : BoundUserInterfaceState
{
    public readonly string Page;
    public readonly WikiLoadStatus Status;
    public readonly WikiDocument? Document;
    public readonly bool Specialized;

    public WikiArticleUiState(string page, WikiLoadStatus status, WikiDocument? document, bool specialized)
    {
        Page = page;
        Status = status;
        Document = document;
        Specialized = specialized;
    }
}
