// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server.Afk;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;

namespace Content.Server.DeadSpace._Soyuz.Credits;

/// <summary>
/// Считает время игры именно на этом сервере (по server_id), выдаёт реферальные награды
/// и награду за каждый час игры. Считаются игроки в игре с прикреплённой сущностью (лобби не считается).
/// Игроки в AFK (IAfkManager, порог задаётся стандартным CVar afk.time) не считаются: им не идёт ни время игры,
/// ни награда за него, ни прогресс по реферальному порогу. Отключается CVar soyuz.credits.afk_check.
/// </summary>
public sealed class CreditPlaytimeSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly ICreditsDbFactory _db = default!;
    [Dependency] private readonly ICreditRepository _repo = default!;
    [Dependency] private readonly IReferralService _referrals = default!;
    [Dependency] private readonly ICreditNotifier _notifier = default!;
    [Dependency] private readonly ICreditWebhook _webhook = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IAfkManager _afk = default!;

    private const float TickSeconds = 60f;
    private float _accum;

    public override void Initialize()
    {
        base.Initialize();
        _db.Migrate(); // применяет миграции Кредитов; при пустом server_id сервер не запустится
    }

    public override void Update(float frameTime)
    {
        _accum += frameTime;
        if (_accum < TickSeconds)
            return;

        _accum -= TickSeconds;

        var checkAfk = _cfg.GetCVar(SoyuzCreditsCCVars.AfkCheck);

        var users = new List<Guid>();
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame || session.AttachedEntity == null)
                continue;

            if (checkAfk && _afk.IsAfk(session))
                continue;

            users.Add(session.UserId.UserId);
        }

        if (users.Count > 0)
            Tick(users);
    }

    private async void Tick(List<Guid> users)
    {
        try
        {
            await _repo.AddPlaytimeAsync(users, (int) TickSeconds);
            await _referrals.CompleteDueAsync(users);
            await RewardPlaytimeAsync(users);
        }
        catch (Exception e)
        {
            Log.Error($"Ошибка тика Советских кредитов: {e}");
        }
    }

    private async Task RewardPlaytimeAsync(List<Guid> users)
    {
        var perHour = _cfg.GetCVar(SoyuzCreditsCCVars.PlaytimeRewardPerHour);
        if (perHour <= 0)
            return;

        var rewards = await _repo.RewardPlaytimeAsync(users, perHour);
        foreach (var reward in rewards)
        {
            if (_cfg.GetCVar(SoyuzCreditsCCVars.NotifyPlaytimeReward))
                _notifier.Notify(reward.UserId, Loc.GetString("credits-notify-playtime", ("amount", reward.Amount)));

            if (_cfg.GetCVar(SoyuzCreditsCCVars.DiscordLogPlaytimeReward))
            {
                await _webhook.LogGrantAsync(reward.UserId, null, null, reward.Amount,
                    CreditTransactionType.PlaytimeReward, $"Время игры: {reward.Hours} ч.");
            }
        }
    }
}