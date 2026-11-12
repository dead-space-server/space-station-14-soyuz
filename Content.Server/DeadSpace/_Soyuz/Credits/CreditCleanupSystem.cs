// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server.DeadSpace._Soyuz.Credits;

/// <summary>
/// Периодическая очистка устаревшего: истёкшие покупки, неподтверждённые рефералы и истёкшие промокоды
/// удаляются через soyuz.credits.cleanup_days дней (по умолчанию 30). Журнал операций не трогается никогда
public sealed class CreditCleanupSystem : EntitySystem
{
    [Dependency] private readonly ICreditRepository _repo = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private const float IntervalSeconds = 6 * 3600f;
    private float _accum = IntervalSeconds - 60f;

    public override void Update(float frameTime)
    {
        _accum += frameTime;
        if (_accum < IntervalSeconds)
            return;

        _accum = 0f;

        if (!_cfg.GetCVar(SoyuzCreditsCCVars.CleanupEnabled))
            return;

        var days = _cfg.GetCVar(SoyuzCreditsCCVars.CleanupDays);
        if (days <= 0)
            return;

        Cleanup(DateTime.UtcNow.AddDays(-days));
    }

    private async void Cleanup(DateTime cutoffUtc)
    {
        try
        {
            var result = await _repo.CleanupAsync(cutoffUtc);
            if (result.Owned + result.Referrals + result.Promos > 0)
            {
                Log.Info($"Очистка Советских кредитов: покупок {result.Owned}, рефералов {result.Referrals}, промокодов {result.Promos}.");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Ошибка очистки Советских кредитов: {e}");
        }
    }
}
