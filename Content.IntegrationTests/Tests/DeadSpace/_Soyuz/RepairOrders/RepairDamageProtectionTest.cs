// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.RepairOrders;

[TestFixture]
public sealed class RepairDamageProtectionTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: RepairProtectionFutureVending
          parent: VendingMachineBooze
        - type: repairValueGroup
          id: RepairProtectionFutureVendingValue
          tag: RepairFurniture
          entities: [RepairProtectionFutureVending]
        - type: repairDamageEvent
          id: RepairProtectionGenericRemove
          name: repair-damage-smallexplosion-name
          description: repair-damage-smallexplosion-description
          weight: 1
          severity: Light
          placement: AnyOccupied
          stages:
          - shape: Disc
            minRadius: 30
            maxRadius: 30
            operations:
            - type: RemoveAnchoredEntity
              chance: 1
        """;

    [TestCase("RepairDamageMeteorImpact")]
    [TestCase("RepairDamageHeavyMeteorImpact")]
    [TestCase("RepairDamageSmallExplosion")]
    [TestCase("RepairDamageLargeExplosion")]
    [TestCase("RepairDamageChainExplosions")]
    [TestCase("RepairDamageMeteorShower")]
    [TestCase("RepairDamageHullBreach")]
    [TestCase("RepairProtectionGenericRemove")]
    public async Task DestructionCannotRemoveProtectedEntitiesOrTheirSupport(string eventId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var transform = server.System<SharedTransformSystem>();
            var protection = server.System<RepairDamageProtectionSystem>();
            protection.ValidateConfiguration();
            var steel = server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId;
            var cells = Enumerable.Range(0, 11).SelectMany(x => Enumerable.Range(0, 11).Select(y => new Vector2i(x, y))).ToArray();
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, cells.Select(c => (c, new Tile(steel))).ToList());
            var protectedIds = new[]
            {
                "VendingMachineBooze", "RepairProtectionFutureVending", "BoozeDispenser", "PosterLegitSafetyInternals",
                "ButtonFrameCaution", "SignalButton", "TwoWayLever", "ShuttleGunSvalinnMachineGun",
                "WeaponTurretSyndicate", "WeaponEnergyTurretAI", "WeaponTurretSyndicateBroken",
                "PipeShuttleCallButton", "PipeShuttleModeButton",
            };
            var protectedEntities = new List<EntityUid>();
            foreach (var id in protectedIds)
            {
                var prototype = server.ProtoMan.Index<EntityPrototype>(id);
                Assert.That(protection.CanProcedurallyDamage(prototype), Is.False, id);
                var uid = server.EntMan.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, new Vector2(5.5f, 5.5f)));
                transform.AnchorEntity(uid);
                Assert.That(server.EntMan.GetComponent<TransformComponent>(uid).Anchored, Is.True, id);
                protectedEntities.Add(uid);
            }
            var machines = new List<EntityUid>();
            foreach (var cell in cells.Where(c => c != new Vector2i(5, 5)))
            {
                var boundary = cell.X == 0 || cell.Y == 0 || cell.X == 10 || cell.Y == 10;
                var uid = server.EntMan.SpawnEntity(boundary ? "WallSolid" : "Autolathe",
                    new EntityCoordinates(map.Grid.Owner, new Vector2(cell.X + .5f, cell.Y + .5f)));
                transform.AnchorEntity(uid);
                if (!boundary) machines.Add(uid);
            }
            var damage = server.System<RepairOrderDamageSystem>();
            var order = server.ProtoMan.Index<RepairOrderPrototype>("RepairOrderDamagedCargoShuttle");
            var snapshot = damage.Snapshot(map.Grid, order);
            Assert.That(snapshot.ProtectedFloors, Does.Contain(new Vector2i(5, 5)));
            var profile = new RepairDamageProfilePrototype
            {
                Events = new() { eventId }, MinEvents = 1, MaxEvents = 1, MaxGenerationAttempts = 40, MaxEventRolls = 40,
                MinDamageFraction = .00001f, MaxDamageFraction = .99f, MaxRemovedFloorFraction = .5f,
                MaxRemovedAnchoredEntityFraction = 1, MinRemainingFloor = 4, MinChangedRequirements = 1,
                MinimumEventSeparation = 0,
            };
            Assert.That(damage.TryGeneratePlan(snapshot, profile, 12345, out var plan, out var reason), Is.True, reason);
            Assert.That(plan.RemovedEntities.All(i => !protectedEntities.Contains(snapshot.Entities[i].Uid)), Is.True);
            Assert.That(plan.RemovedTiles, Does.Not.Contain(new Vector2i(5, 5)));
            if (eventId == "RepairProtectionGenericRemove")
                Assert.That(plan.RemovedEntities.Any(i => machines.Contains(snapshot.Entities[i].Uid)), Is.True);
            damage.ApplyPlan(map.Grid, snapshot, profile, plan);
            foreach (var uid in protectedEntities)
            {
                Assert.That(server.EntMan.EntityExists(uid), Is.True);
                var xform = server.EntMan.GetComponent<TransformComponent>(uid);
                Assert.That(xform.Anchored, Is.True);
                Assert.That(xform.ParentUid, Is.EqualTo(map.Grid.Owner));
            }
            Assert.That(maps.GetTileRef(map.Grid.Owner, map.Grid.Comp, new Vector2i(5, 5)).Tile.IsEmpty, Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProductionCoveringsAlwaysRetainIndependentBaseTiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var validation = server.System<RepairOrderValidationSystem>();
            foreach (var order in server.ProtoMan.EnumeratePrototypes<RepairOrderPrototype>().Where(o => !pair.IsTestPrototype(o)))
            {
                var map = maps.CreateMap(out var mapId);
                maps.SetPaused(map, true);
                var station = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
                server.EntMan.AddComponent<RepairOrderStationComponent>(station);
                EntityUid grid = default;
                try
                {
                    Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, order.TargetGridPath, out var loaded), Is.True);
                    grid = loaded!.Value.Owner;
                    Assert.That(validation.TryPrepareSession(station, 1, order.ID, grid, out _), Is.True);
                    var blueprint = server.EntMan.GetComponent<RepairBlueprintComponent>(grid);
                    var cells = new HashSet<Vector2i>();
                    var children = server.EntMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
                    while (children.MoveNext(out var child))
                    {
                        var xform = server.EntMan.GetComponent<TransformComponent>(child);
                        var proto = server.EntMan.GetComponent<MetaDataComponent>(child).EntityPrototype;
                        if (!xform.Anchored || proto == null || !server.ProtoMan.EnumerateAllParents<EntityPrototype>(proto.ID, true).Any(p => p.id == "CarpetBase")) continue;
                        var cell = new Vector2i((int) MathF.Floor(xform.LocalPosition.X / loaded.Value.Comp.TileSize),
                            (int) MathF.Floor(xform.LocalPosition.Y / loaded.Value.Comp.TileSize));
                        if (!maps.GetTileRef(grid, loaded.Value.Comp, cell).Tile.IsEmpty) cells.Add(cell);
                    }
                    var covered = cells.Count(c => blueprint.ExpectedCells[c].Tile != null &&
                        blueprint.TasksByCell[c].Any(t => t.Type == RepairTaskType.Tile) &&
                        blueprint.TasksByCell[c].Any(t => t.Type == RepairTaskType.AnchoredEntity));
                    TestContext.Out.WriteLine($"{order.ID}: target cells with covering={cells.Count}; covering and base tile requirement={covered}");
                    Assert.That(covered, Is.EqualTo(cells.Count));
                }
                finally
                {
                    if (grid.IsValid()) validation.DiscardPreparedSession(grid);
                    maps.DeleteMap(mapId);
                    server.EntMan.DeleteEntity(station);
                }
            }
        });
        await pair.CleanReturnAsync();
    }
}
