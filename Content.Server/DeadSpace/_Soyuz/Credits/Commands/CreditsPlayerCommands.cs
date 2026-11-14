// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Content.Shared.Administration;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using Robust.Shared.Console;
using Robust.Shared.IoC;
using Content.Server.DeadSpace._Soyuz.Credits;

namespace Content.Server.DeadSpace._Soyuz.Credits.Commands;

[AnyCommand]
public sealed class CreditsCommand : IConsoleCommand
{
    [Dependency] private readonly Content.Server.EUI.EuiManager _eui = default!;
    [Dependency] private readonly ICreditRateLimiter _limiter = default!;

    public string Command => "sovcredits";
    public string Description => "Открыть окно Советских кредитов (баланс, магазин, история, рефералы).";
    public string Help => "sovcredits";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } session)
        {
            shell.WriteError("Команда доступна только игроку.");
            return;
        }

        // Открытие окна читает базу: частое повторение ограничивается так же, как действия в самом окне.
        if (!_limiter.TryAcquire(session.UserId.UserId, CreditRateKind.Browse))
        {
            shell.WriteError("Слишком часто. Подождите немного.");
            return;
        }

        _eui.OpenEui(new Eui.CreditsEui(), session);
    }
}

[AnyCommand]
public sealed class CreditsReferralCommand : IConsoleCommand
{
    [Dependency] private readonly IReferralService _referrals = default!;
    [Dependency] private readonly ICreditRateLimiter _limiter = default!;

    public string Command => "credits_referral";
    public string Description => "Реферальная система Советских кредитов.";
    public string Help => "credits_referral code | credits_referral apply <code>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        try
        {
            if (shell.Player is not { } session)
            {
                shell.WriteError("Команда доступна только игроку.");
                return;
            }

            var user = session.UserId.UserId;

            if (args.Length == 1 && args[0] == "code")
            {
                shell.WriteLine($"Ваш реферальный код: {await _referrals.GetCodeAsync(user)}");
                return;
            }

            if (args.Length == 2 && args[0] == "apply")
            {
                if (!_limiter.TryAcquire(user, CreditRateKind.Code))
                {
                    shell.WriteError("Слишком много попыток. Подождите немного.");
                    return;
                }

                var result = await _referrals.ApplyAsync(user, args[1]);
                shell.WriteLine(result switch
                {
                    ReferralApplyResult.Ok => "Код принят. Награда придёт после нужного времени игры на сервере.",
                    ReferralApplyResult.UnknownCode => "Такого кода нет.",
                    ReferralApplyResult.SelfReferral => "Нельзя использовать собственный код.",
                    ReferralApplyResult.SameDevice => "Этот код нельзя использовать: у вас и у пригласившего совпадает устройство или IP.",
                    _ => "Вы уже активировали реферальный код.",
                });
                return;
            }

            shell.WriteError($"Использование: {Help}");
        }
        catch (Exception e)
        {
            shell.WriteError($"Ошибка: {e.Message}");
        }
    }
}

[AnyCommand]
public sealed class CreditsPromoCommand : IConsoleCommand
{
    [Dependency] private readonly IPromoService _promos = default!;
    [Dependency] private readonly ICreditRateLimiter _limiter = default!;

    public string Command => "credits_promo";
    public string Description => "Активировать промокод Советских кредитов.";
    public string Help => "credits_promo <code>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        try
        {
            if (shell.Player is not { } session)
            {
                shell.WriteError("Команда доступна только игроку.");
                return;
            }

            if (args.Length != 1)
            {
                shell.WriteError($"Использование: {Help}");
                return;
            }

            var user = session.UserId.UserId;
            if (!_limiter.TryAcquire(user, CreditRateKind.Code))
            {
                shell.WriteError("Слишком много попыток. Подождите немного.");
                return;
            }

            var outcome = await _promos.RedeemAsync(user, args[0]);
            shell.WriteLine(outcome.Result switch
            {
                PromoRedeemResult.Ok => "Промокод активирован.",
                PromoRedeemResult.Disabled => "Этот промокод отключён.",
                PromoRedeemResult.Expired => "Срок действия промокода истёк.",
                PromoRedeemResult.Exhausted => "Промокод уже использован максимальное число раз.",
                PromoRedeemResult.AlreadyUsed => "Вы уже использовали этот промокод.",
                PromoRedeemResult.AlreadyOwned => "Этот товар у вас уже куплен навсегда.",
                PromoRedeemResult.CategoryLimit => "Нельзя активировать: достигнут лимит товаров этой категории.",
                PromoRedeemResult.BlockedByPermanent => "Нельзя активировать: у вас есть постоянный товар этой же группы.",
                PromoRedeemResult.InvalidProduct => "Товар этого промокода недоступен.",
                _ => "Такого промокода нет.",
            });
        }
        catch (Exception e)
        {
            shell.WriteError($"Ошибка: {e.Message}");
        }
    }
}

[AnyCommand]
public sealed class CreditsDiscordCommand : IConsoleCommand
{
    [Dependency] private readonly ICreditDiscordLinks _links = default!;
    [Dependency] private readonly ICreditRateLimiter _limiter = default!;

    public string Command => "credits_discord";
    public string Description => "Привязка Discord-аккаунта к игроку (для бота: баланс, топ, промокоды).";
    public string Help => "credits_discord | credits_discord unlink";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        try
        {
            if (shell.Player is not { } session)
            {
                shell.WriteError("Команда доступна только игроку.");
                return;
            }

            var user = session.UserId.UserId;
            if (!_limiter.TryAcquire(user, CreditRateKind.Code))
            {
                shell.WriteError("Слишком часто. Подождите немного.");
                return;
            }

            if (args.Length == 1 && args[0] == "unlink")
            {
                shell.WriteLine(await _links.UnlinkAsync(user)
                    ? "Discord-аккаунт отвязан."
                    : "Discord-аккаунт не был привязан.");
                return;
            }

            if (args.Length != 0)
            {
                shell.WriteError($"Использование: {Help}");
                return;
            }

            if (await _links.GetDiscordIdAsync(user) is { } discordId)
            {
                shell.WriteLine($"Ваш аккаунт уже привязан к Discord (ID {discordId}). Отвязать: credits_discord unlink");
                return;
            }

            var code = _links.CreateCode(user);
            shell.WriteLine($"Код привязки: {code}. Он действует 10 минут. В Discord введите команду бота: /link {code}");
        }
        catch (Exception e)
        {
            shell.WriteError($"Ошибка: {e.Message}");
        }
    }
}