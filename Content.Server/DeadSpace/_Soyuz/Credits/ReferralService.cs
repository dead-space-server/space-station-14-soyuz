// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Network;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public interface IReferralService
{
    Task<string> GetCodeAsync(Guid userId);
    Task<ReferralApplyResult> ApplyAsync(Guid invitee, string code);

    /// <summary>Выдаёт награды тем из переданных игроков, кто уже наиграл нужное время на этом сервере.</summary>
    Task CompleteDueAsync(IReadOnlyCollection<Guid> users);
}

public sealed class ReferralService : IReferralService
{
    [Dependency] private readonly ICreditRepository _repo = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ICreditNotifier _notifier = default!;
    [Dependency] private readonly ICreditWebhook _webhook = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly ILogManager _log = default!;

    public Task<string> GetCodeAsync(Guid userId) => _repo.GetOrCreateReferralCodeAsync(userId);

    public async Task<ReferralApplyResult> ApplyAsync(Guid invitee, string code)
    {
        // Защита от мультиаккаунтов: сразу отказываем, если у приглашённого совпадает IP или HWID с владельцем кода.
        if (_cfg.GetCVar(SoyuzCreditsCCVars.ReferralBlockSameDevice)
            && await _repo.FindReferralCodeOwnerAsync(code) is { } inviter
            && inviter != invitee
            && await SameDeviceAsync(invitee, inviter))
        {
            return ReferralApplyResult.SameDevice;
        }

        return await _repo.ApplyReferralCodeAsync(invitee, code);
    }

    public async Task CompleteDueAsync(IReadOnlyCollection<Guid> users)
    {
        var pending = await _repo.GetPendingReferralsAsync(users);
        if (pending.Count == 0)
            return;

        var needSeconds = _cfg.GetCVar(SoyuzCreditsCCVars.ReferralMinHours) * 3600L;
        var inviteeReward = _cfg.GetCVar(SoyuzCreditsCCVars.ReferralInviteeReward);
        var inviterReward = _cfg.GetCVar(SoyuzCreditsCCVars.ReferralInviterReward);
        var blockSameDevice = _cfg.GetCVar(SoyuzCreditsCCVars.ReferralBlockSameDevice);

        foreach (var referral in pending)
        {
            if (referral.InviteePlaytimeSeconds < needSeconds)
                continue;

            // Повторная проверка перед выдачей: IP или HWID могли совпасть уже после ввода кода.
            // Реферал помечается отклонённым и больше не проверяется, награда не выдаётся.
            if (blockSameDevice && await SameDeviceAsync(referral.Invitee, referral.Inviter))
            {
                await _repo.RejectReferralAsync(referral.Invitee);
                _log.GetSawmill("credits").Info(
                    $"Реферал отклонён (совпадение IP/HWID): приглашённый {referral.Invitee}, пригласивший {referral.Inviter}");
                continue;
            }

            if (!await _repo.TryCompleteReferralAsync(referral.Invitee, referral.Inviter, inviteeReward, inviterReward))
                continue;

            if (inviteeReward > 0)
            {
                _notifier.Notify(referral.Invitee, Loc.GetString("credits-notify-referral-invitee", ("amount", inviteeReward)));
                await _webhook.LogGrantAsync(referral.Invitee, null, null, inviteeReward,
                    CreditTransactionType.Referral, "Реферальная награда (приглашённый)");
            }

            if (inviterReward > 0)
            {
                _notifier.Notify(referral.Inviter, Loc.GetString("credits-notify-referral-inviter", ("amount", inviterReward)));
                await _webhook.LogGrantAsync(referral.Inviter, null, null, inviterReward,
                    CreditTransactionType.Referral, "Реферальная награда (пригласивший)");
            }
        }
    }

    /// <summary>
    /// Совпадает ли IP или HWID двух аккаунтов (по последним данным подключения из основной БД).
    /// Loopback-адрес не считается совпадением (так не блокируются тесты на локальном сервере).
    /// Если данных нет или запрос к БД не удался, реферал НЕ блокируется (ошибка пишется в лог).
    /// </summary>
    private async Task<bool> SameDeviceAsync(Guid a, Guid b)
    {
        try
        {
            var first = await _db.GetPlayerRecordByUserId(new NetUserId(a));
            var second = await _db.GetPlayerRecordByUserId(new NetUserId(b));
            if (first == null || second == null)
                return false;

            var addrA = first.LastSeenAddress;
            var addrB = second.LastSeenAddress;
            if (addrA != null && addrB != null
                && addrA.Equals(addrB)
                && !IPAddress.IsLoopback(addrA))
            {
                return true;
            }

            return first.HWId != null && second.HWId != null
                && first.HWId.Hwid.SequenceEqual(second.HWId.Hwid);
        }
        catch (Exception e)
        {
            _log.GetSawmill("credits").Error($"Проверка IP/HWID для реферала: {e}");
            return false;
        }
    }
}