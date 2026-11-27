// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.WikiBook;

/// <summary>Программа КПК «Руководства»: поиск по статьям вики из списков <see cref="WikiArticleListPrototype"/></summary>
[RegisterComponent]
public sealed partial class WikiSearchCartridgeComponent : Component
{
    /// <summary>Какие списки статей доступны. Пусто = все списки</summary>
    [DataField]
    public HashSet<ProtoId<WikiArticleListPrototype>> ArticleLists = new();
}
