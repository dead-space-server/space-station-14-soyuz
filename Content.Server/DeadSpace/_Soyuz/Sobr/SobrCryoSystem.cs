// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Content.Shared.Popups;
using Content.Shared.DeadSpace._Soyuz.Sobr;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.Sobr;

public sealed class SobrCryoSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    private readonly List<Entity<SobrCryoCapsuleComponent>> _buffer = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SobrSquadRemoteComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<SobrSquadRemoteComponent, SobrRemoteWakeMessage>(OnWake);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<SobrCryoCapsuleComponent>();
        while (query.MoveNext(out _, out var capsule))
        {
            if (capsule.NextRefill is not { } next || curTime < next)
                continue;

            capsule.NextRefill = null;
            capsule.Used = false;
            capsule.Occupants.Clear();
        }
    }

    private void OnUiOpened(Entity<SobrSquadRemoteComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not SobrRemoteUiKey)
            return;

        UpdateUi(ent);
    }

    private void OnWake(Entity<SobrSquadRemoteComponent> ent, ref SobrRemoteWakeMessage args)
    {
        if (!ent.Comp.Squads.Contains(args.Squad) || !_proto.TryIndex(args.Squad, out var squad))
            return;

        if (_timing.CurTime < ent.Comp.NextUse)
        {
            _popup.PopupEntity(Loc.GetString("sobr-remote-cooldown"), ent.Owner, args.Actor);
            return;
        }

        ent.Comp.NextUse = _timing.CurTime + ent.Comp.Cooldown;

        GetCapsules(ent, args.Squad, _buffer);

        var woken = 0;
        foreach (var capsule in _buffer)
        {
            woken += WakeCapsule(capsule, args.Squad);
        }

        var name = Loc.GetString(squad.Name);
        var msg = woken > 0
            ? Loc.GetString("sobr-remote-woke", ("squad", name), ("count", woken))
            : Loc.GetString("sobr-remote-nothing", ("squad", name));
        _popup.PopupEntity(msg, ent.Owner, args.Actor);

        UpdateUi(ent);
    }

    private int WakeCapsule(Entity<SobrCryoCapsuleComponent> capsule, ProtoId<SobrSquadPrototype> squad)
    {
        if (!IsReady(capsule.Comp) || !capsule.Comp.Fighters.TryGetValue(squad, out var fighter))
            return 0;

        var xform = Transform(capsule);
        var offset = xform.LocalRotation.RotateVec(capsule.Comp.SpawnOffset);
        var count = Math.Max(1, capsule.Comp.Count);

        capsule.Comp.Occupants.Clear();
        for (var i = 0; i < count; i++)
        {
            var spawned = SpawnAtPosition(fighter, xform.Coordinates.Offset(offset));
            _transform.SetLocalRotation(spawned, xform.LocalRotation);
            capsule.Comp.Occupants.Add(spawned);
        }

        capsule.Comp.Used = true;

        if (capsule.Comp.RefillMinutes > 0)
            capsule.Comp.NextRefill = _timing.CurTime + TimeSpan.FromMinutes(capsule.Comp.RefillMinutes);

        _audio.PlayPvs(capsule.Comp.WakeSound, capsule.Owner);
        return count;
    }

    private bool IsReady(SobrCryoCapsuleComponent capsule)
    {
        foreach (var occupant in capsule.Occupants)
        {
            if (!TerminatingOrDeleted(occupant))
                return false;
        }

        return capsule.Reusable || !capsule.Used;
    }

    private void GetCapsules(
        Entity<SobrSquadRemoteComponent> remote,
        ProtoId<SobrSquadPrototype> squad,
        List<Entity<SobrCryoCapsuleComponent>> result)
    {
        result.Clear();

        var origin = _transform.GetMapCoordinates(remote.Owner);
        var query = EntityQueryEnumerator<SobrCryoCapsuleComponent>();
        while (query.MoveNext(out var uid, out var capsule))
        {
            if (!capsule.Fighters.ContainsKey(squad))
                continue;

            var coords = _transform.GetMapCoordinates(uid);
            if (coords.MapId != origin.MapId)
                continue;

            if (remote.Comp.MaxRange is { } range && (coords.Position - origin.Position).Length() > range)
                continue;

            result.Add((uid, capsule));
        }
    }

    private void UpdateUi(Entity<SobrSquadRemoteComponent> ent)
    {
        var squads = new List<SobrSquadInfo>();
        foreach (var id in ent.Comp.Squads)
        {
            if (!_proto.TryIndex(id, out var squad))
                continue;

            GetCapsules(ent, id, _buffer);

            var total = 0;
            var ready = 0;
            foreach (var capsule in _buffer)
            {
                var count = Math.Max(1, capsule.Comp.Count);
                total += count;
                if (IsReady(capsule.Comp))
                    ready += count;
            }

            squads.Add(new SobrSquadInfo(id, Loc.GetString(squad.Name), squad.Color, total, ready));
        }

        _ui.SetUiState(ent.Owner, SobrRemoteUiKey.Key, new SobrRemoteBuiState(squads));
    }
}