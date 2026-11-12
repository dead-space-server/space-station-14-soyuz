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

[AdminCommand(AdminFlags.Server)]
public sealed class CreditsTakeCommand : IConsoleCommand
{
    [Dependency] private readonly ICreditService _credits = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;

    public string Command => "credits_take";
    public string Description => "Списать Советские кредиты у игрока с указанием причины (только флаг Server).";
    public string Help => "credits_take <userId|username> <amount> <reason>";

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
            var result = await _credits.TakeAsync(located.UserId.UserId, amount, reason, shell.Player);

            switch (result)
            {
                case CreditResult.Ok:
                    shell.WriteLine($"Списано {amount} кр. у игрока {located.Username}. В журнале добавлена запись.");
                    break;
                case CreditResult.InvalidAmount:
                    shell.WriteError($"Сумма должна быть больше 0 и не больше {CreditService.MaxGrant}. Операция отклонена.");
                    break;
                case CreditResult.InvalidReason:
                    shell.WriteError($"Причина обязательна (до {CreditService.MaxReasonLength} символов).");
                    break;
                case CreditResult.Insufficient:
                    shell.WriteError("У игрока недостаточно кредитов, баланс не может стать отрицательным. Операция отклонена.");
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
public sealed class CreditsRefundCommand : IConsoleCommand
{
    [Dependency] private readonly ICreditService _credits = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;

    public string Command => "credits_refund";
    public string Description => "Вернуть кредиты за покупку и забрать товар (только флаг Server).";
    public string Help => "credits_refund <userId|username> <productId> <reason>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        try
        {
            if (args.Length < 3)
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
            var refund = await _credits.RefundAsync(located.UserId.UserId, args[1], reason, shell.Player);

            switch (refund.Result)
            {
                case CreditResult.Ok:
                    shell.WriteLine($"Возвращено {refund.Amount} кр. игроку {located.Username}, товар {args[1]} забран. В журнале добавлена обратная запись.");
                    break;
                case CreditResult.InvalidReason:
                    shell.WriteError($"Причина обязательна (до {CreditService.MaxReasonLength} символов).");
                    break;
                case CreditResult.NotFound:
                    shell.WriteError("У игрока нет невозвращённых покупок этого товара.");
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
public sealed class CreditsPromoCreateCommand : IConsoleCommand
{
    [Dependency] private readonly IPromoService _promos = default!;

    public string Command => "credits_promo_create";
    public string Description => "Создать промокод на кредиты и/или товар (только флаг Server).";
    public string Help => "credits_promo_create <code> <credits> <productId|-> <maxUses|0> <days|0>\n" +
                          "  credits: сколько кредитов даёт код (0, если только товар)\n" +
                          "  productId: id товара или «-», если код только на кредиты\n" +
                          "  maxUses: сколько раз можно использовать (0 = без лимита)\n" +
                          "  days: сколько дней действует код (0 = бессрочно)";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        try
        {
            if (args.Length != 5
                || !long.TryParse(args[1], out var credits)
                || !int.TryParse(args[3], out var maxUses)
                || !int.TryParse(args[4], out var days))
            {
                shell.WriteError($"Использование: {Help}");
                return;
            }

            var productId = args[2] == "-" ? null : args[2];
            var result = await _promos.CreateAsync(args[0], credits, productId,
                maxUses == 0 ? null : maxUses, days == 0 ? null : days, shell.Player);

            switch (result)
            {
                case PromoResult.Ok:
                    shell.WriteLine($"Промокод {args[0].ToUpperInvariant()} создан.");
                    break;
                case PromoResult.InvalidCode:
                    shell.WriteError($"Код: латинские буквы, цифры, «-» и «_», от {CreditPromoService.MinCodeLength} до {CreditPromoService.MaxCodeLength} символов.");
                    break;
                case PromoResult.InvalidAmount:
                    shell.WriteError($"Сумма от 0 до {CreditService.MaxGrant}; если товара нет, она должна быть больше 0.");
                    break;
                case PromoResult.InvalidProduct:
                    shell.WriteError("Такого товара нет.");
                    break;
                case PromoResult.InvalidUses:
                    shell.WriteError("Число использований должно быть положительным (0 = без лимита).");
                    break;
                case PromoResult.InvalidDuration:
                    shell.WriteError("Срок в днях: от 1 до 3650 (0 = бессрочно).");
                    break;
                case PromoResult.AlreadyExists:
                    shell.WriteError("Такой промокод уже есть.");
                    break;
                case PromoResult.Forbidden:
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
public sealed class CreditsPromoListCommand : IConsoleCommand
{
    [Dependency] private readonly IPromoService _promos = default!;

    private const int Count = 20;

    public string Command => "credits_promo_list";
    public string Description => "Последние промокоды: использования, срок, состояние.";
    public string Help => "credits_promo_list";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        try
        {
            var list = await _promos.ListAsync(Count);
            if (list.Count == 0)
            {
                shell.WriteLine("Промокодов нет.");
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var p in list)
            {
                var uses = p.MaxUses is { } max ? $"{p.Uses}/{max}" : $"{p.Uses}/без лимита";
                var until = p.ExpiresAt is { } e ? $"до {e:yyyy-MM-dd HH:mm} UTC" : "бессрочно";
                var state = p.Disabled ? "ОТКЛЮЧЁН" : p.ExpiresAt is { } ex && ex <= now ? "истёк" : "активен";
                shell.WriteLine($"{p.Code} | {p.Credits} кр. | товар: {p.ProductId ?? "-"} | использовано {uses} | {until} | {state}");
            }
        }
        catch (Exception e)
        {
            shell.WriteError($"Ошибка: {e.Message}");
        }
    }
}

[AdminCommand(AdminFlags.Server)]
public sealed class CreditsPromoDisableCommand : IConsoleCommand
{
    [Dependency] private readonly IPromoService _promos = default!;

    public string Command => "credits_promo_disable";
    public string Description => "Отключить промокод. Чтобы включить обратно: credits_promo_enable.";
    public string Help => "credits_promo_disable <code>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                shell.WriteError($"Использование: {Help}");
                return;
            }

            var result = await _promos.SetDisabledAsync(args[0], true, shell.Player);
            shell.WriteLine(result switch
            {
                PromoResult.Ok => "Промокод отключён.",
                PromoResult.Forbidden => "Недостаточно прав.",
                _ => "Такого промокода нет.",
            });
        }
        catch (Exception e)
        {
            shell.WriteError($"Ошибка: {e.Message}");
        }
    }
}

[AdminCommand(AdminFlags.Server)]
public sealed class CreditsPromoEnableCommand : IConsoleCommand
{
    [Dependency] private readonly IPromoService _promos = default!;

    public string Command => "credits_promo_enable";
    public string Description => "Снова включить отключённый промокод.";
    public string Help => "credits_promo_enable <code>";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                shell.WriteError($"Использование: {Help}");
                return;
            }

            var result = await _promos.SetDisabledAsync(args[0], false, shell.Player);
            shell.WriteLine(result switch
            {
                PromoResult.Ok => "Промокод включён.",
                PromoResult.Forbidden => "Недостаточно прав.",
                _ => "Такого промокода нет.",
            });
        }
        catch (Exception e)
        {
            shell.WriteError($"Ошибка: {e.Message}");
        }
    }
}
