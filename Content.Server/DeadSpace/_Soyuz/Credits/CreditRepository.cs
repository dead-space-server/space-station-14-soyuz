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
            await EnsureBalanceRowAsync(db, userId);

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
                db.ChangeTracker.Clear();
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
                db.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Не удалось создать реферальный код.");
    }

    public async Task<ReferralApplyResult> ApplyReferralCodeAsync(Guid invitee, string code)
    {
        code = code.Trim().ToUpperInvariant();

        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

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
            return ReferralApplyResult.AlreadyApplied;
        }

        return ReferralApplyResult.Ok;
    }

    public async Task<(bool Applied, bool Rewarded)> GetReferralStatusAsync(Guid invitee)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        var row = await db.Referrals
            .Where(r => r.ServerId == sid && r.InviteeId == invitee)
            .Select(r => new { r.RewardedAt })
            .FirstOrDefaultAsync();

        return row == null ? (false, false) : (true, row.RewardedAt != null);
    }

    public async Task<ReferralStats> GetReferralStatsAsync(Guid inviter)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        var invitees = await db.Referrals
            .Where(r => r.ServerId == sid && r.InviterId == inviter)
            .Select(r => new { r.InviteeId, r.RewardedAt })
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

        return new ReferralStats(invitees.Count, rewardedIds.Count, invitees.Count - rewardedIds.Count, earned);
    }

    public async Task<IReadOnlyList<PendingReferral>> GetPendingReferralsAsync(IReadOnlyCollection<Guid> invitees)
    {
        var list = invitees.ToList();
        if (list.Count == 0)
            return Array.Empty<PendingReferral>();

        await using var db = _factory.Create();
        var sid = db.CurrentServerId;

        var pending = await db.Referrals
            .Where(r => r.ServerId == sid && r.RewardedAt == null && list.Contains(r.InviteeId))
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

        var now = DateTime.UtcNow;
        var marked = await db.Referrals
            .Where(r => r.ServerId == sid && r.InviteeId == invitee && r.RewardedAt == null)
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
        IReadOnlyCollection<string> exclusiveSiblings)
    {
        await using var db = _factory.Create();
        var sid = db.CurrentServerId;
        var now = DateTime.UtcNow;
        var siblings = exclusiveSiblings.ToList();
        var hasRow = false;

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();

            hasRow = await db.Owned.AnyAsync(o => o.ServerId == sid && o.UserId == userId && o.ProductId == productId);
            if (hasRow)
            {
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
                    changed = await timed.ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, (DateTime?) null));
                }

                if (changed == 0)
                    return PurchaseDbResult.AlreadyOwned;
            }

            if (!await ApplyInternalAsync(db, userId, -price, CreditTransactionType.Purchase,
                    $"Покупка: {productId}", userId, productId))
            {
                await tx.RollbackAsync();
                return PurchaseDbResult.Insufficient;
            }

            if (!hasRow)
            {
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
            }

            db.Purchases.Add(new CreditPurchaseRow
            {
                ServerId = sid,
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
            db.ChangeTracker.Clear();
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