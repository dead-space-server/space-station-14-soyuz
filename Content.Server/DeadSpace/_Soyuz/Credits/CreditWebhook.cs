// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public interface ICreditWebhook
{
    /// <summary>Запись о выдаче в журнал Discord. Без пингов.</summary>
    Task LogGrantAsync(Guid target, Guid? actor, string? actorName, long amount, CreditTransactionType type, string reason);

    /// <summary>Покупка Discord-роли: сообщение с пингом роли из CVar.</summary>
    Task NotifyRolePurchaseAsync(Guid buyer, string productName, DateTime? expiresAtUtc);

    /// <summary>Отдельное оповещение: администратор выдал за сутки больше лимита. Без пингов.</summary>
    Task NotifyGrantLimitAsync(Guid actor, string? actorName, long total, long limit);
}

/// <summary>
/// Отправка в Discord через вебхуки. Сообщения идут через очередь с паузами и повтором при лимите Discord (429),
/// ошибки отправки никогда не влияют на игру.
/// </summary>
public sealed class CreditWebhook : ICreditWebhook
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly ILogManager _log = default!;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Channel<(string Url, string Json)> _queue = Channel.CreateUnbounded<(string, string)>();
    private readonly object _startLock = new();
    private bool _started;
    private ISawmill? _sawmill;

    private ISawmill Sawmill => _sawmill ??= _log.GetSawmill("credits.webhook");

    public async Task LogGrantAsync(Guid target, Guid? actor, string? actorName, long amount, CreditTransactionType type, string reason)
    {
        try
        {
            var url = _cfg.GetCVar(SoyuzCreditsCCVars.DiscordLogWebhook);
            if (!IsValidUrl(url))
                return;

            var targetName = await NameOfAsync(target);
            var who = actor == null ? "система/консоль" : $"{actorName ?? "?"} (`{actor}`)";
            var text = $"**Выдача кредитов** | Кому: {targetName} (`{target}`) | Сумма: **{amount:+#;-#;0}** | " +
                       $"Тип: {type} | Кто: {who} | Причина: {Trim(reason, 300)}";

            // parse: [] — Discord не будет пинговать ни пользователей, ни роли, ни @everyone.
            var json = JsonSerializer.Serialize(new
            {
                content = Trim(text, 1900),
                allowed_mentions = new { parse = Array.Empty<string>() },
            });

            Enqueue(url, json);
        }
        catch (Exception e)
        {
            Sawmill.Error($"LogGrant: {e}");
        }
    }

    public async Task NotifyRolePurchaseAsync(Guid buyer, string productName, DateTime? expiresAtUtc)
    {
        try
        {
            var url = _cfg.GetCVar(SoyuzCreditsCCVars.DiscordRoleWebhook);
            if (!IsValidUrl(url))
                return;

            var roleIdText = _cfg.GetCVar(SoyuzCreditsCCVars.DiscordRoleId).Trim();
            var hasRole = ulong.TryParse(roleIdText, out var roleId);

            var name = await NameOfAsync(buyer);
            var until = expiresAtUtc is { } e ? $"до {e:yyyy-MM-dd HH:mm} UTC" : "навсегда";
            var ping = hasRole ? $"<@&{roleId}> " : "";
            var text = $"{ping}Игрок **{name}** (`{buyer}`) купил «{Trim(productName, 100)}» ({until}). Нужно выдать роль в Discord.";

            var json = hasRole
                ? JsonSerializer.Serialize(new { content = text, allowed_mentions = new { roles = new[] { roleId.ToString() } } })
                : JsonSerializer.Serialize(new { content = text, allowed_mentions = new { parse = Array.Empty<string>() } });

            Enqueue(url, json);
        }
        catch (Exception e)
        {
            Sawmill.Error($"NotifyRolePurchase: {e}");
        }
    }

    public async Task NotifyGrantLimitAsync(Guid actor, string? actorName, long total, long limit)
    {
        try
        {
            // Отдельный вебхук для оповещений; если он не задан, используется вебхук журнала выдач.
            var url = _cfg.GetCVar(SoyuzCreditsCCVars.DiscordLimitWebhook);
            if (!IsValidUrl(url))
                url = _cfg.GetCVar(SoyuzCreditsCCVars.DiscordLogWebhook);
            if (!IsValidUrl(url))
                return;

            var name = actorName ?? await NameOfAsync(actor);
            var text = $"**Превышен суточный лимит выдачи кредитов** | Администратор: {Trim(name, 100)} (`{actor}`) | " +
                       $"Выдано за 24 ч: **{total}** | Лимит: {limit}";

            var json = JsonSerializer.Serialize(new
            {
                content = Trim(text, 1900),
                allowed_mentions = new { parse = Array.Empty<string>() },
            });

            Enqueue(url, json);
        }
        catch (Exception e)
        {
            Sawmill.Error($"NotifyGrantLimit: {e}");
        }
    }

    private async Task<string> NameOfAsync(Guid id)
    {
        try
        {
            var data = await _locator.LookupIdAsync(new NetUserId(id));
            if (data != null)
                return data.Username;
        }
        catch
        {
            // имя не нашли — покажем только Id
        }

        return id.ToString();
    }

    private static bool IsValidUrl(string url)
    {
        return url.StartsWith("https://discord.com/api/webhooks/", StringComparison.Ordinal)
               || url.StartsWith("https://discordapp.com/api/webhooks/", StringComparison.Ordinal);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private void Enqueue(string url, string json)
    {
        lock (_startLock)
        {
            if (!_started)
            {
                _started = true;
                _ = Task.Run(SendLoopAsync);
            }
        }

        _queue.Writer.TryWrite((url, json));
    }

    private async Task SendLoopAsync()
    {
        await foreach (var (url, json) in _queue.Reader.ReadAllAsync())
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var response = await Http.PostAsync(url, content);

                    if ((int) response.StatusCode == 429)
                    {
                        var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                        await Task.Delay(wait + TimeSpan.FromMilliseconds(250));
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                        Sawmill.Warning($"Webhook вернул {(int) response.StatusCode}");

                    break;
                }
                catch (Exception e)
                {
                    Sawmill.Error($"Ошибка отправки webhook: {e.Message}");
                    break;
                }
            }

            await Task.Delay(300); // не упираемся в лимиты Discord
        }
    }
}