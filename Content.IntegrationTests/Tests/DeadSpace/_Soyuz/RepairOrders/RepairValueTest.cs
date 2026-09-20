// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Collections.Generic;
using System.Linq;
using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Maps;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.RepairOrders;

[TestFixture]
public sealed class RepairValueTest
{
    // This ID is loaded by TestPrototypes, not by the production YAML directory validator.
    private static EntProtoId UnknownStructure => "RepairOrderTestUnknownStructure";

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: RepairOrderTestUnknownStructure
          components:
          - type: Transform
            anchored: true
          - type: Physics
            bodyType: Static
        - type: repairOrder
          id: RepairValueTestMiniWreck
          name: repair-order-damaged-cargo-shuttle-name
          description: repair-order-damaged-cargo-shuttle-description
          objectType: repair-order-object-type-small-shuttle
          objectName: repair-order-object-name-dinero-mk2
          difficulty: 99
          targetGridPath: /Maps/_Soyuz/RepairOrders/mini_wreck_target.yml
          damageProfile: RepairDamageLight
          weight: 0
          scoreProfile: RepairOrderEngineeringScore
          rewardPool: RepairOrderEngineeringRewardPool
          repairTime: 20m
        - type: repairOrder
          id: RepairValueTestFloorTraining
          name: repair-order-damaged-cargo-shuttle-name
          description: repair-order-damaged-cargo-shuttle-description
          objectType: repair-order-object-type-small-shuttle
          objectName: repair-order-object-name-dinero-mk2
          difficulty: 99
          targetGridPath: /Maps/_Soyuz/RepairOrders/floor_training_target.yml
          damageProfile: RepairDamageLight
          weight: 0
          scoreProfile: RepairOrderEngineeringScore
          rewardPool: RepairOrderEngineeringRewardPool
          repairTime: 20m
        """;

    [Test]
    public async Task ValueProductionCoverage()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ProtoMan;
            var catalog = RepairValueCatalog.Build(prototypes);
            var report = catalog.Scan(prototypes.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !pair.IsTestPrototype(p)));
            TestContext.Out.WriteLine(report);
            Assert.Multiple(() =>
            {
                Assert.That(catalog.Errors, Is.Empty, string.Join("\n", catalog.Errors));
                Assert.That(report.Candidates, Is.GreaterThan(0));
                Assert.That(report.Unclassified, Is.Empty, report.ToString());
                foreach (var profile in prototypes.EnumeratePrototypes<RepairScoreProfilePrototype>())
                    Assert.That(profile.FloorTilePoints, Is.GreaterThan(0), profile.ID);
            });

            // A future anchored structure cannot silently receive a price or disappear from coverage.
            var unknown = prototypes.Index(UnknownStructure);
            Assert.That(catalog.Scan(new[] { unknown }).Unclassified,
                Is.EqualTo(new[] { unknown.ID }));
            Assert.That(catalog.TryResolve(unknown.ID, out _), Is.False);

            var tags = prototypes.EnumeratePrototypes<RepairValueTagPrototype>().ToDictionary(p => p.ID);
            var overrides = prototypes.EnumeratePrototypes<RepairValueOverridePrototype>()
                .ToDictionary(p => p.Entity.Id, p => p.Value);
            foreach (var group in prototypes.EnumeratePrototypes<RepairValueGroupPrototype>())
            {
                foreach (var entity in group.Entities)
                {
                    Assert.That(catalog.TryResolve(entity.Id, out var value), Is.True, entity.Id);
                    Assert.That(value, Is.EqualTo(overrides.GetValueOrDefault(entity.Id, tags[group.Tag.Id].Value)), entity.Id);
                }
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ValueInvalidConfiguration()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var prototypes = pair.Server.ProtoMan;
            var tags = prototypes.EnumeratePrototypes<RepairValueTagPrototype>().ToArray();
            var tag = tags.First();
            const string entity = "RepairOrderTestUnknownStructure";
            var serialization = pair.Server.ResolveDependency<ISerializationManager>();
            // Copy manager-loaded prototypes so invalid fixtures never modify the shared catalog.
            RepairValueGroupPrototype Group(ProtoId<RepairValueTagPrototype> category, params EntProtoId[] entities)
            {
                var group = serialization.CreateCopy(prototypes.EnumeratePrototypes<RepairValueGroupPrototype>().First());
                group.Tag = category;
                group.Entities = entities.ToList();
                return group;
            }

            RepairValueExclusionPrototype Exclusion(string reason, params EntProtoId[] entities)
            {
                var result = serialization.CreateCopy(prototypes.EnumeratePrototypes<RepairValueExclusionPrototype>().First());
                result.Reason = reason;
                result.Entities = entities.ToList();
                return result;
            }

            RepairValueOverridePrototype Override(int value)
            {
                var result = serialization.CreateCopy(prototypes.EnumeratePrototypes<RepairValueOverridePrototype>().First());
                result.Entity = entity;
                result.Value = value;
                return result;
            }

            var known = Group(tag.ID, entity);
            var exclusion = Exclusion("Test helper", entity);
            RepairValueCatalog Build(RepairValueGroupPrototype[] groups,
                RepairValueOverridePrototype[] overrides = null,
                RepairValueExclusionPrototype[] exclusions = null)
                => RepairValueCatalog.Build(prototypes, tags, groups,
                    overrides ?? Array.Empty<RepairValueOverridePrototype>(),
                    exclusions ?? Array.Empty<RepairValueExclusionPrototype>());

            var valid = Build(new[] { known });
            Assert.That(valid.Errors, Is.Empty);
            Assert.That(valid.TryResolve(entity, out var points), Is.True);
            Assert.That(points, Is.EqualTo(tag.Value));

            void Invalid(RepairValueCatalog catalog, string diagnostic)
            {
                Assert.That(string.Join("\n", catalog.Errors), Does.Contain(diagnostic));
                Assert.That(catalog.TryResolve(entity, out _), Is.False, "Invalid data must never use first-entry wins.");
            }

            Invalid(Build(new[] { known, known }), "duplicate classification");
            Invalid(Build(new[] { Group(tag.ID, entity, entity) }), "duplicate classification");
            Invalid(Build(new[] { Group(tag.ID) }), "empty group");
            Invalid(Build(new[] { Group("MissingRepairValueTag", entity) }), "missing RepairValueTag");
            Invalid(Build(new[] { Group(tag.ID, "RemovedRepairValueEntity") }), "missing EntityPrototype");
            Invalid(Build(new[] { known }, exclusions: new[] { exclusion }), "classified AND excluded");
            Invalid(Build(Array.Empty<RepairValueGroupPrototype>(), exclusions: new[]
            {
                Exclusion("Invalid reference", "RemovedRepairValueEntity"),
            }), "missing EntityPrototype");

            var exact = Override(123);
            var overridden = Build(new[] { known }, new[] { exact });
            Assert.That(overridden.TryResolve(entity, out points), Is.True);
            Assert.That(points, Is.EqualTo(123));
            Invalid(Build(new[] { known }, new[] { exact, exact }), "duplicate override");
            foreach (var value in new[] { 0, -1 })
            {
                Invalid(Build(new[] { known }, new[] { Override(value) }), "must be positive");
                var badTag = serialization.CreateCopy(tag);
                badTag.Value = value;
                var badTags = RepairValueCatalog.Build(prototypes, new[] { badTag },
                    Array.Empty<RepairValueGroupPrototype>(), Array.Empty<RepairValueOverridePrototype>(),
                    Array.Empty<RepairValueExclusionPrototype>());
                Assert.That(string.Join("\n", badTags.Errors), Does.Contain("must be positive"));
            }

            var excluded = Build(Array.Empty<RepairValueGroupPrototype>(), exclusions: new[] { exclusion });
            Assert.That(excluded.Errors, Is.Empty);
            Assert.That(excluded.IsExcluded(entity), Is.True);
            Assert.That(excluded.Scan(new[] { prototypes.Index(UnknownStructure) }).Unclassified, Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ValueAllTileDefinitions()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var definitions = pair.Server.ResolveDependency<ITileDefinitionManager>();
            var steel = definitions[RepairValueCatalog.StandardFloor.Id].TileId;
            Assert.That(steel, Is.Not.EqualTo(Tile.Empty.TypeId));
            foreach (var tile in definitions.Cast<ContentTileDefinition>())
            {
                var canonical = RepairValueCatalog.CanonicalizeTile(tile.TileId, definitions);
                if (tile.TileId == Tile.Empty.TypeId)
                    Assert.That(canonical, Is.EqualTo(Tile.Empty.TypeId), tile.ID);
                else if (tile.IsSubFloor)
                    Assert.That(canonical, Is.Not.EqualTo(steel), $"{tile.ID}: a support layer is not a finished floor");
                else
                    Assert.That(canonical, Is.EqualTo(steel), tile.ID);
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("FloorGlass", "FloorSteel", true)]
    [TestCase("FloorGlass", "FloorWood", true)]
    [TestCase("FloorSteel", "Plating", false)]
    [TestCase("FloorSteel", "Lattice", false)]
    [TestCase("Plating", "Lattice", false)]
    [TestCase("Plating", "PlatingDamaged", true)]
    [TestCase("Lattice", "TrainLattice", true)]
    [TestCase("FloorSteel", "FloorSteel", true)]
    [TestCase("FloorSteel", "FloorWood", true)]
    [TestCase("FloorWood", "FloorSteel", true)]
    [TestCase("FloorCarpetOffice", "FloorFreezer", true)]
    [TestCase("FloorSteel", "Space", false)]
    [TestCase("Space", "FloorWood", false)]
    [TestCase("Space", "Space", true)]
    public async Task ValueTileIdentity(string target, string actual, bool matches)
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var definitions = pair.Server.ResolveDependency<ITileDefinitionManager>();
            var expected = new Tile(definitions[target].TileId);
            var current = new Tile(definitions[actual].TileId, 0, 0, 1);
            Assert.That(RepairValueCatalog.CanonicalizeTile(expected.TypeId, definitions) ==
                        RepairValueCatalog.CanonicalizeTile(current.TypeId, definitions), Is.EqualTo(matches));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ValueRepairMapsBuildBlueprints()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var loader = server.System<MapLoaderSystem>();
            var validation = server.System<RepairOrderValidationSystem>();
            var catalog = RepairValueCatalog.Build(server.ProtoMan);
            var orders = server.ProtoMan.EnumeratePrototypes<RepairOrderPrototype>().ToArray();
            var referencedPaths = orders.Select(order => order.TargetGridPath).ToHashSet();
            var resources = server.ResolveDependency<IResourceManager>();
            foreach (var path in resources.ContentFindFiles(new ResPath("/Maps/_Soyuz/RepairOrders"))
                         .Where(path => path.Extension == "yml"))
                Assert.That(referencedPaths, Does.Contain(path), $"Repair map {path} needs a blueprint test order.");

            foreach (var order in orders)
            foreach (var path in new[] { order.TargetGridPath })
            {
                var station = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
                server.EntMan.AddComponent<RepairOrderStationComponent>(station);
                maps.CreateMap(out var mapId);
                EntityUid grid = default;
                try
                {
                    Assert.That(loader.TryLoadGrid(mapId, path, out var loaded), Is.True, path.ToString());
                    grid = loaded!.Value.Owner;
                    Assert.That(validation.TryPrepareSession(station, 1, order.ID, grid, out var session), Is.True, $"{order.ID}: {path}");
                    var blueprint = server.EntMan.GetComponent<RepairBlueprintComponent>(grid);
                    Assert.That(blueprint.Ready, Is.True);
                    Assert.That(session.CurrentPoints, Is.Zero);
                    Assert.That(session.MaxPoints, Is.GreaterThanOrEqualTo(0));
                    if (path == order.TargetGridPath)
                    {
                        Assert.That(blueprint.FullyMatchesTarget, Is.True);
                        Assert.That(session.MaxPoints, Is.Zero, "Initially correct targets do not award free points.");
                    }
                    if (order.ID == "RepairValueTestFloorTraining" && path == order.TargetGridPath)
                    {
                        var definitions = server.ResolveDependency<ITileDefinitionManager>();
                        var cell = blueprint.ExpectedCells.Where(c => c.Value.Tiles.ContainsKey(RepairTileLayer.Floor))
                            .OrderBy(c => c.Key.X).ThenBy(c => c.Key.Y).First();
                        var expectedTile = cell.Value.Tile!;
                        var originalId = expectedTile.TileId;
                        var originalPrototype = expectedTile.TilePrototype;
                        maps.SetTile(grid, loaded.Value.Comp, cell.Key, Tile.Empty);
                        Assert.That(validation.RevalidateAll(grid), Is.True);
                        var layerPoints = cell.Value.Tiles.Values.Sum(t => t.Points);
                        var task = blueprint.TasksByCell[cell.Key].Single(t => t.TileLayer == RepairTileLayer.Floor);
                        Assert.That(blueprint.TasksByCell[cell.Key].Select(t => t.RequirementId).Distinct().Count(), Is.EqualTo(3));
                        Assert.That(blueprint.TasksByCell[cell.Key].All(t => t.State == RepairTaskState.Missing), Is.True);
                        Assert.That(task.State, Is.EqualTo(RepairTaskState.Missing));
                        Assert.That(task.ExpectedTileId, Is.EqualTo(originalId));
                        Assert.That(task.ExpectedTilePrototype, Is.EqualTo(originalPrototype), "Analyzer uses the original tile.");
                        Assert.That(blueprint.CurrentPoints, Is.EqualTo(-layerPoints));

                        maps.SetTile(grid, loaded.Value.Comp, cell.Key, new Tile(definitions["Lattice"].TileId));
                        Assert.That(validation.RevalidateAll(grid), Is.True);
                        Assert.That(blueprint.TasksByCell[cell.Key].Count(t => t.State == RepairTaskState.Correct), Is.EqualTo(1));
                        maps.SetTile(grid, loaded.Value.Comp, cell.Key, new Tile(definitions["Plating"].TileId));
                        Assert.That(validation.RevalidateAll(grid), Is.True);
                        Assert.That(blueprint.TasksByCell[cell.Key].Count(t => t.State == RepairTaskState.Correct), Is.EqualTo(2));
                        Assert.That(blueprint.TasksByCell[cell.Key].Single(t => t.TileLayer == RepairTileLayer.Floor).State,
                            Is.EqualTo(RepairTaskState.Missing));
                        Assert.That(blueprint.CurrentPoints, Is.EqualTo(-expectedTile.Points));

                        maps.SetTile(grid, loaded.Value.Comp, cell.Key, new Tile(definitions["FloorWood"].TileId));
                        Assert.That(validation.RevalidateAll(grid), Is.True);
                        Assert.That(blueprint.FullyMatchesTarget, Is.True);
                        Assert.That(blueprint.CurrentPoints, Is.Zero);

                        var extraCell = cell.Key + new Robust.Shared.Maths.Vector2i(-1, 0);
                        maps.SetTile(grid, loaded.Value.Comp, extraCell, new Tile(definitions["FloorFreezer"].TileId));
                        Assert.That(validation.RevalidateAll(grid), Is.True);
                        Assert.That(blueprint.TasksByCell[extraCell], Has.Count.EqualTo(3));
                        Assert.That(blueprint.TasksByCell[extraCell].All(t => t.State == RepairTaskState.Wrong), Is.True);
                        Assert.That(blueprint.CurrentPoints, Is.EqualTo(-layerPoints));
                        maps.SetTile(grid, loaded.Value.Comp, extraCell, Tile.Empty);
                        Assert.That(validation.RevalidateAll(grid), Is.True);
                        Assert.That(blueprint.FullyMatchesTarget, Is.True);
                        Assert.That(blueprint.CurrentPoints, Is.Zero);
                    }
                    foreach (var expected in blueprint.ExpectedCells.Values.SelectMany(c => c.Entities))
                    {
                        Assert.That(catalog.TryResolve(expected.Signature.ValuePrototype!, out var points), Is.True);
                        Assert.That(expected.Points, Is.EqualTo(points));
                    }
                    foreach (var unexpected in blueprint.UnexpectedBaselineCells.Values.SelectMany(c => c.Entities))
                    {
                        Assert.That(catalog.TryResolve(unexpected.Signature.ValuePrototype!, out var points), Is.True);
                        Assert.That(unexpected.Points, Is.EqualTo(points));
                    }
                }
                finally
                {
                    if (grid.IsValid())
                        validation.DiscardPreparedSession(grid);
                    maps.DeleteMap(mapId);
                    server.EntMan.DeleteEntity(station);
                }
            }
        });
        await pair.CleanReturnAsync();
    }
}
