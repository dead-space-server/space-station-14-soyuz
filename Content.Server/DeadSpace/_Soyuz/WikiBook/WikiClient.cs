// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Content.Shared.DeadSpace._Soyuz.WikiBook;
using Robust.Shared.Log;

namespace Content.Server.DeadSpace._Soyuz.WikiBook;

/// <summary>Снимок настроек, прочитанный на основном потоке и безопасный для фоновых задач</summary>
public sealed record WikiSettings(string ApiUrl, string UserAgent, TimeSpan Ttl, TimeSpan Timeout, int MaxBytes);

public sealed record WikiCacheEntry(DateTime FetchedUtc, WikiLoadStatus Status, WikiDocument? Document, string Title, string? Error);

/// <summary>Загрузка страниц MediaWiki через Action API с кэшем</summary>
public sealed class WikiClient
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
    });

    private readonly ISawmill _sawmill;
    private readonly ConcurrentDictionary<string, WikiCacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, Task<WikiCacheEntry>> _inflight = new();

    public WikiClient(ISawmill sawmill)
    {
        _sawmill = sawmill;
    }

    public void Clear()
    {
        _cache.Clear();
    }

    public async Task<WikiCacheEntry> GetAsync(string title, WikiSettings settings)
    {
        _cache.TryGetValue(title, out var cached);
        if (cached != null && DateTime.UtcNow - cached.FetchedUtc < settings.Ttl)
            return cached;

        var task = _inflight.GetOrAdd(title, t => FetchAndStoreAsync(t, cached, settings));
        try
        {
            return await task;
        }
        finally
        {
            _inflight.TryRemove(title, out _);
        }
    }

    private async Task<WikiCacheEntry> FetchAndStoreAsync(string title, WikiCacheEntry? old, WikiSettings settings)
    {
        WikiCacheEntry entry;
        try
        {
            entry = await FetchAsync(title, settings);
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Не удалось загрузить страницу вики '{title}': {e.Message}");

            if (old is { Document: not null })
            {
                entry = old with
                {
                    Status = WikiLoadStatus.Stale,
                    FetchedUtc = DateTime.UtcNow - settings.Ttl + TimeSpan.FromSeconds(30),
                };
            }
            else
            {
                entry = new WikiCacheEntry(
                    DateTime.UtcNow - settings.Ttl + TimeSpan.FromSeconds(15),
                    WikiLoadStatus.Error,
                    null,
                    title,
                    e.Message);
            }
        }

        _cache[title] = entry;
        return entry;
    }

    private static async Task<WikiCacheEntry> FetchAsync(string title, WikiSettings settings)
    {
        var url = settings.ApiUrl
                  + "?action=parse&page=" + Uri.EscapeDataString(title)
                  + "&prop=text&redirects=1&disableeditsection=1&disablelimitreport=1&disabletoc=1"
                  + "&format=json&formatversion=2";

        using var cts = new CancellationTokenSource(settings.Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(settings.UserAgent);

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), cts.Token)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > settings.MaxBytes)
                throw new InvalidDataException("Ответ вики слишком большой.");
        }

        ms.Position = 0;
        using var json = await JsonDocument.ParseAsync(ms, cancellationToken: cts.Token);
        var root = json.RootElement;

        if (root.TryGetProperty("error", out var error))
        {
            var code = error.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;
            if (code == "missingtitle")
                return new WikiCacheEntry(DateTime.UtcNow, WikiLoadStatus.NotFound, null, title, null);

            throw new InvalidDataException($"Вики вернула ошибку: {code}");
        }

        var parse = root.GetProperty("parse");
        var html = parse.GetProperty("text").GetString() ?? string.Empty;
        var resolvedTitle = parse.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? title : title;

        var document = WikiHtmlConverter.Convert(html, resolvedTitle);
        return new WikiCacheEntry(DateTime.UtcNow, WikiLoadStatus.Ok, document, resolvedTitle, null);
    }
}
