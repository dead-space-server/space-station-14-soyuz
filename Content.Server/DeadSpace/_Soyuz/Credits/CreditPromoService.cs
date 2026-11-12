// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public enum PromoResult { Ok, Forbidden, InvalidCode, InvalidAmount, InvalidProduct, InvalidUses, InvalidDuration, AlreadyExists, NotFound }

public enum PromoRedeemResult
{
    Ok, Unknown, Disabled, Expired, Exhausted, AlreadyUsed, AlreadyOwned, CategoryLimit, BlockedByPermanent, InvalidProduct,
}

public sealed record PromoRedeemOutcome(PromoRedeemResult Result, long Credits = 0, string? ProductId = null);

/// <summary>
/// Промокоды: администратор создаёт код на кредиты и/или на товар с лимитом использований и сроком,
/// игрок активирует его в окне или командой. Один игрок может использовать код один раз.
/// Хорошо работает с розыгрышами в Discord: код публикуется, а лимит использований не даёт забрать больше положенного.
/// </summary>
public interface IPromoService
{
    /// <summary>
    /// Создание кода. credits — сколько кредитов даёт код (0, если код только на товар), productId — товар (null, если только кредиты).
    /// maxUses — сколько раз можно использовать (null = без лимита), expiresInDays — срок действия кода (null = бессрочно).
    /// </summary>
    Task<PromoResult> CreateAsync(string code, long credits, string? productId, int? maxUses, int? expiresInDays,
        ICommonSession? actor);

    Task<PromoInfo?> GetAsync(string code);
    Task<IReadOnlyList<PromoInfo>> ListAsync(int take);

    /// <summary>Отключить или снова включить код.</summary>
    Task<PromoResult> SetDisabledAsync(string code, bool disabled, ICommonSession? actor);

    Task<PromoRedeemOutcome> RedeemAsync(Guid userId, string code);
}

public sealed class CreditPromoService : IPromoService
{
    [Dependency] private readonly ICreditRepository _repo = default!;
    [Dependency] private readonly IShopService _shop = default!;
    [Dependency] private readonly ICreditPerkCache _perks = default!;
    [Dependency] private readonly ICreditNotifier _notifier = default!;
    [Dependency] private readonly ICreditWebhook _webhook = default!;
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly ILogManager _log = default!;

    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // без похожих символов

    /// <summary>Случайный код для розыгрышей (например, от Discord-бота).</summary>
    public static string GenerateCode(int length = 10)
    {
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(chars);
    }

    public const int MinCodeLength = 3;
    public const int MaxCodeLength = 32;

    public async Task<PromoResult> CreateAsync(string code, long credits, string? productId, int? maxUses, int? expiresInDays,
        ICommonSession? actor)
    {
        if (actor != null && !_admin.HasAdminFlag(actor, AdminFlags.Server))
            return PromoResult.Forbidden;

        code = code.Trim();
        if (!IsValidCode(code))
            return PromoResult.InvalidCode;

        productId = string.IsNullOrWhiteSpace(productId) ? null : productId.Trim();
        if (credits < 0 || credits > CreditService.MaxGrant || (credits == 0 && productId == null))
            return PromoResult.InvalidAmount;

        if (productId != null && !_proto.HasIndex<CreditProductPrototype>(productId))
            return PromoResult.InvalidProduct;

        if (maxUses is <= 0)
            return PromoResult.InvalidUses;

        if (expiresInDays is <= 0 or > 3650)
            return PromoResult.InvalidDuration;

        DateTime? expires = expiresInDays is { } days ? DateTime.UtcNow.AddDays(days) : null;
        var result = await _repo.CreatePromoAsync(code, credits, productId, maxUses, expires, actor?.UserId.UserId);
        if (result == PromoCreateResult.AlreadyExists)
            return PromoResult.AlreadyExists;

        _log.GetSawmill("credits").Info(
            $"Промокод {code.ToUpperInvariant()} создан ({actor?.Name ?? "консоль"}): кредиты {credits}, товар {productId ?? "-"}, использований {(maxUses?.ToString() ?? "без лимита")}, срок {(expires?.ToString("u") ?? "бессрочно")}");
        return PromoResult.Ok;
    }

    public Task<PromoInfo?> GetAsync(string code) => _repo.GetPromoAsync(code);

    public Task<IReadOnlyList<PromoInfo>> ListAsync(int take) => _repo.ListPromosAsync(take);

    public async Task<PromoResult> SetDisabledAsync(string code, bool disabled, ICommonSession? actor)
    {
        if (actor != null && !_admin.HasAdminFlag(actor, AdminFlags.Server))
            return PromoResult.Forbidden;

        return await _repo.SetPromoDisabledAsync(code, disabled) ? PromoResult.Ok : PromoResult.NotFound;
    }

    public async Task<PromoRedeemOutcome> RedeemAsync(Guid userId, string code)
    {
        code = code.Trim();
        if (!IsValidCode(code))
            return new PromoRedeemOutcome(PromoRedeemResult.Unknown);

        var info = await _repo.GetPromoAsync(code);
        if (info == null)
            return new PromoRedeemOutcome(PromoRedeemResult.Unknown);

        var productId = info.ProductId;
        int? duration = null;
        IReadOnlyCollection<string> siblings = Array.Empty<string>();
        CategoryLimit? limit = null;

        if (productId != null)
        {
            var plan = await _shop.GetGrantPlanAsync(userId, productId);
            if (plan == null)
                return new PromoRedeemOutcome(PromoRedeemResult.InvalidProduct);

            if (plan.BlockedByPermanent)
                return new PromoRedeemOutcome(PromoRedeemResult.BlockedByPermanent);

            duration = plan.DurationDays;
            siblings = plan.Siblings;
            limit = plan.Limit;
        }

        var result = await _repo.TryRedeemPromoAsync(userId, code, productId, duration, siblings, limit);
        if (result.Status != PromoRedeemStatus.Ok)
            return new PromoRedeemOutcome(Map(result.Status));

        if (productId != null)
        {
            await _perks.RefreshAsync(userId); // чтобы предмет и цвет заработали сразу
            await NotifyDiscordRoleAsync(userId, productId);
        }

        var name = productId != null && _proto.TryIndex<CreditProductPrototype>(productId, out var product)
            ? Loc.GetString(product.Name)
            : null;

        var message = (result.Credits > 0, name != null) switch
        {
            (true, true) => Loc.GetString("credits-notify-promo-both", ("amount", result.Credits), ("product", name!)),
            (false, true) => Loc.GetString("credits-notify-promo-product", ("product", name!)),
            _ => Loc.GetString("credits-notify-promo-credits", ("amount", result.Credits)),
        };
        _notifier.Notify(userId, message);

        await _webhook.LogGrantAsync(userId, null, null, result.Credits, CreditTransactionType.PromoCode,
            productId == null ? $"Промокод {code.ToUpperInvariant()}" : $"Промокод {code.ToUpperInvariant()} (товар {productId})");

        return new PromoRedeemOutcome(PromoRedeemResult.Ok, result.Credits, productId);
    }

    private async Task NotifyDiscordRoleAsync(Guid userId, string productId)
    {
        if (!_proto.TryIndex<CreditProductPrototype>(productId, out var product)
            || product.ProductType != CreditProductType.DiscordTitle)
        {
            return;
        }

        DateTime? expires = _perks.TryGetOwned(userId, productId, out var info) ? info.ExpiresAt : null;
        await _webhook.NotifyRolePurchaseAsync(userId, Loc.GetString(product.Name), expires);
    }

    private static PromoRedeemResult Map(PromoRedeemStatus status) => status switch
    {
        PromoRedeemStatus.Disabled => PromoRedeemResult.Disabled,
        PromoRedeemStatus.Expired => PromoRedeemResult.Expired,
        PromoRedeemStatus.Exhausted => PromoRedeemResult.Exhausted,
        PromoRedeemStatus.AlreadyUsed => PromoRedeemResult.AlreadyUsed,
        PromoRedeemStatus.AlreadyOwned => PromoRedeemResult.AlreadyOwned,
        PromoRedeemStatus.CategoryLimit => PromoRedeemResult.CategoryLimit,
        _ => PromoRedeemResult.Unknown,
    };

    /// <summary>Код: латинские буквы, цифры, «-» и «_», от 3 до 32 символов.</summary>
    public static bool IsValidCode(string code)
    {
        if (code.Length < MinCodeLength || code.Length > MaxCodeLength)
            return false;

        foreach (var c in code)
        {
            var ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_';
            if (!ok)
                return false;
        }

        return true;
    }
}