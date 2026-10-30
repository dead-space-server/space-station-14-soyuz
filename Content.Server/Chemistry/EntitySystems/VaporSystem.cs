using Content.Server.Chemistry.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Physics;
using Content.Shared.Throwing;
using Content.Shared.Chemistry.EntitySystems;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using System.Numerics;
using System.Linq; // DS14-Soyuz
using Content.Server.Chemistry.TileReactions; // DS14-Soyuz
using Robust.Shared.Timing; // DS14-Soyuz

namespace Content.Server.Chemistry.EntitySystems
{
    [UsedImplicitly]
    internal sealed class VaporSystem : EntitySystem
    {
        [Dependency] private readonly IPrototypeManager _protoManager = default!;
        [Dependency] private readonly SharedMapSystem _map = default!;
        [Dependency] private readonly SharedPhysicsSystem _physics = default!;
        [Dependency] private readonly SharedSolutionContainerSystem _solutionContainerSystem = default!;
        [Dependency] private readonly ThrowingSystem _throwing = default!;
        [Dependency] private readonly ReactiveSystem _reactive = default!;
        [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
        [Dependency] private readonly IGameTiming _timing = default!; // DS14-Soyuz

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<VaporComponent, StartCollideEvent>(HandleCollide);
        }

        private void HandleCollide(Entity<VaporComponent> entity, ref StartCollideEvent args)
        {
            if (!TryComp(entity.Owner, out SolutionContainerManagerComponent? contents)) return;

            foreach (var (_, soln) in _solutionContainerSystem.EnumerateSolutions((entity.Owner, contents)))
            {
                var solution = soln.Comp.Solution;
                _reactive.DoEntityReaction(args.OtherEntity, solution, ReactionMethod.Touch);
            }

            // Check for collision with a impassable object (e.g. wall) and stop
            if ((args.OtherFixture.CollisionLayer & (int)CollisionGroup.Impassable) != 0 && args.OtherFixture.Hard)
            {
                QueueDel(entity);
            }
        }

        public void Start(Entity<VaporComponent> vapor,
            TransformComponent vaporXform,
            Vector2 dir,
            float speed,
            MapCoordinates target,
            float aliveTime,
            EntityUid? user = null)
        {
            vapor.Comp.Active = true;
            var despawn = EnsureComp<TimedDespawnComponent>(vapor);
            despawn.Lifetime = aliveTime;

            // Set Move
            if (TryComp(vapor, out PhysicsComponent? physics))
            {
                _physics.SetLinearDamping(vapor, physics, 0f);
                _physics.SetAngularDamping(vapor, physics, 0f);

                _throwing.TryThrow(vapor, dir, speed, user: user);

                var distance = (target.Position - _transformSystem.GetWorldPosition(vaporXform)).Length();
                var time = (distance / physics.LinearVelocity.Length());
                despawn.Lifetime = MathF.Min(aliveTime, time);
            }
        }

        internal bool TryAddSolution(Entity<VaporComponent> vapor, Solution solution)
        {
            if (solution.Volume == 0)
            {
                return false;
            }

            if (!_solutionContainerSystem.TryGetSolution(vapor.Owner,
                    VaporComponent.SolutionName,
                    out var vaporSolution))
            {
                return false;
            }

            return _solutionContainerSystem.TryAddSolution(vaporSolution.Value, solution);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            // Enumerate over all VaporComponents
            var query = EntityQueryEnumerator<VaporComponent, SolutionContainerManagerComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var vaporComp, out var container, out var xform))
            {
                // Return early if we're not active
                if (!vaporComp.Active || MetaData(uid).EntityPaused) // DS14-Soyuz: pause repeated cleaning as well.
                    continue;

                // Get the current location of the vapor entity first
                if (TryComp(xform.GridUid, out MapGridComponent? gridComp))
                {
                    var tile = _map.GetTileRef(xform.GridUid.Value, gridComp, xform.Coordinates);

                    // DS14-Soyuz-start: cleaning can retry while the vapor remains over a tile.
                    var sameTile = vaporComp.PreviousTileRef == tile;
                    if (sameTile && _timing.CurTime < vaporComp.NextCleaningReaction)
                        continue;
                    // DS14-Soyuz-end

                    // Enumerate over all the reagents in the vapor entity solution
                    foreach (var (_, soln) in _solutionContainerSystem.EnumerateSolutions((uid, container)))
                    {
                        // Iterate over the reagents in the solution
                        // Reason: Each reagent in our solution may have a unique TileReaction
                        // In this instance, we check individually for each reagent's TileReaction
                        // This is not doing chemical reactions!
                        var contents = soln.Comp.Solution;
                        foreach (var reagentQuantity in contents.Contents.ToArray())
                        {
                            // Check if the reagent is empty
                            if (reagentQuantity.Quantity == FixedPoint2.Zero)
                                continue;

                            var reagent = _protoManager.Index<ReagentPrototype>(reagentQuantity.Reagent.Prototype);

                            // DS14-Soyuz-start
                            var cleaningReagent = reagent.TileReactions.Any(tileReaction => tileReaction is CleanTileReaction);
                            if (sameTile && !cleaningReagent)
                                continue;
                            // DS14-Soyuz-end

                            // Limit the reaction amount to a minimum value to ensure no floating point funnies.
                            // Ex: A solution with a low percentage transfer amount will slowly approach 0.01... and never get deleted
                            // DS14-Soyuz: offer the available cleaner, consuming only what the tile actually uses.
                            var clampedAmount = cleaningReagent ? (float) reagentQuantity.Quantity :
                                Math.Min((float) reagentQuantity.Quantity, Math.Max(
                                    (float) reagentQuantity.Quantity * vaporComp.TransferAmountPercentage,
                                    vaporComp.MinimumTransferAmount)); // DS14-Soyuz

                            // Preform the reagent's TileReaction
                            var reaction =
                                reagent.ReactionTile(tile,
                                    clampedAmount,
                                    EntityManager,
                                    reagentQuantity.Reagent.Data);

                            if (reaction > reagentQuantity.Quantity)
                                reaction = reagentQuantity.Quantity;

                            _solutionContainerSystem.RemoveReagent(soln, reagentQuantity.Reagent, reaction);
                        }

                        // Delete the vapor entity if it has no contents
                        if (contents.Volume == 0)
                            QueueDel(uid);

                    }

                    // Set the previous tile reference to the current tile
                    vaporComp.PreviousTileRef = tile;
                    vaporComp.NextCleaningReaction = _timing.CurTime + TimeSpan.FromSeconds(0.1); // DS14-Soyuz
                }
            }
        }
    }
}
