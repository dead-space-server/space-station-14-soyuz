// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public interface ICreditServerIdProvider
{
    string ServerId { get; }
}

/// <summary>
/// Единственное место, где читается server_id. Пустое значение = сервер не запускается
/// </summary>
public sealed class CreditServerIdProvider : ICreditServerIdProvider
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private string? _id;

    public string ServerId => _id ??= Resolve();

    private string Resolve()
    {
        var id = _cfg.GetCVar(SoyuzCreditsCCVars.ServerId).Trim();
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("soyuz.credits.server_id не задан: запуск системы Советских кредитов невозможен.");

        return id;
    }
}