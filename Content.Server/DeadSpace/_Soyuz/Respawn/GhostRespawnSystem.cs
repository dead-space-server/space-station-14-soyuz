// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using Content.Server.GameTicking;
using Content.Shared.Actions;
using Content.Shared.DeadSpace._Soyuz.Respawn;
using Content.Shared.Ghost;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.Respawn;

/// <summary>
/// Респавн призрака через заданное время с возвратом в лобби В режимах, где активен геймрул с <see cref="RespawnDisabledComponent"/>, респавн отключён
/// </summary>
public sealed class GhostRespawnSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly GameTicker _ticker = default!;

    private static readonly EntProtoId ActionPrototype = "ActionGhostRespawn";

    private readonly List<EntityUid> _newGhosts = new();
    private TimeSpan _nextScan;

    public override void Initialize()
    {
        SubscribeLocalEvent<GhostRespawnComponent, GhostRespawnActionEvent>(OnRespawnAction);
        SubscribeNetworkEvent<RespawnConfirmedEvent>(OnRespawnConfirmed);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextScan)
            return;

        _nextScan = _timing.CurTime + TimeSpan.FromSeconds(1);

        _newGhosts.Clear();
        var query = EntityQueryEnumerator<GhostComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!HasComp<GhostRespawnComponent>(uid))
                _newGhosts.Add(uid);
        }

        foreach (var uid in _newGhosts)
        {
            InitGhost(uid);
        }
    }

    /// <summary>
    /// Игрок стал призраком (смерть, выход из тела, наблюдатель)
    /// </summary>
    private void InitGhost(EntityUid uid)
    {
        var comp = EnsureComp<GhostRespawnComponent>(uid);
        var now = _timing.CurTime;
        comp.RespawnAt = now + TimeSpan.FromSeconds(_cfg.GetCVar(SoyuzRespawnCVars.RespawnDelaySeconds));

        if (IsRespawnDisabled())
            return;

        _actions.AddAction(uid, ref comp.Action, ActionPrototype);

        if (comp.Action is { } action)
            _actions.SetCooldown(action, now, comp.RespawnAt);
    }

    /// <summary>
    /// Проверяем условия и просим клиент показать подтверждение
    /// </summary>
    private void OnRespawnAction(EntityUid uid, GhostRespawnComponent comp, GhostRespawnActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;

        if (IsRespawnDisabled())
        {
            _popup.PopupEntity(Loc.GetString("respawn-disabled"), user, user);
            return;
        }

        var left = comp.RespawnAt - _timing.CurTime;
        if (left > TimeSpan.Zero)
        {
            _popup.PopupEntity(Loc.GetString("respawn-wait", ("time", FormatTime(left))), user, user);
            return;
        }

        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        RaiseNetworkEvent(new RespawnConfirmRequestEvent(), actor.PlayerSession);
        args.Handled = true;
    }

    /// <summary>
    /// Игрок подтвердил респавн.
    /// </summary>
    private void OnRespawnConfirmed(RespawnConfirmedEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (session.AttachedEntity is not { } entity
            || !TryComp<GhostRespawnComponent>(entity, out var comp))
            return;

        if (IsRespawnDisabled() || _timing.CurTime < comp.RespawnAt)
            return;

        _ticker.Respawn(session);
    }

    private bool IsRespawnDisabled()
    {
        foreach (var rule in _ticker.GetActiveGameRules())
        {
            if (HasComp<RespawnDisabledComponent>(rule))
                return true;
        }

        return false;
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{(int) time.TotalMinutes:00}:{time.Seconds:00}";
    }
}