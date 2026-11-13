// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Shared.DeadSpace._Soyuz.Credits;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public sealed record CreditLogEntry(long Id, Guid UserId, Guid? Actor, long Amount,
    CreditTransactionType Type, string Reason, DateTime CreatedAtUtc);

public sealed record PendingReferral(Guid Invitee, Guid Inviter, long InviteePlaytimeSeconds);

/// <summary>Действующая покупка: срок (null = навсегда) и надета ли она.</summary>
public sealed record OwnedProduct(DateTime? ExpiresAt, bool Equipped);

/// <summary>Фильтры журнала. Любое поле null = не фильтровать.</summary>
public sealed record CreditLogFilter(CreditTransactionType? Type = null, Guid? Actor = null, string? ReasonContains = null);

public sealed record ReferralStats(int Invited, int Rewarded, int Pending, long EarnedAsInviter);

public sealed record PlaytimeRewardResult(Guid UserId, long Hours, long Amount);

public enum ReferralApplyResult { Ok, UnknownCode, SelfReferral, AlreadyApplied }

public enum PurchaseDbResult { Ok, AlreadyOwned, Insufficient }

public enum EquipDbResult { Ok, NotOwned }

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

    Task<long> GetPlaytimeSecondsAsync(Guid userId);
    Task AddPlaytimeAsync(IReadOnlyCollection<Guid> users, int seconds);

    /// <summary>
    /// Начисляет награду за новые полные часы игры. Возвращает тех, кому начислено
    /// </summary>
    Task<IReadOnlyList<PlaytimeRewardResult>> RewardPlaytimeAsync(IReadOnlyCollection<Guid> users, long perHour);

    Task<string> GetOrCreateReferralCodeAsync(Guid userId);
    Task<ReferralApplyResult> ApplyReferralCodeAsync(Guid invitee, string code);
    Task<(bool Applied, bool Rewarded)> GetReferralStatusAsync(Guid invitee);
    Task<ReferralStats> GetReferralStatsAsync(Guid inviter);
    Task<IReadOnlyList<PendingReferral>> GetPendingReferralsAsync(IReadOnlyCollection<Guid> invitees);

    /// <summary>
    /// Одна транзакция: пометить награду выданной (ровно один раз) + начислить обеим сторонам
    /// </summary>
    Task<bool> TryCompleteReferralAsync(Guid invitee, Guid inviter, long inviteeReward, long inviterReward);

    /// <summary>
    /// Действующие покупки: id товара -> срок и состояние «надето»
    /// </summary>
    Task<Dictionary<string, OwnedProduct>> GetOwnedAsync(Guid userId);

    /// <summary>
    /// Покупка. Если товар с сроком уже действует — срок продлевается; permanent повторно не купить
    /// exclusiveSiblings — товары той же эксклюзивной группы: при автонадевании новой покупки они снимаются
    /// </summary>
    Task<PurchaseDbResult> TryPurchaseAsync(Guid userId, string productId, long price, int? durationDays,
        IReadOnlyCollection<string> exclusiveSiblings);

    Task<EquipDbResult> SetEquippedAsync(Guid userId, string productId, bool equipped,
        IReadOnlyCollection<string> exclusiveSiblings);
}