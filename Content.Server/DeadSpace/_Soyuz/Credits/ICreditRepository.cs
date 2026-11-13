// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Shared.DeadSpace._Soyuz.Credits;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public sealed record CreditLogEntry(long Id, Guid UserId, Guid? Actor, long Amount,
    CreditTransactionType Type, string Reason, DateTime CreatedAtUtc);

public sealed record PendingReferral(Guid Invitee, Guid Inviter, long InviteePlaytimeSeconds);

/// <summary>
/// Действующая покупка: срок (null = навсегда) и надета ли она
/// </summary>
public sealed record OwnedProduct(DateTime? ExpiresAt, bool Equipped);

/// <summary>
/// Фильтры журнала. Любое поле null = не фильтровать
/// </summary>
public sealed record CreditLogFilter(CreditTransactionType? Type = null, Guid? Actor = null, string? ReasonContains = null);

public sealed record ReferralStats(int Invited, int Rewarded, int Pending, long EarnedAsInviter);

/// <summary>
/// Состояние реферала приглашённого: активировал ли код, получена ли награда, отклонён ли (совпали IP/HWID)
/// <summary>
public sealed record ReferralStatus(bool Applied, bool Rewarded, bool Rejected);

public sealed record PlaytimeRewardResult(Guid UserId, long Hours, long Amount);

/// <summary>
/// Лимит категории: сколько товаров из ProductIds можно иметь одновременно
/// </summary>
public sealed record CategoryLimit(IReadOnlyCollection<string> ProductIds, int Max);

public enum ReferralApplyResult { Ok, UnknownCode, SelfReferral, AlreadyApplied, SameDevice }

public enum PurchaseDbResult { Ok, AlreadyOwned, Insufficient, CategoryLimit }

public enum EquipDbResult { Ok, NotOwned }

public enum RefundDbStatus { Ok, NothingToRefund }

/// <summary>
/// Amount — сколько кредитов возвращено (сумма всех невозвращённых покупок этого товара)
/// </summary>
public sealed record RefundDbResult(RefundDbStatus Status, long Amount);

public sealed record PromoInfo(string Code, long Credits, string? ProductId, int? MaxUses, int Uses,
    DateTime? ExpiresAt, bool Disabled, Guid? CreatedBy, DateTime CreatedAt);

public enum PromoCreateResult { Ok, AlreadyExists }

public enum PromoRedeemStatus { Ok, Unknown, Disabled, Expired, Exhausted, AlreadyUsed, AlreadyOwned, CategoryLimit }

public sealed record PromoRedeemDbResult(PromoRedeemStatus Status, long Credits = 0, string? ProductId = null);

public sealed record CleanupResult(int Owned, int Referrals, int Promos);

public sealed record BalanceEntry(Guid UserId, long Balance);

/// <summary>
/// Действующий товар игрока: для ролей Discord и напоминаний об истечении
/// </summary>
public sealed record ActiveOwned(Guid UserId, string ProductId, DateTime? ExpiresAt);

public sealed record ProductSales(string ProductId, int Count, long Revenue);

/// <summary>
/// Сводка по экономике за период (журнал неизменен, поэтому суммы считаются прямо по нему)
/// </summary>
public sealed record EconomyStats(long InCirculation, int PlayersWithBalance, long Issued, long Spent, long Taken,
    long Refunded, IReadOnlyList<ProductSales> TopProducts);

public enum DiscordLinkDbResult { Ok, DiscordAlreadyLinked, UserAlreadyLinked }

/// <summary>
/// Весь доступ к данным Кредитов. В сигнатурах НЕТ server_id — он берётся изнутри,
/// поэтому прочитать или записать данные другого сервера через этот слой нельзя
/// </summary>
public interface ICreditRepository
{
    Task<long> GetBalanceAsync(Guid userId);

    /// <summary>
    /// Атомарно меняет баланс и пишет лог. false = недостаточно средств (баланс не меняется)
    /// </summary>
    Task<bool> TryApplyAsync(Guid userId, long delta, CreditTransactionType type, string reason, Guid? actor, string? refId = null);

    /// <summary>
    /// Журнал. userId == null — журнал по всем игрокам сервера
    /// </summary>
    Task<(IReadOnlyList<CreditLogEntry> Entries, bool HasMore)> GetLogAsync(Guid? userId, int page, int pageSize, CreditLogFilter? filter = null);

    /// <summary>
    /// Сколько кредитов вручную выдал этот администратор с момента sinceUtc (только положительные ручные выдачи)
    /// </summary>
    Task<long> GetGrantedByActorSinceAsync(Guid actor, DateTime sinceUtc);

    Task<long> GetPlaytimeSecondsAsync(Guid userId);
    Task AddPlaytimeAsync(IReadOnlyCollection<Guid> users, int seconds);

    /// <summary>
    /// Начисляет награду за новые полные часы игры. Возвращает тех, кому начислено
    /// </summary>
    Task<IReadOnlyList<PlaytimeRewardResult>> RewardPlaytimeAsync(IReadOnlyCollection<Guid> users, long perHour);

    Task<string> GetOrCreateReferralCodeAsync(Guid userId);

    /// <summary>
    /// Владелец реферального кода или null, если такого кода нет
    /// </summary>
    Task<Guid?> FindReferralCodeOwnerAsync(string code);

    Task<ReferralApplyResult> ApplyReferralCodeAsync(Guid invitee, string code);
    Task<ReferralStatus> GetReferralStatusAsync(Guid invitee);
    Task<ReferralStats> GetReferralStatsAsync(Guid inviter);
    Task<IReadOnlyList<PendingReferral>> GetPendingReferralsAsync(IReadOnlyCollection<Guid> invitees);

    /// <summary>
    /// Одна транзакция: пометить награду выданной (ровно один раз) + начислить обеим сторонам
    /// </summary>
    Task<bool> TryCompleteReferralAsync(Guid invitee, Guid inviter, long inviteeReward, long inviterReward);

    /// <summary>
    /// Пометить реферал отклонённым: награда по нему больше не проверяется и не выдаётся
    /// </summary>
    Task RejectReferralAsync(Guid invitee);

    /// <summary>
    /// Действующие покупки: id товара -> срок и состояние «надето»
    /// </summary>
    Task<Dictionary<string, OwnedProduct>> GetOwnedAsync(Guid userId);

    /// <summary>
    /// Покупка. Если товар с сроком уже действует — срок продлевается; permanent повторно не купить
    /// exclusiveSiblings — товары той же эксклюзивной группы: при автонадевании новой покупки они снимаются
    /// limit — лимит категории: проверяется только для нового (или истёкшего) товара, продление его не затрагивает
    /// </summary>
    Task<PurchaseDbResult> TryPurchaseAsync(Guid userId, string productId, long price, int? durationDays,
        IReadOnlyCollection<string> exclusiveSiblings, CategoryLimit? limit = null);

    Task<EquipDbResult> SetEquippedAsync(Guid userId, string productId, bool equipped,
        IReadOnlyCollection<string> exclusiveSiblings);

    /// <summary>
    /// Возврат: все невозвращённые покупки товара помечаются возвращёнными, сумма зачисляется обратной записью
    /// в журнал (тип Refund), товар у игрока забирается. Записи журнала не изменяются и не удаляются
    /// </summary>
    Task<RefundDbResult> TryRefundAsync(Guid userId, string productId, Guid? actor, string reason);

    Task<PromoCreateResult> CreatePromoAsync(string code, long credits, string? productId, int? maxUses,
        DateTime? expiresAtUtc, Guid? createdBy);

    Task<PromoInfo?> GetPromoAsync(string code);
    Task<IReadOnlyList<PromoInfo>> ListPromosAsync(int take);

    /// <summary>
    /// false = такого кода нет
    /// </summary>
    Task<bool> SetPromoDisabledAsync(string code, bool disabled);

    /// <summary>
    /// Атомарно: проверка кода, учёт использования (один игрок — один раз), начисление кредитов и/или выдача товара
    /// productId, durationDays, siblings и limit передаёт сервис промокодов (они нужны только для кодов на товар)
    /// </summary>
    Task<PromoRedeemDbResult> TryRedeemPromoAsync(Guid userId, string code, string? productId, int? durationDays,
        IReadOnlyCollection<string> siblings, CategoryLimit? limit);
    /// <summary>
    /// Сколько раз куплен каждый товар (возвращённые покупки не считаются)
    /// </summary>
    Task<Dictionary<string, int>> GetSoldCountsAsync();

    Task<int> GetSoldCountAsync(string productId);

    Task<EconomyStats> GetEconomyStatsAsync(DateTime sinceUtc);

    /// <summary>
    /// Действующие товары всех игроков. productIds == null — любые товары;
    /// expiringBeforeUtc задан — только те, что истекают до этого момента (бессрочные не попадают)
    /// </summary>
    Task<IReadOnlyList<ActiveOwned>> GetActiveOwnedAsync(IReadOnlyCollection<string>? productIds, DateTime? expiringBeforeUtc);

    /// <summary>
    /// Discord-аккаунты привязанных игроков: UserId -> Discord Id.
    /// </summary>
    Task<Dictionary<Guid, string>> GetDiscordIdsAsync(IReadOnlyCollection<Guid> userIds);

    /// <summary>
    /// Топ игроков этого сервера по балансу (только с положительным балансом)
    /// </summary>
    Task<IReadOnlyList<BalanceEntry>> GetTopBalancesAsync(int take);

    Task<DiscordLinkDbResult> TryLinkDiscordAsync(Guid userId, string discordId);
    Task<Guid?> FindUserByDiscordAsync(string discordId);
    Task<string?> GetDiscordIdAsync(Guid userId);

    /// <summary>
    /// false = привязки не было
    /// </summary>
    Task<bool> UnlinkDiscordAsync(Guid userId);

    /// <summary>
    /// Удаляет устаревшее: истёкшие до cutoffUtc покупки, неподтверждённые рефералы (награда не выдана),
    /// созданные до cutoffUtc, и промокоды, истёкшие до cutoffUtc. Журнал и подтверждённые рефералы не трогает
    /// </summary>
    Task<CleanupResult> CleanupAsync(DateTime cutoffUtc);
}
