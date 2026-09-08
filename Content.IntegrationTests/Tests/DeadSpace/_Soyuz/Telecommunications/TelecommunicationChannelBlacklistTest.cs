// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using System.Linq;
using Content.Server.DeadSpace._Soyuz.Telecommunications;
using Content.Server.Radio;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.DeadSpace._Soyuz.Telecommunications;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Lock;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Radio;
using Content.Shared.Research.Prototypes;
using Content.Shared.Station.Components;
using Content.Shared.StationRecords;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.Telecommunications;

[TestFixture]
[TestOf(typeof(TelecommunicationChannelBlacklistSystem))]
public sealed class TelecommunicationChannelBlacklistTest
{
    [Test]
    public async Task BlacklistIsAtomicMindAndStationLocalAndCancelsRadioSend()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var blacklist = server.System<TelecommunicationChannelBlacklistSystem>();
        var consoleSystem = server.System<TelecommunicationBlacklistConsoleSystem>();
        var idCard = server.System<SharedIdCardSystem>();
        var lockSystem = server.System<LockSystem>();
        var mindSystem = server.System<SharedMindSystem>();
        var stationSystem = server.System<StationSystem>();
        var stationRecords = server.System<StationRecordsSystem>();
        var ui = server.System<UserInterfaceSystem>();
        var prototype = server.ResolveDependency<IPrototypeManager>();
        var testMap = await pair.CreateTestMap();

        ProtoId<RadioChannelPrototype> commonId = "Common";
        ProtoId<RadioChannelPrototype> engineeringId = "Engineering";
        ProtoId<RadioChannelPrototype> invalidId = "TelecommunicationBlacklistTestInvalid";
        EntProtoId consoleId = "TelecommunicationBlacklistComputer";
        EntProtoId consoleBoardId = "TelecommunicationBlacklistComputerCircuitboard";
        ProtoId<LatheRecipePrototype> consoleBoardRecipeId = "TelecommunicationBlacklistComputerCircuitboard";
        ProtoId<LatheRecipePackPrototype> serviceBoardsId = "ServiceBoards";
        ProtoId<TechnologyPrototype> audioVisualCommunicationId = "AudioVisualCommunication";
        ProtoId<TechDisciplinePrototype> civilianServicesId = "CivilianServices";
        ProtoId<RadioChannelPrototype>[] excludedChannelIds =
        [
            "CentCom",
            "DeathSquad",
            "Xenoborg",
            "Mothership",
            "Merc",
            "Freelance",
            "SOCChannel",
            "CarpDragonChannel",
            "Unitolog",
            "Handheld",
            "TaipanHandheld",
            "Syndicate",
            "SpiderTerrorChannel",
            "Taipan",
            "Shadowling",
            "Hivemind",
            "CriticalForce",
        ];
        var common = prototype.Index(commonId);
        var engineering = prototype.Index(engineeringId);

        await server.WaitAssertion(() =>
        {
            var consoleBoardRecipe = prototype.Index(consoleBoardRecipeId);
            var serviceBoards = prototype.Index(serviceBoardsId);
            var audioVisualCommunication = prototype.Index(audioVisualCommunicationId);

            Assert.Multiple(() =>
            {
                Assert.That(consoleBoardRecipe.Result, Is.EqualTo(consoleBoardId));
                Assert.That(serviceBoards.Recipes, Does.Contain(consoleBoardRecipeId));
                Assert.That(audioVisualCommunication.RecipeUnlocks, Does.Contain(consoleBoardRecipeId));
                Assert.That(audioVisualCommunication.Discipline, Is.EqualTo(civilianServicesId));
                Assert.That(audioVisualCommunication.Tier, Is.EqualTo(1));
            });
            foreach (var excludedChannelId in excludedChannelIds)
                Assert.That(prototype.HasIndex(excludedChannelId), Is.True);

            var stationA = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var stationB = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<StationDataComponent>(stationA);
            entMan.EnsureComponent<StationDataComponent>(stationB);
            entMan.EnsureComponent<StationRecordsComponent>(stationA);
            stationSystem.AddGridToStation(stationA, testMap.Grid);

            var bodyA = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var bodyB = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.EnsureComponent<MindContainerComponent>(bodyA);
            entMan.EnsureComponent<MindContainerComponent>(bodyB);

            var mindA = mindSystem.CreateMind(null, "Same display name");
            var mindB = mindSystem.CreateMind(null, "Same display name");
            mindSystem.TransferTo(mindA, bodyA, mind: mindA.Comp);
            mindSystem.TransferTo(mindB, bodyB, mind: mindB.Comp);

            Assert.That(blacklist.SetBlockedChannels(stationA, mindA, [commonId]), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(blacklist.IsChannelBlocked(stationA, mindA, commonId), Is.True);
                Assert.That(blacklist.IsChannelBlocked(stationA, mindA, engineeringId), Is.False);
                Assert.That(blacklist.IsChannelBlocked(stationA, mindB, commonId), Is.False);
                Assert.That(blacklist.IsChannelBlocked(stationB, mindA, commonId), Is.False);
            });

            var blockedCommon = new RadioSendAttemptEvent(bodyA, common, stationA);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref blockedCommon);
            var allowedEngineering = new RadioSendAttemptEvent(bodyA, engineering, stationA);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref allowedEngineering);
            var allowedOtherStation = new RadioSendAttemptEvent(bodyA, common, stationB);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref allowedOtherStation);
            var radioSourceOnStationGrid = entMan.SpawnEntity(null, testMap.GridCoords);
            var blockedFromStationGrid = new RadioSendAttemptEvent(bodyA, common, radioSourceOnStationGrid);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref blockedFromStationGrid);

            Assert.Multiple(() =>
            {
                Assert.That(blockedCommon.Cancelled, Is.True);
                Assert.That(allowedEngineering.Cancelled, Is.False);
                Assert.That(allowedOtherStation.Cancelled, Is.False);
                Assert.That(blockedFromStationGrid.Cancelled, Is.True);
            });

            Assert.That(blacklist.SetBlockedChannels(stationA, mindA, [commonId, engineeringId, commonId]), Is.True);
            Assert.That(blacklist.SetBlockedChannels(stationA, mindA, [commonId, engineeringId]), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(blacklist.GetBlockedChannels(stationA, mindA), Is.EquivalentTo(new[] { commonId, engineeringId }));
                Assert.That(blacklist.IsChannelBlocked(stationA, mindA, commonId), Is.True);
                Assert.That(blacklist.IsChannelBlocked(stationA, mindA, engineeringId), Is.True);
            });

            Assert.That(blacklist.SetBlockedChannels(stationA, mindA, [commonId, invalidId]), Is.False);
            Assert.That(
                blacklist.GetBlockedChannels(stationA, mindA),
                Is.EquivalentTo(new[] { commonId, engineeringId }),
                "An invalid channel must leave the complete previous set unchanged.");

            Assert.That(blacklist.ClearBlockedChannels(stationA, mindA), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(blacklist.GetBlockedChannels(stationA, mindA), Is.Empty);
                Assert.That(blacklist.IsChannelBlocked(stationA, mindA, commonId), Is.False);
            });

            // Model the post-spawn state after the character has lost their ID: the station-record
            // association captured on the mind remains, while no physical ID is currently findable.
            var recordKeyA = stationRecords.AddRecordEntry(stationA, new GeneralStationRecord
            {
                Name = "Zulu Character",
                JobTitle = "Station Engineer",
            });
            var recordKeyB = stationRecords.AddRecordEntry(stationA, new GeneralStationRecord
            {
                Name = "Alpha Character",
                JobTitle = "Medical Doctor",
            });
            Assert.That(consoleSystem.TryAssociateMindWithStationRecord(mindA, recordKeyA), Is.True);
            Assert.That(consoleSystem.TryAssociateMindWithStationRecord(mindB, recordKeyB), Is.True);
            Assert.That(idCard.TryFindIdCard(bodyA, out _), Is.False);

            Assert.That(prototype.HasIndex(consoleBoardId), Is.True);
            var console = entMan.SpawnEntity(consoleId, testMap.GridCoords);
            lockSystem.Unlock(console, null);
            entMan.EventBus.RaiseLocalEvent(
                console,
                new BoundUIOpenedEvent(TelecommunicationBlacklistConsoleUiKey.Key, console, bodyB));

            Assert.That(
                ui.TryGetUiState<TelecommunicationBlacklistConsoleState>(
                    console,
                    TelecommunicationBlacklistConsoleUiKey.Key,
                    out var consoleState),
                Is.True);
            Assert.That(
                consoleState!.Roster.Any(entry => entry.Mind == entMan.GetNetEntity(mindA.Owner)),
                Is.True,
                "A station-record-backed mind must remain in the roster without a physical ID card.");
            Assert.That(
                consoleState.Roster.Select(entry => entry.Name),
                Is.EqualTo(new[] { "Alpha Character", "Zulu Character" }));
            Assert.Multiple(() =>
            {
                Assert.That(consoleState.Channels.Intersect(excludedChannelIds), Is.Empty);
                Assert.That(consoleState.Channels, Does.Contain(commonId));
            });
        });

        await pair.CleanReturnAsync();
    }
}
