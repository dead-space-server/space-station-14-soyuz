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

namespace Content.Server.DeadSpace._Soyuz.Credits.Commands;

[AnyCommand]
public sealed class CreditsCommand : IConsoleCommand
{
    [Dependency] private readonly Content.Server.EUI.EuiManager _eui = default!;

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

        _eui.OpenEui(new Eui.CreditsEui(), session);
    }
}

[AnyCommand]
public sealed class CreditsReferralCommand : IConsoleCommand
{
    [Dependency] private readonly IReferralService _referrals = default!;

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
                var result = await _referrals.ApplyAsync(user, args[1]);
                shell.WriteLine(result switch
                {
                    ReferralApplyResult.Ok => "Код принят. Награда придёт после нужного времени игры на сервере.",
                    ReferralApplyResult.UnknownCode => "Такого кода нет.",
                    ReferralApplyResult.SelfReferral => "Нельзя использовать собственный код.",
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