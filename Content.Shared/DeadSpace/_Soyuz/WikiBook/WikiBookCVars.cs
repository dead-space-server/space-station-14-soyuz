// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared.DeadSpace._Soyuz.WikiBook;

/// <summary>
/// Настройки загрузки руководств с вики
/// </summary>
[CVarDefs]
public sealed class WikiBookCVars : CVars
{
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("soyuz.wiki.enabled", true, CVar.SERVERONLY,
            "Включить загрузку руководств с вики.");

    public static readonly CVarDef<string> ApiUrl =
        CVarDef.Create("soyuz.wiki.api_url", "https://wiki.deadspace14.net/api.php", CVar.SERVERONLY,
            "Адрес api.php вики (MediaWiki).");

    public static readonly CVarDef<string> UserAgent =
        CVarDef.Create("soyuz.wiki.user_agent", "SoyuzGameServer/1.0", CVar.SERVERONLY,
            "User-Agent для запросов к вики.");

    /// <summary>Период обновления: сколько секунд страница считается свежей и не перезапрашивается</summary>
    public static readonly CVarDef<int> CacheSeconds =
        CVarDef.Create("soyuz.wiki.cache_seconds", 300, CVar.SERVERONLY,
            "Период обновления страниц с вики, секунды (кэш).");

    public static readonly CVarDef<int> TimeoutSeconds =
        CVarDef.Create("soyuz.wiki.timeout_seconds", 10, CVar.SERVERONLY,
            "Таймаут запроса к вики, секунды.");

    public static readonly CVarDef<int> MaxResponseKb =
        CVarDef.Create("soyuz.wiki.max_response_kb", 1024, CVar.SERVERONLY,
            "Максимальный размер ответа вики, КБ.");

    /// <summary>Как строится путь к странице-варианту для специализации</summary>
    public static readonly CVarDef<string> SpecializationPattern =
        CVarDef.Create("soyuz.wiki.specialization_pattern", "{page}-{spec}", CVar.SERVERONLY,
            "Шаблон пути страницы для специализации станции: {page} - исходная страница, {spec} - wikiSuffix специализации.");

    /// <summary>Разрешённые префиксы страниц через запятую</summary>
    public static readonly CVarDef<string> AllowedPrefixes =
        CVarDef.Create("soyuz.wiki.allowed_prefixes", string.Empty, CVar.SERVERONLY,
            "Разрешённые префиксы страниц через запятую (например, Союз:,Шаблон:). Пусто = любые.");
}
