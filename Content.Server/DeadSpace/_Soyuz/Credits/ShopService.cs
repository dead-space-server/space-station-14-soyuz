// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public enum PurchaseResult { Ok, UnknownProduct, InvalidProduct, AlreadyOwned, Insufficient, BlockedByPermanent }

public enum EquipResult { Ok, UnknownProduct, NotOwned, Permanent, BlockedByPermanent }

public interface IShopService
{
    /// <summary>
    /// Покупка. Действующий товар со сроком продлевается, бессрочный повторно не покупается
    /// </summary>
    Task<PurchaseResult> PurchaseAsync(Guid userId, string productId);

    /// <summary>
    /// Надеть/снять купленный товар. Постоянные (permanent) товары снять нельзя
    /// </summary>
    Task<EquipResult> SetEquippedAsync(Guid userId, string productId, bool equip);
}

public sealed class ShopService : IShopService
{
    [Dependency] private readonly ICreditRepository _repo = default!;
    [Dependency] private readonly ICreditPerkCache _perks = default!;
    [Dependency] private readonly ICreditWebhook _webhook = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public async Task<PurchaseResult> PurchaseAsync(Guid userId, string productId)
    {
        if (!_proto.TryIndex<CreditProductPrototype>(productId, out var product))
            return PurchaseResult.UnknownProduct;

        if (product.Price <= 0 || product.DurationDays is <= 0)
            return PurchaseResult.InvalidProduct;

        var owned = await _repo.GetOwnedAsync(userId);
        if (!owned.ContainsKey(product.ID) && HasPermanentSibling(owned, product))
            return PurchaseResult.BlockedByPermanent;

        var siblings = SiblingPrototypes(product).Select(p => p.ID).ToList();
        var result = await _repo.TryPurchaseAsync(userId, product.ID, product.Price, product.DurationDays, siblings);
        switch (result)
        {
            case PurchaseDbResult.Ok:
                await _perks.RefreshAsync(userId);

                if (product.ProductType == CreditProductType.DiscordTitle)
                {
                    DateTime? expires = _perks.TryGetOwned(userId, product.ID, out var info) ? info.ExpiresAt : null;
                    await _webhook.NotifyRolePurchaseAsync(userId, Loc.GetString(product.Name), expires);
                }

                return PurchaseResult.Ok;
            case PurchaseDbResult.AlreadyOwned:
                return PurchaseResult.AlreadyOwned;
            default:
                return PurchaseResult.Insufficient;
        }
    }

    public async Task<EquipResult> SetEquippedAsync(Guid userId, string productId, bool equip)
    {
        if (!_proto.TryIndex<CreditProductPrototype>(productId, out var product))
            return EquipResult.UnknownProduct;

        if (!equip && product.Permanent)
            return EquipResult.Permanent;

        if (equip)
        {
            var owned = await _repo.GetOwnedAsync(userId);
            if (HasPermanentSibling(owned, product))
                return EquipResult.BlockedByPermanent;
        }

        var siblings = equip
            ? SiblingPrototypes(product).Select(p => p.ID).ToList()
            : new List<string>();

        var result = await _repo.SetEquippedAsync(userId, product.ID, equip, siblings);
        if (result == EquipDbResult.NotOwned)
            return EquipResult.NotOwned;

        await _perks.RefreshAsync(userId);
        return EquipResult.Ok;
    }

    /// <summary>
    /// У игрока есть действующий постоянный товар из той же группы (он блокирует остальные)
    /// </summary>
    private bool HasPermanentSibling(Dictionary<string, OwnedProduct> owned, CreditProductPrototype product)
    {
        return SiblingPrototypes(product).Any(s => s.Permanent && owned.ContainsKey(s.ID));
    }

    /// <summary>
    /// Товары, которые нельзя носить одновременно с данным: та же exclusiveGroup,
    /// а также любые два цвета OOC (цвет ника всегда один)
    /// </summary>
    private IEnumerable<CreditProductPrototype> SiblingPrototypes(CreditProductPrototype product)
    {
        foreach (var other in _proto.EnumeratePrototypes<CreditProductPrototype>())
        {
            if (other.ID == product.ID)
                continue;

            var sameGroup = !string.IsNullOrEmpty(product.ExclusiveGroup) && other.ExclusiveGroup == product.ExclusiveGroup;
            var bothOocColor = product.ProductType == CreditProductType.OocColor
                               && other.ProductType == CreditProductType.OocColor;

            if (sameGroup || bothOocColor)
                yield return other;
        }
    }
}