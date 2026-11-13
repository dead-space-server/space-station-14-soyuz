// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Localization;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public interface IReferralService
{
    Task<string> GetCodeAsync(Guid userId);
    Task<ReferralApplyResult> ApplyAsync(Guid invitee, string code);

    /// <summary>
    /// Выдаёт награды тем из переданных игроков, кто уже наиграл нужное время на этом сервере
    /// </summary>
    Task CompleteDueAsync(IReadOnlyCollection<Guid> users);
}

public sealed class ReferralService : IReferralService
{
    [Dependency] private readonly ICreditRepository _repo = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ICreditNotifier _notifier = default!;
    [Dependency] private readonly ICreditWebhook _webhook = default!;

    public Task<string> GetCodeAsync(Guid userId) => _repo.GetOrCreateReferralCodeAsync(userId);

    public Task<ReferralApplyResult> ApplyAsync(Guid invitee, string code) => _repo.ApplyReferralCodeAsync(invitee, code);

    public async Task CompleteDueAsync(IReadOnlyCollection<Guid> users)
    {
        var pending = await _repo.GetPendingReferralsAsync(users);
        if (pending.Count == 0)
            return;

        var needSeconds = _cfg.GetCVar(SoyuzCreditsCCVars.ReferralMinHours) * 3600L;
        var inviteeReward = _cfg.GetCVar(SoyuzCreditsCCVars.ReferralInviteeReward);
        var inviterReward = _cfg.GetCVar(SoyuzCreditsCCVars.ReferralInviterReward);

        foreach (var referral in pending)
        {
            if (referral.InviteePlaytimeSeconds < needSeconds)
                continue;

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
}