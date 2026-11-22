// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.WikiBook;

/// <summary>Список статей вики, доступных для поиска в приложении КПК</summary>
[Prototype]
public sealed partial class WikiArticleListPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<WikiArticleEntry> Articles = new();
}

[DataDefinition]
public sealed partial class WikiArticleEntry
{
    /// <summary>Название страницы на вики, например Союз:Правила_Внутреннего_Распорядка</summary>
    [DataField(required: true)]
    public string Page = string.Empty;

    /// <summary>Название в результатах поиска: ключ локализации или готовый текст. Если не задано, берётся название страницы без пространства имён</summary>
    [DataField]
    public string? Title;

    [DataField]
    public string? Category;

    [DataField]
    public List<string> Keywords = new();

    /// <summary>
    /// Если true и у станции есть специализация, сначала открывается вариант страницы с суффиксом специализации
    /// </summary>
    [DataField]
    public bool Specialization;
}
