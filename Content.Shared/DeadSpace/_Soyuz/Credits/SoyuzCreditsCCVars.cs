// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using Robust.Shared.Configuration;

namespace Content.Shared.DeadSpace._Soyuz.Credits;

[CVarDefs]
public sealed class SoyuzCreditsCCVars
{
    /// <summary>Идентификатор сервера. Все данные Кредитов привязаны к нему.</summary>
    public static readonly CVarDef<string> ServerId =
        CVarDef.Create("soyuz.credits.server_id", "soyuz-1", CVar.SERVERONLY);

    // ---------- Рефералы ----------

    /// <summary>Сколько часов приглашённый должен наиграть на этом сервере.</summary>
    public static readonly CVarDef<int> ReferralMinHours =
        CVarDef.Create("soyuz.credits.referral_min_hours", 25, CVar.SERVERONLY);

    /// <summary>Награда пригласившему.</summary>
    public static readonly CVarDef<int> ReferralInviterReward =
        CVarDef.Create("soyuz.credits.referral_inviter_reward", 50, CVar.SERVERONLY);

    /// <summary>Награда приглашённому.</summary>
    public static readonly CVarDef<int> ReferralInviteeReward =
        CVarDef.Create("soyuz.credits.referral_invitee_reward", 25, CVar.SERVERONLY);

    /// <summary>Сколько кредитов давать за каждый полный час игры на сервере (0 = выключено).</summary>
    public static readonly CVarDef<int> PlaytimeRewardPerHour =
        CVarDef.Create("soyuz.credits.playtime_reward_per_hour", 1, CVar.SERVERONLY);

    /// <summary>Писать игроку в чат о начислении за время игры.</summary>
    public static readonly CVarDef<bool> NotifyPlaytimeReward =
        CVarDef.Create("soyuz.credits.notify_playtime_reward", true, CVar.SERVERONLY);

    // ---------- Discord ----------

    /// <summary>Вебхук для журнала всех выдач (без пингов).</summary>
    public static readonly CVarDef<string> DiscordLogWebhook =
        CVarDef.Create("soyuz.credits.discord_log_webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>Вебхук для покупок Discord-ролей (с пингом роли).</summary>
    public static readonly CVarDef<string> DiscordRoleWebhook =
        CVarDef.Create("soyuz.credits.discord_role_webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>Id Discord-роли, которую пингуют при покупке Discord-титула.</summary>
    public static readonly CVarDef<string> DiscordRoleId =
        CVarDef.Create("soyuz.credits.discord_role_id", "", CVar.SERVERONLY);

    /// <summary>Отправлять в журнал вебхука и ежечасные награды (может быть шумно).</summary>
    public static readonly CVarDef<bool> DiscordLogPlaytimeReward =
        CVarDef.Create("soyuz.credits.discord_log_playtime_reward", false, CVar.SERVERONLY);

    // ---------- Антиафк ----------

    /// <summary>Не считать время игры (а значит и награды за него) игрокам в AFK.</summary>
    public static readonly CVarDef<bool> AfkCheck =
        CVarDef.Create("soyuz.credits.afk_check", true, CVar.SERVERONLY);

    // ---------- Защита рефералов ----------

    /// <summary>Не засчитывать реферала, если у приглашённого и пригласившего совпадает IP или HWID.</summary>
    public static readonly CVarDef<bool> ReferralBlockSameDevice =
        CVarDef.Create("soyuz.credits.referral_block_same_device", true, CVar.SERVERONLY);

    // ---------- Ограничение частоты действий в окне ----------
    // Действие = покупка/надевание (Shop), ввод кодов (Code), листание и обновление (Browse).
    // Сколько действий разрешено за окно в секундах. 0 действий = ограничение выключено.

    public static readonly CVarDef<int> RateShopActions =
        CVarDef.Create("soyuz.credits.rate_shop_actions", 5, CVar.SERVERONLY);

    public static readonly CVarDef<int> RateShopWindow =
        CVarDef.Create("soyuz.credits.rate_shop_window", 10, CVar.SERVERONLY);

    public static readonly CVarDef<int> RateCodeActions =
        CVarDef.Create("soyuz.credits.rate_code_actions", 5, CVar.SERVERONLY);

    public static readonly CVarDef<int> RateCodeWindow =
        CVarDef.Create("soyuz.credits.rate_code_window", 60, CVar.SERVERONLY);

    public static readonly CVarDef<int> RateBrowseActions =
        CVarDef.Create("soyuz.credits.rate_browse_actions", 10, CVar.SERVERONLY);

    public static readonly CVarDef<int> RateBrowseWindow =
        CVarDef.Create("soyuz.credits.rate_browse_window", 5, CVar.SERVERONLY);

    // ---------- Лимит выдачи ----------

    /// <summary>Если один администратор выдал за последние 24 часа больше этой суммы, в Discord уходит отдельное оповещение. 0 = выключено.</summary>
    public static readonly CVarDef<int> GrantDailyLimit =
        CVarDef.Create("soyuz.credits.grant_daily_limit", 500, CVar.SERVERONLY);

    /// <summary>Вебхук для оповещений о превышении лимита выдачи. Пусто = используется вебхук журнала выдач.</summary>
    public static readonly CVarDef<string> DiscordLimitWebhook =
        CVarDef.Create("soyuz.credits.discord_limit_webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    // ---------- API для Discord-бота ----------

    /// <summary>
    /// Токен HTTP API для бота (заголовок Authorization: Bearer ...). Пусто = API выключено.
    /// Задавайте длинный случайный токен и держите порт статуса за HTTPS-прокси или во внутренней сети.
    /// </summary>
    public static readonly CVarDef<string> ApiToken =
        CVarDef.Create("soyuz.credits.api_token", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    /// Сколько кредитов за сутки можно выдать через API бота (выдача, промокоды по сумме использований). 0 = выдача через API выключена.
    /// Ограничивает ущерб при утечке токена API.
    /// </summary>
    public static readonly CVarDef<int> ApiGrantDailyLimit =
        CVarDef.Create("soyuz.credits.api_grant_daily_limit", 2000, CVar.SERVERONLY);

    // ---------- Очистка устаревшего ----------

    public static readonly CVarDef<bool> CleanupEnabled =
        CVarDef.Create("soyuz.credits.cleanup_enabled", true, CVar.SERVERONLY);

    /// <summary>Через сколько дней удалять истёкшие покупки, неподтверждённые рефералы и истёкшие промокоды.</summary>
    public static readonly CVarDef<int> CleanupDays =
        CVarDef.Create("soyuz.credits.cleanup_days", 30, CVar.SERVERONLY);
}
