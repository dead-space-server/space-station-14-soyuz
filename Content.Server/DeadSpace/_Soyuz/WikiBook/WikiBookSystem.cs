// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Content.Shared.DeadSpace._Soyuz.StationSpecialization;
using Content.Shared.DeadSpace._Soyuz.WikiBook;
using Robust.Server.GameObjects;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using System.Threading.Tasks;

namespace Content.Server.DeadSpace._Soyuz.WikiBook;

/// <summary>
/// Загрузка страниц вики
/// </summary>
public sealed class WikiBookSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ITaskManager _taskManager = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedStationSpecializationSystem _specialization = default!;

    private sealed record Candidate(string Title, bool Specialized);

    private sealed record Resolved(WikiCacheEntry? Entry, bool Specialized);

    private WikiClient _client = default!;
    private readonly Dictionary<EntityUid, DateTime> _lastRequest = new();

    public WikiClient Client => _client;

    public override void Initialize()
    {
        base.Initialize();

        _client = new WikiClient(Log);

        SubscribeLocalEvent<WikiBookComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<WikiBookComponent, WikiBookNavigateMessage>(OnNavigate);
        SubscribeLocalEvent<WikiBookComponent, ComponentShutdown>(OnShutdown);
    }

    /// <summary>Сбросить кэш страниц, чтобы они перезагрузились при следующем открытии</summary>
    public void ClearCache()
    {
        _client.Clear();
    }

    private void OnShutdown(EntityUid uid, WikiBookComponent component, ComponentShutdown args)
    {
        _lastRequest.Remove(uid);
    }

    private void OnOpened(EntityUid uid, WikiBookComponent component, BoundUIOpenedEvent args)
    {
        if (!args.UiKey.Equals(WikiBookUiKey.Key))
            return;

        Request(uid, component, component.Page);
    }

    private void OnNavigate(EntityUid uid, WikiBookComponent component, WikiBookNavigateMessage args)
    {
        Request(uid, component, args.Page);
    }

    private void Request(EntityUid uid, WikiBookComponent component, string page)
    {
        var now = DateTime.UtcNow;
        if (_lastRequest.TryGetValue(uid, out var last) && now - last < TimeSpan.FromSeconds(0.5))
            return;

        _lastRequest[uid] = now;

        Push(uid, new WikiBookState(page, WikiLoadStatus.Loading, null, false));

        LoadPage(uid, page, component.UseSpecialization, (status, document, specialized) =>
        {
            if (!Deleted(uid))
                Push(uid, new WikiBookState(page, status, document, specialized));
        });
    }

    public void LoadPage(EntityUid context, string page, bool useSpecialization, Action<WikiLoadStatus, WikiDocument?, bool> callback)
    {
        if (!_cfg.GetCVar(WikiBookCVars.Enabled))
        {
            callback(WikiLoadStatus.Disabled, null, false);
            return;
        }

        if (!WikiTitle.IsValid(page) || !AllowedByPrefix(page))
        {
            callback(WikiLoadStatus.NotFound, null, false);
            return;
        }

        var settings = ReadSettings();
        var candidates = BuildCandidates(context, useSpecialization, page);

        _ = ResolveAndDeliverAsync(page, candidates, settings, callback);
    }

    public WikiSettings ReadSettings()
    {
        return new WikiSettings(
            _cfg.GetCVar(WikiBookCVars.ApiUrl),
            _cfg.GetCVar(WikiBookCVars.UserAgent),
            TimeSpan.FromSeconds(Math.Max(5, _cfg.GetCVar(WikiBookCVars.CacheSeconds))),
            TimeSpan.FromSeconds(Math.Max(1, _cfg.GetCVar(WikiBookCVars.TimeoutSeconds))),
            Math.Max(16, _cfg.GetCVar(WikiBookCVars.MaxResponseKb)) * 1024);
    }

    private bool AllowedByPrefix(string page)
    {
        var raw = _cfg.GetCVar(WikiBookCVars.AllowedPrefixes);
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        foreach (var prefix in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (page.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private List<Candidate> BuildCandidates(EntityUid context, bool useSpecialization, string page)
    {
        var candidates = new List<Candidate>();

        if (useSpecialization
            && _specialization.TryGetStationSpecialization(context, out _, out var spec)
            && !string.IsNullOrWhiteSpace(spec.WikiSuffix))
        {
            var title = _cfg.GetCVar(WikiBookCVars.SpecializationPattern)
                .Replace("{page}", page)
                .Replace("{spec}", spec.WikiSuffix);

            if (WikiTitle.IsValid(title) && title != page)
                candidates.Add(new Candidate(title, true));
        }

        candidates.Add(new Candidate(page, false));
        return candidates;
    }

    private async Task<Resolved> ResolveAsync(List<Candidate> candidates, WikiSettings settings)
    {
        WikiCacheEntry? last = null;
        var lastSpecialized = false;

        foreach (var candidate in candidates)
        {
            var entry = await _client.GetAsync(candidate.Title, settings);
            if (entry.Status == WikiLoadStatus.NotFound)
            {
                last = entry;
                lastSpecialized = candidate.Specialized;
                continue;
            }

            return new Resolved(entry, candidate.Specialized);
        }

        return new Resolved(last, lastSpecialized);
    }

    private async Task ResolveAndDeliverAsync(
        string page,
        List<Candidate> candidates,
        WikiSettings settings,
        Action<WikiLoadStatus, WikiDocument?, bool> callback)
    {
        Resolved? resolved = null;
        try
        {
            resolved = await Task.Run(() => ResolveAsync(candidates, settings));
        }
        catch (Exception e)
        {
            Log.Warning($"Ошибка при загрузке страницы вики '{page}': {e.Message}");
        }

        _taskManager.RunOnMainThread(() => Deliver(resolved, callback));
    }

    private static void Deliver(Resolved? resolved, Action<WikiLoadStatus, WikiDocument?, bool> callback)
    {
        if (resolved == null)
        {
            callback(WikiLoadStatus.Error, null, false);
            return;
        }

        var entry = resolved.Entry;
        if (entry == null)
        {
            callback(WikiLoadStatus.NotFound, null, false);
            return;
        }

        callback(entry.Status, entry.Document, resolved.Specialized);
    }

    private void Push(EntityUid uid, WikiBookState state)
    {
        _ui.SetUiState(uid, WikiBookUiKey.Key, state);
    }
}
