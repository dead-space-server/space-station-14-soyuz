// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.WikiBook;

[Serializable, NetSerializable]
public enum WikiBlockKind : byte
{
    Heading,
    Paragraph,
    ListItem,
    Table,
    Collapsible,
    Rule,
}

/// <summary>Кусок текста со стилем. Ссылка - это название страницы вики</summary>
[Serializable, NetSerializable]
public sealed class WikiInline
{
    public string Text = string.Empty;
    public bool Bold;
    public bool Italic;
    public bool Underline;
    public string? Link;
}

[Serializable, NetSerializable]
public sealed class WikiTableCell
{
    public bool Header;
    public List<WikiBlock> Blocks = new();
}

[Serializable, NetSerializable]
public sealed class WikiTableRow
{
    public List<WikiTableCell> Cells = new();
}

/// <summary>Блок документа. Какие поля заполнены, зависит от <see cref="Kind"/></summary>
[Serializable, NetSerializable]
public sealed class WikiBlock
{
    public WikiBlockKind Kind;

    /// <summary>Heading: уровень заголовка. ListItem: глубина вложенности</summary>
    public int Level;

    /// <summary>ListItem: нумерованный список</summary>
    public bool Ordered;

    /// <summary>ListItem: номер пункта, -1 = продолжение без маркера</summary>
    public int Number;

    /// <summary>Heading, Paragraph, ListItem</summary>
    public List<WikiInline> Inlines = new();

    /// <summary>Collapsible: заголовок</summary>
    public string Title = string.Empty;

    /// <summary>Collapsible: свёрнут по умолчанию</summary>
    public bool Collapsed;

    /// <summary>Collapsible: содержимое</summary>
    public List<WikiBlock> Children = new();

    /// <summary>Table: строки</summary>
    public List<WikiTableRow> Rows = new();
}

[Serializable, NetSerializable]
public sealed class WikiDocument
{
    public string Title = string.Empty;
    public List<WikiBlock> Blocks = new();
}
