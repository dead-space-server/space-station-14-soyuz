// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.Credits;

/// <summary>
/// Кэш действующих покупок онлайн-игроков. Нужен, потому что выдача предмета при спавне
/// и подстановка цвета в чат происходят синхронно и не могут ждать запрос к БД
/// </summary>
public interface ICreditPerkCache
{
    Task RefreshAsync(Guid userId);
    void Remove(Guid userId);
    IReadOnlyList<string> GetActiveEquippedProducts(Guid userId);

    bool TryGetOwned(Guid userId, string productId, out OwnedProduct owned);
}

public sealed class CreditPerkCache : ICreditPerkCache
{
    [Dependency] private readonly ICreditRepository _repo = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private readonly ConcurrentDictionary<Guid, Dictionary<string, OwnedProduct>> _owned = new();

    public async Task RefreshAsync(Guid userId)
    {
        _owned[userId] = await _repo.GetOwnedAsync(userId);
    }

    public void Remove(Guid userId)
    {
        _owned.TryRemove(userId, out _);
    }

    public IReadOnlyList<string> GetActiveEquippedProducts(Guid userId)
    {
        if (!_owned.TryGetValue(userId, out var owned))
            return Array.Empty<string>();

        var now = DateTime.UtcNow;
        return owned
            .Where(p => (p.Value.Equipped || IsPermanent(p.Key)) && (p.Value.ExpiresAt == null || p.Value.ExpiresAt > now))
            .Select(p => p.Key)
            .ToList();
    }

    private bool IsPermanent(string productId)
        => _proto.TryIndex<CreditProductPrototype>(productId, out var product) && product.Permanent;

    public bool TryGetOwned(Guid userId, string productId, out OwnedProduct owned)
    {
        if (_owned.TryGetValue(userId, out var all) && all.TryGetValue(productId, out var found))
        {
            owned = found;
            return true;
        }

        owned = default!;
        return false;
    }
}