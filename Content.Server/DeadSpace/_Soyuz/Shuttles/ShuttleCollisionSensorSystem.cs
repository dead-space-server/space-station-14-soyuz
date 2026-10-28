// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.DeadSpace._Soyuz.Shuttles;
using Content.Shared.DeviceLinking;
using Content.Shared.Examine;
using Content.Shared.Physics;
using Content.Shared.Power;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.Shuttles;

public sealed class ShuttleCollisionSensorSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly DeviceLinkSystem _links = default!;
    [Dependency] private readonly ShuttleControlSystem _control = default!;
    [Dependency] private readonly ShuttleConsoleSystem _consoles = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;

    private const string DangerPort = "ShuttleCollisionDanger";
    private const string ClearPort = "ShuttleCollisionClear";
    private const string StatusPort = "ShuttleCollisionStatus";
    private Dictionary<EntityUid, Reading> _readings = new();
    private Dictionary<EntityUid, List<Reading>> _grids = new();
    private readonly Dictionary<EntityUid, Output> _outputs = new();
    private readonly HashSet<EntityUid> _pending = new();
    private bool _flushing;
    private bool _scanFailed;
    private int _outputRevision;
    private readonly Dictionary<EntityUid, TimeSpan> _alarms = new();
    private readonly HashSet<Entity<DockingComponent>> _docks = new();
    private readonly HashSet<Entity<ShuttleConsoleComponent>> _alarmConsoles = new();
    private readonly HashSet<EntityUid> _refresh = new();
    private TimeSpan _nextScan;
    private TimeSpan _nextUi;

    private sealed class Reading
    {
        public EntityUid Sensor;
        public EntityUid Grid;
        public Vector2 Direction;
        public Vector2 HitPosition;
        public EntityUid? Obstacle;
        public float? Distance;
        public bool Powered;
        public ShuttleCollisionSensorState State;
    }

    private sealed class Output
    {
        public ShuttleCollisionSensorState State;
        public bool? Sent;
        public bool? Edge;
        public int Revision;
        public bool Failed;
    }

    public override void Initialize()
    {
        SubscribeLocalEvent<ShuttleCollisionSensorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ShuttleCollisionSensorComponent, PowerChangedEvent>(OnPower);
        SubscribeLocalEvent<ShuttleCollisionSensorComponent, AnchorStateChangedEvent>(OnAnchor);
        SubscribeLocalEvent<ShuttleCollisionSensorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ConstructionComponent, AfterConstructionChangeEntityEvent>(OnConstructed);
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
            subs.Event<ShuttleCollisionModeMessage>(OnMode));
    }

    private void OnMode(Entity<ShuttleConsoleComponent> ent, ref ShuttleCollisionModeMessage args)
    {
        if (!Enum.IsDefined(args.Mode) || !this.IsPowered(ent, EntityManager) ||
            !Transform(ent).Anchored || !_blocker.CanInteract(args.Actor, ent) ||
            !TryComp<PilotComponent>(args.Actor, out var pilot) || pilot.Console != ent.Owner)
            return;

        var resolve = new ConsoleShuttleEvent { Console = ent.Owner };
        RaiseLocalEvent(ent.Owner, ref resolve);
        if (resolve.Console is not { } console || !this.IsPowered(console, EntityManager) ||
            !Transform(console).Anchored || Transform(console).GridUid is not { } grid ||
            !HasComp<ShuttleComponent>(grid) || !_control.CanControl(grid, ShuttleControlType.Pilot, args.Actor))
            return;

        var existed = TryComp<ShuttleCollisionControllerComponent>(grid, out var controller);
        controller ??= EnsureComp<ShuttleCollisionControllerComponent>(grid);
        var oldMode = controller.Mode;
        var oldPreferred = controller.PreferredMode;
        if (args.Mode != ShuttleCollisionMode.Off)
            controller.PreferredMode = args.Mode;
        controller.Mode = args.Enabled ? args.Mode : ShuttleCollisionMode.Off;
        try
        {
            if (controller.Mode == ShuttleCollisionMode.Off)
                DisableGrid(grid);
            else
                Scan();
        }
        catch (Exception error)
        {
            controller.Mode = oldMode;
            controller.PreferredMode = oldPreferred;
            if (!existed)
                RemComp<ShuttleCollisionControllerComponent>(grid);
            Log.Error($"Collision sensor scan failed while changing mode: {error}");
        }
        _nextScan = TimeSpan.Zero;
        FlushOutputs();
        RefreshSafetyUi(grid);
    }

    private void DisableGrid(EntityUid grid)
    {
        var query = EntityQueryEnumerator<ShuttleCollisionSensorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != grid)
                continue;
            if (_readings.TryGetValue(uid, out var reading))
            {
                reading.Distance = null;
                reading.Obstacle = null;
                reading.State = ShuttleCollisionSensorState.Off;
            }
            QueueOutput(uid, ShuttleCollisionSensorState.Off);
        }
    }

    private void OnShutdown(Entity<ShuttleCollisionSensorComponent> ent, ref ComponentShutdown args)
    {
        Reset(ent);
        _readings.Remove(ent);
    }

    private void OnPower(Entity<ShuttleCollisionSensorComponent> ent, ref PowerChangedEvent args) => Reset(ent);
    private void OnAnchor(Entity<ShuttleCollisionSensorComponent> ent, ref AnchorStateChangedEvent args) => Reset(ent);

    private void Reset(EntityUid uid)
    {
        if (_readings.TryGetValue(uid, out var reading))
        {
            reading.Powered = false;
            reading.Distance = null;
            reading.Obstacle = null;
            reading.State = ShuttleCollisionSensorState.Off;
        }
        QueueOutput(uid, ShuttleCollisionSensorState.Off);
        _nextScan = TimeSpan.Zero;
        FlushOutputs();
    }

    private void OnConstructed(Entity<ConstructionComponent> ent, ref AfterConstructionChangeEntityEvent args)
    {
        if (args.Graph != "ShuttleCollisionSensor" || args.PreviousNode != "sensor" || args.CurrentNode != "assembly")
            return;
        if (_containers.TryGetContainer(ent, "sensorParts", out var parts))
            _containers.EmptyContainer(parts, true);
    }

    private void OnExamined(Entity<ShuttleCollisionSensorComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("shuttle-collision-examine", ("range", ent.Comp.Range)));
        if (_readings.TryGetValue(ent, out var reading) && reading.Distance is { } distance)
            args.PushMarkup(Loc.GetString("shuttle-collision-obstacle", ("distance", distance.ToString("0.#", CultureInfo.InvariantCulture))));
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextScan)
            return;
        _nextScan = _timing.CurTime + TimeSpan.FromSeconds(0.1);
        try
        {
            Scan();
            _scanFailed = false;
        }
        catch (Exception error)
        {
            if (!_scanFailed)
                Log.Error($"Collision sensor scan failed: {error}");
            _scanFailed = true;
        }
        FlushOutputs();

        if (_timing.CurTime >= _nextUi)
        {
            _nextUi = _timing.CurTime + TimeSpan.FromSeconds(0.5);
            RefreshSafetyUi();
            _refresh.Clear();
        }

        foreach (var (grid, readings) in _grids)
        {
            if (GetMode(grid) == ShuttleCollisionMode.Off || Suspended(grid) ||
                (_alarms.TryGetValue(grid, out var next) && next > _timing.CurTime))
                continue;
            foreach (var reading in readings)
            {
                if (!IsActive(reading))
                    continue;
                _alarms[grid] = _timing.CurTime + TimeSpan.FromSeconds(5);
                _alarmConsoles.Clear();
                _lookup.GetChildEntities(grid, _alarmConsoles);
                foreach (var console in _alarmConsoles)
                {
                    if (this.IsPowered(console, EntityManager))
                        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/beep.ogg"), console);
                }
                break;
            }
        }
        foreach (var grid in _alarms.Keys.ToArray())
        {
            if (!_grids.ContainsKey(grid))
                _alarms.Remove(grid);
        }
    }

    private void Scan()
    {
        var nextReadings = new Dictionary<EntityUid, Reading>();
        var nextGrids = new Dictionary<EntityUid, List<Reading>>();
        var query = EntityQueryEnumerator<ShuttleCollisionSensorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var sensor, out var xform))
        {
            var reading = new Reading { Sensor = uid };
            nextReadings[uid] = reading;

            if (!xform.Anchored || xform.GridUid is not { } grid ||
                !HasComp<ShuttleComponent>(grid) || xform.MapID == MapId.Nullspace)
                continue;

            reading.Grid = grid;
            if (!nextGrids.TryGetValue(grid, out var readings))
                nextGrids[grid] = readings = new List<Reading>();
            readings.Add(reading);
            reading.Powered = this.IsPowered(uid, EntityManager);
            if (!reading.Powered || GetMode(grid) == ShuttleCollisionMode.Off || Paused(grid) || InFtl(grid))
                continue;

            var (position, rotation) = _transform.GetWorldPositionRotation(xform);
            reading.Direction = rotation.RotateVec(-Vector2.UnitY);
            var range = float.IsFinite(sensor.Range) ? Math.Clamp(sensor.Range, 0f, 30f) : 0f;
            var ray = new CollisionRay(position, reading.Direction, (int)(CollisionGroup.MapGrid | CollisionGroup.Impassable));
            foreach (var hit in _physics.IntersectRayWithPredicate(xform.MapID, ray, range,
                         other => IgnoreObstacle(other, grid), false))
            {
                reading.Distance = hit.Distance;
                reading.HitPosition = hit.HitPos;
                reading.Obstacle = hit.HitEntity;
                break;
            }
            reading.State = reading.Distance == null ? ShuttleCollisionSensorState.On : ShuttleCollisionSensorState.Warning;
        }

        _refresh.UnionWith(_grids.Keys);
        _refresh.UnionWith(nextGrids.Keys);
        _readings = nextReadings;
        _grids = nextGrids;
        foreach (var (uid, reading) in nextReadings)
            QueueOutput(uid, reading.State);
        foreach (var uid in _outputs.Keys.ToArray())
        {
            if (!nextReadings.ContainsKey(uid))
                QueueOutput(uid, ShuttleCollisionSensorState.Off);
        }
    }

    private bool IgnoreObstacle(EntityUid uid, EntityUid grid)
    {
        if (uid == grid || !TryComp(uid, out TransformComponent? xform) || xform.GridUid == grid)
            return true;
        return !HasComp<MapGridComponent>(uid) && !xform.Anchored;
    }

    private void RefreshSafetyUi(EntityUid? onlyGrid = null)
    {
        var query = EntityQueryEnumerator<ShuttleConsoleComponent>();
        while (query.MoveNext(out var console, out _))
        {
            if (!_ui.IsUiOpen(console, ShuttleConsoleUiKey.Key) ||
                !_ui.TryGetUiState<ShuttleBoundUserInterfaceState>(console, ShuttleConsoleUiKey.Key, out var previous))
                continue;
            var resolved = _consoles.GetDroneConsole(console);
            var grid = TryComp(resolved, out TransformComponent? xform) ? xform.GridUid : null;
            if (grid == null || (onlyGrid is { } target ? grid != target : !_refresh.Contains(grid.Value)))
                continue;
            _ui.SetUiState(console, ShuttleConsoleUiKey.Key,
                new ShuttleBoundUserInterfaceState(previous.NavState, previous.MapState, previous.DockState)
                {
                    CollisionSafety = GetStatus(grid),
                });
        }
    }

    private void QueueOutput(EntityUid uid, ShuttleCollisionSensorState state)
    {
        if (!_outputs.TryGetValue(uid, out var output))
        {
            _outputs[uid] = output = new Output();
            _outputRevision++;
        }
        if (output.State != state)
        {
            output.State = state;
            output.Revision++;
            _outputRevision++;
            output.Edge = null;
        }
        _pending.Add(uid);
    }

    private void FlushOutputs()
    {
        if (_flushing)
            return;
        _flushing = true;
        try
        {
            for (var pass = 0; pass < 4; pass++)
            {
                var batchRevision = _outputRevision;
                var changed = false;
                foreach (var uid in _pending.ToArray())
                {
                    if (!_outputs.TryGetValue(uid, out var output))
                        continue;
                    if (!TryComp<DeviceLinkSourceComponent>(uid, out var source))
                    {
                        output.Sent = null;
                        output.Edge = null;
                        if (TerminatingOrDeleted(uid) || !HasComp<ShuttleCollisionSensorComponent>(uid))
                        {
                            _outputs.Remove(uid);
                            _pending.Remove(uid);
                        }
                        continue;
                    }
                    if (source.LifeStage < ComponentLifeStage.Running)
                        continue;
                    var revision = output.Revision;
                    var danger = output.State == ShuttleCollisionSensorState.Warning;
                    try
                    {
                        if (!TerminatingOrDeleted(uid))
                            _appearance.SetData(uid, ShuttleCollisionVisuals.State, output.State);
                        if (output.Revision != revision)
                        {
                            changed = true;
                            continue;
                        }
                        if (output.Sent != danger)
                        {
                            _links.SendSignal(uid, StatusPort, danger);
                            if (output.Revision != revision)
                            {
                                output.Sent = null;
                                changed = true;
                                continue;
                            }
                            output.Sent = danger;
                            output.Edge = danger;
                        }
                        if (output.Edge is { } edge)
                        {
                            _links.InvokePort(uid, edge ? DangerPort : ClearPort);
                            if (output.Revision != revision)
                            {
                                output.Sent = null;
                                changed = true;
                                continue;
                            }
                            output.Edge = null;
                        }
                        output.Failed = false;
                        _pending.Remove(uid);
                        if (!HasComp<ShuttleCollisionSensorComponent>(uid))
                            _outputs.Remove(uid);
                    }
                    catch (Exception error)
                    {
                        if (!output.Failed)
                            Log.Error($"Collision sensor output failed for {uid}: {error}");
                        output.Failed = true;
                        output.Sent = null;
                    }
                }
                if (!changed && batchRevision == _outputRevision)
                    break;
            }
        }
        finally
        {
            _flushing = false;
        }
    }

    private ShuttleCollisionMode GetMode(EntityUid grid) =>
        TryComp<ShuttleCollisionControllerComponent>(grid, out var controller) ? controller.Mode : ShuttleCollisionMode.Automatic;

    private bool InFtl(EntityUid grid) => TryComp<FTLComponent>(grid, out var ftl) &&
        (ftl.State & (FTLState.Starting | FTLState.Travelling | FTLState.Arriving)) != 0;

    private bool Suspended(EntityUid grid)
    {
        if (TerminatingOrDeleted(grid) || EntityManager.IsQueuedForDeletion(grid) || Paused(grid) || InFtl(grid) || HasComp<PreventPilotComponent>(grid) ||
            !TryComp<ShuttleComponent>(grid, out var shuttle) || !shuttle.Enabled ||
            !_control.CanControl(grid, ShuttleControlType.Movement))
            return true;
        _docks.Clear();
        _lookup.GetChildEntities(grid, _docks);
        foreach (var dock in _docks)
        {
            if (dock.Comp.Docked)
                return true;
        }
        return false;
    }

    public IEnumerable<EntityUid> GetControlledShuttles()
    {
        foreach (var (grid, readings) in _grids)
        {
            if (GetMode(grid) != ShuttleCollisionMode.Automatic || Suspended(grid))
                continue;
            foreach (var reading in readings)
            {
                if (!IsActive(reading))
                    continue;
                yield return grid;
                break;
            }
        }
    }

    private bool IsActive(Reading reading) => reading.Distance != null && reading.Powered &&
        !TerminatingOrDeleted(reading.Sensor) && !EntityManager.IsQueuedForDeletion(reading.Sensor) && Transform(reading.Sensor).Anchored &&
        Transform(reading.Sensor).GridUid == reading.Grid && this.IsPowered(reading.Sensor, EntityManager) &&
        reading.Obstacle is { } obstacle && !TerminatingOrDeleted(obstacle) && !EntityManager.IsQueuedForDeletion(obstacle);

    public void FilterMovement(EntityUid grid, ref Vector2 linear, ref float angular, ref float brakes)
    {
        if (GetMode(grid) != ShuttleCollisionMode.Automatic || !_grids.TryGetValue(grid, out var readings) || Suspended(grid))
            return;
        var rotation = _transform.GetWorldRotation(grid);
        foreach (var reading in readings)
        {
            if (!IsActive(reading))
                continue;
            if (Vector2.Dot(rotation.RotateVec(linear), reading.Direction) > 0f)
                linear = Vector2.Zero;
            angular = 0f;
            var ownVelocity = PointVelocity(grid, reading.HitPosition);
            var velocity = ownVelocity - PointVelocity(reading.Obstacle!.Value, reading.HitPosition);
            if (Vector2.Dot(velocity, reading.Direction) <= 0.05f || Vector2.Dot(ownVelocity, reading.Direction) <= 0.05f)
                continue;
            linear = Vector2.Zero;
            brakes = 1f;
        }
    }

    private Vector2 PointVelocity(EntityUid uid, Vector2 point)
    {
        var xform = Transform(uid);
        var bodyUid = xform.GridUid ?? uid;
        if (!TryComp<PhysicsComponent>(bodyUid, out var body))
            return Vector2.Zero;
        var center = _transform.GetWorldPosition(bodyUid) + _transform.GetWorldRotation(bodyUid).RotateVec(body.LocalCenter);
        var offset = point - center;
        return body.LinearVelocity + new Vector2(-offset.Y, offset.X) * body.AngularVelocity;
    }

    public ShuttleCollisionStatus? GetStatus(EntityUid? grid)
    {
        if (grid is not { } uid || !_grids.TryGetValue(uid, out var readings) || readings.Count == 0)
            return null;
        var preferred = TryComp<ShuttleCollisionControllerComponent>(uid, out var controller) ? controller.PreferredMode : ShuttleCollisionMode.Automatic;
        var status = new ShuttleCollisionStatus { Mode = GetMode(uid), PreferredMode = preferred, Installed = readings.Count, Suspended = Suspended(uid) };
        foreach (var reading in readings)
        {
            if (reading.Powered)
                status.Powered++;
            if (!IsActive(reading) || status.Mode == ShuttleCollisionMode.Off ||
                (status.Distance is { } previous && previous <= reading.Distance!.Value))
                continue;
            status.Distance = MathF.Round(reading.Distance!.Value, 1);
            status.Direction = (-_transform.GetWorldRotation(uid)).RotateVec(reading.Direction).ToWorldAngle().GetCardinalDir();
        }
        if (TryComp<ShuttleComponent>(uid, out var shuttle))
        {
            for (var i = 0; i < shuttle.LinearThrust.Length; i++)
                status.InsufficientThrust |= shuttle.LinearThrust[i] <= 0f;
        }
        return status;
    }
}
