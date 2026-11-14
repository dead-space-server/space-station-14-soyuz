// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;

namespace Content.Server.DeadSpace._Soyuz.Credits;

/// <summary>Вид действий в окне Кредитов: у каждого свой лимит (CVars soyuz.credits.rate_*).</summary>
public enum CreditRateKind
{
    /// <summary>Покупка и надевание/снятие.</summary>
    Shop,

    /// <summary>Ввод реферального кода и промокода (защита от подбора).</summary>
    Code,

    /// <summary>Листание истории и любые запросы, которые читают базу.</summary>
    Browse,
}

/// <summary>
/// Ограничение частоты действий игрока: не больше N действий за окно в секундах (скользящее окно).
/// Хранится в памяти сервера, в БД ничего не пишется: так нельзя заспамить базу и самим лимитером.
/// </summary>
public interface ICreditRateLimiter
{
    /// <summary>true = действие разрешено и засчитано. false = лимит исчерпан, действие нужно отклонить (отказ не засчитывается).</summary>
    bool TryAcquire(Guid userId, CreditRateKind kind);

    /// <summary>Забыть игрока (вызывается при выходе с сервера).</summary>
    void Forget(Guid userId);
}

public sealed class CreditRateLimiter : ICreditRateLimiter
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private readonly object _lock = new();
    private readonly Dictionary<(Guid User, CreditRateKind Kind), Queue<long>> _hits = new();

    public bool TryAcquire(Guid userId, CreditRateKind kind)
    {
        var (max, windowSeconds) = Limits(kind);
        if (max <= 0 || windowSeconds <= 0)
            return true; // выключено

        var now = Environment.TickCount64;
        var windowMs = windowSeconds * 1000L;

        lock (_lock)
        {
            var key = (userId, kind);
            if (!_hits.TryGetValue(key, out var queue))
            {
                queue = new Queue<long>();
                _hits[key] = queue;
            }

            while (queue.Count > 0 && now - queue.Peek() >= windowMs)
                queue.Dequeue();

            if (queue.Count >= max)
                return false;

            queue.Enqueue(now);
            return true;
        }
    }

    public void Forget(Guid userId)
    {
        lock (_lock)
        {
            foreach (var kind in Enum.GetValues<CreditRateKind>())
                _hits.Remove((userId, kind));
        }
    }

    private (int Max, int WindowSeconds) Limits(CreditRateKind kind) => kind switch
    {
        CreditRateKind.Shop => (_cfg.GetCVar(SoyuzCreditsCCVars.RateShopActions), _cfg.GetCVar(SoyuzCreditsCCVars.RateShopWindow)),
        CreditRateKind.Code => (_cfg.GetCVar(SoyuzCreditsCCVars.RateCodeActions), _cfg.GetCVar(SoyuzCreditsCCVars.RateCodeWindow)),
        _ => (_cfg.GetCVar(SoyuzCreditsCCVars.RateBrowseActions), _cfg.GetCVar(SoyuzCreditsCCVars.RateBrowseWindow)),
    };
}
