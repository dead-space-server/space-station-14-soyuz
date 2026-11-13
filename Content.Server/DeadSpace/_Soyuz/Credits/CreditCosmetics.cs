// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.Credits;

/// <summary>
/// Косметические эффекты, которые другие системы (например, чат) спрашивают у Кредитов
/// </summary>
public interface ICreditCosmetics
{
    /// <summary>
    /// Hex-цвет ника в OOC из надетого товара или null, если не куплен/не надет
    /// </summary>
    string? GetOocColor(Guid userId);
}

public sealed class CreditCosmetics : ICreditCosmetics
{
    [Dependency] private readonly ICreditPerkCache _cache = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public string? GetOocColor(Guid userId)
    {
        CreditProductPrototype? chosen = null;
        foreach (var id in _cache.GetActiveEquippedProducts(userId))
        {
            if (!_proto.TryIndex<CreditProductPrototype>(id, out var product)
                || product.ProductType != CreditProductType.OocColor
                || string.IsNullOrEmpty(product.Color))
            {
                continue;
            }

            if (chosen == null || (product.Permanent && !chosen.Permanent))
                chosen = product;
        }

        return chosen?.Color;
    }
}