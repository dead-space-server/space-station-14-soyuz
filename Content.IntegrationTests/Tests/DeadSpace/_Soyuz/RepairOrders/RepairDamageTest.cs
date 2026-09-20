// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.RepairOrders;

[TestFixture]
public sealed class RepairDamageTest
{
    [Test]
    public async Task ProductionSeedMatrixAndRepairBaseline()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var damage = server.System<RepairOrderDamageSystem>();
            var loader = server.System<MapLoaderSystem>();
            var maps = server.System<SharedMapSystem>();
            var validation = server.System<RepairOrderValidationSystem>();
            var orders = server.ProtoMan.EnumeratePrototypes<RepairOrderPrototype>()
                .Where(p => !pair.IsTestPrototype(p)).OrderBy(p => p.ID).ToArray();
            Assert.That(orders, Is.Not.Empty);
            foreach (var order in orders)
            {
                var signatures = new HashSet<string>();
                foreach (var seed in new[] { 1, 2, 3, 12345, 987654 })
                {
                    var mapUid = maps.CreateMap(out var mapId);
                    maps.SetPaused(mapUid, true);
                    EntityUid grid = default;
                    var station = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
                    server.EntMan.AddComponent<RepairOrderStationComponent>(station);
                    try
                    {
                        Assert.That(loader.TryLoadGrid(mapId, order.TargetGridPath, out var loaded), Is.True, order.ID);
                        grid = loaded!.Value.Owner;
                        var profile = server.ProtoMan.Index(order.DamageProfile);
                        var snapshot = damage.Snapshot(loaded.Value, order);
                        Assert.That(damage.TryGeneratePlan(snapshot, profile, seed, out var plan, out var rejection), Is.True,
                            $"{order.ID}, seed {seed}: {rejection}");
                        // Repeat on a fresh load: runtime UIDs and enumeration order may differ.
                        var secondMapUid = maps.CreateMap(out var secondMap);
                        maps.SetPaused(secondMapUid, true);
                        try
                        {
                            Assert.That(loader.TryLoadGrid(secondMap, order.TargetGridPath, out var other), Is.True);
                            var second = damage.Snapshot(other!.Value, order);
                            Assert.That(damage.TryGeneratePlan(second, profile, seed, out var repeated, out rejection), Is.True, rejection);
                            Assert.That(Signature(second, repeated), Is.EqualTo(Signature(snapshot, plan)));
                        }
                        finally { maps.DeleteMap(secondMap); }
                        signatures.Add(Signature(snapshot, plan));
                        TestContext.Out.WriteLine($"{order.ID}; seed={seed}; events={string.Join(',', plan.Events.Select(e => e.Event.Id))}; " +
                            $"tiles={plan.RemovedTiles.Length}; entities={plan.RemovedEntities.Length}; fraction={plan.DamageFraction}");
                        var info = plan.ToInfo();
                        damage.ApplyPlan(loaded.Value, snapshot, profile, plan);
                        Assert.That(validation.TryPrepareSession(station, seed, order.ID, grid, out var active), Is.True);
                        active.DamageGeneration = info;
                        var blueprint = server.EntMan.GetComponent<RepairBlueprintComponent>(grid);
                        Assert.That(blueprint.TotalTasks, Is.GreaterThan(0));
                        Assert.That(blueprint.MaxPoints, Is.EqualTo(plan.DamageValue));
                        Assert.That(blueprint.CurrentPoints, Is.Zero);
                        Assert.That(blueprint.FullyMatchesTarget, Is.False);
                        // Restore only damage chosen by the plan. The original blueprint, not DamageSystem,
                        // owns the tasks and credits these initial losses as positive repair work.
                        maps.SetTiles(grid, loaded.Value.Comp, plan.RemovedTiles.Select(c => (c, new Tile((ushort) blueprint.ExpectedCells[c].Tile!.TileId))).ToList());
                        var transform = server.System<SharedTransformSystem>();
                        foreach (var index in plan.RemovedEntities)
                        {
                            var entity = snapshot.Entities[index];
                            var restored = server.EntMan.SpawnEntity(entity.Prototype, new EntityCoordinates(grid, entity.Position));
                            transform.SetLocalRotation(restored, entity.Rotation);
                            transform.AnchorEntity(restored);
                        }
                        Assert.That(validation.RevalidateAll(grid), Is.True);
                        Assert.That(blueprint.FullyMatchesTarget, Is.True);
                        Assert.That(blueprint.CurrentPoints, Is.EqualTo(plan.DamageValue));
                        Assert.That(active.DamageGeneration, Is.SameAs(info), "Repair progress must not rewrite diagnostic history.");
                    }
                    finally
                    {
                        if (grid.IsValid()) validation.DiscardPreparedSession(grid);
                        maps.DeleteMap(mapId);
                        server.EntMan.DeleteEntity(station);
                    }
                }
                // This is a fixed matrix, not a probabilistic assertion over newly randomized test seeds.
                Assert.That(signatures.Count, Is.GreaterThan(1), order.ID);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AllEventFamiliesAndConfiguration()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            Assert.That(RepairDamageConfiguration.Validate(server.ProtoMan), Is.Empty);
            var damage = server.System<RepairOrderDamageSystem>();
            var events = server.ProtoMan.EnumeratePrototypes<RepairDamageEventPrototype>()
                .Where(ev => !pair.IsTestPrototype(ev)).ToArray();
            Assert.That(events, Has.Length.EqualTo(40));
            var floors = Enumerable.Range(0, 21).SelectMany(x => Enumerable.Range(0, 21).Select(y => new Vector2i(x, y))).ToImmutableArray();
            var entities = new List<RepairDamageEntity>();
            var categories = server.ProtoMan.EnumeratePrototypes<RepairValueTagPrototype>().OrderBy(t => t.ID).ToArray();
            foreach (var tag in categories)
            {
                // Both boundary and interior equipment: all semantic families share a compact fixture.
                foreach (var cell in new[] { new Vector2i(0, 1 + entities.Count % 19), new Vector2i(10, 10), new Vector2i(11, 10) })
                    entities.Add(new RepairDamageEntity(entities.Count, EntityUid.Invalid, "Fixture", new Vector2(cell.X + .5f, cell.Y + .5f),
                        Angle.Zero, cell, tag.ID, tag.Value));
            }
            var snapshot = new RepairDamageSnapshot(EntityUid.Invalid, floors, entities.ToImmutableArray(), ImmutableArray<Vector2i>.Empty, 2);
            foreach (var ev in events)
            {
                Assert.That(Loc.TryGetString(ev.Name, out var name), Is.True, ev.ID);
                Assert.That(Loc.TryGetString(ev.Description, out var description), Is.True, ev.ID);
                Assert.That(name, Is.Not.Empty);
                Assert.That(description, Is.Not.Empty);
                Assert.That(damage.CanApply(snapshot, ev), Is.True, ev.ID);
                var profile = FixtureProfile(server.ProtoMan, server.ResolveDependency<ISerializationManager>(), ev.ID);
                Assert.That(damage.TryGeneratePlan(snapshot, profile, 12345, out var plan, out var rejection), Is.True, $"{ev.ID}: {rejection}");
                Assert.That(plan.Events, Has.Length.EqualTo(1));
                Assert.That(plan.Events[0].Event.Id, Is.EqualTo(ev.ID));
                Assert.That(plan.Events[0].ChangedRequirements, Is.GreaterThan(0));
                Assert.That(plan.Events[0].Centers.Length, Is.InRange(ev.MinCenters, ev.MaxCenters));
                Assert.That(plan.ToInfo().SelectedEvents, Is.EqualTo(new[] { ev.ID }));
                if (ev.Targets.Count > 0)
                    Assert.That(plan.RemovedEntities.Any(i => ev.Targets.Any(t => t.Id == snapshot.Entities[i].Category)), Is.True, ev.ID);
                if (ev.RequiresFloorRemoval)
                    Assert.That(plan.RemovedTiles, Is.Not.Empty, ev.ID);
                if (ev.Stages.SelectMany(s => s.Operations).Any(op => op.ConnectedTargets))
                    Assert.That(RepairOrderDamageSystem.IsConnected(plan.RemovedEntities.Select(i => snapshot.Entities[i].Cell).ToHashSet()), Is.True, ev.ID);
            }
            var withoutEngines = snapshot with { Entities = snapshot.Entities.Where(e => !e.Category.StartsWith("RepairEngine", StringComparison.Ordinal)).ToImmutableArray() };
            ProtoId<RepairDamageEventPrototype> engineFailure = "RepairDamageEngineFailure";
            Assert.That(damage.CanApply(withoutEngines, server.ProtoMan.Index(engineFailure)), Is.False);
            var duplicate = RepairDamageSummary.Format(server.ProtoMan,
                new[] { "RepairDamageMicroMeteorImpact", "RepairDamageMicroMeteorImpact" }, detailed: false);
            Assert.That(duplicate, Does.Contain("2"));
            Assert.That(duplicate.Split('•').Length, Is.EqualTo(2), "Only one grouped entry should be shown.");
            Assert.That(RepairDamageSummary.Format(server.ProtoMan, new[] { "NonexistentDamageEvent" }, true), Is.Not.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InvalidPlansAreRejectedBeforeMutation()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var profile = FixtureProfile(pair.Server.ProtoMan, pair.Server.ResolveDependency<ISerializationManager>(), "RepairDamageFloorCollapse");
            var floors = new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(2, 0), new Vector2i(3, 0), new Vector2i(4, 0) }.ToImmutableArray();
            var snapshot = new RepairDamageSnapshot(EntityUid.Invalid, floors, ImmutableArray<RepairDamageEntity>.Empty, ImmutableArray<Vector2i>.Empty, 2);
            var cell = new Vector2i(2, 0);
            var ev = new RepairDamageEventResult("RepairDamageFloorCollapse", ImmutableArray.Create(cell), ImmutableArray.Create(cell), 1);
            var split = RepairOrderDamageSystem.MakePlan(snapshot, 1, 0, new[] { cell }, Array.Empty<int>(), new[] { ev });
            Assert.That(RepairOrderDamageSystem.ValidatePlan(snapshot, profile, split, out var reason), Is.False);
            Assert.That(reason, Does.Contain("split"));
            var edge = floors[0];
            var plan = RepairOrderDamageSystem.MakePlan(snapshot, 1, 0, new[] { edge }, Array.Empty<int>(), new[] { ev with { Centers = ImmutableArray.Create(edge), AffectedCells = ImmutableArray.Create(edge) } });
            Assert.That(RepairOrderDamageSystem.ValidatePlan(snapshot, profile, plan, out reason), Is.True, reason);
            profile.MaxRemovedFloorFraction = .1f;
            Assert.That(RepairOrderDamageSystem.ValidatePlan(snapshot, profile, plan, out reason), Is.False);
            Assert.That(reason, Does.Contain("Floor removal cap"));
            profile.MaxRemovedFloorFraction = .9f;
            profile.MinDamageFraction = .5f;
            Assert.That(RepairOrderDamageSystem.ValidatePlan(snapshot, profile, plan, out reason), Is.False);
            Assert.That(reason, Does.Contain("Insufficient"));
            profile.MinDamageFraction = .01f;
            profile.MaxDamageFraction = .1f;
            Assert.That(RepairOrderDamageSystem.ValidatePlan(snapshot, profile, plan, out reason), Is.False);
            Assert.That(reason, Does.Contain("Maximum damage"));
            profile.MaxDamageFraction = .99f;
            profile.MinChangedRequirements = 2;
            Assert.That(RepairOrderDamageSystem.ValidatePlan(snapshot, profile, plan, out _), Is.False);
            profile.MinChangedRequirements = 1;
            Assert.That(RepairOrderDamageSystem.ValidatePlan(snapshot, profile, plan with { Events = ImmutableArray.Create(ev with { ChangedRequirements = 0 }) }, out _), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    private static RepairDamageProfilePrototype FixtureProfile(IPrototypeManager prototypes, ISerializationManager serialization, string ev)
    {
        ProtoId<RepairDamageProfilePrototype> profileId = "RepairDamageLight";
        var profile = serialization.CreateCopy(prototypes.Index(profileId));
        profile.Events = new() { ev };
        profile.MinEvents = 1;
        profile.MaxEvents = 1;
        profile.MaxGenerationAttempts = 30;
        profile.MaxEventRolls = 30;
        profile.MinDamageFraction = .00001f;
        profile.MaxDamageFraction = .99f;
        profile.MaxRemovedFloorFraction = .9f;
        profile.MaxRemovedAnchoredEntityFraction = .99f;
        profile.MinChangedRequirements = 1;
        profile.MinRemainingFloor = 2;
        profile.MinimumEventSeparation = 0;
        profile.MaxOccurrencesPerEvent = 1;
        profile.MaxSeverity = RepairDamageSeverity.Heavy;
        return profile;
    }

    private static string Signature(RepairDamageSnapshot snapshot, RepairOrderDamagePlan plan)
        => string.Join(";", plan.Events.Select(ev => ev.Event.Id + ":" + string.Join("/", ev.Centers))) +
           "|" + string.Join(";", plan.RemovedTiles) + "|" +
           string.Join(";", plan.RemovedEntities.Select(i => $"{snapshot.Entities[i].Prototype}@{snapshot.Entities[i].Position}:{snapshot.Entities[i].Rotation}")) +
           $"|{plan.DamageValue}/{plan.TotalValue}";
}
