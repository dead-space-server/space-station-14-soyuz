// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Content.Server.DeadSpace._Soyuz.MilitiaOperations;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.DoAfter;

namespace Content.Server.DeadSpace.TEMP_FOR_EVENT.Elfiry;

public sealed partial class EventCommandOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entities = default!;
    private MilitiaOperationSystem _combat = default!;
    private EventCommandSystem _commands = default!;
    private PathfindingSystem _pathfinding = default!;
    private const string PathKey = "ElfiryEventCommandPath";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _combat = sysManager.GetEntitySystem<MilitiaOperationSystem>();
        _commands = sysManager.GetEntitySystem<EventCommandSystem>();
        _pathfinding = sysManager.GetEntitySystem<PathfindingSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_combat.EventMovementAllowed(owner) || _commands.MovementTarget(owner) is not { } target ||
            !_entities.TryGetComponent<EventSquadUnitComponent>(owner, out var unit) ||
            !_entities.TryGetComponent<TransformComponent>(owner, out var transform))
            return (false, null);
        var revision = unit.Revision;
        var destination = target.Destination;
        if (unit.Path is { } running && _commands.PathCurrent(owner, running) &&
            running.Route.All(point => point.IsValid(_entities)))
            return (true, new Dictionary<string, object> { [PathKey] = running });
        if (_combat.EventPassagePath(owner, revision, destination) is { } passage)
            return Planned(passage);
        if (_combat.DirectEventCommandPath(owner, revision, destination) is { } direct)
            return Planned(direct);
        if (unit.WalkSearch != null)
        {
            var resumed = _combat.FindEventWalkPath(owner, revision, target);
            return resumed != null ? Planned(resumed) : (false, null);
        }
        var path = await _pathfinding.GetPath(owner, transform.Coordinates, destination, 0.6f,
            cancelToken, _pathfinding.GetFlags(blackboard));
        cancelToken.ThrowIfCancellationRequested();
        if (path.Result == PathResult.Path && _combat.PrepareEventCommandPath(owner, revision, destination, path.Path) is { } plan)
            return Planned(plan);
        if (_combat.FindEventWalkPath(owner, revision, target) is { } fallback)
            return Planned(fallback);
        _commands.PathFailed(owner, revision);
        return (false, null);

        (bool Valid, Dictionary<string, object>? Effects) Planned(EventCommandPath value)
            => _commands.MovementTarget(owner) is { } current && target.Matches(current)
                ? (true, new Dictionary<string, object> { [PathKey] = value with { Target = target } })
                : (false, null);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        _combat.BeginEventCommandPath(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner),
            blackboard.GetValue<EventCommandPath>(PathKey));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var path = blackboard.GetValue<EventCommandPath>(PathKey);
        if (!_commands.PathCurrent(owner, path))
            return HTNOperatorStatus.Failed;
        return _combat.RunEventCommandPath(owner, path);
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (blackboard.TryGetValue<EventCommandPath>(PathKey, out var path, _entities))
            _combat.EndEventCommandPath(owner, path);
    }

    public override void PlanShutdown(NPCBlackboard blackboard)
    {
        TaskShutdown(blackboard, HTNOperatorStatus.Failed);
    }
}

public sealed partial class EventUncuffOperator : HTNOperator
{
    [Dependency] private readonly IEntityManager _entities = default!;
    private MilitiaOperationSystem _combat = default!;
    private SharedCuffableSystem _cuffs = default!;
    private SharedDoAfterSystem _doAfter = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _combat = sysManager.GetEntitySystem<MilitiaOperationSystem>();
        _cuffs = sysManager.GetEntitySystem<SharedCuffableSystem>();
        _doAfter = sysManager.GetEntitySystem<SharedDoAfterSystem>();
    }

    private bool Restrained(EntityUid owner)
        => _entities.TryGetComponent<CuffableComponent>(owner, out var cuffs) &&
           _cuffs.IsCuffed((owner, cuffs), requireFullyCuffed: false);

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return Task.FromResult<(bool, Dictionary<string, object>?)>((_combat.EventUnitAvailable(owner) && Restrained(owner), null));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_combat.EventUnitAvailable(owner))
            return HTNOperatorStatus.Failed;
        if (!Restrained(owner))
            return HTNOperatorStatus.Finished;
        if (!_entities.TryGetComponent<DoAfterComponent>(owner, out var actions))
            return HTNOperatorStatus.Failed;
        if (!actions.DoAfters.Values.Any(action => action.Args.Event is UnCuffDoAfterEvent && _doAfter.IsRunning(action.Id)))
            _cuffs.TryUncuff(owner, owner);
        return HTNOperatorStatus.Continuing;
    }
}
