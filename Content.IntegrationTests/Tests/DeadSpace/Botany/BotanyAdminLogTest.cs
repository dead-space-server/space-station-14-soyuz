// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using System.Collections.Generic;
using Content.Server.Administration.Logs;
using Content.Shared.Administration.Logs;
using Content.Shared.Botany.Events;
using Content.Shared.Botany.Systems;
using Content.Shared.Database;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.DeadSpace.Botany;

[TestFixture]
public sealed class BotanyAdminLogTest
{
    [Test]
    public async Task MutationAndGasExchangeAreLogged()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            AdminLogsEnabled = true,
            DummyTicker = false,
            Connected = true,
        });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var admin = server.ResolveDependency<IAdminLogManager>();

        string wheatPretty = null;
        string quietPretty = null;
        string gasPretty = null;

        await server.WaitPost(() =>
        {
            var ents = server.EntMan;
            var wheat = ents.SpawnEntity("WheatPlants", map.GridCoords);
            wheatPretty = ents.ToPrettyString(wheat).ToString();
            server.System<PlantMutationSystem>().CheckRandomMutations(wheat, 25f);

            var quiet = ents.SpawnEntity("WheatPlants", map.GridCoords);
            quietPretty = ents.ToPrettyString(quiet).ToString();
            var quietGrow = new PlantGrowEvent(NetEntity.Invalid);
            ents.EventBus.RaiseLocalEvent(quiet, ref quietGrow);

            var gasPlant = ents.SpawnEntity("tritiumCannabis", map.GridCoords);
            gasPretty = ents.ToPrettyString(gasPlant).ToString();
            var grow = new PlantGrowEvent(NetEntity.Invalid);
            ents.EventBus.RaiseLocalEvent(gasPlant, ref grow);
            ents.EventBus.RaiseLocalEvent(gasPlant, ref grow);
        });

        await PoolManager.WaitUntil(server, async () =>
        {
            var logs = await admin.CurrentRoundLogs(new LogFilter
            {
                Types = new HashSet<LogType> { LogType.Botany },
            });

            var sawChemical = false;
            var sawGas = false;
            foreach (var log in logs)
            {
                if (log.Message.Contains(wheatPretty) && log.Message.Contains("mutated ChangeChemicals"))
                    sawChemical = true;
                if (log.Message.Contains(gasPretty) && log.Message.Contains("gas exchange"))
                    sawGas = true;
            }

            return sawChemical && sawGas;
        });

        var stored = await admin.CurrentRoundLogs(new LogFilter
        {
            Types = new HashSet<LogType> { LogType.Botany },
        });

        var chemical = 0;
        var unviable = 0;
        var gas = 0;
        var quietGas = 0;
        foreach (var log in stored)
        {
            if (log.Message.Contains(wheatPretty) && log.Message.Contains("mutated ChangeChemicals"))
            {
                chemical++;
                Assert.That(log.Type, Is.EqualTo(LogType.Botany));
                Assert.That(log.Impact, Is.EqualTo(LogImpact.Medium));
            }

            if (log.Message.Contains(wheatPretty) && log.Message.Contains("mutated Unviable"))
            {
                unviable++;
                Assert.That(log.Impact, Is.EqualTo(LogImpact.Low));
            }

            if (log.Message.Contains(gasPretty) && log.Message.Contains("gas exchange"))
            {
                gas++;
                Assert.That(log.Impact, Is.EqualTo(LogImpact.Medium));
                Assert.That(log.Message, Does.Contain("Tritium 2 mol"));
            }

            if (log.Message.Contains(quietPretty) && log.Message.Contains("gas exchange"))
                quietGas++;
        }

        Assert.That(chemical, Is.EqualTo(1));
        Assert.That(unviable, Is.EqualTo(1));
        Assert.That(gas, Is.EqualTo(1));
        Assert.That(quietGas, Is.EqualTo(0));

        await pair.CleanReturnAsync();
    }
}
