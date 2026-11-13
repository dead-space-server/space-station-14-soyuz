// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using Robust.Shared.Configuration;

namespace Content.Shared.DeadSpace._Soyuz.Credits;

[CVarDefs]
public sealed class SoyuzCreditsCCVars
{
    /// <summary>Идентификатор сервера. Все данные Кредитов привязаны к нему</summary>
    public static readonly CVarDef<string> ServerId =
        CVarDef.Create("soyuz.credits.server_id", "soyuz-1", CVar.SERVERONLY);

    /// <summary>Сколько часов приглашённый должен наиграть на этом сервере</summary>
    public static readonly CVarDef<int> ReferralMinHours =
        CVarDef.Create("soyuz.credits.referral_min_hours", 25, CVar.SERVERONLY);

    /// <summary>Награда пригласившему</summary>
    public static readonly CVarDef<int> ReferralInviterReward =
        CVarDef.Create("soyuz.credits.referral_inviter_reward", 50, CVar.SERVERONLY);

    /// <summary>Награда приглашённом.</summary>
    public static readonly CVarDef<int> ReferralInviteeReward =
        CVarDef.Create("soyuz.credits.referral_invitee_reward", 25, CVar.SERVERONLY);

    /// <summary>Сколько кредитов давать за каждый полный час игры на сервере (0 = выключено)</summary>
    public static readonly CVarDef<int> PlaytimeRewardPerHour =
        CVarDef.Create("soyuz.credits.playtime_reward_per_hour", 1, CVar.SERVERONLY);

    /// <summary>Писать игроку в чат о начислении за время игры</summary>
    public static readonly CVarDef<bool> NotifyPlaytimeReward =
        CVarDef.Create("soyuz.credits.notify_playtime_reward", true, CVar.SERVERONLY);

    /// <summary>Вебхук для журнала всех выдач (без пингов)</summary>
    public static readonly CVarDef<string> DiscordLogWebhook =
        CVarDef.Create("soyuz.credits.discord_log_webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>Вебхук для покупок Discord-ролей (с пингом роли)</summary>
    public static readonly CVarDef<string> DiscordRoleWebhook =
        CVarDef.Create("soyuz.credits.discord_role_webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>Id Discord-роли, которую пингуют при покупке Discord-титула</summary>
    public static readonly CVarDef<string> DiscordRoleId =
        CVarDef.Create("soyuz.credits.discord_role_id", "", CVar.SERVERONLY);

    /// <summary>Отправлять в журнал вебхука и ежечасные награды (может быть шумно)</summary>
    public static readonly CVarDef<bool> DiscordLogPlaytimeReward =
        CVarDef.Create("soyuz.credits.discord_log_playtime_reward", false, CVar.SERVERONLY);
}