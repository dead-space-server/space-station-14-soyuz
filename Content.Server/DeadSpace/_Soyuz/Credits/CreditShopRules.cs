// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Globalization;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Shared.Localization;

namespace Content.Server.DeadSpace._Soyuz.Credits;

/// <summary>П
/// равила магазина, одинаковые для покупки, окна игрока и API: даты, скидки, требования
/// </summary>
public static class CreditShopRules
{
    /// <summary>
    /// Разбор даты из прототипа ("2026-12-01" или "2026-12-01 18:00", всегда UTC). Пусто или ошибка = null
    /// </summary>
    public static DateTime? ParseUtc(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var ok = DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date);

        return ok ? date : null;
    }

    /// <summary>
    /// Товар сейчас продаётся (сезонное окно availableFrom/availableUntil)
    /// </summary>
    public static bool IsAvailable(CreditProductPrototype product, DateTime now)
    {
        if (ParseUtc(product.AvailableFrom) is { } from && now < from)
            return false;

        return ParseUtc(product.AvailableUntil) is not { } until || now < until;
    }

    public static bool IsSaleActive(CreditProductPrototype product, DateTime now)
    {
        if (product.DiscountPercent is not > 0)
            return false;

        if (ParseUtc(product.SaleFrom) is { } from && now < from)
            return false;

        return ParseUtc(product.SaleUntil) is not { } until || now < until;
    }

    /// <summary>
    /// Скидка в процентах, если она действует сейчас (иначе 0). Ограничена 90%
    /// </summary>
    public static int ActiveDiscount(CreditProductPrototype product, DateTime now)
        => IsSaleActive(product, now) ? Math.Clamp(product.DiscountPercent!.Value, 1, 90) : 0;

    /// <summary>
    /// Цена к оплате с учётом скидки (не меньше 1)
    /// </summary>
    public static int EffectivePrice(CreditProductPrototype product, DateTime now)
    {
        var discount = ActiveDiscount(product, now);
        if (discount == 0)
            return product.Price;

        return (int) Math.Max(1, Math.Round(product.Price * (100 - discount) / 100.0));
    }

    /// <summary>
    /// Локализованная причина, по которой купить нельзя (требования к игроку). null = можно
    /// </summary>
    public static string? LockReason(CreditProductPrototype product, int playtimeHours, bool hasDiscord)
    {
        if (product.MinPlaytimeHours is { } need && playtimeHours < need)
            return Loc.GetString("credits-lock-playtime", ("need", need), ("have", playtimeHours));

        if (product.RequiresDiscord && !hasDiscord)
            return Loc.GetString("credits-lock-discord");

        return null;
    }
}