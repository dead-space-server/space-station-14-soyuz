using Content.Server.Administration.Logs;
using Content.Server.Body.Systems;
using Content.Shared.EntityEffects.Effects;
using Content.Server.Spreader;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Smoking;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Linq;
using Content.Shared.Body.Systems;
using Content.Shared.EntityEffects.Effects.Solution;
using TimedDespawnComponent = Robust.Shared.Spawners.TimedDespawnComponent;
using Content.Server.Chemistry.TileReactions; // DS14-Soyuz

namespace Content.Server.Fluids.EntitySystems;

/// <summary>
/// Handles non-atmos solution entities similar to puddles.
/// </summary>
public sealed class SmokeSystem : EntitySystem
{
    // If I could do it all again this could probably use a lot more of puddles.
    [Dependency] private readonly IAdminLogManager _logger = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly BloodstreamSystem _blood = default!;
    [Dependency] private readonly InternalsSystem _internals = default!;
    [Dependency] private readonly ReactiveSystem _reactive = default!;
    [Dependency] private readonly SharedBroadphaseSystem _broadphase = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainerSystem = default!;

    private EntityQuery<SmokeComponent> _smokeQuery;
    private EntityQuery<SmokeAffectedComponent> _smokeAffectedQuery;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        _smokeQuery = GetEntityQuery<SmokeComponent>();
        _smokeAffectedQuery = GetEntityQuery<SmokeAffectedComponent>();

        SubscribeLocalEvent<SmokeComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<SmokeComponent, EndCollideEvent>(OnEndCollide);
        SubscribeLocalEvent<SmokeComponent, ReactionAttemptEvent>(OnReactionAttempt);
        SubscribeLocalEvent<SmokeComponent, SolutionRelayEvent<ReactionAttemptEvent>>(OnReactionAttempt);
        SubscribeLocalEvent<SmokeComponent, SpreadNeighborsEvent>(OnSmokeSpread);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SmokeAffectedComponent>();
        var curTime = _timing.CurTime;
        while (query.MoveNext(out var uid, out var smoke))
        {
            if (curTime < smoke.NextSecond)
                continue;

            smoke.NextSecond += TimeSpan.FromSeconds(1);
            SmokeReact(uid, smoke.SmokeEntity);
        }

        // DS14-Soyuz-start: persistent cleaning only; other smoke chemistry retains its one-shot tile reactions.
        var cleaningQuery = EntityQueryEnumerator<SmokeComponent>();
        while (cleaningQuery.MoveNext(out var foamUid, out var foam))
        {
            if (foam.SharedSolutionOwner == null || curTime < foam.NextTileReaction || MetaData(foamUid).EntityPaused)
                continue;

            foam.NextTileReaction = curTime + TimeSpan.FromSeconds(0.5);
            ReactOnTile(foamUid, foam);
        }
        // DS14-Soyuz-end
    }

    private void OnStartCollide(Entity<SmokeComponent> entity, ref StartCollideEvent args)
    {
        if (_smokeAffectedQuery.HasComponent(args.OtherEntity))
            return;

        var smokeAffected = AddComp<SmokeAffectedComponent>(args.OtherEntity);
        smokeAffected.SmokeEntity = entity;
        smokeAffected.NextSecond = _timing.CurTime + TimeSpan.FromSeconds(1);
    }

    private void OnEndCollide(Entity<SmokeComponent> entity, ref EndCollideEvent args)
    {

        //DS14-start
        if (Terminating(args.OtherEntity))
            return;
        //DS14-end

        // if we are already in smoke, make sure the thing we are exiting is the current smoke we are in.
        if (_smokeAffectedQuery.TryGetComponent(args.OtherEntity, out var smokeAffectedComponent))
        {
            if (smokeAffectedComponent.SmokeEntity != entity.Owner)
                return;
        }

        var exists = Exists(entity);

        if (!TryComp<PhysicsComponent>(args.OtherEntity, out var body))
            return;

        foreach (var ent in _physics.GetContactingEntities(args.OtherEntity, body))
        {
            if (exists && ent == entity.Owner)
                continue;

            if (!_smokeQuery.HasComponent(ent))
                continue;

            smokeAffectedComponent ??= EnsureComp<SmokeAffectedComponent>(args.OtherEntity);
            smokeAffectedComponent.SmokeEntity = ent;
            return; // exit the function so we don't remove the component.
        }

        if (smokeAffectedComponent != null)
            RemComp(args.OtherEntity, smokeAffectedComponent);
    }

    private void OnSmokeSpread(Entity<SmokeComponent> entity, ref SpreadNeighborsEvent args)
    {
        // DS14-Soyuz-start
        if (entity.Comp.SharedSolutionOwner != null)
        {
            SpreadCleaningSmoke(entity, ref args);
            return;
        }
        // DS14-Soyuz-end

        if (entity.Comp.SpreadAmount == 0 || !_solutionContainerSystem.ResolveSolution(entity.Owner, SmokeComponent.SolutionName, ref entity.Comp.Solution, out var solution))
        {
            RemCompDeferred<ActiveEdgeSpreaderComponent>(entity);
            return;
        }

        if (Prototype(entity) is not { } prototype)
        {
            RemCompDeferred<ActiveEdgeSpreaderComponent>(entity);
            return;
        }

        if (args.NeighborFreeTiles.Count == 0)
            return;

        TryComp<TimedDespawnComponent>(entity, out var timer);

        // wtf is the logic behind any of this.
        var smokePerSpread = entity.Comp.SpreadAmount / Math.Max(1, args.NeighborFreeTiles.Count);
        var hasSolution = solution.Volume > FixedPoint2.Zero; // DS14
        var solutionPerSpread = hasSolution ? solution.Volume / (args.NeighborFreeTiles.Count + 1) : FixedPoint2.Zero; // DS14
        foreach (var neighbor in args.NeighborFreeTiles)
        {
            // DS14-start
            var spreadSolution = hasSolution
                ? _solutionContainerSystem.SplitSolution(entity.Comp.Solution!.Value, solutionPerSpread)
                : new Solution();
            if (hasSolution && spreadSolution.Volume == FixedPoint2.Zero)
            {
                RemCompDeferred<ActiveEdgeSpreaderComponent>(entity);
                break;
            }
            // DS14-end

            var coords = _map.GridTileToLocal(neighbor.Tile.GridUid, neighbor.Grid, neighbor.Tile.GridIndices);
            var ent = Spawn(prototype.ID, coords);
            var spreadAmount = Math.Max(0, smokePerSpread);
            entity.Comp.SpreadAmount -= args.NeighborFreeTiles.Count;

            StartSmoke(ent, spreadSolution, timer?.Lifetime ?? entity.Comp.Duration, spreadAmount); // DS14

            if (entity.Comp.SpreadAmount == 0 || (hasSolution && solution.Volume == FixedPoint2.Zero)) // DS14
            {
                RemCompDeferred<ActiveEdgeSpreaderComponent>(entity);
                break;
            }
        }

        // DS14-start
        if (hasSolution)
        {
            UpdateVisuals((entity.Owner, entity.Comp, null));

            if (solution.Volume == FixedPoint2.Zero)
            {
                RemCompDeferred<ActiveEdgeSpreaderComponent>(entity);
                return;
            }
        }
        // DS14-end

        args.Updates--;

        if (args.NeighborFreeTiles.Count > 0 || args.Neighbors.Count == 0 || entity.Comp.SpreadAmount < 1)
            return;

        // We have no more neighbours to spread to. So instead we will randomly distribute our volume to neighbouring smoke tiles.

        var smokeQuery = GetEntityQuery<SmokeComponent>();

        _random.Shuffle(args.Neighbors);
        foreach (var neighbor in args.Neighbors)
        {
            if (!smokeQuery.TryGetComponent(neighbor, out var smoke))
                continue;

            smoke.SpreadAmount++;
            entity.Comp.SpreadAmount--;
            EnsureComp<ActiveEdgeSpreaderComponent>(neighbor);

            if (entity.Comp.SpreadAmount == 0)
            {
                RemCompDeferred<ActiveEdgeSpreaderComponent>(entity);
                break;
            }
        }

    }

    private void OnReactionAttempt(Entity<SmokeComponent> entity, ref ReactionAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        // Prevent smoke/foam fork bombs (smoke creating more smoke).
        foreach (var effect in args.Reaction.Effects)
        {
            if (effect is AreaReactionEffect)
            {
                args.Cancelled = true;
                return;
            }
        }
    }

    private void OnReactionAttempt(Entity<SmokeComponent> entity, ref SolutionRelayEvent<ReactionAttemptEvent> args)
    {
        if (args.Name == SmokeComponent.SolutionName)
            OnReactionAttempt(entity, ref args.Event);
    }

    /// <summary>
    /// Sets up a smoke component for spreading.
    /// </summary>
    public void StartSmoke(EntityUid uid, Solution solution, float duration, int spreadAmount, SmokeComponent? component = null,
        EntityUid? sharedSolutionOwner = null) // DS14-Soyuz
    {
        if (!Resolve(uid, ref component))
            return;

        component.SpreadAmount = spreadAmount;
        component.Duration = duration;
        component.TransferRate = solution.Volume / duration;
        // DS14-Soyuz-start: share only pure cleaning solutions; do not concentrate poison or other smoke mixtures.
        component.SharedSolutionOwner = sharedSolutionOwner;
        if (sharedSolutionOwner == null && solution.Contents.Count > 0 && solution.Contents.All(quantity =>
                _prototype.Index<ReagentPrototype>(quantity.Reagent.Prototype).TileReactions.Any(reaction => reaction is CleanTileReaction)))
        {
            component.SharedSolutionOwner = uid;
        }
        if (sharedSolutionOwner is { } owner && _smokeQuery.TryGetComponent(owner, out var reservoir))
            component.TransferRate = reservoir.TransferRate;
        component.NextTileReaction = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        // DS14-Soyuz-end
        TryAddSolution(uid, solution);
        Dirty(uid, component);
        EnsureComp<ActiveEdgeSpreaderComponent>(uid);

        if (TryComp<PhysicsComponent>(uid, out var body) && TryComp<FixturesComponent>(uid, out var fixtures))
        {
            var xform = Transform(uid);
            _physics.SetBodyType(uid, BodyType.Dynamic, fixtures, body, xform);
            _physics.SetCanCollide(uid, true, manager: fixtures, body: body);
            _broadphase.RegenerateContacts((uid, body, fixtures, xform));
        }

        var timer = EnsureComp<TimedDespawnComponent>(uid);
        timer.Lifetime = duration;

        // DS14-Soyuz: cleaning clouds also retry later, charging every reaction to their shared reservoir.
        ReactOnTile(uid, component);
        // DS14-Soyuz-start: child cleaning tiles have an empty local solution.
        if (component.SharedSolutionOwner != null)
            UpdateVisuals((uid, component, null));
        // DS14-Soyuz-end
    }

    /// <summary>
    /// Does the relevant smoke reactions for an entity.
    /// </summary>
    public void SmokeReact(EntityUid entity, EntityUid smokeUid, SmokeComponent? component = null)
    {
        if (!Resolve(smokeUid, ref component))
            return;

        if (!TryGetSmokeSolution((smokeUid, component), out var source, out var solution) || // DS14-Soyuz
            solution.Contents.Count == 0)
        {
            return;
        }

        ReactWithEntity(entity, smokeUid, source, solution, component); // DS14-Soyuz
        UpdateVisuals((smokeUid, component));
    }

    private void ReactWithEntity(EntityUid entity, EntityUid smokeUid, Entity<SolutionComponent> source, Solution solution, SmokeComponent? component = null) // DS14-Soyuz
    {
        if (!Resolve(smokeUid, ref component))
            return;

        if (!TryComp<BloodstreamComponent>(entity, out var bloodstream))
            return;

        if (!_solutionContainerSystem.ResolveSolution(entity, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var bloodSolution) || bloodSolution.AvailableVolume <= 0)
            return;

        var blockIngestion = _internals.AreInternalsWorking(entity);
        var availableTransfer = FixedPoint2.Min(solution.Volume, component.TransferRate); // DS14
        var transferAmount = FixedPoint2.Min(availableTransfer, bloodSolution.AvailableVolume);
        // DS14-start
        if (transferAmount <= FixedPoint2.Zero)
            return;

        if (blockIngestion)
        {
            var sampledSolution = solution.Clone().SplitSolution(transferAmount);
            ReactWithSolution(entity, sampledSolution, false);
            return;
        }

        var transferSolution = _solutionContainerSystem.SplitSolution(source, transferAmount); // DS14-Soyuz
        ReactWithSolution(entity, transferSolution, true);
        // DS14-end

        if (_blood.TryAddToBloodstream((entity, bloodstream), transferSolution))
        {
            // Log solution addition by smoke
            _logger.Add(LogType.ForceFeed, LogImpact.Medium, $"{ToPrettyString(entity):target} ingested smoke {SharedSolutionContainerSystem.ToPrettyString(transferSolution)}");
        }
    }

    // DS14-start
    private void ReactWithSolution(EntityUid entity, Solution solution, bool ingestion)
    {
        foreach (var reagentQuantity in solution.Contents.ToArray())
        {
            if (reagentQuantity.Quantity == FixedPoint2.Zero)
                continue;

            _reactive.ReactionEntity(entity, ReactionMethod.Touch, reagentQuantity);

            if (ingestion)
                _reactive.ReactionEntity(entity, ReactionMethod.Ingestion, reagentQuantity);
        }
    }
    // DS14-end

    private void ReactOnTile(EntityUid uid, SmokeComponent? component = null, TransformComponent? xform = null)
    {
        if (!Resolve(uid, ref component, ref xform))
            return;

        if (!TryGetSmokeSolution((uid, component), out var source, out var solution) || !solution.Any()) // DS14-Soyuz
            return;

        if (!TryComp<MapGridComponent>(xform.GridUid, out var mapGrid))
            return;

        var tile = _map.GetTileRef(xform.GridUid.Value, mapGrid, xform.Coordinates);

        foreach (var reagentQuantity in solution.Contents.ToArray())
        {
            if (reagentQuantity.Quantity == FixedPoint2.Zero)
                continue;

            var reagent = _prototype.Index<ReagentPrototype>(reagentQuantity.Reagent.Prototype);
            // DS14-Soyuz-start: tile cleaning consumes real reagent, including every repeated pass.
            var reacted = reagent.ReactionTile(tile, reagentQuantity.Quantity, EntityManager, reagentQuantity.Reagent.Data);
            if (component.SharedSolutionOwner != null)
                _solutionContainerSystem.RemoveReagent(source, reagentQuantity.Reagent,
                    FixedPoint2.Min(reagentQuantity.Quantity, FixedPoint2.Max(FixedPoint2.Zero, reacted)));
            // DS14-Soyuz-end
        }
    }

    // DS14-Soyuz-start
    private bool TryGetSmokeSolution(Entity<SmokeComponent> smoke, out Entity<SolutionComponent> source, out Solution solution)
    {
        source = default;
        solution = default!;
        var owner = smoke.Comp.SharedSolutionOwner ?? smoke.Owner;
        if (TerminatingOrDeleted(owner) || !_smokeQuery.TryGetComponent(owner, out var reservoir) ||
            !_solutionContainerSystem.ResolveSolution(owner, SmokeComponent.SolutionName, ref reservoir.Solution, out var found))
            return false;

        source = reservoir.Solution.Value;
        solution = found;
        return true;
    }

    private void SpreadCleaningSmoke(Entity<SmokeComponent> entity, ref SpreadNeighborsEvent args)
    {
        if (entity.Comp.SharedSolutionOwner is not { } owner ||
            !_smokeQuery.TryGetComponent(owner, out var reservoir) || reservoir.SpreadAmount <= 0 ||
            !TryGetSmokeSolution(entity, out _, out var solution) || solution.Volume <= FixedPoint2.Zero ||
            Prototype(entity) is not { } prototype)
        {
            RemCompDeferred<ActiveEdgeSpreaderComponent>(entity);
            return;
        }

        TryComp<TimedDespawnComponent>(entity, out var timer);
        foreach (var neighbor in args.NeighborFreeTiles)
        {
            if (args.Updates <= 0 || reservoir.SpreadAmount <= 0 || solution.Volume <= FixedPoint2.Zero)
                break;

            var coords = _map.GridTileToLocal(neighbor.Tile.GridUid, neighbor.Grid, neighbor.Tile.GridIndices);
            var child = Spawn(prototype.ID, coords);
            reservoir.SpreadAmount--;
            args.Updates--;
            StartSmoke(child, new Solution(), timer?.Lifetime ?? entity.Comp.Duration, 0, sharedSolutionOwner: owner);
        }

        // Every new tile spends one shared spread unit; enclosed branches cannot strand or duplicate the budget.
        if (args.NeighborFreeTiles.Count == 0 || reservoir.SpreadAmount <= 0 || solution.Volume <= FixedPoint2.Zero)
            RemCompDeferred<ActiveEdgeSpreaderComponent>(entity);
    }
    // DS14-Soyuz-end

    /// <summary>
    /// Adds the specified solution to the relevant smoke solution.
    /// </summary>
    private void TryAddSolution(Entity<SmokeComponent?> smoke, Solution solution)
    {
        if (solution.Volume == FixedPoint2.Zero)
            return;

        if (!Resolve(smoke, ref smoke.Comp))
            return;

        if (!_solutionContainerSystem.ResolveSolution(smoke.Owner, SmokeComponent.SolutionName, ref smoke.Comp.Solution, out var solutionArea))
            return;

        var addSolution = solution.SplitSolution(FixedPoint2.Min(solution.Volume, solutionArea.AvailableVolume));
        _solutionContainerSystem.TryAddSolution(smoke.Comp.Solution.Value, addSolution);

        UpdateVisuals(smoke);
    }

    private void UpdateVisuals(Entity<SmokeComponent?, AppearanceComponent?> smoke)
    {
        if (!Resolve(smoke, ref smoke.Comp1, ref smoke.Comp2) ||
            !TryGetSmokeSolution((smoke.Owner, smoke.Comp1), out _, out var solution)) // DS14-Soyuz
            return;

        // DS14-Soyuz-start: exhausted cleaning foam remains visible until its normal dissolve animation.
        if (smoke.Comp1.SharedSolutionOwner != null && solution.Volume <= FixedPoint2.Zero)
            return;
        // DS14-Soyuz-end

        var color = solution.GetColor(_prototype);
        _appearance.SetData(smoke.Owner, SmokeVisuals.Color, color, smoke.Comp2);
    }
}
