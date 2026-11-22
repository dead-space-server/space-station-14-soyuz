// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Linq;
using System.Threading.Tasks;
using Content.Server.CartridgeLoader;
using Content.Shared.CartridgeLoader;
using Content.Shared.DeadSpace._Soyuz.Localization;
using Content.Shared.DeadSpace._Soyuz.WikiBook;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.WikiBook;

/// <summary>
/// Программа КПК Руководства
/// </summary>
public sealed class WikiSearchCartridgeSystem : EntitySystem
{
    private const int MaxResults = 30;

    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly WikiBookSystem _wiki = default!;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _index = new();

    private volatile bool _indexBuilding;
    private DateTime _indexBuilt = DateTime.MinValue;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WikiSearchCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<WikiSearchCartridgeComponent, CartridgeMessageEvent>(OnMessage);
    }

    private void OnUiReady(Entity<WikiSearchCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        EnsureIndex(GetArticles(ent.Comp));
        _cartridgeLoader.UpdateCartridgeUiState(args.Loader, new WikiSearchUiState(string.Empty, new List<WikiSearchResult>()));
    }

    private void OnMessage(EntityUid uid, WikiSearchCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not WikiSearchUiMessageEvent message)
            return;

        var loader = GetEntity(args.LoaderUid);
        var articles = GetArticles(component);

        switch (message.Action)
        {
            case WikiSearchAction.Search:
                {
                    EnsureIndex(articles);
                    var query = message.Query.Length > 100 ? message.Query[..100] : message.Query;
                    _cartridgeLoader.UpdateCartridgeUiState(loader, new WikiSearchUiState(query, Search(query, articles)));
                    break;
                }
            case WikiSearchAction.Open:
                {
                    var page = message.Page;
                    var useSpecialization = articles.Any(a => a.Page == page && a.Specialization);

                    _cartridgeLoader.UpdateCartridgeUiState(loader, new WikiArticleUiState(page, WikiLoadStatus.Loading, null, false));

                    _wiki.LoadPage(loader, page, useSpecialization, (status, document, specialized) =>
                {
                    if (!Deleted(loader))
                        _cartridgeLoader.UpdateCartridgeUiState(loader, new WikiArticleUiState(page, status, document, specialized));
                });
                    break;
                }
        }
    }

    private List<WikiArticleEntry> GetArticles(WikiSearchCartridgeComponent component)
    {
        var result = new List<WikiArticleEntry>();
        var seen = new HashSet<string>();

        foreach (var list in _proto.EnumeratePrototypes<WikiArticleListPrototype>())
        {
            if (component.ArticleLists.Count > 0 && !component.ArticleLists.Contains(list.ID))
                continue;

            foreach (var article in list.Articles)
            {
                if (WikiTitle.IsValid(article.Page) && seen.Add(article.Page))
                    result.Add(article);
            }
        }

        return result;
    }

    private static string ArticleTitle(WikiArticleEntry article)
    {
        return string.IsNullOrWhiteSpace(article.Title)
            ? WikiTitleHelper.Short(article.Page)
            : PrototypeLocHelper.Resolve(article.Title);
    }

    private List<WikiSearchResult> Search(string query, List<WikiArticleEntry> articles)
    {
        var tokens = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return new List<WikiSearchResult>();

        var found = new List<(int Rank, WikiSearchResult Result)>();

        foreach (var article in articles)
        {
            var title = ArticleTitle(article);
            var meta = $"{title} {article.Page.Replace('_', ' ')} {article.Category} {string.Join(' ', article.Keywords)}".ToLowerInvariant();

            var inMeta = tokens.All(t => meta.Contains(t));
            var inText = !inMeta && _index.TryGetValue(article.Page, out var text) && tokens.All(t => text.Contains(t));

            if (!inMeta && !inText)
                continue;

            found.Add((inMeta ? 0 : 1, new WikiSearchResult
            {
                Page = article.Page,
                Title = title,
                Category = article.Category,
                TextMatch = inText,
            }));
        }

        return found
            .OrderBy(f => f.Rank)
            .ThenBy(f => f.Result.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxResults)
            .Select(f => f.Result)
            .ToList();
    }

    private void EnsureIndex(List<WikiArticleEntry> articles)
    {
        var settings = _wiki.ReadSettings();
        if (_indexBuilding || DateTime.UtcNow - _indexBuilt < settings.Ttl || articles.Count == 0)
            return;

        _indexBuilding = true;
        _ = BuildIndexAsync(articles.Select(a => a.Page).ToList(), settings);
    }

    private async Task BuildIndexAsync(List<string> pages, WikiSettings settings)
    {
        try
        {
            foreach (var page in pages)
            {
                var entry = await _wiki.Client.GetAsync(page, settings);
                if (entry.Document != null)
                    _index[page] = WikiDocumentText.Plain(entry.Document).ToLowerInvariant();
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Не удалось построить индекс статей вики: {e.Message}");
        }
        finally
        {
            _indexBuilt = DateTime.UtcNow;
            _indexBuilding = false;
        }
    }
}
