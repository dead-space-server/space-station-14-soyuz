// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public enum CreditResult { Ok, InvalidAmount, InvalidReason, Forbidden, Failed, Insufficient, NotFound }

/// <summary>Amount — сколько кредитов возвращено игроку (заполнено при Ok).</summary>
public sealed record CreditRefundResult(CreditResult Result, long Amount);

public interface ICreditService
{
    Task<long> GetBalanceAsync(Guid userId);

    /// <summary>
    /// Ручная выдача. actor == null означает консоль сервера. Для игроков требуется флаг Server.
    /// </summary>
    Task<CreditResult> GiveAsync(Guid target, long amount, string reason, ICommonSession? actor,
        CreditTransactionType type = CreditTransactionType.AdminGive);

    /// <summary>
    /// Списание с причиной. Баланс не может стать отрицательным: при нехватке средств возвращается Insufficient.
    /// В журнале появляется отрицательная запись (тип AdminTake), старые записи не меняются.
    /// </summary>
    Task<CreditResult> TakeAsync(Guid target, long amount, string reason, ICommonSession? actor);

    /// <summary>
    /// Возврат кредитов за покупку товара и изъятие товара. В журнале появляется обратная запись (тип Refund).
    /// Возвращаются все ещё не возвращённые покупки этого товара.
    /// </summary>
    Task<CreditRefundResult> RefundAsync(Guid target, string productId, string reason, ICommonSession? actor);

    /// <summary>Журнал. userId == null — по всем игрокам. Поддерживает фильтры по типу, исполнителю и тексту причины.</summary>
    Task<(IReadOnlyList<CreditLogEntry> Entries, bool HasMore)> GetLogAsync(Guid? userId, int page, int pageSize,
        CreditLogFilter? filter = null);
}

public sealed class CreditService : ICreditService
{
    [Dependency] private readonly ICreditRepository _repo = default!;
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly ICreditNotifier _notifier = default!;
    [Dependency] private readonly ICreditWebhook _webhook = default!;
    [Dependency] private readonly ICreditPerkCache _perks = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ILogManager _log = default!;

    public const long MaxGrant = 1_000_000;
    public const int MaxReasonLength = 256;

    public Task<long> GetBalanceAsync(Guid userId) => _repo.GetBalanceAsync(userId);

    public async Task<CreditResult> GiveAsync(Guid target, long amount, string reason, ICommonSession? actor,
        CreditTransactionType type = CreditTransactionType.AdminGive)
    {
        // Отрицательная и нулевая суммы отклоняются ещё до обращения к БД.
        if (amount <= 0 || amount > MaxGrant)
            return CreditResult.InvalidAmount;

        reason = reason.Trim();
        if (reason.Length == 0 || reason.Length > MaxReasonLength)
            return CreditResult.InvalidReason;

        // Только ручные типы; остальные создаёт исключительно код.
        if (type is CreditTransactionType.Purchase or CreditTransactionType.Referral or CreditTransactionType.PlaytimeReward
            or CreditTransactionType.AdminTake or CreditTransactionType.Refund or CreditTransactionType.PromoCode)
            return CreditResult.Forbidden;

        // Второй рубеж (первый — [AdminCommand(AdminFlags.Server)] на команде).
        if (actor != null && !_admin.HasAdminFlag(actor, AdminFlags.Server))
            return CreditResult.Forbidden;

        var actorId = actor?.UserId.UserId;
        var ok = await _repo.TryApplyAsync(target, amount, type, reason, actorId);
        if (!ok)
            return CreditResult.Failed;

        _notifier.Notify(target, Loc.GetString("credits-notify-admin-give", ("amount", amount), ("reason", reason)));
        await _webhook.LogGrantAsync(target, actorId, actor?.Name, amount, type, reason);
        await CheckDailyLimitAsync(actorId, actor?.Name);
        return CreditResult.Ok;
    }

    public async Task<CreditResult> TakeAsync(Guid target, long amount, string reason, ICommonSession? actor)
    {
        if (amount <= 0 || amount > MaxGrant)
            return CreditResult.InvalidAmount;

        reason = reason.Trim();
        if (reason.Length == 0 || reason.Length > MaxReasonLength)
            return CreditResult.InvalidReason;

        if (actor != null && !_admin.HasAdminFlag(actor, AdminFlags.Server))
            return CreditResult.Forbidden;

        var actorId = actor?.UserId.UserId;

        // Атомарно: UPDATE ... WHERE balance - amount >= 0. Баланс никогда не уходит в минус.
        var ok = await _repo.TryApplyAsync(target, -amount, CreditTransactionType.AdminTake, reason, actorId);
        if (!ok)
            return CreditResult.Insufficient;

        _notifier.Notify(target, Loc.GetString("credits-notify-admin-take", ("amount", amount), ("reason", reason)));
        await _webhook.LogGrantAsync(target, actorId, actor?.Name, -amount, CreditTransactionType.AdminTake, reason);
        return CreditResult.Ok;
    }

    public async Task<CreditRefundResult> RefundAsync(Guid target, string productId, string reason, ICommonSession? actor)
    {
        productId = productId.Trim();
        if (productId.Length == 0)
            return new CreditRefundResult(CreditResult.NotFound, 0);

        reason = reason.Trim();
        if (reason.Length == 0 || reason.Length > MaxReasonLength)
            return new CreditRefundResult(CreditResult.InvalidReason, 0);

        if (actor != null && !_admin.HasAdminFlag(actor, AdminFlags.Server))
            return new CreditRefundResult(CreditResult.Forbidden, 0);

        var actorId = actor?.UserId.UserId;
        var result = await _repo.TryRefundAsync(target, productId, actorId, reason);
        if (result.Status != RefundDbStatus.Ok)
            return new CreditRefundResult(CreditResult.NotFound, 0);

        // Кэш нужен только онлайн-игрокам: у офлайн-игрока он загрузится при входе.
        if (_players.TryGetSessionById(new NetUserId(target), out _))
            await _perks.RefreshAsync(target);

        var name = _proto.TryIndex<CreditProductPrototype>(productId, out var product) ? Loc.GetString(product.Name) : productId;
        _notifier.Notify(target, Loc.GetString("credits-notify-admin-refund",
            ("amount", result.Amount), ("product", name), ("reason", reason)));
        await _webhook.LogGrantAsync(target, actorId, actor?.Name, result.Amount, CreditTransactionType.Refund,
            $"Возврат: {productId}. {reason}");

        return new CreditRefundResult(CreditResult.Ok, result.Amount);
    }

    public Task<(IReadOnlyList<CreditLogEntry> Entries, bool HasMore)> GetLogAsync(Guid? userId, int page, int pageSize,
        CreditLogFilter? filter = null)
        => _repo.GetLogAsync(userId, page, pageSize, filter);

    /// <summary>
    /// Если администратор за последние 24 часа выдал больше лимита (CVar), в Discord уходит отдельное оповещение.
    /// Оповещение приходит при каждой выдаче, после которой сумма остаётся выше лимита. Ошибки здесь не влияют на выдачу.
    /// </summary>
    private async Task CheckDailyLimitAsync(Guid? actorId, string? actorName)
    {
        if (actorId == null)
            return; // консоль сервера не считается

        try
        {
            var limit = _cfg.GetCVar(SoyuzCreditsCCVars.GrantDailyLimit);
            if (limit <= 0)
                return;

            var total = await _repo.GetGrantedByActorSinceAsync(actorId.Value, DateTime.UtcNow.AddHours(-24));
            if (total > limit)
                await _webhook.NotifyGrantLimitAsync(actorId.Value, actorName, total, limit);
        }
        catch (Exception e)
        {
            _log.GetSawmill("credits").Error($"Проверка суточного лимита выдачи: {e}");
        }
    }
}
