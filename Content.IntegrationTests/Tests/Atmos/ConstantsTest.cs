using System.Linq;
using Content.Server.Atmos.EntitySystems;
// DS14-Soyuz start
using Content.Server.Atmos.Monitor.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Monitor;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
// DS14-Soyuz end

namespace Content.IntegrationTests.Tests.Atmos;

[TestOf(typeof(Atmospherics))]
public sealed class ConstantsTest
{
    // DS14-Soyuz start
    private static readonly EntProtoId AirSensorId = "AirSensor";
    private static readonly EntProtoId AirSensorVoxId = "AirSensorVox";
    // DS14-Soyuz end

    [Test]
    public async Task TotalGasesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entityManager = server.EntMan;
        var protoManager = server.ProtoMan;

        await server.WaitPost(() =>
        {
            var atmosSystem = entityManager.System<AtmosphereSystem>();

            Assert.Multiple(() =>
            {
                // adding new gases needs a few changes in the code, so make sure this is done everywhere
                var gasProtos = protoManager.EnumeratePrototypes<GasPrototype>().ToList();

                // number of gas prototypes
                Assert.That(gasProtos, Has.Count.EqualTo(Atmospherics.TotalNumberOfGases),
                     $"Number of GasPrototypes is not equal to TotalNumberOfGases.");
                // number of gas prototypes used in the atmos system
                Assert.That(atmosSystem.Gases.Count(), Is.EqualTo(Atmospherics.TotalNumberOfGases),
                     $"AtmosSystem.Gases is not equal to TotalNumberOfGases.");
                // enum mapping gases to their Id
                Assert.That(Enum.GetValues<Gas>(), Has.Length.EqualTo(Atmospherics.TotalNumberOfGases),
                     $"Gas enum size is not equal to TotalNumberOfGases.");
                // localized abbreviations for UI purposes
                Assert.That(Atmospherics.GasAbbreviations, Has.Count.EqualTo(Atmospherics.TotalNumberOfGases),
                     $"GasAbbreviations size is not equal to TotalNumberOfGases.");

                // DS14-Soyuz start
                var gases = Enum.GetValues<Gas>();
                Assert.That(gases.Max(gas => (int) gas) + 1, Is.EqualTo(Atmospherics.TotalNumberOfGases));
                Assert.That(gases.Select(gas => (int) gas).Distinct().Count(), Is.EqualTo(Atmospherics.TotalNumberOfGases));
                var mixture = new GasMixture();
                var visibleGases = entityManager.System<GasTileOverlaySystem>().VisibleGasId;
                var sensor = protoManager.Index<EntityPrototype>(AirSensorId);
                var voxSensor = protoManager.Index<EntityPrototype>(AirSensorVoxId);
                foreach (var sensorProto in new[] { sensor, voxSensor })
                {
                    Assert.That(sensorProto.TryGetComponent<AtmosMonitorComponent>(out var monitor, entityManager.ComponentFactory), Is.True);
                    Assert.That(monitor!.GasThresholdPrototypes, Is.Not.Null);
                    foreach (var gas in gases)
                    {
                        Assert.That(monitor.GasThresholdPrototypes!.TryGetValue(gas, out var threshold), Is.True,
                            $"{sensorProto.ID} has no threshold for {gas}");
                        if (threshold != null)
                            Assert.That(protoManager.TryIndex<AtmosAlarmThresholdPrototype>(threshold, out _), Is.True);
                    }
                }

                foreach (var gas in gases)
                {
                    Assert.That((int) gas, Is.InRange(0, Atmospherics.TotalNumberOfGases - 1));
                    Assert.DoesNotThrow(() => mixture.GetMoles(gas), $"GasMixture cannot index {gas}");
                    Assert.That(protoManager.TryIndex<GasPrototype>(gas.ToString(), out var gasProto), Is.True,
                        $"No GasPrototype for {gas}");
                    Assert.That(Atmospherics.GasAbbreviations.ContainsKey(gas), Is.True,
                        $"No abbreviation for {gas}");
                    if (gasProto?.Reagent is { } reagent)
                        Assert.That(protoManager.TryIndex<ReagentPrototype>(reagent, out _), Is.True,
                            $"No reagent {reagent} for {gas}");
                    if (gas >= Gas.Kryoxide)
                    {
                        Assert.That(gasProto!.Reagent, Is.Not.Null, $"No reagent mapping for {gas}");
                        Assert.That(GasVentScrubberData.DefaultFilterGases.Contains(gas), Is.True,
                            $"Advanced gas {gas} is missing from scrubber defaults");
                        Assert.That(visibleGases, Does.Contain((int) gas),
                            $"Advanced gas {gas} is missing from the visible gas overlay");
                    }
                }
                // DS14-Soyuz end

                // the ID for each gas has to correspond to a value in the Gas enum (converted to a string)
                foreach (var gas in gasProtos)
                {
                    Assert.That(Enum.TryParse<Gas>(gas.ID, out _), $"GasPrototype {gas.ID} has an invalid ID. It must correspond to a value in the {nameof(Gas)} enum.");
                }
            });
        });
        await pair.CleanReturnAsync();
    }
}

