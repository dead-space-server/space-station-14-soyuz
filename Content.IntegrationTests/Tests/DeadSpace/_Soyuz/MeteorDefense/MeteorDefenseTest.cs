// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using System;
using System.Linq;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.DeadSpace._Soyuz.MeteorDefense;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Construction.Components;
using Content.Shared.Containers;
using Content.Shared.Coordinates;
using Content.Shared.DeadSpace._Soyuz.MeteorDefense;
using Content.Shared.EntityTable;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Station.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.MeteorDefense;

[TestFixture]
public sealed class MeteorDefenseTest
{
    private static readonly EntProtoId Smes = "SMESBasic";
    private static readonly EntProtoId BoardBox = "BoxCECircuitboards";
    private static readonly ProtoId<CargoProductPrototype> CargoModule = "EngineeringMeteorDefenseTargetingModule";

    [Test]
    public async Task StationDefenseAndConstruction()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var defense = em.System<MeteorDefenseSystem>();
            var batteries = em.System<SharedBatterySystem>();
            var stations = em.System<StationSystem>();
            var maps = em.System<SharedMapSystem>();
            var mapManager = server.ResolveDependency<IMapManager>();
            var proto = server.ProtoMan;
            maps.CreateMap(out var mapId);
            var grid = mapManager.CreateGridEntity(mapId);
            maps.SetTile(grid, Vector2i.Zero, new Tile(1));
            maps.SetTile(grid, new Vector2i(1, 0), new Tile(1));
            var stationA = em.SpawnEntity(null, MapCoordinates.Nullspace);
            var stationB = em.SpawnEntity(null, MapCoordinates.Nullspace);
            em.AddComponent<StationDataComponent>(stationA);
            em.AddComponent<StationDataComponent>(stationB);
            stations.AddGridToStation(stationA, grid);
            var beacon = em.SpawnEntity("MeteorDefenseBeacon", grid.Owner.ToCoordinates());
            var second = em.SpawnEntity("MeteorDefenseBeacon", new EntityCoordinates(grid, 1, 0));
            var battery = em.GetComponent<BatteryComponent>(beacon);
            var config = em.GetComponent<MeteorDefenseBeaconComponent>(beacon);
            var network = em.GetComponent<PowerNetworkBatteryComponent>(beacon);
            var cost = config.EnergyPerIntercept;
            var originalCapacity = battery.MaxCharge;
            var rate = network.MaxChargeRate;

            bool Attempt(EntityUid target, EntProtoId meteor)
            {
                var ev = new MeteorInterceptAttemptEvent(target, meteor);
                em.EventBus.RaiseEvent(EventSource.Local, ref ev);
                return ev.Cancelled;
            }

            batteries.SetCharge((beacon, battery), 3 * cost);
            Assert.That(Attempt(grid, "MeteorSmall"), Is.False, "OFF does not intercept.");
            Assert.That(batteries.GetCharge((beacon, battery)), Is.EqualTo(3 * cost));
            Assert.That(network.CanCharge && network.Enabled, Is.True, "OFF preserves standard HV charging.");
            Assert.That(defense.TrySetEnabled(beacon, true, out _), Is.True);
            Assert.That(defense.TrySetEnabled(second, true, out _), Is.False);
            Assert.That(Attempt(stationB, "MeteorSmall"), Is.False);
            Assert.That(batteries.GetCharge((beacon, battery)), Is.EqualTo(3 * cost));

            // Normal meteors, space debris, and Kessler's rod share the same event and energy owner.
            Assert.That(Attempt(grid, "MeteorSmall"), Is.True);
            Assert.That(batteries.GetCharge((beacon, battery)), Is.EqualTo(2 * cost));
            Assert.That(Attempt(grid, "MeteorSpaceDust"), Is.True);
            Assert.That(batteries.GetCharge((beacon, battery)), Is.EqualTo(cost));
            Assert.That(Attempt(grid, "ImmovableRodKeepTilesStill"), Is.True);
            Assert.That(batteries.GetCharge((beacon, battery)), Is.Zero);
            batteries.SetCharge((beacon, battery), cost / 2);
            Assert.That(Attempt(grid, "MeteorSmall"), Is.False);
            Assert.That(batteries.GetCharge((beacon, battery)), Is.EqualTo(cost / 2));

            foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, float.MaxValue })
            {
                Assert.That(defense.TrySetMaxCharge(beacon, invalid), Is.False);
                Assert.That(battery.MaxCharge, Is.EqualTo(originalCapacity));
                Assert.That(batteries.GetCharge((beacon, battery)), Is.EqualTo(cost / 2));
            }

            Assert.That(defense.TrySetMaxCharge(beacon, cost / 4), Is.True);
            Assert.That(batteries.GetCharge((beacon, battery)), Is.EqualTo(cost / 4));
            Assert.That(defense.TrySetMaxCharge(beacon, config.MaxAllowedCharge), Is.True);
            Assert.That(batteries.GetCharge((beacon, battery)), Is.EqualTo(cost / 4), "Increasing capacity creates no energy.");
            Assert.That(network.MaxChargeRate, Is.EqualTo(rate));
            Assert.That(config.EnergyPerIntercept, Is.EqualTo(cost));
            Assert.That(defense.TrySetEnabled(beacon, false, out _), Is.True);
            Assert.That(defense.TrySetEnabled(second, true, out _), Is.True);
            em.DeleteEntity(second);
            Assert.That(defense.TrySetEnabled(beacon, true, out _), Is.True, "Deleted beacons do not reserve the station.");

            stations.RemoveGridFromStation(stationA, grid);
            stations.AddGridToStation(stationB, grid);
            batteries.SetCharge((beacon, battery), cost);
            Assert.That(Attempt(grid, "MeteorSmall"), Is.False, "Moving a grid does not transfer active defense.");
            Assert.That(batteries.GetCharge((beacon, battery)), Is.EqualTo(cost));

            var smes = proto.Index(Smes);
            Assert.That(smes.TryGetComponent<BatteryComponent>(out var smesBattery, em.ComponentFactory), Is.True);
            Assert.That(cost, Is.EqualTo(smesBattery.MaxCharge));
            var board = em.SpawnEntity("MeteorDefenseBeaconCircuitboard", MapCoordinates.Nullspace);
            var machineBoard = em.GetComponent<MachineBoardComponent>(board);
            Assert.That(machineBoard.Prototype, Is.EqualTo(new EntProtoId("MeteorDefenseBeacon")));
            var requirement = machineBoard.ComponentRequirements[nameof(MeteorDefenseTargetingModuleComponent).Replace("Component", "")];
            Assert.That(requirement.Amount, Is.EqualTo(1));
            Assert.That(requirement.DefaultPrototype, Is.EqualTo(new EntProtoId("MeteorDefenseTargetingModule")));
            Assert.That(machineBoard.ComponentRequirements["PowerCell"].Amount, Is.EqualTo(5));
            Assert.That(machineBoard.StackRequirements.Count, Is.EqualTo(2));
            var module = em.SpawnEntity(requirement.DefaultPrototype, MapCoordinates.Nullspace);
            Assert.That(em.HasComponent<MeteorDefenseTargetingModuleComponent>(module), Is.True);
            Assert.That(em.GetComponent<MachineComponent>(beacon).Board?.Id, Is.EqualTo("MeteorDefenseBeaconCircuitboard"));

            // Standard construction must retain the physical board and all parts, including the module.
            var machine = em.GetComponent<MachineComponent>(beacon);
            var frameUid = em.SpawnEntity("MachineFrame", MapCoordinates.Nullspace);
            var frame = em.GetComponent<MachineFrameComponent>(frameUid);
            var frames = em.System<MachineFrameSystem>();
            var containers = em.System<SharedContainerSystem>();
            Assert.That(frames.IsComplete(frame), Is.False, "A frame without a board cannot be completed.");
            Assert.That(containers.Insert(board, frame.BoardContainer), Is.True);
            foreach (var part in machine.PartContainer.ContainedEntities.ToArray())
            {
                if (!em.HasComponent<MeteorDefenseTargetingModuleComponent>(part))
                    Assert.That(containers.Insert(part, frame.PartContainer), Is.True);
            }
            frames.RegenerateProgress(frame);
            Assert.That(frames.IsComplete(frame), Is.False, "Standard parts alone cannot replace the targeting module.");
            Assert.That(containers.Insert(module, frame.PartContainer), Is.True);
            frames.RegenerateProgress(frame);
            Assert.That(frames.IsComplete(frame), Is.True);

            var cargo = proto.Index(CargoModule);
            Assert.That(cargo.Cost, Is.EqualTo(333333));
            Assert.That(cargo.Product, Is.EqualTo(new EntProtoId("MeteorDefenseTargetingModule")));
            var box = proto.Index(BoardBox);
            Assert.That(box.TryGetComponent<EntityTableContainerFillComponent>(out var fill, em.ComponentFactory), Is.True);
            var contents = em.System<EntityTableSystem>().GetSpawns(fill.Containers["storagebase"]).ToList();
            Assert.That(contents.Count(id => id == "MeteorDefenseBeaconCircuitboard"), Is.EqualTo(1));
            Assert.That(contents.Contains("MeteorDefenseTargetingModule"), Is.False);
        });

        await pair.CleanReturnAsync();
    }
}
