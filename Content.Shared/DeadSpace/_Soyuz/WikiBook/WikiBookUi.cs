// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.WikiBook;

[Serializable, NetSerializable]
public enum WikiBookUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum WikiLoadStatus : byte
{
    Loading,
    Ok,
    /// <summary>Вики недоступна, показана сохранённая копия</summary>
    Stale,
    NotFound,
    Error,
    Disabled,
}

/// <summary>Клиент просит открыть другую страницу</summary>
[Serializable, NetSerializable]
public sealed class WikiBookNavigateMessage : BoundUserInterfaceMessage
{
    public readonly string Page;

    public WikiBookNavigateMessage(string page)
    {
        Page = page;
    }
}

[Serializable, NetSerializable]
public sealed class WikiBookState : BoundUserInterfaceState
{
    /// <summary>Запрошенная страница</summary>
    public readonly string Page;

    public readonly WikiLoadStatus Status;

    public readonly WikiDocument? Document;

    /// <summary>Показан вариант страницы для специализации станции</summary>
    public readonly bool Specialized;

    public WikiBookState(string page, WikiLoadStatus status, WikiDocument? document, bool specialized)
    {
        Page = page;
        Status = status;
        Document = document;
        Specialized = specialized;
    }
}
