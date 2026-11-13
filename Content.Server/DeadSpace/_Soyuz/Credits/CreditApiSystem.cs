// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using Content.Server.Administration;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.Credits;

/// <summary>
/// HTTP API для Discord-бота (на порту статуса сервера, пути /soyuz/credits/...)
/// </summary>
public sealed class CreditApiSystem : EntitySystem
{
    [Dependency] private readonly IStatusHost _status = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ITaskManager _tasks = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly ICreditRepository _repo = default!;
    [Dependency] private readonly ICreditDiscordLinks _links = default!;
    [Dependency] private readonly IPromoService _promos = default!;
    [Dependency] private readonly ICreditRateLimiter _limiter = default!;
    [Dependency] private readonly ICreditService _credits = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private const string Prefix = "/soyuz/credits/";
    private const int TopMax = 25;
    private static readonly TimeSpan TopCacheTime = TimeSpan.FromMinutes(1);

    private readonly Queue<(DateTime At, long Amount)> _apiGrants = new();
    private DateTime _topAt = DateTime.MinValue;
    private List<TopRow> _top = new();

    public override void Initialize()
    {
        base.Initialize();
        _status.AddHandler(HandleAsync);
    }

    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        var path = context.Url.AbsolutePath;
        if (!path.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var token = _cfg.GetCVar(SoyuzCreditsCCVars.ApiToken);
        if (string.IsNullOrEmpty(token))
            return false;

        if (!IsAuthorized(context, token))
        {
            await context.RespondJsonAsync(new { error = "unauthorized" }, HttpStatusCode.Unauthorized);
            return true;
        }

        var route = path[Prefix.Length..].Trim('/');
        var method = context.RequestMethod;

        try
        {
            await OnMainThread(() => RouteAsync(context, method, route));
        }
        catch (Exception e)
        {
            Log.Error($"API Кредитов: {e}");
            await context.RespondJsonAsync(new { error = "internal_error" }, HttpStatusCode.InternalServerError);
        }

        return true;
    }

    private async Task RouteAsync(IStatusHandlerContext context, HttpMethod method, string route)
    {
        if (method == HttpMethod.Get && route == "balance")
            await BalanceAsync(context);
        else if (method == HttpMethod.Get && route == "top")
            await TopAsync(context);
        else if (method == HttpMethod.Post && route == "link")
            await LinkAsync(context);
        else if (method == HttpMethod.Post && route == "promo/redeem")
            await RedeemAsync(context);
        else if (method == HttpMethod.Post && route == "promo/create")
            await CreatePromoAsync(context);
        else if (method == HttpMethod.Get && route == "history")
            await HistoryAsync(context);
        else if (method == HttpMethod.Get && route == "shop")
            await ShopAsync(context);
        else if (method == HttpMethod.Get && route == "roles")
            await RolesAsync(context);
        else if (method == HttpMethod.Get && route == "expiring")
            await ExpiringAsync(context);
        else if (method == HttpMethod.Get && route == "stats")
            await StatsAsync(context);
        else if (method == HttpMethod.Post && route == "give")
            await GiveAsync(context);
        else if (method == HttpMethod.Post && route == "take")
            await TakeAsync(context);
        else if (method == HttpMethod.Post && route == "refund")
            await RefundAsync(context);
        else
            await context.RespondJsonAsync(new { error = "not_found" }, HttpStatusCode.NotFound);
    }

    private async Task BalanceAsync(IStatusHandlerContext context)
    {
        var discordId = HttpUtility.ParseQueryString(context.Url.Query)["discordId"] ?? "";
        if (!ValidDiscordId(discordId))
        {
            await context.RespondJsonAsync(new { error = "bad_request" }, HttpStatusCode.BadRequest);
            return;
        }

        if (await _links.FindUserAsync(discordId) is not { } user)
        {
            await context.RespondJsonAsync(new { error = "not_linked" }, HttpStatusCode.NotFound);
            return;
        }

        if (!_limiter.TryAcquire(user, CreditRateKind.Browse))
        {
            await context.RespondJsonAsync(new { error = "rate_limited" }, (HttpStatusCode) 429);
            return;
        }

        var balance = await _repo.GetBalanceAsync(user);
        var seconds = await _repo.GetPlaytimeSecondsAsync(user);
        await context.RespondJsonAsync(new
        {
            username = await NameOfAsync(user),
            balance,
            playtimeHours = seconds / 3600,
        });
    }

    private async Task TopAsync(IStatusHandlerContext context)
    {
        var count = int.TryParse(HttpUtility.ParseQueryString(context.Url.Query)["count"], out var c)
            ? Math.Clamp(c, 1, TopMax)
            : 10;

        if (DateTime.UtcNow - _topAt > TopCacheTime)
        {
            var rows = await _repo.GetTopBalancesAsync(TopMax);
            var list = new List<TopRow>();
            foreach (var row in rows)
                list.Add(new TopRow(await NameOfAsync(row.UserId), row.Balance));

            _top = list;
            _topAt = DateTime.UtcNow;
        }

        await context.RespondJsonAsync(new
        {
            top = _top.Take(count).Select(r => new { username = r.Username, balance = r.Balance }).ToList(),
        });
    }

    private async Task LinkAsync(IStatusHandlerContext context)
    {
        var req = await context.RequestBodyJsonAsync<LinkRequest>();
        if (req == null || !ValidDiscordId(req.DiscordId) || string.IsNullOrWhiteSpace(req.Code))
        {
            await context.RespondJsonAsync(new { error = "bad_request" }, HttpStatusCode.BadRequest);
            return;
        }

        var (result, userId) = await _links.LinkAsync(req.DiscordId, req.Code);
        switch (result)
        {
            case DiscordLinkResult.Ok:
                await context.RespondJsonAsync(new { username = await NameOfAsync(userId!.Value) });
                break;
            case DiscordLinkResult.InvalidCode:
                await context.RespondJsonAsync(new { error = "invalid_code" }, HttpStatusCode.NotFound);
                break;
            case DiscordLinkResult.DiscordAlreadyLinked:
                await context.RespondJsonAsync(new { error = "discord_already_linked" }, HttpStatusCode.Conflict);
                break;
            default:
                await context.RespondJsonAsync(new { error = "user_already_linked" }, HttpStatusCode.Conflict);
                break;
        }
    }

    private async Task RedeemAsync(IStatusHandlerContext context)
    {
        var req = await context.RequestBodyJsonAsync<RedeemRequest>();
        if (req == null || !ValidDiscordId(req.DiscordId) || string.IsNullOrWhiteSpace(req.Code))
        {
            await context.RespondJsonAsync(new { error = "bad_request" }, HttpStatusCode.BadRequest);
            return;
        }

        if (await _links.FindUserAsync(req.DiscordId) is not { } user)
        {
            await context.RespondJsonAsync(new { error = "not_linked" }, HttpStatusCode.NotFound);
            return;
        }

        if (!_limiter.TryAcquire(user, CreditRateKind.Code))
        {
            await context.RespondJsonAsync(new { error = "rate_limited" }, (HttpStatusCode) 429);
            return;
        }

        var outcome = await _promos.RedeemAsync(user, req.Code);
        await context.RespondJsonAsync(new
        {
            result = outcome.Result.ToString(),
            credits = outcome.Credits,
            productId = outcome.ProductId,
        });
    }

    private async Task CreatePromoAsync(IStatusHandlerContext context)
    {
        var req = await context.RequestBodyJsonAsync<CreatePromoRequest>();
        if (req == null)
        {
            await context.RespondJsonAsync(new { error = "bad_request" }, HttpStatusCode.BadRequest);
            return;
        }

        if (req.MaxUses is not > 0)
        {
            await context.RespondJsonAsync(new { error = "max_uses_required" }, HttpStatusCode.BadRequest);
            return;
        }

        if (req.Credits > 0 && !TryReserveApiGrant(req.Credits * req.MaxUses.Value))
        {
            await context.RespondJsonAsync(new { error = "api_limit" }, (HttpStatusCode) 429);
            return;
        }

        var explicitCode = string.IsNullOrWhiteSpace(req.Code) ? null : req.Code.Trim();
        string code;
        PromoResult result;

        if (explicitCode != null)
        {
            code = explicitCode;
            result = await _promos.CreateAsync(code, req.Credits, req.ProductId, req.MaxUses, req.ExpiresInDays, null);
        }
        else
        {
            code = CreditPromoService.GenerateCode();
            result = PromoResult.AlreadyExists;
            for (var attempt = 0; attempt < 5 && result == PromoResult.AlreadyExists; attempt++)
            {
                code = CreditPromoService.GenerateCode();
                result = await _promos.CreateAsync(code, req.Credits, req.ProductId, req.MaxUses, req.ExpiresInDays, null);
            }
        }

        if (result != PromoResult.Ok)
        {
            var status = result == PromoResult.AlreadyExists ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest;
            await context.RespondJsonAsync(new { error = result.ToString() }, status);
            return;
        }

        Log.Info($"API: промокод {code.ToUpperInvariant()} создан ({req.CreatedBy ?? "не указано"})");
        await context.RespondJsonAsync(new { code = code.ToUpperInvariant() });
    }

    private async Task HistoryAsync(IStatusHandlerContext context)
    {
        var query = HttpUtility.ParseQueryString(context.Url.Query);
        var discordId = query["discordId"] ?? "";
        var page = int.TryParse(query["page"], out var p) ? Math.Max(0, p) : 0;

        if (!ValidDiscordId(discordId))
        {
            await context.RespondJsonAsync(new { error = "bad_request" }, HttpStatusCode.BadRequest);
            return;
        }

        if (await _links.FindUserAsync(discordId) is not { } user)
        {
            await context.RespondJsonAsync(new { error = "not_linked" }, HttpStatusCode.NotFound);
            return;
        }

        if (!_limiter.TryAcquire(user, CreditRateKind.Browse))
        {
            await context.RespondJsonAsync(new { error = "rate_limited" }, (HttpStatusCode) 429);
            return;
        }

        var (entries, hasMore) = await _repo.GetLogAsync(user, page, 10);
        await context.RespondJsonAsync(new
        {
            page,
            hasMore,
            entries = entries.Select(e => new
            {
                amount = e.Amount,
                type = e.Type.ToString(),
                reason = e.Reason,
                createdAt = e.CreatedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            }).ToList(),
        });
    }

    private async Task ShopAsync(IStatusHandlerContext context)
    {
        var text = (HttpUtility.ParseQueryString(context.Url.Query)["query"] ?? "").Trim();
        var now = DateTime.UtcNow;
        var sold = await _repo.GetSoldCountsAsync();

        var items = new List<object>();
        foreach (var product in _proto.EnumeratePrototypes<CreditProductPrototype>().OrderBy(p => p.Price))
        {
            if (!CreditShopRules.IsAvailable(product, now))
                continue;

            var name = Loc.GetString(product.Name);
            if (text.Length > 0 && !name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                continue;

            var category = product.Category is { } c && _proto.TryIndex(c, out var cat) ? Loc.GetString(cat.Name) : null;
            items.Add(new
            {
                id = product.ID,
                name,
                category,
                price = CreditShopRules.EffectivePrice(product, now),
                originalPrice = product.Price,
                discountPercent = CreditShopRules.ActiveDiscount(product, now),
                durationDays = product.DurationDays,
                stockLeft = product.Stock is { } stock ? Math.Max(0, stock - sold.GetValueOrDefault(product.ID)) : -1,
            });

            if (items.Count >= 25)
                break;
        }

        await context.RespondJsonAsync(new { products = items });
    }

    private async Task RolesAsync(IStatusHandlerContext context)
    {
        var productIds = _proto.EnumeratePrototypes<CreditProductPrototype>()
            .Where(p => p.ProductType == CreditProductType.DiscordTitle)
            .Select(p => p.ID)
            .ToList();

        var owned = await _repo.GetActiveOwnedAsync(productIds, null);
        var discord = await _repo.GetDiscordIdsAsync(owned.Select(o => o.UserId).Distinct().ToList());

        await context.RespondJsonAsync(new
        {
            productIds,
            holders = owned
                .Where(o => discord.ContainsKey(o.UserId))
                .Select(o => new { discordId = discord[o.UserId], productId = o.ProductId, expiresAt = o.ExpiresAt?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") })
                .ToList(),
        });
    }

    private async Task ExpiringAsync(IStatusHandlerContext context)
    {
        var days = int.TryParse(HttpUtility.ParseQueryString(context.Url.Query)["days"], out var d) ? Math.Clamp(d, 1, 14) : 3;

        var owned = await _repo.GetActiveOwnedAsync(null, DateTime.UtcNow.AddDays(days));
        var discord = await _repo.GetDiscordIdsAsync(owned.Select(o => o.UserId).Distinct().ToList());

        await context.RespondJsonAsync(new
        {
            items = owned
                .Where(o => discord.ContainsKey(o.UserId))
                .Select(o => new
                {
                    discordId = discord[o.UserId],
                    productId = o.ProductId,
                    name = _proto.TryIndex<CreditProductPrototype>(o.ProductId, out var product) ? Loc.GetString(product.Name) : o.ProductId,
                    expiresAt = o.ExpiresAt?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                })
                .ToList(),
        });
    }

    private async Task StatsAsync(IStatusHandlerContext context)
    {
        var days = int.TryParse(HttpUtility.ParseQueryString(context.Url.Query)["days"], out var d) ? Math.Clamp(d, 1, 90) : 7;
        var s = await _repo.GetEconomyStatsAsync(DateTime.UtcNow.AddDays(-days));

        await context.RespondJsonAsync(new
        {
            days,
            inCirculation = s.InCirculation,
            playersWithBalance = s.PlayersWithBalance,
            issued = s.Issued,
            spent = s.Spent,
            taken = s.Taken,
            refunded = s.Refunded,
            topProducts = s.TopProducts.Select(t => new
            {
                name = _proto.TryIndex<CreditProductPrototype>(t.ProductId, out var product) ? Loc.GetString(product.Name) : t.ProductId,
                count = t.Count,
                revenue = t.Revenue,
            }).ToList(),
        });
    }

    private async Task GiveAsync(IStatusHandlerContext context)
    {
        var req = await context.RequestBodyJsonAsync<AmountRequest>();
        if (req == null || !ValidDiscordId(req.DiscordId) || string.IsNullOrWhiteSpace(req.Reason))
        {
            await context.RespondJsonAsync(new { error = "bad_request" }, HttpStatusCode.BadRequest);
            return;
        }

        if (await _links.FindUserAsync(req.DiscordId) is not { } user)
        {
            await context.RespondJsonAsync(new { error = "not_linked" }, HttpStatusCode.NotFound);
            return;
        }

        if (req.Amount <= 0 || !TryReserveApiGrant(req.Amount))
        {
            await context.RespondJsonAsync(new { error = req.Amount <= 0 ? "bad_request" : "api_limit" },
                req.Amount <= 0 ? HttpStatusCode.BadRequest : (HttpStatusCode) 429);
            return;
        }

        var result = await _credits.GiveAsync(user, req.Amount, WithActor(req), null);
        if (result != CreditResult.Ok)
            _apiGrants.Enqueue((DateTime.UtcNow, -req.Amount));

        await RespondResultAsync(context, result);
    }

    private async Task TakeAsync(IStatusHandlerContext context)
    {
        var req = await context.RequestBodyJsonAsync<AmountRequest>();
        if (req == null || !ValidDiscordId(req.DiscordId) || string.IsNullOrWhiteSpace(req.Reason))
        {
            await context.RespondJsonAsync(new { error = "bad_request" }, HttpStatusCode.BadRequest);
            return;
        }

        if (await _links.FindUserAsync(req.DiscordId) is not { } user)
        {
            await context.RespondJsonAsync(new { error = "not_linked" }, HttpStatusCode.NotFound);
            return;
        }

        await RespondResultAsync(context, await _credits.TakeAsync(user, req.Amount, WithActor(req), null));
    }

    private async Task RefundAsync(IStatusHandlerContext context)
    {
        var req = await context.RequestBodyJsonAsync<AmountRequest>();
        if (req == null || !ValidDiscordId(req.DiscordId) || string.IsNullOrWhiteSpace(req.Reason)
            || string.IsNullOrWhiteSpace(req.ProductId))
        {
            await context.RespondJsonAsync(new { error = "bad_request" }, HttpStatusCode.BadRequest);
            return;
        }

        if (await _links.FindUserAsync(req.DiscordId) is not { } user)
        {
            await context.RespondJsonAsync(new { error = "not_linked" }, HttpStatusCode.NotFound);
            return;
        }

        var refund = await _credits.RefundAsync(user, req.ProductId, WithActor(req), null);
        if (refund.Result == CreditResult.Ok)
            await context.RespondJsonAsync(new { result = "Ok", amount = refund.Amount });
        else
            await RespondResultAsync(context, refund.Result);
    }

    private static async Task RespondResultAsync(IStatusHandlerContext context, CreditResult result)
    {
        if (result == CreditResult.Ok)
            await context.RespondJsonAsync(new { result = "Ok" });
        else
            await context.RespondJsonAsync(new { result = result.ToString() }, HttpStatusCode.BadRequest);
    }

    private static string WithActor(AmountRequest req)
    {
        var reason = req.Reason.Trim();
        if (reason.Length > 150)
            reason = reason[..150];

        return $"{reason} (Discord: {Trim(req.Actor, 60)})";
    }

    private static string Trim(string? s, int max) => string.IsNullOrEmpty(s) ? "?" : s.Length <= max ? s : s[..max];

    /// <summary>Резервирует сумму в суточном лимите выдачи через API. false = лимит исчерпан или выдача выключена</summary>
    private bool TryReserveApiGrant(long amount)
    {
        var limit = _cfg.GetCVar(SoyuzCreditsCCVars.ApiGrantDailyLimit);
        if (limit <= 0)
            return false;

        var cutoff = DateTime.UtcNow.AddHours(-24);
        while (_apiGrants.Count > 0 && _apiGrants.Peek().At < cutoff)
            _apiGrants.Dequeue();

        if (_apiGrants.Sum(g => g.Amount) + amount > limit)
            return false;

        _apiGrants.Enqueue((DateTime.UtcNow, amount));
        return true;
    }

    private static bool IsAuthorized(IStatusHandlerContext context, string token)
    {
        if (!context.RequestHeaders.TryGetValue("Authorization", out var header))
            return false;

        var given = Encoding.UTF8.GetBytes(header.ToString());
        var expected = Encoding.UTF8.GetBytes("Bearer " + token);
        return CryptographicOperations.FixedTimeEquals(given, expected);
    }

    private static bool ValidDiscordId(string id)
    {
        if (id.Length is < 5 or > 25)
            return false;

        foreach (var c in id)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return true;
    }

    private async Task<string> NameOfAsync(Guid id)
    {
        try
        {
            var data = await _locator.LookupIdAsync(new NetUserId(id));
            if (data != null)
                return data.Username;
        }
        catch
        {

        }

        return id.ToString();
    }

    private Task OnMainThread(Func<Task> action)
    {
        var tcs = new TaskCompletionSource();
        _tasks.RunOnMainThread(async () =>
        {
            try
            {
                await action();
                tcs.SetResult();
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        return tcs.Task;
    }

    private sealed record TopRow(string Username, long Balance);

    private sealed class LinkRequest
    {
        [JsonPropertyName("discordId")] public string DiscordId { get; set; } = "";
        [JsonPropertyName("code")] public string Code { get; set; } = "";
    }

    private sealed class RedeemRequest
    {
        [JsonPropertyName("discordId")] public string DiscordId { get; set; } = "";
        [JsonPropertyName("code")] public string Code { get; set; } = "";
    }

    private sealed class AmountRequest
    {
        [JsonPropertyName("discordId")] public string DiscordId { get; set; } = "";
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("reason")] public string Reason { get; set; } = "";
        [JsonPropertyName("productId")] public string ProductId { get; set; } = "";
        [JsonPropertyName("actor")] public string? Actor { get; set; }
    }

    private sealed class CreatePromoRequest
    {
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("credits")] public long Credits { get; set; }
        [JsonPropertyName("productId")] public string? ProductId { get; set; }
        [JsonPropertyName("maxUses")] public int? MaxUses { get; set; }
        [JsonPropertyName("expiresInDays")] public int? ExpiresInDays { get; set; }
        [JsonPropertyName("createdBy")] public string? CreatedBy { get; set; }
    }
}
