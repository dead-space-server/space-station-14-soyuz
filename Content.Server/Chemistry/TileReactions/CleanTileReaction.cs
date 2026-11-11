using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using System.Linq;

namespace Content.Server.Chemistry.TileReactions;

/// <summary>
/// Turns all of the reagents on a puddle into water.
/// </summary>
[DataDefinition]
public sealed partial class CleanTileReaction : ITileReaction
{
    // DS14-Soyuz-start: optional exact-tile cleaning for open Iney.
    [DataField]
    public bool OnlyCurrentTile;
    // DS14-Soyuz-end
    /// <summary>
    /// How much it costs to clean 1 unit of reagent.
    /// </summary>
    /// <remarks>
    /// In terms of space cleaner can clean 1 average puddle per 5 units.
    /// </remarks>
    [DataField("cleanCost")]
    public float CleanAmountMultiplier { get; private set; } = 0.25f;

    /// <summary>
    /// What reagent to replace the tile conents with.
    /// </summary>
    [DataField("reagent", customTypeSerializer: typeof(PrototypeIdSerializer<ReagentPrototype>))]
    public string ReplacementReagent = "Water";

    FixedPoint2 ITileReaction.TileReact(TileRef tile,
        ReagentPrototype reagent,
        FixedPoint2 reactVolume,
        IEntityManager entityManager
        , List<ReagentData>? data)
    {
        // DS14-Soyuz-start: spend the available cleaner on the aimed tile before overlapping neighbours.
        if (reactVolume <= FixedPoint2.Zero || !float.IsFinite(CleanAmountMultiplier) || CleanAmountMultiplier <= 0)
            return FixedPoint2.Zero;

        var puddleQuery = entityManager.GetEntityQuery<PuddleComponent>();
        var transformSystem = entityManager.System<SharedTransformSystem>();
        var entities = entityManager.System<EntityLookupSystem>().GetLocalEntitiesIntersecting(tile, 0f)
            .Where(entity => puddleQuery.HasComponent(entity))
            .OrderBy(entity => entityManager.TryGetComponent(entity, out TransformComponent? transform) &&
                               transform.GridUid == tile.GridUid &&
                               transformSystem.GetGridTilePositionOrDefault((entity, transform)) == tile.GridIndices ? 0 : 1)
            .ToArray();
        // DS14-Soyuz-end
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        // Multiply as the amount we can actually purge is higher than the react amount.
        var purgeAmount = reactVolume / CleanAmountMultiplier;

        foreach (var entity in entities)
        {
            // DS14-Soyuz-start: an already clean puddle must not stop the rest of the pass.
            if (purgeAmount <= FixedPoint2.Zero)
                break;
            // DS14-Soyuz-end
            // DS14-Soyuz-start: intersecting bounds alone can include a neighbouring puddle.
            if (OnlyCurrentTile &&
                (!entityManager.TryGetComponent(entity, out TransformComponent? xform) ||
                 xform.GridUid != tile.GridUid ||
                 transformSystem.GetGridTilePositionOrDefault((entity, xform)) != tile.GridIndices))
                continue;
            // DS14-Soyuz-end
            if (!puddleQuery.TryGetComponent(entity, out var puddle) ||
                !solutionContainerSystem.TryGetSolution(entity, puddle.SolutionName, out var puddleSolution, out _))
            {
                continue;
            }

            var purgeable = solutionContainerSystem.SplitSolutionWithout(puddleSolution.Value, purgeAmount, ReplacementReagent, reagent.ID);

            // DS14-Soyuz-start
            if (purgeable.Volume <= FixedPoint2.Zero)
                continue;
            // DS14-Soyuz-end

            purgeAmount -= purgeable.Volume;

            solutionContainerSystem.TryAddSolution(puddleSolution.Value, new Solution(ReplacementReagent, purgeable.Volume));
        }

        // DS14-Soyuz-start: charge trace amounts too, and never exceed the supplied dose after rounding.
        var cleaned = reactVolume / CleanAmountMultiplier - purgeAmount;
        return cleaned <= FixedPoint2.Zero ? FixedPoint2.Zero :
            FixedPoint2.Min(reactVolume, FixedPoint2.Max(FixedPoint2.Epsilon, cleaned * CleanAmountMultiplier));
        // DS14-Soyuz-end
    }
}
