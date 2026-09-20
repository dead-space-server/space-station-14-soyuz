// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Paper;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.RepairOrders;

[TestFixture]
public sealed class RepairTechnicalExclusionTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: repairOrder
          id: RepairTestFloorCover
          name: repair-order-damaged-cargo-shuttle-name
          description: repair-order-damaged-cargo-shuttle-description
          objectType: repair-order-object-type-small-shuttle
          objectName: repair-order-object-name-dinero-mk2
          targetGridPath: /Maps/_Soyuz/RepairOrders/Tests/floor_cover_target.yml
          damageProfile: RepairDamageLight
          scoreProfile: RepairOrderEngineeringScore
          rewardPool: RepairOrderEngineeringRewardPool
          repairTime: 20m
          weight: 0
        """;

    [TestCase(0, 1000)]
    [TestCase(1, 990)]
    [TestCase(2, 980)]
    [TestCase(3, 970)]
    [TestCase(5, 950)]
    [TestCase(10, 900)]
    [TestCase(100, 0)]
    [TestCase(101, 0)]
    public void LinearPenaltyAndRewardBudget(int count, int expected)
    {
        var totals = new RepairExclusionTotals(count, count * 2, 500, 1000);
        Assert.That(totals.PenaltyPercent, Is.EqualTo(count));
        Assert.That(totals.FinalPoints, Is.EqualTo(expected));
        Assert.That(RepairOrderRewardBudget.ForSuccessfulCompletion(totals.FinalPoints), Is.EqualTo(expected));
        Assert.That(RepairOrderRewardBudget.ForExpiration(totals.FinalPoints), Is.EqualTo(expected / 2));
    }

    [TestCase(800, 3, 776)]
    [TestCase(820, 4, 787)]
    public void PenaltyUsesExactIntegerFloor(int raw, int count, int expected)
        => Assert.That(RepairTechnicalExclusion.FinalPoints(raw, count), Is.EqualTo(expected));

    [Test]
    public void CostCapIsIndependentFromCount()
    {
        var max = RepairTechnicalExclusion.MaxWaivedPoints(1001);
        Assert.That(max, Is.EqualTo(500));
        Assert.That(RepairTechnicalExclusion.CanAdd(0, max, 100), Is.True);
        Assert.That(RepairTechnicalExclusion.CanAdd(450, max, 50), Is.True);
        Assert.That(RepairTechnicalExclusion.CanAdd(450, max, 51), Is.False);
        Assert.That(RepairTechnicalExclusion.CanAdd(0, max, 0), Is.False);
        Assert.That(RepairTechnicalExclusion.CanAdd(int.MaxValue, max, 1), Is.False);
        Assert.That(new RepairExclusionTotals(1, 500, max, 1000).FinalPoints, Is.EqualTo(990));
        Assert.That(new RepairExclusionTotals(10, 100, max, 1000).FinalPoints, Is.EqualTo(900));
    }

    [Test]
    public async Task CoveringAndFloorRemainIndependentThroughDamageWaiverAndRepair()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var map = maps.CreateMap(out var mapId);
            maps.SetPaused(map, true);
            var station = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var state = server.EntMan.AddComponent<RepairOrderStationComponent>(station);
            var validation = server.System<RepairOrderValidationSystem>();
            EntityUid grid = default;
            try
            {
                ProtoId<RepairOrderPrototype> orderId = "RepairTestFloorCover";
                var order = server.ProtoMan.Index(orderId);
                Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, order.TargetGridPath, out var loaded), Is.True);
                var target = loaded!.Value;
                grid = target.Owner;
                var damage = server.System<RepairOrderDamageSystem>();
                var snapshot = damage.Snapshot(target, order);
                var removedTiles = snapshot.Floors.Where(c => c.X < 4 && c.Y < 4).ToArray();
                var removedEntities = snapshot.Entities.Where(e => removedTiles.Contains(e.Cell)).Select(e => e.Index).ToArray();
                var center = new Vector2i(1, 1);
                var ev = new RepairDamageEventResult("RepairDamageFloorCollapse", ImmutableArray.Create(center),
                    removedTiles.ToImmutableArray(), removedTiles.Length + removedEntities.Length);
                ProtoId<RepairDamageProfilePrototype> profileId = "RepairDamageLight";
                var profile = server.ResolveDependency<ISerializationManager>().CreateCopy(server.ProtoMan.Index(profileId));
                profile.Events = new() { ev.Event };
                profile.MinEvents = 1;
                profile.MaxEvents = 1;
                profile.MaxGenerationAttempts = 0;
                profile.MaxEventRolls = 0;
                profile.MinDamageFraction = .001f;
                profile.MaxDamageFraction = .99f;
                profile.MaxRemovedFloorFraction = .99f;
                profile.MaxRemovedAnchoredEntityFraction = 1;
                profile.MinRemainingFloor = 2;
                profile.MinChangedRequirements = 1;
                profile.MinimumEventSeparation = 0;
                profile.MaxOccurrencesPerEvent = 1;
                profile.MaxSeverity = RepairDamageSeverity.Heavy;
                var plan = RepairOrderDamageSystem.MakePlan(snapshot, 1, 0, removedTiles, removedEntities, new[] { ev });
                damage.ApplyPlan(target, snapshot, profile, plan);
                Assert.That(validation.TryPrepareSession(station, 1, order.ID, grid, out var active), Is.True);
                active.ExpiresAt = TimeSpan.MaxValue;
                state.Active = active;
                var blueprint = server.EntMan.GetComponent<RepairBlueprintComponent>(grid);
                var initialMax = blueprint.MaxPoints;
                var limit = blueprint.MaxWaivedPoints;
                var missingFloors = blueprint.TasksByCell.Values.SelectMany(t => t)
                    .Where(t => t.Type == RepairTaskType.Tile && t.State == RepairTaskState.Missing).ToArray();
                var waivedFloorIds = new List<int>();
                foreach (var task in missingFloors)
                {
                    var fits = active.Exclusions!.Totals.WaivedPoints + task.Points <= limit;
                    Assert.That(validation.TrySetTechnicalExclusion(grid, 1, task.RequirementId, false, out _), Is.EqualTo(fits));
                    if (!fits) break;
                    waivedFloorIds.Add(task.RequirementId);
                }
                Assert.That(active.Exclusions!.Totals.WaivedPoints, Is.LessThanOrEqualTo(limit));
                Assert.That(waivedFloorIds.Count, Is.LessThan(missingFloors.Length), "The server must refuse to waive every missing floor.");
                foreach (var id in waivedFloorIds)
                    Assert.That(validation.TrySetTechnicalExclusion(grid, 1, id, true, out _), Is.True);
                RepairTask Floor() => blueprint.TasksByCell[center].Single(t => t.Type == RepairTaskType.Tile);
                RepairTask[] Covers() => blueprint.TasksByCell[center].Where(t => t.Type == RepairTaskType.AnchoredEntity).OrderBy(t => t.RequiredMatchingCount).ToArray();
                Assert.That(Floor().State, Is.EqualTo(RepairTaskState.Missing));
                Assert.That(Covers(), Has.Length.EqualTo(2));
                Assert.That(Covers().All(t => t.State == RepairTaskState.Missing), Is.True);
                var compatibleTile = server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId;
                maps.SetTile(grid, target.Comp, center, new Tile(compatibleTile));
                validation.RevalidateAll(grid);
                Assert.That(Floor().State, Is.EqualTo(RepairTaskState.Correct));
                Assert.That(Covers().All(t => t.State == RepairTaskState.Missing && !t.Waived), Is.True);
                Assert.That(blueprint.CanComplete, Is.False, "Restoring only the base tile does not restore its coverings.");
                maps.SetTile(grid, target.Comp, center, Tile.Empty);
                validation.RevalidateAll(grid);
                // Anchored CarpetBase coverings cannot be restored over empty space in this engine.
                var unsupported = server.EntMan.SpawnEntity("Carpet", new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));
                Assert.That(server.System<SharedTransformSystem>().AnchorEntity(unsupported), Is.False);
                server.EntMan.DeleteEntity(unsupported);
                var firstId = Covers()[0].RequirementId;
                var secondId = Covers()[1].RequirementId;
                Assert.That(firstId, Is.Not.EqualTo(secondId));
                Assert.That(validation.TrySetTechnicalExclusion(grid, 999, firstId, false, out _), Is.False);
                Assert.That(validation.TrySetTechnicalExclusion(grid, 1, int.MaxValue, false, out _), Is.False);
                Assert.That(validation.TrySetTechnicalExclusion(grid, 1, firstId, false, out _), Is.True);
                Assert.That(validation.TrySetTechnicalExclusion(grid, 1, firstId, false, out _), Is.False);
                Assert.That(validation.TrySetTechnicalExclusion(grid, 1, secondId, false, out _), Is.True);
                Assert.That(active.Exclusions!.Totals.Count, Is.EqualTo(2));
                Assert.That(validation.TryRevalidateForCompletion(grid, out var complete), Is.True);
                Assert.That(complete, Is.False, "Waiving coverings must not waive the missing base floor.");
                var intactTask = blueprint.TasksByCell[new Vector2i(4, 4)].Single();
                Assert.That(validation.TrySetTechnicalExclusion(grid, 1, intactTask.RequirementId, false, out _), Is.False);
                var tiles = server.ResolveDependency<ITileDefinitionManager>();
                maps.SetTiles(grid, target.Comp, removedTiles.Select(c => (c, new Tile(tiles["FloorSteel"].TileId))).ToList());
                validation.RevalidateAll(grid);
                Assert.That(Floor().State, Is.EqualTo(RepairTaskState.Correct));
                Assert.That(Floor().ExpectedTilePrototype, Is.EqualTo("FloorWhite"), "Keep original analyzer visuals.");
                Assert.That(Covers().All(t => t.State == RepairTaskState.Missing && t.Waived), Is.True);
                var transform = server.System<SharedTransformSystem>();
                // The third covering is a different cell and must still be repaired.
                var other = server.EntMan.SpawnEntity("Carpet", new EntityCoordinates(grid, new Vector2(3.5f, 1.5f)));
                transform.AnchorEntity(other);
                Assert.That(validation.TryRevalidateForCompletion(grid, out complete), Is.True);
                Assert.That(complete, Is.True);
                Assert.That(blueprint.FullyMatchesTarget, Is.False, "Waived is not physically Correct.");
                var rawBefore = active.CurrentPoints;
                var wrong = server.EntMan.SpawnEntity("CarpetBlack", new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));
                transform.AnchorEntity(wrong);
                validation.RevalidateAll(grid);
                Assert.That(Floor().State, Is.EqualTo(RepairTaskState.Correct));
                Assert.That(Covers().All(t => t.State != RepairTaskState.Correct), Is.True);
                Assert.That(blueprint.CanComplete, Is.False, "An unexpected covering is a separate unfinished removal task.");
                server.EntMan.DeleteEntity(wrong);
                for (var i = 0; i < 2; i++)
                {
                    var restored = server.EntMan.SpawnEntity("Carpet", new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));
                    transform.AnchorEntity(restored);
                }
                validation.RevalidateAll(grid);
                Assert.That(Covers().All(t => t.State == RepairTaskState.Correct && t.Waived), Is.True);
                Assert.That(active.CurrentPoints, Is.EqualTo(rawBefore), "Restored waived requirements still earn no points.");
                var frozen = active.Exclusions!;
                Assert.That(blueprint.MaxPoints, Is.EqualTo(initialMax));
                Assert.That(blueprint.MaxWaivedPoints, Is.EqualTo(limit));
                state.Completing = true;
                Assert.That(validation.TrySetTechnicalExclusion(grid, 1, firstId, true, out _), Is.False);
                state.Completing = false;
                active.ExpirationFrozen = true;
                Assert.That(validation.TrySetTechnicalExclusion(grid, 1, firstId, true, out _), Is.False);
                active.ExpirationFrozen = false;
                var deadline = active.ExpiresAt;
                active.ExpiresAt = TimeSpan.Zero;
                Assert.That(validation.TrySetTechnicalExclusion(grid, 1, firstId, true, out _), Is.False);
                active.ExpiresAt = deadline;
                Assert.That(validation.TrySetTechnicalExclusion(grid, 1, firstId, true, out _), Is.True);
                Assert.That(active.Exclusions!.Totals.PenaltyPercent, Is.EqualTo(1));
                Assert.That(validation.TrySetTechnicalExclusion(grid, 1, secondId, true, out _), Is.True);
                Assert.That(active.Exclusions!.Totals.PenaltyPercent, Is.Zero);
                Assert.That(frozen.Totals.Count, Is.EqualTo(2), "Previously captured terminal/report data is immutable.");
                Assert.That(active.FinalPoints, Is.EqualTo(initialMax));
                Assert.That(blueprint.FullyMatchesTarget, Is.True);
                var paper = server.EntMan.SpawnEntity("Paper", new EntityCoordinates(grid, Vector2.Zero));
                var paperComp = server.EntMan.GetComponent<PaperComponent>(paper);
                Assert.That(server.System<RepairOrderReportSystem>().StampReport((paper, paperComp)), Is.True);
                Assert.That(paperComp.StampedBy.Single().StampedName, Is.EqualTo("stamp-component-stamped-name-centcom"));
                Assert.That(paperComp.StampedBy.Single().StampTexture, Is.EqualTo("/Textures/Interface/Stamps/centralcommand_print.png"));
                // Exercise the terminal commit with actual nonzero exclusions and a penalized budget.
                foreach (var child in server.EntMan.EntityQuery<TransformComponent>().Where(x => x.ParentUid == grid &&
                    x.LocalPosition == new Vector2(1.5f, 1.5f)).Select(x => x.Owner).ToArray())
                    server.EntMan.DeleteEntity(child);
                validation.RevalidateAll(grid);
                Assert.That(validation.TrySetTechnicalExclusion(grid, 1, firstId, false, out _), Is.True);
                Assert.That(validation.TrySetTechnicalExclusion(grid, 1, secondId, false, out _), Is.True);
                var terminalInfo = active.Exclusions!;
                var budget = RepairOrderRewardBudget.ForSuccessfulCompletion(active.FinalPoints);
                Assert.That(budget, Is.EqualTo((int) ((long) active.CurrentPoints * 98 / 100)));
                var completed = new CompletedRepairOrder(active.RuntimeId, active.Prototype, active.CompletedTasks,
                    active.TotalTasks, active.FinalPoints, active.MaxPoints, budget, RepairOrderResult.Completed,
                    false, null, Array.Empty<RepairOrderRewardResult>());
                Assert.That(server.System<RepairOrderSystem>().TryCommitCompletion(station, active, completed, out _), Is.True);
                Assert.That(completed.Exclusions, Is.SameAs(terminalInfo));
                Assert.That(completed.Exclusions!.Totals.Count, Is.EqualTo(2));
                Assert.That(completed.FinalPoints, Is.EqualTo(completed.Exclusions.Totals.FinalPoints));
            }
            finally
            {
                state.Active = null;
                if (grid.IsValid()) validation.DiscardPreparedSession(grid);
                maps.DeleteMap(mapId);
                server.EntMan.DeleteEntity(station);
            }
        });
        await pair.CleanReturnAsync();
    }
}
