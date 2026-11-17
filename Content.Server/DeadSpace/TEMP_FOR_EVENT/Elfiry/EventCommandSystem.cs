// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.DeadSpace._Soyuz.MilitiaOperations;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage.Systems;
using Content.Shared.DeadSpace.TEMP_FOR_EVENT.Elfiry;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Pinpointer;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.TEMP_FOR_EVENT.Elfiry;

public sealed partial class EventCommandSystem : EntitySystem
{
    [Dependency] private readonly MilitiaOperationSystem _combat = default!;
    [Dependency] private readonly NPCSystem _npcs = default!;
    [Dependency] private readonly HTNSystem _htn = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly IMapManager _maps = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private TimeSpan _nextUpdate;
    private TimeSpan _nextUi;
    private readonly Dictionary<string, Dictionary<int, string>> _squads = new();
    private int _nextSquadId;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(NPCSystem));
        UpdatesBefore.Add(typeof(MilitiaOperationSystem));
        SubscribeLocalEvent<EventSquadUnitComponent, MapInitEvent>(OnUnitInit, after: [typeof(MilitiaOperationSystem)]);
        SubscribeLocalEvent<EventSquadUnitComponent, ComponentShutdown>(OnUnitShutdown);
        SubscribeLocalEvent<EventSquadUnitComponent, MobStateChangedEvent>(OnUnitMobStateChanged);
        SubscribeLocalEvent<EventSquadUnitComponent, DamageChangedEvent>(OnUnitDamaged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _nextUpdate = TimeSpan.Zero;
            _nextUi = TimeSpan.Zero;
            _nextFollow = TimeSpan.Zero;
            ClearBoarding();
            _mapViews.Clear();
            _squads.Clear();
            _nextSquadId = 0;
        });
        Subs.BuiEvents<EventCommandTabletComponent>(EventCommandUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<EventSquadRequest>(OnOrder);
            subs.Event<EventSquadAssignRequest>(OnAssign);
            subs.Event<EventSquadManageRequest>(OnManageSquad);
        });
    }

    private void OnUnitInit(Entity<EventSquadUnitComponent> unit, ref MapInitEvent args)
    {
        unit.Comp.Squad = 0;
        unit.Comp.Destination = Transform(unit).Coordinates;
        unit.Comp.DefensiveGrid = Transform(unit).GridUid;
        unit.Comp.Status = Loc.GetString("elfiry-command-unit-ready");
    }

    private void OnUnitShutdown(Entity<EventSquadUnitComponent> unit, ref ComponentShutdown args)
    {
        _combat.EndEventCommandPath(unit);
    }

    private void OnUnitDamaged(Entity<EventSquadUnitComponent> unit, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || !args.InterruptsDoAfters)
            return;
        unit.Comp.DamageSource = args.Origin;
        if (args.Origin is { } source && Exists(source) && !TerminatingOrDeleted(source) && source != unit.Owner &&
            Transform(source).MapID == Transform(unit).MapID)
        {
            unit.Comp.DamagePosition = _transform.GetMapCoordinates(source);
            unit.Comp.DamageUntil = _timing.CurTime + TimeSpan.FromSeconds(12);
        }
    }

    private bool CommanderAvailable(EntityUid uid, string team)
        => Exists(uid) && !TerminatingOrDeleted(uid) && HasComp<ActorComponent>(uid) && !_mobs.IsIncapacitated(uid) &&
           TryComp<EventCommanderComponent>(uid, out var commander) && commander.Team == team;

    private bool CanUse(Entity<EventCommandTabletComponent> tablet, EntityUid user)
        => !TerminatingOrDeleted(tablet) && CommanderAvailable(user, tablet.Comp.Team) &&
           _transform.ContainsEntity(user, (tablet.Owner, Transform(tablet))) && _blocker.CanInteract(user, tablet);

    private void OnOpened(Entity<EventCommandTabletComponent> tablet, ref BoundUIOpenedEvent args)
    {
        if (!CanUse(tablet, args.Actor))
        {
            _ui.CloseUi(tablet.Owner, EventCommandUiKey.Key, args.Actor);
            return;
        }
        if (tablet.Comp.User is { } previous && previous != args.Actor)
            _ui.CloseUi(tablet.Owner, EventCommandUiKey.Key, previous);
        tablet.Comp.User = args.Actor;
        tablet.Comp.Token = Guid.NewGuid();
        UpdateUi(tablet, args.Actor);
    }

    private bool AcceptRequest(Entity<EventCommandTabletComponent> tablet, EntityUid user, Guid token)
    {
        if (!CanUse(tablet, user) || tablet.Comp.User != user || token == Guid.Empty || token != tablet.Comp.Token ||
            _timing.CurTime < tablet.Comp.NextRequest)
            return false;
        tablet.Comp.NextRequest = _timing.CurTime + TimeSpan.FromSeconds(0.35);
        tablet.Comp.Token = Guid.NewGuid();
        return true;
    }

    private List<Entity<EventSquadUnitComponent>> Units(EntityUid commander, string team)
    {
        var result = new List<Entity<EventSquadUnitComponent>>();
        var map = Transform(commander).MapID;
        var query = EntityQueryEnumerator<EventSquadUnitComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var unit, out var transform))
        {
            if (unit.Team == team && transform.MapID == map && !TerminatingOrDeleted(uid) && HasComp<MilitiaOperationActorComponent>(uid) && !_combat.EventUnitHasMind(uid) &&
                _factions.IsEntityFriendly(commander, uid))
                result.Add((uid, unit));
        }
        return result.OrderBy(unit => unit.Owner.GetHashCode()).ToList();
    }

    private void OnAssign(Entity<EventCommandTabletComponent> tablet, ref EventSquadAssignRequest args)
    {
        if (!AcceptRequest(tablet, args.Actor, args.Token))
            return;
        if (args.Squad < 0 || args.Squad != 0 && !Squads(tablet.Comp.Team).ContainsKey(args.Squad) ||
            !TryGetEntity(args.Unit, out var uid) ||
            !Units(args.Actor, tablet.Comp.Team).Any(unit => unit.Owner == uid.Value))
        {
            Reply(tablet, args.Actor, "elfiry-command-invalid-unit");
            return;
        }
        var unit = Comp<EventSquadUnitComponent>(uid.Value);
        if (!_combat.EventUnitAvailable(uid.Value))
        {
            Reply(tablet, args.Actor, "elfiry-command-invalid-unit");
            return;
        }
        if (args.Squad != 0 && unit.Squad != args.Squad && SquadMembers(tablet.Comp.Team, args.Squad) >= EventSquadLimits.Members)
        {
            Reply(tablet, args.Actor, "elfiry-command-squad-full");
            return;
        }
        var before = EventOrderState.Capture(unit);
        var after = before with
        {
            Squad = args.Squad, Commander = args.Actor, Destination = Transform(uid.Value).Coordinates,
            Ordered = false, Arrived = true, Status = Loc.GetString("elfiry-command-unit-ready"),
        };
        var user = args.Actor;
        var targetSquad = args.Squad;
        var applied = TryApplyOrders([new EventOrderChange((uid.Value, unit), before, after)], () => CanUse(tablet, user) && (targetSquad == 0 || Squads(tablet.Comp.Team).ContainsKey(targetSquad) &&
            (unit.Squad == targetSquad || SquadMembers(tablet.Comp.Team, targetSquad) < EventSquadLimits.Members)));
        Reply(tablet, user, applied ? "elfiry-command-assigned" : "elfiry-command-order-failed");
    }

    private void OnOrder(Entity<EventCommandTabletComponent> tablet, ref EventSquadRequest args)
    {
        if (!AcceptRequest(tablet, args.Actor, args.Token))
            return;
        var origin = _transform.GetMapCoordinates(args.Actor);
        var limit = Math.Clamp(tablet.Comp.CommandRange, 8f, 512f);
        if (args.Squad < -1 || args.Squad == 0 || args.Squad > 0 && !Squads(tablet.Comp.Team).ContainsKey(args.Squad) || !Enum.IsDefined(args.Order) || args.MapId != (int) origin.MapId ||
            !float.IsFinite(args.Position.X) || !float.IsFinite(args.Position.Y) ||
            args.Order != EventSquadOrder.Follow && Vector2.DistanceSquared(origin.Position, args.Position) > limit * limit)
        {
            Reply(tablet, args.Actor, "elfiry-command-invalid-position");
            return;
        }
        var squad = args.Squad;
        var selected = Units(args.Actor, tablet.Comp.Team).Where(unit => (squad == -1 ? unit.Comp.Squad > 0 : unit.Comp.Squad == squad) &&
            _combat.EventUnitAvailable(unit)).ToArray();
        if (selected.GroupBy(unit => unit.Comp.Squad).Any(group => group.Count() > EventSquadLimits.Members))
        {
            Reply(tablet, args.Actor, "elfiry-command-squad-full");
            return;
        }
        if (selected.Length == 0)
        {
            Reply(tablet, args.Actor, "elfiry-command-no-units");
            return;
        }
        var target = args.Order == EventSquadOrder.Follow ? origin : new MapCoordinates(args.Position, origin.MapId);
        var positions = new List<EntityCoordinates>();
        for (var i = 0; i < selected.Length; i++)
        {
            if (FormationPosition(target, i, positions) is not { } position)
            {
                Reply(tablet, args.Actor, "elfiry-command-no-space");
                return;
            }
            positions.Add(position);
        }
        var changes = new List<EventOrderChange>();
        for (var i = 0; i < selected.Length; i++)
        {
            var unit = selected[i];
            var before = EventOrderState.Capture(unit.Comp);
            changes.Add(new EventOrderChange(unit, before, before with
            {
                Commander = args.Actor, Order = args.Order, Destination = positions[i], FormationSlot = i,
                Ordered = true, Arrived = false, Status = Loc.GetString("elfiry-command-unit-moving"),
                FollowPosition = Transform(args.Actor).Coordinates,
            }));
        }
        var user = args.Actor;
        var applied = TryApplyOrders(changes, () => CanUse(tablet, user));
        Reply(tablet, user, applied ? "elfiry-command-accepted" : "elfiry-command-order-failed");
    }

    private EntityCoordinates? FormationPosition(MapCoordinates target, int slot, List<EntityCoordinates>? occupied = null)
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var index = slot + attempt;
            var angle = index * 2.3999632f;
            var radius = 1.4f + MathF.Sqrt(index) * 0.8f;
            var point = new MapCoordinates(target.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius, target.MapId);
            var coordinates = _transform.ToCoordinates(point);
            if (_turf.GetTileRef(coordinates) is not { } tile || tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile))
                continue;
            var centered = _turf.GetTileCenter(tile);
            point = _transform.ToMapCoordinates(centered);
            if (!_combat.EventStandingSpace(point) ||
                occupied != null && occupied.Any(other => Vector2.DistanceSquared(_transform.ToMapCoordinates(other).Position, point.Position) < 0.81f))
                continue;
            return centered;
        }
        return null;
    }

    private void Replan(EntityUid uid)
    {
        if (TryComp<HTNComponent>(uid, out var htn))
        {
            _htn.CancelPlanning(htn);
            _htn.Replan(htn);
        }
    }

    private void Reply(Entity<EventCommandTabletComponent> tablet, EntityUid user, string message)
    {
        if (TerminatingOrDeleted(tablet))
            return;
        tablet.Comp.Status = Loc.GetString(message);
        if (Exists(user) && !TerminatingOrDeleted(user))
            UpdateUi(tablet, user);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        UpdateBoarding();
        UpdateFollowers();
        var units = EntityQueryEnumerator<EventSquadUnitComponent>();
        while (units.MoveNext(out var uid, out var unit))
        {
            if (!_combat.EventUnitAvailable(uid))
                continue;
            _combat.SyncEventDefense(uid);
            _npcs.KeepEventCommandActive(uid);
            _combat.UpdateEventPassage(uid);
            if (unit.Ordered && unit.Destination.IsValid(EntityManager) &&
                _transform.ToMapCoordinates(unit.Destination).MapId == Transform(uid).MapID)
            {
                unit.RemainingDistance = Vector2.Distance(_transform.GetWorldPosition(uid), _transform.ToMapCoordinates(unit.Destination).Position);
                unit.Arrived = _combat.EventDestinationReached(uid, unit.Destination, unit.Arrived ? 1.1f : 0.65f);
            }
            unit.Status = Loc.GetString(_combat.EventUnitInCombat(uid) ? "elfiry-command-unit-combat" :
                unit.Passage != null ? "elfiry-command-unit-yielding" :
                unit.WaitingFor != null ? "elfiry-command-unit-waiting" :
                _timing.CurTime < unit.RetryAfter ? "elfiry-command-unit-blocked" :
                unit.Ordered && !unit.Arrived ? "elfiry-command-unit-moving" : "elfiry-command-unit-ready");
        }
        if (_timing.CurTime < _nextUi)
            return;
        _nextUi = _timing.CurTime + TimeSpan.FromSeconds(1);
        var tablets = EntityQueryEnumerator<EventCommandTabletComponent>();
        while (tablets.MoveNext(out var uid, out var tablet))
        {
            if (tablet.User is not { } user || !_ui.IsUiOpen(uid, EventCommandUiKey.Key))
                continue;
            if (CanUse((uid, tablet), user))
                UpdateUi((uid, tablet), user);
            else
            {
                _ui.CloseUi(uid, EventCommandUiKey.Key, user);
                tablet.User = null;
            }
        }
    }

    public EventMovementTarget? MovementTarget(EntityUid uid)
    {
        if (!_combat.EventUnitAvailable(uid) || !TryComp<EventSquadUnitComponent>(uid, out var unit))
            return null;
        if (_combat.EventMedicalDestination(uid) is { } patient)
            return FormationPosition(_transform.ToMapCoordinates(patient.Position), unit.FormationSlot) is { } position
                ? new EventMovementTarget(EventMovementKind.Medicine, position, patient.Subject) : null;
        if (unit.Passage is { } passage && passage.Revision == unit.Revision && passage.Until > _timing.CurTime &&
            passage.Destination.IsValid(EntityManager))
            return new EventMovementTarget(EventMovementKind.Passage, passage.Destination, passage.Passer, passage);
        if (unit.Ordered && !unit.Arrived && unit.Destination.IsValid(EntityManager) &&
            _transform.ToMapCoordinates(unit.Destination).MapId == Transform(uid).MapID)
            return new EventMovementTarget(EventMovementKind.Command, unit.Destination);
        return _combat.EventCombatDestination(uid) is { } enemy
            ? new EventMovementTarget(EventMovementKind.Combat, enemy.Position, enemy.Subject) : null;
    }

    public bool PathCurrent(EntityUid uid, EventCommandPath path)
        => TryComp<EventSquadUnitComponent>(uid, out var unit) && unit.Revision == path.Revision &&
           MovementTarget(uid) is { } target && path.Target is { } planned &&
           (planned.Matches(target) || planned.Kind == target.Kind &&
            planned.Kind is EventMovementKind.Combat or EventMovementKind.Medicine && planned.Subject == target.Subject &&
            planned.Destination.EntityId == target.Destination.EntityId && _combat.EventCommandInteractionBusy(uid)) &&
           path.Destination.IsValid(EntityManager);

    public void PathFailed(EntityUid uid, uint revision)
    {
        if (TryComp<EventSquadUnitComponent>(uid, out var unit) && unit.Revision == revision &&
            unit.Path == null && unit.RetryAfter <= _timing.CurTime)
            unit.RetryAfter = _timing.CurTime + TimeSpan.FromSeconds(2 + unit.FormationSlot % 3 * 0.4);
    }

    private void UpdateUi(Entity<EventCommandTabletComponent> tablet, EntityUid user)
    {
        var origin = _transform.GetMapCoordinates(user);
        var units = Units(user, tablet.Comp.Team).Select(unit => new EventCommandUnitState(GetNetEntity(unit),
            Name(unit), unit.Comp.Squad, _transform.GetWorldPosition(unit), unit.Comp.Status,
            !_mobs.IsIncapacitated(unit), Comp<MilitiaOperationActorComponent>(unit).Medic)).ToArray();
        var points = new List<EventCommandPointState>();
        var waypoints = EntityQueryEnumerator<EventCommandWaypointComponent, TransformComponent>();
        while (waypoints.MoveNext(out var uid, out var waypoint, out var transform))
        {
            if (waypoint.Team == tablet.Comp.Team && transform.MapID == origin.MapId && points.Count < 32)
                points.Add(new EventCommandPointState(Name(uid), _transform.GetWorldPosition(uid)));
        }
        var beacons = EntityQueryEnumerator<NavMapBeaconComponent, TransformComponent>();
        while (beacons.MoveNext(out var uid, out var beacon, out var transform))
        {
            if (!beacon.Enabled || !transform.Anchored || transform.MapID != origin.MapId || points.Count >= 32 ||
                Vector2.DistanceSquared(origin.Position, _transform.GetWorldPosition(uid)) > 288f * 288f)
                continue;
            var name = string.IsNullOrWhiteSpace(beacon.Text) ? Name(uid) : beacon.Text;
            var position = _transform.GetWorldPosition(uid);
            if (!points.Any(point => point.Name == name && Vector2.DistanceSquared(point.Position, position) < 1f))
                points.Add(new EventCommandPointState(name, position));
        }
        _ui.SetUiState(tablet.Owner, EventCommandUiKey.Key, new EventCommandUiState(tablet.Comp.Token, true,
            (int) origin.MapId, origin.Position, units, points.ToArray(), MapView(origin, tablet.Comp.CommandRange), tablet.Comp.Status,
            SquadStates(tablet.Comp.Team)));
    }
}
