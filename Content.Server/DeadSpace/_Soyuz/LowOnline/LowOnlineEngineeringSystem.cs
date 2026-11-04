// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared.DeadSpace._Soyuz.LowOnline;
using Content.Shared.GameTicking;
using Content.Shared.Power.Components;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.LowOnline;

/// <summary>
/// Если в инженерном отделе нет живых игроков, АэС «Марта» берёт на себя питание
/// станции и бесконечно заряжает все СМЭСы. Когда появляется инженер, через заданное время заряд отключается.
/// </summary>
public sealed class LowOnlineEngineeringSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly LowOnlineDepartmentSystem _departments = default!;
    [Dependency] private readonly StationSystem _station = default!;

    private const string EngineeringDepartment = "Engineering";

    /// <summary>
    /// Как часто пересчитываем инженеров. Заряд СМЭСов восстанавливается каждый тик
    /// </summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    private bool _powerActive;

    /// <summary>
    /// С какого момента (длительность раунда) в инженерном отделе никого нет
    /// </summary>
    private TimeSpan? _absentSince;

    /// <summary>
    /// Когда (длительность раунда) бесконечный заряд отключится, если инженер останется
    /// </summary>
    private TimeSpan? _disableAt;

    private TimeSpan _nextCheck;

    /// <summary>
    /// СМЭСы станции. Список обновляется при проверках, заряд восстанавливается каждый тик
    /// </summary>
    private readonly List<EntityUid> _smes = new();

    /// <summary>
    /// Результат определения СМЭСа по ID прототипа, чтобы не перебирать родителей прототипа каждый раз
    /// </summary>
    private readonly Dictionary<string, bool> _smesPrototypes = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Reset());
    }

    private void Reset()
    {
        _powerActive = false;
        _absentSince = null;
        _disableAt = null;
        _nextCheck = TimeSpan.Zero;
        _smes.Clear();
    }

    public override void Update(float frameTime)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        if (_powerActive)
            RefillSmes();

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;

        if (_powerActive)
            RefreshSmesList();

        var now = _ticker.RoundDuration();
        var engineers = _departments.CountAlive(EngineeringDepartment);

        if (!_powerActive)
        {
            if (engineers > 0)
            {
                _absentSince = null;
                return;
            }

            _absentSince ??= now;

            var activateDelay = TimeSpan.FromSeconds(_cfg.GetCVar(SoyuzLowOnlineCVars.EngineeringActivateDelay));
            if (now - _absentSince.Value < activateDelay)
                return;

            _powerActive = true;
            _absentSince = null;
            _disableAt = null;
            RefreshSmesList();
            Announce("low-online-engineering-on");
            return;
        }

        if (engineers == 0)
        {
            _disableAt = null;
            return;
        }

        var disableDelay = TimeSpan.FromSeconds(_cfg.GetCVar(SoyuzLowOnlineCVars.EngineeringDisableDelay));
        _disableAt ??= now + disableDelay;

        if (now < _disableAt.Value)
            return;

        _powerActive = false;
        _disableAt = null;
        _absentSince = null;
        _smes.Clear();
        Announce("low-online-engineering-off");
    }

    /// <summary>
    /// Все СМЭСы на станции получают полный заряд
    /// </summary>
    private void RefillSmes()
    {
        foreach (var uid in _smes)
        {
            if (!TryComp<BatteryComponent>(uid, out var battery))
                continue;

            _battery.SetCharge(uid, battery.MaxCharge);
        }
    }

    /// <summary>
    /// Собирает СМЭСы, стоящие на станции
    /// </summary>
    private void RefreshSmesList()
    {
        _smes.Clear();

        var query = EntityQueryEnumerator<BatteryComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var meta))
        {
            if (meta.EntityPrototype is not { } proto || !IsSmesPrototype(proto))
                continue;

            if (_station.GetOwningStation(uid) == null)
                continue;

            _smes.Add(uid);
        }
    }

    /// <summary>
    /// СМЭС определяется по ID прототипа или его родителей
    /// </summary>
    private bool IsSmesPrototype(EntityPrototype proto)
    {
        if (_smesPrototypes.TryGetValue(proto.ID, out var cached))
            return cached;

        var result = false;
        foreach (var parent in _proto.EnumerateParents<EntityPrototype>(proto.ID, true))
        {
            if (!parent.ID.Contains("SMES", StringComparison.OrdinalIgnoreCase))
                continue;

            result = true;
            break;
        }

        _smesPrototypes[proto.ID] = result;
        return result;
    }

    private void Announce(string locKey)
    {
        var color = _proto.TryIndex<DepartmentPrototype>(EngineeringDepartment, out var dept)
            ? dept.Color
            : Color.Orange;

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString(locKey),
            Loc.GetString("low-online-sender"),
            playSound: true,
            colorOverride: color);
    }
}
