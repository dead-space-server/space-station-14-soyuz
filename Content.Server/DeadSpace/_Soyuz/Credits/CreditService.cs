// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Player;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public enum CreditResult { Ok, InvalidAmount, InvalidReason, Forbidden, Failed }

public interface ICreditService
{
    Task<long> GetBalanceAsync(Guid userId);

    /// <summary>
    /// Ручная выдача. actor == null означает консоль сервера
    /// </summary>
    Task<CreditResult> GiveAsync(Guid target, long amount, string reason, ICommonSession? actor,
        CreditTransactionType type = CreditTransactionType.AdminGive);

    Task<(IReadOnlyList<CreditLogEntry> Entries, bool HasMore)> GetLogAsync(Guid? userId, int page, int pageSize,
        CreditLogFilter? filter = null);
}

public sealed class CreditService : ICreditService
{
    [Dependency] private readonly ICreditRepository _repo = default!;
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly ICreditNotifier _notifier = default!;
    [Dependency] private readonly ICreditWebhook _webhook = default!;

    public const long MaxGrant = 1_000_000;
    public const int MaxReasonLength = 256;

    public Task<long> GetBalanceAsync(Guid userId) => _repo.GetBalanceAsync(userId);

    public async Task<CreditResult> GiveAsync(Guid target, long amount, string reason, ICommonSession? actor,
        CreditTransactionType type = CreditTransactionType.AdminGive)
    {
        if (amount <= 0 || amount > MaxGrant)
            return CreditResult.InvalidAmount;

        reason = reason.Trim();
        if (reason.Length == 0 || reason.Length > MaxReasonLength)
            return CreditResult.InvalidReason;

        if (type is CreditTransactionType.Purchase or CreditTransactionType.Referral or CreditTransactionType.PlaytimeReward)
            return CreditResult.Forbidden;

        if (actor != null && !_admin.HasAdminFlag(actor, AdminFlags.Server))
            return CreditResult.Forbidden;

        var actorId = actor?.UserId.UserId;
        var ok = await _repo.TryApplyAsync(target, amount, type, reason, actorId);
        if (!ok)
            return CreditResult.Failed;

        _notifier.Notify(target, Loc.GetString("credits-notify-admin-give", ("amount", amount), ("reason", reason)));
        await _webhook.LogGrantAsync(target, actorId, actor?.Name, amount, type, reason);
        return CreditResult.Ok;
    }

    public Task<(IReadOnlyList<CreditLogEntry> Entries, bool HasMore)> GetLogAsync(Guid? userId, int page, int pageSize,
        CreditLogFilter? filter = null)
        => _repo.GetLogAsync(userId, page, pageSize, filter);
}