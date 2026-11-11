// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.LowOnline;

/// <summary>
/// Считает живых игроков по отделам. Используется механиками для малого онлайна
/// </summary>
public sealed class LowOnlineDepartmentSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;

    /// <summary>
    /// Результат подсчёта живёт секунду, чтобы частые запросы (например, инъекции) не пересчитывали игроков заново
    /// </summary>
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, (TimeSpan Time, int Count)> _cache = new();

    /// <summary>
    /// Число живых подключённых игроков с ролью из отдела.
    /// Катоники, призраки и находящиеся в криосне не учитываются
    /// </summary>
    public int CountAlive(string department)
    {
        var now = _timing.CurTime;
        if (_cache.TryGetValue(department, out var cached) && now - cached.Time < CacheTime)
            return cached.Count;

        var count = 0;

        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out _, out var mobState))
        {
            if (!_mobState.IsAlive(uid, mobState))
                continue;

            if (!_mind.TryGetMind(uid, out var mindId, out _))
                continue;

            if (!_jobs.MindTryGetJobId(mindId, out var jobId) || jobId == null)
                continue;

            if (!_jobs.TryGetAllDepartments(jobId.Value, out var departments))
                continue;

            foreach (var dept in departments)
            {
                if (dept.ID != department)
                    continue;

                count++;
                break;
            }
        }

        _cache[department] = (now, count);
        return count;
    }
}
