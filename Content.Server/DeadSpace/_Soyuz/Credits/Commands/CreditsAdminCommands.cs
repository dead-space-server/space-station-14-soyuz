// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.IoC;

namespace Content.Server.DeadSpace._Soyuz.Credits.Commands;

[AdminCommand(AdminFlags.Server)]
public sealed class CreditsGiveCommand : IConsoleCommand
{
    [Dependency] private readonly ICreditService _credits = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;

    public string Command => "credits_give";
    public string Description => "Выдать Советские кредиты игроку (только флаг Server).";
    public string Help => "credits_give <userId|username> <amount> <reason>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        try
        {
            if (args.Length < 3 || !long.TryParse(args[1], out var amount))
            {
                shell.WriteError($"Использование: {Help}");
                return;
            }

            var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
            if (located == null)
            {
                shell.WriteError("Игрок не найден.");
                return;
            }

            var reason = string.Join(' ', args.Skip(2));
            var result = await _credits.GiveAsync(located.UserId.UserId, amount, reason, shell.Player);

            switch (result)
            {
                case CreditResult.Ok:
                    shell.WriteLine($"Выдано {amount} кр. игроку {located.Username}.");
                    break;
                case CreditResult.InvalidAmount:
                    shell.WriteError($"Сумма должна быть больше 0 и не больше {CreditService.MaxGrant}. Операция отклонена.");
                    break;
                case CreditResult.InvalidReason:
                    shell.WriteError($"Причина обязательна (до {CreditService.MaxReasonLength} символов).");
                    break;
                case CreditResult.Forbidden:
                    shell.WriteError("Недостаточно прав.");
                    break;
                default:
                    shell.WriteError("Не удалось выполнить операцию.");
                    break;
            }
        }
        catch (Exception e)
        {
            shell.WriteError($"Ошибка: {e.Message}");
        }
    }
}

[AdminCommand(AdminFlags.Server)]
public sealed class CreditsBalanceCommand : IConsoleCommand
{
    [Dependency] private readonly ICreditService _credits = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;

    public string Command => "credits_balance";
    public string Description => "Показать баланс Советских кредитов игрока.";
    public string Help => "credits_balance <userId|username>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                shell.WriteError($"Использование: {Help}");
                return;
            }

            var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
            if (located == null)
            {
                shell.WriteError("Игрок не найден.");
                return;
            }

            shell.WriteLine($"{located.Username}: {await _credits.GetBalanceAsync(located.UserId.UserId)} кр.");
        }
        catch (Exception e)
        {
            shell.WriteError($"Ошибка: {e.Message}");
        }
    }
}

[AdminCommand(AdminFlags.Server)]
public sealed class CreditsLogCommand : IConsoleCommand
{
    [Dependency] private readonly ICreditService _credits = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;

    private const int PageSize = 15;

    public string Command => "credits_log";
    public string Description => "Журнал операций Советских кредитов игрока.";
    public string Help => "credits_log <userId|username> [page]";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        try
        {
            if (args.Length is < 1 or > 2)
            {
                shell.WriteError($"Использование: {Help}");
                return;
            }

            var page = 0;
            if (args.Length == 2 && (!int.TryParse(args[1], out page) || page < 1))
            {
                shell.WriteError("Номер страницы — число от 1.");
                return;
            }

            if (args.Length == 2)
                page -= 1;

            var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
            if (located == null)
            {
                shell.WriteError("Игрок не найден.");
                return;
            }

            var (entries, hasMore) = await _credits.GetLogAsync(located.UserId.UserId, page, PageSize);
            if (entries.Count == 0)
            {
                shell.WriteLine("Записей нет.");
                return;
            }

            foreach (var e in entries)
            {
                var actor = e.Actor?.ToString() ?? "система/консоль";
                shell.WriteLine($"#{e.Id} {e.CreatedAtUtc:yyyy-MM-dd HH:mm} UTC | {e.Amount:+#;-#;0} | {e.Type} | {e.Reason} | кто: {actor}");
            }

            if (hasMore)
                shell.WriteLine($"Далее: {Command} {args[0]} {page + 2}");
        }
        catch (Exception ex)
        {
            shell.WriteError($"Ошибка: {ex.Message}");
        }
    }
}

[AdminCommand(AdminFlags.Server)]
public sealed class CreditsAdminCommand : IConsoleCommand
{
    [Dependency] private readonly Content.Server.EUI.EuiManager _eui = default!;

    public string Command => "credits_admin";
    public string Description => "Открыть окно управления Советскими кредитами.";
    public string Help => "credits_admin";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } session)
        {
            shell.WriteError("Команда доступна только игроку.");
            return;
        }

        _eui.OpenEui(new Eui.CreditsAdminEui(), session);
    }
}