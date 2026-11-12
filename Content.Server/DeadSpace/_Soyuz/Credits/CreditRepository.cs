// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Content.Server.Database.DeadSpace._Soyuz.Credits;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.IoC;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public sealed class CreditRepository : ICreditRepository
{
    [Dependency] private readonly ICreditsDbFactory _factory = default!;

    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<long> GetBalanceAsync(Guid userId)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;
        return await db.Balances
            .Where(b => b.ServerId == sid && b.UserId == userId)
            .Select(b => b.Balance)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> TryApplyAsync(Guid userId, long delta, CreditTransactionType type, string reason, Guid? actor, string? refId = null)
    {
        if (delta == 0)
            return false;

        await using var db = _factory.Create();

        if (delta > 0)
            await EnsureBalanceRowAsync(db, userId); // вне транзакции: гонка создания строки безопасна

        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await ApplyInternalAsync(db, userId, delta, type, reason, actor, refId))
        {
            await tx.RollbackAsync();
            return false;
        }

        await tx.CommitAsync();
        return true;
    }

    public async Task<(IReadOnlyList<CreditLogEntry> Entries, bool HasMore)> GetLogAsync(
        Guid? userId, int page, int pageSize, CreditLogFilter? filter = null)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        var query = db.Log.Where(l => l.ServerId == sid);

        if (userId is { } uid)
            query = query.Where(l => l.UserId == uid);

        if (filter?.Type is { } type)
        {
            var typeId = (int) type;
            query = query.Where(l => l.Type == typeId);
        }

        if (filter?.Actor is { } actor)
            query = query.Where(l => l.ActorUserId == actor);

        if (!string.IsNullOrWhiteSpace(filter?.ReasonContains))
        {
            var term = filter!.ReasonContains!.Trim().ToLowerInvariant();
            query = query.Where(l => l.Reason.ToLower().Contains(term));
        }

        var rows = await query
            .OrderByDescending(l => l.Id)
            .Skip(Math.Max(0, page) * pageSize)
            .Take(pageSize + 1)
            .ToListAsync();

        var hasMore = rows.Count > pageSize;
        var entries = rows.Take(pageSize)
            .Select(l => new CreditLogEntry(l.Id, l.UserId, l.ActorUserId, l.Amount,
                (CreditTransactionType) l.Type, l.Reason, DateTime.SpecifyKind(l.CreatedAt, DateTimeKind.Utc)))
            .ToList();

        return (entries, hasMore);
    }

    public async Task<long> GetPlaytimeSecondsAsync(Guid userId)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;
        return await db.Playtimes
            .Where(p => p.ServerId == sid && p.UserId == userId)
            .Select(p => p.Seconds)
            .FirstOrDefaultAsync();
    }

    public async Task AddPlaytimeAsync(IReadOnlyCollection<Guid> users, int seconds)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        foreach (var user in users)
        {
            var rows = await db.Playtimes
                .Where(p => p.ServerId == sid && p.UserId == user)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Seconds, p => p.Seconds + seconds));

            if (rows != 0)
                continue;

            db.Playtimes.Add(new CreditPlaytimeRow { ServerId = sid, UserId = user, Seconds = seconds });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear(); // строку создали параллельно — не критично
            }
        }
    }

    public async Task<IReadOnlyList<PlaytimeRewardResult>> RewardPlaytimeAsync(IReadOnlyCollection<Guid> users, long perHour)
    {
        var results = new List<PlaytimeRewardResult>();
        if (perHour <= 0)
            return results;

        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        foreach (var user in users)
        {
            var row = await db.Playtimes
                .Where(p => p.ServerId == sid && p.UserId == user)
                .Select(p => new { p.Seconds, p.HoursRewarded })
                .FirstOrDefaultAsync();

            if (row == null)
                continue;

            var hours = row.Seconds / 3600 - row.HoursRewarded;
            if (hours <= 0)
                continue;

            await EnsureBalanceRowAsync(db, user);

            await using var tx = await db.Database.BeginTransactionAsync();

            // Оптимистичная отметка: награду за эти часы получит только один вызов.
            var oldRewarded = row.HoursRewarded;
            var newRewarded = oldRewarded + hours;
            var marked = await db.Playtimes
                .Where(p => p.ServerId == sid && p.UserId == user && p.HoursRewarded == oldRewarded)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.HoursRewarded, newRewarded));

            if (marked == 0)
            {
                await tx.RollbackAsync();
                continue;
            }

            var amount = hours * perHour;
            await ApplyInternalAsync(db, user, amount, CreditTransactionType.PlaytimeReward,
                $"Награда за время игры ({hours} ч.)", null, null);

            await tx.CommitAsync();
            results.Add(new PlaytimeRewardResult(user, hours, amount));
        }

        return results;
    }

    public async Task<string> GetOrCreateReferralCodeAsync(Guid userId)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var existing = await db.ReferralCodes
                .Where(c => c.ServerId == sid && c.UserId == userId)
                .Select(c => c.Code)
                .FirstOrDefaultAsync();

            if (existing != null)
                return existing;

            db.ReferralCodes.Add(new CreditReferralCodeRow { ServerId = sid, UserId = userId, Code = GenerateCode() });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear(); // коллизия кода или гонка — пробуем снова
            }
        }

        throw new InvalidOperationException("Не удалось создать реферальный код.");
    }

    public async Task<ReferralApplyResult> ApplyReferralCodeAsync(Guid invitee, string code)
    {
        code = code.Trim().ToUpperInvariant();

        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        // Код ищется только среди кодов ЭТОГО сервера.
        var inviter = await db.ReferralCodes
            .Where(c => c.ServerId == sid && c.Code == code)
            .Select(c => (Guid?) c.UserId)
            .FirstOrDefaultAsync();

        if (inviter == null)
            return ReferralApplyResult.UnknownCode;

        if (inviter.Value == invitee)
            return ReferralApplyResult.SelfReferral;

        if (await db.Referrals.AnyAsync(r => r.ServerId == sid && r.InviteeId == invitee))
            return ReferralApplyResult.AlreadyApplied;

        db.Referrals.Add(new CreditReferralRow
        {
            ServerId = sid,
            InviteeId = invitee,
            InviterId = inviter.Value,
            Code = code,
            AppliedAt = DateTime.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return ReferralApplyResult.AlreadyApplied; // PK (server_id, invitee_id): активировать можно только один раз
        }

        return ReferralApplyResult.Ok;
    }

    public async Task<ReferralStatus> GetReferralStatusAsync(Guid invitee)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        var row = await db.Referrals
            .Where(r => r.ServerId == sid && r.InviteeId == invitee)
            .Select(r => new { r.RewardedAt, r.RejectedAt })
            .FirstOrDefaultAsync();

        return row == null
            ? new ReferralStatus(false, false, false)
            : new ReferralStatus(true, row.RewardedAt != null, row.RejectedAt != null);
    }

    public async Task<ReferralStats> GetReferralStatsAsync(Guid inviter)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        var invitees = await db.Referrals
            .Where(r => r.ServerId == sid && r.InviterId == inviter)
            .Select(r => new { r.InviteeId, r.RewardedAt, r.RejectedAt })
            .ToListAsync();

        var rewardedIds = invitees
            .Where(i => i.RewardedAt != null)
            .Select(i => i.InviteeId.ToString())
            .ToList();

        long earned = 0;
        if (rewardedIds.Count > 0)
        {
            var refType = (int) CreditTransactionType.Referral;
            earned = await db.Log
                .Where(l => l.ServerId == sid && l.UserId == inviter && l.Type == refType
                            && l.RefId != null && rewardedIds.Contains(l.RefId))
                .SumAsync(l => l.Amount);
        }

        var pending = invitees.Count(i => i.RewardedAt == null && i.RejectedAt == null);
        return new ReferralStats(invitees.Count, rewardedIds.Count, pending, earned);
    }

    public async Task<IReadOnlyList<PendingReferral>> GetPendingReferralsAsync(IReadOnlyCollection<Guid> invitees)
    {
        var list = invitees.ToList();
        if (list.Count == 0)
            return Array.Empty<PendingReferral>();

        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        var pending = await db.Referrals
            .Where(r => r.ServerId == sid && r.RewardedAt == null && r.RejectedAt == null && list.Contains(r.InviteeId))
            .Select(r => new { r.InviteeId, r.InviterId })
            .ToListAsync();

        if (pending.Count == 0)
            return Array.Empty<PendingReferral>();

        var ids = pending.Select(p => p.InviteeId).ToList();
        var times = await db.Playtimes
            .Where(p => p.ServerId == sid && ids.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.Seconds);

        return pending
            .Select(p => new PendingReferral(p.InviteeId, p.InviterId, times.GetValueOrDefault(p.InviteeId)))
            .ToList();
    }

    public async Task<bool> TryCompleteReferralAsync(Guid invitee, Guid inviter, long inviteeReward, long inviterReward)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        if (inviteeReward > 0)
            await EnsureBalanceRowAsync(db, invitee);
        if (inviterReward > 0)
            await EnsureBalanceRowAsync(db, inviter);

        await using var tx = await db.Database.BeginTransactionAsync();

        // «Ровно один раз»: условное обновление затронет строку только у первого вызова.
        var now = DateTime.UtcNow;
        var marked = await db.Referrals
            .Where(r => r.ServerId == sid && r.InviteeId == invitee && r.RewardedAt == null && r.RejectedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RewardedAt, (DateTime?) now));

        if (marked == 0)
        {
            await tx.RollbackAsync();
            return false;
        }

        if (inviteeReward > 0)
            await ApplyInternalAsync(db, invitee, inviteeReward, CreditTransactionType.Referral,
                "Реферальная награда (приглашённый)", null, inviter.ToString());

        if (inviterReward > 0)
            await ApplyInternalAsync(db, inviter, inviterReward, CreditTransactionType.Referral,
                "Реферальная награда (пригласивший)", null, invitee.ToString());

        await tx.CommitAsync();
        return true;
    }

    public async Task<Dictionary<string, OwnedProduct>> GetOwnedAsync(Guid userId)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;
        var now = DateTime.UtcNow;

        var rows = await db.Owned
            .Where(o => o.ServerId == sid && o.UserId == userId && (o.ExpiresAt == null || o.ExpiresAt > now))
            .Select(o => new { o.ProductId, o.ExpiresAt, o.Equipped })
            .ToListAsync();

        return rows.ToDictionary(
            r => r.ProductId,
            r => new OwnedProduct(
                r.ExpiresAt == null ? null : DateTime.SpecifyKind(r.ExpiresAt.Value, DateTimeKind.Utc),
                r.Equipped));
    }

    public async Task<PurchaseDbResult> TryPurchaseAsync(Guid userId, string productId, long price, int? durationDays,
        IReadOnlyCollection<string> exclusiveSiblings, CategoryLimit? limit = null)
    {
        await using var db = _factory.Create();
        var now = DateTime.UtcNow;
        var hasRow = false;

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();

            // Списание атомарно: UPDATE ... WHERE balance - price >= 0.
            if (!await ApplyInternalAsync(db, userId, -price, CreditTransactionType.Purchase,
                    $"Покупка: {productId}", userId, productId))
            {
                await tx.RollbackAsync();
                return PurchaseDbResult.Insufficient;
            }

            // Продление, новая строка или отказ (бессрочный уже куплен / лимит категории). При отказе откатывается и списание.
            var (grant, hadRow) = await GrantOwnedAsync(db, userId, productId, durationDays, exclusiveSiblings.ToList(), limit, now);
            hasRow = hadRow;
            if (grant != PurchaseDbResult.Ok)
            {
                await tx.RollbackAsync();
                return grant;
            }

            db.Purchases.Add(new CreditPurchaseRow
            {
                ServerId = db.CurrentServerId,
                UserId = userId,
                ProductId = productId,
                Price = price,
                CreatedAt = now,
            });
            await db.SaveChangesAsync();

            await tx.CommitAsync();
            return PurchaseDbResult.Ok;
        }
        catch (DbUpdateException) when (!hasRow)
        {
            // Двойная параллельная первая покупка: PK (server_id, user_id, product_id) отбросил вторую, транзакция откатилась.
            return PurchaseDbResult.AlreadyOwned;
        }
    }

    public async Task<EquipDbResult> SetEquippedAsync(Guid userId, string productId, bool equipped,
        IReadOnlyCollection<string> exclusiveSiblings)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;
        var now = DateTime.UtcNow;
        var siblings = exclusiveSiblings.ToList();

        await using var tx = await db.Database.BeginTransactionAsync();

        var rows = await db.Owned
            .Where(o => o.ServerId == sid && o.UserId == userId && o.ProductId == productId
                        && (o.ExpiresAt == null || o.ExpiresAt > now))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Equipped, equipped));

        if (rows == 0)
        {
            await tx.RollbackAsync();
            return EquipDbResult.NotOwned;
        }

        if (equipped && siblings.Count > 0)
        {
            await db.Owned
                .Where(o => o.ServerId == sid && o.UserId == userId && siblings.Contains(o.ProductId))
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Equipped, false));
        }

        await tx.CommitAsync();
        return EquipDbResult.Ok;
    }

    public async Task<long> GetGrantedByActorSinceAsync(Guid actor, DateTime sinceUtc)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;
        var manual = ManualGrantTypes;

        // Только ручные выдачи: покупки, возвраты, промокоды и автоматические награды не считаются.
        return await db.Log
            .Where(l => l.ServerId == sid && l.ActorUserId == actor && l.Amount > 0
                        && l.CreatedAt >= sinceUtc && manual.Contains(l.Type))
            .SumAsync(l => l.Amount);
    }

    public async Task<Guid?> FindReferralCodeOwnerAsync(string code)
    {
        code = code.Trim().ToUpperInvariant();

        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        return await db.ReferralCodes
            .Where(c => c.ServerId == sid && c.Code == code)
            .Select(c => (Guid?) c.UserId)
            .FirstOrDefaultAsync();
    }

    public async Task RejectReferralAsync(Guid invitee)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;
        var now = DateTime.UtcNow;

        await db.Referrals
            .Where(r => r.ServerId == sid && r.InviteeId == invitee && r.RewardedAt == null && r.RejectedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RejectedAt, (DateTime?) now));
    }

    public async Task<RefundDbResult> TryRefundAsync(Guid userId, string productId, Guid? actor, string reason)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        await EnsureBalanceRowAsync(db, userId);
        await using var tx = await db.Database.BeginTransactionAsync();

        var purchases = await db.Purchases
            .Where(p => p.ServerId == sid && p.UserId == userId && p.ProductId == productId && p.RefundedAt == null)
            .Select(p => new { p.Id, p.Price })
            .ToListAsync();

        if (purchases.Count == 0)
        {
            await tx.RollbackAsync();
            return new RefundDbResult(RefundDbStatus.NothingToRefund, 0);
        }

        // Покупки помечаются условным UPDATE: параллельный второй возврат не найдёт невозвращённых строк.
        var ids = purchases.Select(p => p.Id).ToList();
        var now = DateTime.UtcNow;
        var marked = await db.Purchases
            .Where(p => p.ServerId == sid && ids.Contains(p.Id) && p.RefundedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.RefundedAt, (DateTime?) now));

        if (marked != ids.Count)
        {
            await tx.RollbackAsync();
            return new RefundDbResult(RefundDbStatus.NothingToRefund, 0);
        }

        var total = purchases.Sum(p => p.Price);

        // Журнал неизменен: к покупке добавляется обратная запись.
        await ApplyInternalAsync(db, userId, total, CreditTransactionType.Refund, reason, actor, productId);

        await db.Owned
            .Where(o => o.ServerId == sid && o.UserId == userId && o.ProductId == productId)
            .ExecuteDeleteAsync();

        await tx.CommitAsync();
        return new RefundDbResult(RefundDbStatus.Ok, total);
    }

    // ---------- Промокоды ----------

    public async Task<PromoCreateResult> CreatePromoAsync(string code, long credits, string? productId, int? maxUses,
        DateTime? expiresAtUtc, Guid? createdBy)
    {
        code = NormalizePromoCode(code);

        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        if (await db.Promos.AnyAsync(p => p.ServerId == sid && p.Code == code))
            return PromoCreateResult.AlreadyExists;

        db.Promos.Add(new CreditPromoRow
        {
            ServerId = sid,
            Code = code,
            Credits = credits,
            ProductId = productId,
            MaxUses = maxUses,
            ExpiresAt = expiresAtUtc,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return PromoCreateResult.AlreadyExists;
        }

        return PromoCreateResult.Ok;
    }

    public async Task<PromoInfo?> GetPromoAsync(string code)
    {
        code = NormalizePromoCode(code);

        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        var row = await db.Promos
            .Where(p => p.ServerId == sid && p.Code == code)
            .FirstOrDefaultAsync();

        return row == null ? null : ToInfo(row);
    }

    public async Task<IReadOnlyList<PromoInfo>> ListPromosAsync(int take)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        var rows = await db.Promos
            .Where(p => p.ServerId == sid)
            .OrderByDescending(p => p.CreatedAt)
            .Take(Math.Max(1, take))
            .ToListAsync();

        return rows.Select(ToInfo).ToList();
    }

    public async Task<bool> SetPromoDisabledAsync(string code, bool disabled)
    {
        code = NormalizePromoCode(code);

        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        var rows = await db.Promos
            .Where(p => p.ServerId == sid && p.Code == code)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Disabled, disabled));

        return rows != 0;
    }

    public async Task<PromoRedeemDbResult> TryRedeemPromoAsync(Guid userId, string code, string? productId, int? durationDays,
        IReadOnlyCollection<string> siblings, CategoryLimit? limit)
    {
        code = NormalizePromoCode(code);

        await using var db = _factory.Create();
        var sid = db.CurrentServerId;
        var now = DateTime.UtcNow;

        var promo = await db.Promos
            .Where(p => p.ServerId == sid && p.Code == code)
            .Select(p => new { p.Credits, p.ProductId, p.ExpiresAt, p.Disabled })
            .FirstOrDefaultAsync();

        if (promo == null)
            return new PromoRedeemDbResult(PromoRedeemStatus.Unknown);
        if (promo.Disabled)
            return new PromoRedeemDbResult(PromoRedeemStatus.Disabled);
        if (promo.ExpiresAt != null && promo.ExpiresAt <= now)
            return new PromoRedeemDbResult(PromoRedeemStatus.Expired);
        if (promo.ProductId != productId)
            return new PromoRedeemDbResult(PromoRedeemStatus.Unknown); // сервис передал не тот товар

        if (await db.PromoUses.AnyAsync(u => u.ServerId == sid && u.Code == code && u.UserId == userId))
            return new PromoRedeemDbResult(PromoRedeemStatus.AlreadyUsed);

        await EnsureBalanceRowAsync(db, userId); // запись в журнал нужна и для кодов на товар

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();

            // Лимит использований занимается условным UPDATE: двое не займут последнее место одновременно.
            var taken = await db.Promos
                .Where(p => p.ServerId == sid && p.Code == code && !p.Disabled && (p.MaxUses == null || p.Uses < p.MaxUses))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Uses, p => p.Uses + 1));

            if (taken == 0)
            {
                await tx.RollbackAsync();
                return new PromoRedeemDbResult(PromoRedeemStatus.Exhausted);
            }

            // PK (server_id, code, user_id): один игрок — одно использование.
            db.PromoUses.Add(new CreditPromoUseRow { ServerId = sid, Code = code, UserId = userId, UsedAt = now });
            await db.SaveChangesAsync();

            if (promo.ProductId != null)
            {
                var (grant, _) = await GrantOwnedAsync(db, userId, promo.ProductId, durationDays, siblings.ToList(), limit, now);
                if (grant != PurchaseDbResult.Ok)
                {
                    await tx.RollbackAsync(); // использование кода не засчитывается
                    return new PromoRedeemDbResult(grant == PurchaseDbResult.CategoryLimit
                        ? PromoRedeemStatus.CategoryLimit
                        : PromoRedeemStatus.AlreadyOwned);
                }
            }

            var reason = promo.ProductId == null
                ? $"Промокод {code}"
                : promo.Credits > 0
                    ? $"Промокод {code} (и товар {promo.ProductId})"
                    : $"Промокод {code}: товар {promo.ProductId}";

            // Запись есть всегда (для кода на товар сумма 0): так выдача видна в журнале. Заодно сохраняется новая строка товара.
            await ApplyInternalAsync(db, userId, promo.Credits, CreditTransactionType.PromoCode, reason, null, code);

            await tx.CommitAsync();
            return new PromoRedeemDbResult(PromoRedeemStatus.Ok, promo.Credits, promo.ProductId);
        }
        catch (DbUpdateException)
        {
            return new PromoRedeemDbResult(PromoRedeemStatus.AlreadyUsed);
        }
    }

    // ---------- Discord ----------

    public async Task<IReadOnlyList<BalanceEntry>> GetTopBalancesAsync(int take)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        var rows = await db.Balances
            .Where(b => b.ServerId == sid && b.Balance > 0)
            .OrderByDescending(b => b.Balance)
            .ThenBy(b => b.UserId)
            .Take(Math.Max(1, take))
            .Select(b => new { b.UserId, b.Balance })
            .ToListAsync();

        return rows.Select(r => new BalanceEntry(r.UserId, r.Balance)).ToList();
    }

    public async Task<DiscordLinkDbResult> TryLinkDiscordAsync(Guid userId, string discordId)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        if (await db.DiscordLinks.AnyAsync(l => l.ServerId == sid && l.DiscordId == discordId))
            return DiscordLinkDbResult.DiscordAlreadyLinked;

        if (await db.DiscordLinks.AnyAsync(l => l.ServerId == sid && l.UserId == userId))
            return DiscordLinkDbResult.UserAlreadyLinked;

        db.DiscordLinks.Add(new CreditDiscordLinkRow
        {
            ServerId = sid,
            UserId = userId,
            DiscordId = discordId,
            LinkedAt = DateTime.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return DiscordLinkDbResult.DiscordAlreadyLinked; // гонка: уникальный индекс не дал создать дубль
        }

        return DiscordLinkDbResult.Ok;
    }

    public async Task<Guid?> FindUserByDiscordAsync(string discordId)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        return await db.DiscordLinks
            .Where(l => l.ServerId == sid && l.DiscordId == discordId)
            .Select(l => (Guid?) l.UserId)
            .FirstOrDefaultAsync();
    }

    public async Task<string?> GetDiscordIdAsync(Guid userId)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        return await db.DiscordLinks
            .Where(l => l.ServerId == sid && l.UserId == userId)
            .Select(l => l.DiscordId)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> UnlinkDiscordAsync(Guid userId)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        var rows = await db.DiscordLinks
            .Where(l => l.ServerId == sid && l.UserId == userId)
            .ExecuteDeleteAsync();

        return rows != 0;
    }

    // ---------- Очистка ----------

    public async Task<CleanupResult> CleanupAsync(DateTime cutoffUtc)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        var owned = await db.Owned
            .Where(o => o.ServerId == sid && o.ExpiresAt != null && o.ExpiresAt < cutoffUtc)
            .ExecuteDeleteAsync();

        // Подтверждённые рефералы (награда выдана) не удаляются: иначе приглашённый смог бы активировать код второй раз,
        // а пригласивший потерял бы статистику.
        var referrals = await db.Referrals
            .Where(r => r.ServerId == sid && r.RewardedAt == null && r.AppliedAt < cutoffUtc)
            .ExecuteDeleteAsync();

        var oldCodes = await db.Promos
            .Where(p => p.ServerId == sid && p.ExpiresAt != null && p.ExpiresAt < cutoffUtc)
            .Select(p => p.Code)
            .ToListAsync();

        var promos = 0;
        if (oldCodes.Count > 0)
        {
            await db.PromoUses
                .Where(u => u.ServerId == sid && oldCodes.Contains(u.Code))
                .ExecuteDeleteAsync();

            promos = await db.Promos
                .Where(p => p.ServerId == sid && oldCodes.Contains(p.Code))
                .ExecuteDeleteAsync();
        }

        return new CleanupResult(owned, referrals, promos);
    }

    // ------------------------------------------------------------------

    private static async Task<bool> ApplyInternalAsync(SoyuzCreditsDbContext db, Guid userId, long delta,
        CreditTransactionType type, string reason, Guid? actor, string? refId)
    {
        var sid = db.CurrentServerId;
        var now = DateTime.UtcNow;

        var rows = await db.Balances
            .Where(b => b.ServerId == sid && b.UserId == userId && b.Balance + delta >= 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Balance, b => b.Balance + delta)
                .SetProperty(b => b.UpdatedAt, now));

        if (rows == 0)
            return false;

        db.Log.Add(new CreditLogRow
        {
            ServerId = sid,
            UserId = userId,
            ActorUserId = actor,
            Amount = delta,
            Type = (int) type,
            Reason = reason,
            RefId = refId,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Выдаёт товар: действующий со сроком продлевает, иначе добавляет надетую строку (товары той же группы снимаются).
    /// Лимит категории проверяется только для нового или истёкшего товара. Новую строку сохраняет вызывающий (SaveChangesAsync).
    /// </summary>
    private static async Task<(PurchaseDbResult Result, bool HadRow)> GrantOwnedAsync(SoyuzCreditsDbContext db, Guid userId,
        string productId, int? durationDays, IReadOnlyCollection<string> exclusiveSiblings, CategoryLimit? limit, DateTime now)
    {
        var sid = db.CurrentServerId;
        var siblings = exclusiveSiblings.ToList();

        var existing = await db.Owned
            .Where(o => o.ServerId == sid && o.UserId == userId && o.ProductId == productId)
            .Select(o => new { o.ExpiresAt })
            .FirstOrDefaultAsync();

        var hadRow = existing != null;
        var active = existing != null && (existing.ExpiresAt == null || existing.ExpiresAt > now);

        if (!active && limit != null)
        {
            var ids = limit.ProductIds.ToList();
            var count = await db.Owned.CountAsync(o => o.ServerId == sid && o.UserId == userId
                                                       && o.ProductId != productId && ids.Contains(o.ProductId)
                                                       && (o.ExpiresAt == null || o.ExpiresAt > now));
            if (count >= limit.Max)
                return (PurchaseDbResult.CategoryLimit, hadRow);
        }

        if (hadRow)
        {
            // Только товары со сроком можно продлевать (строка permanent имеет ExpiresAt == null и сюда не попадёт).
            // Арифметика выполняется в самом UPDATE, поэтому параллельные покупки складываются корректно.
            var timed = db.Owned.Where(o => o.ServerId == sid && o.UserId == userId
                                            && o.ProductId == productId && o.ExpiresAt != null);
            int changed;
            if (durationDays is { } days)
            {
                var fromNow = now.AddDays(days);
                changed = await timed.ExecuteUpdateAsync(s => s.SetProperty(
                    o => o.ExpiresAt,
                    o => (DateTime?) (o.ExpiresAt > now ? o.ExpiresAt!.Value.AddDays(days) : fromNow)));
            }
            else
            {
                // Товар стал «навсегда»: покупка превращает срок в бессрочный.
                changed = await timed.ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, (DateTime?) null));
            }

            return (changed == 0 ? PurchaseDbResult.AlreadyOwned : PurchaseDbResult.Ok, hadRow);
        }

        // Новая покупка сразу надевается, а товары той же группы снимаются.
        if (siblings.Count > 0)
        {
            await db.Owned
                .Where(o => o.ServerId == sid && o.UserId == userId && siblings.Contains(o.ProductId))
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Equipped, false));
        }

        db.Owned.Add(new CreditOwnedRow
        {
            ServerId = sid,
            UserId = userId,
            ProductId = productId,
            Equipped = true,
            ExpiresAt = durationDays is { } d ? now.AddDays(d) : null,
        });

        return (PurchaseDbResult.Ok, hadRow);
    }

    private static string NormalizePromoCode(string code) => code.Trim().ToUpperInvariant();

    private static PromoInfo ToInfo(CreditPromoRow p) => new(p.Code, p.Credits, p.ProductId, p.MaxUses, p.Uses,
        p.ExpiresAt == null ? null : DateTime.SpecifyKind(p.ExpiresAt.Value, DateTimeKind.Utc),
        p.Disabled, p.CreatedBy, DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc));

    private static readonly int[] ManualGrantTypes =
    {
        (int) CreditTransactionType.AdminGive,
        (int) CreditTransactionType.DeveloperReward,
        (int) CreditTransactionType.EventReward,
        (int) CreditTransactionType.ModeratorReward,
        (int) CreditTransactionType.RolePlayReward,
        (int) CreditTransactionType.OtherReward,
    };

    private static async Task EnsureBalanceRowAsync(SoyuzCreditsDbContext db, Guid userId)
    {
        var sid = db.CurrentServerId;
        if (await db.Balances.AnyAsync(b => b.ServerId == sid && b.UserId == userId))
            return;

        db.Balances.Add(new CreditBalanceRow { ServerId = sid, UserId = userId, Balance = 0, UpdatedAt = DateTime.UtcNow });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear(); // создали параллельно
        }
    }

    private static string GenerateCode()
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(chars);
    }
}