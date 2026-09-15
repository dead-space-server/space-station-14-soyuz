#nullable enable
using System.Linq;
using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Serilog.Events;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.RepairOrders;

public sealed partial class RepairOrderRegressionTest
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task StationRemovalCleansOwnedGrids(bool deleteStation, bool frozen)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid[] ownedGrids = [];
        await server.WaitPost(() =>
        {
            var station = CreateStation(entMan);
            var repairGrid = server.MapMan.CreateGridEntity(map.MapId).Owner;
            var pendingGrid = server.MapMan.CreateGridEntity(map.MapId).Owner;
            var fragment = server.MapMan.CreateGridEntity(map.MapId).Owner;
            ownedGrids = [repairGrid, pendingGrid, fragment];
            try
            {
                var active = new ActiveRepairOrder(station.Comp.NextRuntimeId++, SelectOrder(server.ProtoMan).ID, repairGrid)
                {
                    ExpiresAt = TimeSpan.MaxValue,
                    ExpirationFrozen = frozen,
                };
                station.Comp.Active = active;
                station.Comp.PendingCleanupGrids.Add(pendingGrid);
                entMan.AddComponent<RepairBlueprintComponent>(repairGrid);
                if (frozen)
                {
                    // A split after the deadline keeps the frozen result and owns its fragments until delivery.
                    Assert.That(server.System<RepairOrderSystem>().AbortActiveOrder(station.Owner, repairGrid,
                        RepairOrderAbortReason.RepairGridSplit, new[] { fragment }), Is.False);
                    Assert.That(station.Comp.Active, Is.SameAs(active));
                    Assert.That(active.ExpirationAdditionalGrids, Does.Contain(fragment));
                }
                else
                {
                    station.Comp.PendingCleanupGrids.Add(fragment);
                }

                var controls = server.System<ShuttleControlSystem>();
                foreach (var type in Enum.GetValues<ShuttleControlType>())
                {
                    foreach (var grid in ownedGrids)
                        Assert.That(controls.CanControl(grid, type, out _), Is.False, $"{grid}: {type}");
                    Assert.That(controls.CanControl(map.Grid.Owner, type, out _), Is.True);
                }

                if (deleteStation)
                    entMan.DeleteEntity(station.Owner);
                else
                    entMan.RemoveComponent<RepairOrderStationComponent>(station.Owner);

                Assert.That(station.Comp.Active, Is.Null);
                Assert.That(station.Comp.PendingCleanupGrids, Is.Empty);
                Assert.That(entMan.HasComponent<RepairBlueprintComponent>(repairGrid), Is.False);
                foreach (var grid in ownedGrids)
                    Assert.That(entMan.IsQueuedForDeletion(grid), Is.True);
                Assert.That(entMan.IsQueuedForDeletion(map.Grid.Owner), Is.False);
            }
            finally
            {
                if (entMan.EntityExists(station.Owner))
                    entMan.DeleteEntity(station.Owner);
            }
        });
        await pair.RunTicksSync(2);
        await server.WaitPost(() =>
        {
            foreach (var grid in ownedGrids)
                Assert.That(entMan.EntityExists(grid), Is.False);
            Assert.That(entMan.EntityExists(map.Grid.Owner), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ThrowingAbortLogCannotSkipGridCleanup()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        await server.WaitPost(() =>
        {
            var station = CreateStation(entMan);
            var grid = server.MapMan.CreateGridEntity(map.MapId).Owner;
            station.Comp.Active = new ActiveRepairOrder(station.Comp.NextRuntimeId++, SelectOrder(server.ProtoMan).ID, grid)
            {
                ExpiresAt = TimeSpan.MaxValue,
            };
            entMan.AddComponent<RepairBlueprintComponent>(grid);
            var sawmill = server.ResolveDependency<ILogManager>().GetSawmill("repair_orders");
            var handler = new ThrowingRepairLogHandler(message =>
                message.StartsWith("Aborted repair order ", StringComparison.Ordinal) ||
                message.StartsWith("Repair order post-commit abort log failed:", StringComparison.Ordinal));
            sawmill.AddHandler(handler);
            try
            {
                var orders = server.System<RepairOrderSystem>();
                Assert.That(orders.AbortActiveOrder(station.Owner, grid, RepairOrderAbortReason.ValidationRuntimeLost), Is.True);
                Assert.That(handler.Calls, Is.EqualTo(2), "Both the original log and its error log must throw.");
                Assert.That(station.Comp.Active, Is.Null);
                Assert.That(entMan.HasComponent<RepairBlueprintComponent>(grid), Is.False);
                Assert.That(entMan.IsQueuedForDeletion(grid), Is.True);
                Assert.That(orders.AbortActiveOrder(station.Owner, grid, RepairOrderAbortReason.ValidationRuntimeLost), Is.False);
            }
            finally
            {
                sawmill.RemoveHandler(handler);
                entMan.DeleteEntity(station.Owner);
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ThrowingTerminalLogsPreserveResultAndFinalUi(bool expire)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var stationMap = await pair.CreateTestMap();
        var entMan = server.EntMan;
        await server.WaitPost(() =>
        {
            var station = CreateStation(entMan);
            server.System<StationSystem>().AddGridToStation(station.Owner, stationMap.Grid);
            var console = entMan.SpawnEntity("RepairOrdersConsole", stationMap.GridCoords);
            entMan.RemoveComponent<AccessReaderComponent>(console);
            var actor = entMan.SpawnEntity(null, stationMap.GridCoords);
            var maps = server.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var sawmill = server.ResolveDependency<ILogManager>().GetSawmill("repair_orders");
            var previousLevel = sawmill.Level;
            var handler = new ThrowingRepairLogHandler(message =>
                message.StartsWith(expire ? "Expired repair order " : "Completed repair order ", StringComparison.Ordinal) ||
                message.StartsWith("Repair order post-commit ", StringComparison.Ordinal));
            sawmill.Level = LogLevel.Info;
            sawmill.AddHandler(handler);
            try
            {
                var prototype = SelectOrder(server.ProtoMan);
                Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, prototype.TargetGridPath, out var loaded), Is.True);
                var grid = loaded!.Value.Owner;
                Assert.That(server.System<RepairOrderValidationSystem>().TryPrepareSession(
                    station.Owner, station.Comp.NextRuntimeId++, prototype.ID, grid, out var active), Is.True);
                active.ExpiresAt = expire ? server.ResolveDependency<IGameTiming>().CurTime : TimeSpan.MaxValue;
                station.Comp.Active = active;
                if (expire)
                {
                    Assert.That(server.System<RepairOrderExpirationSystem>().TryExpireActiveOrder(
                        station.Owner, station.Comp, console, actor), Is.True);
                }
                else
                {
                    var message = new RepairOrderCompleteMessage(active.RuntimeId) { Actor = actor, UiKey = RepairOrderUiKey.Key };
                    Assert.DoesNotThrow(() => entMan.EventBus.RaiseLocalEvent(console, message));
                }

                Assert.That(handler.Calls, Is.EqualTo(2), "Exercise the terminal log and the failing error log.");
                Assert.That(station.Comp.Active, Is.Null);
                Assert.That(station.Comp.Completing, Is.False);
                Assert.That(station.Comp.Completed, Is.Not.Null);
                Assert.That(station.Comp.Completed!.RuntimeId, Is.EqualTo(active.RuntimeId));
                Assert.That(station.Comp.Completed.Result, Is.EqualTo(expire ? RepairOrderResult.Expired : RepairOrderResult.Completed));
                Assert.That(entMan.IsQueuedForDeletion(grid), Is.True);
                Assert.That(server.System<SharedUserInterfaceSystem>().TryGetUiState<RepairOrderBoundUserInterfaceState>(
                    console, RepairOrderUiKey.Key, out var ui), Is.True);
                Assert.That(ui!.Completing, Is.False);
                Assert.That(ui.Active, Is.Null);
                Assert.That(ui.Completed?.RuntimeId, Is.EqualTo(active.RuntimeId));
            }
            finally
            {
                sawmill.RemoveHandler(handler);
                sawmill.Level = previousLevel;
                maps.DeleteMap(mapId);
                entMan.DeleteEntity(station.Owner);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FailedDeliveryRollsBackEvenWhenItsErrorLogThrows()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid[] spawned = [];
        await server.WaitPost(() =>
        {
            var station = CreateStation(entMan);
            var console = entMan.SpawnEntity("RepairOrdersConsole", map.GridCoords);
            var prototype = SelectOrder(server.ProtoMan);
            var pool = server.ProtoMan.Index(prototype.RewardPool);
            var reward = server.ProtoMan.Index(pool.Rewards.First());
            var rewards = new[]
            {
                new RepairOrderRewardResult(reward.ID, 1),
                new RepairOrderRewardResult("MissingRepairOrderRewardForRollbackRegression", 1),
            };
            var delivery = server.System<RepairOrderRewardDeliverySystem>();
            var before = entMan.GetEntities().ToHashSet();
            var sawmill = server.ResolveDependency<ILogManager>().GetSawmill("repair_orders");
            var handler = new ThrowingRepairLogHandler(message =>
                message.StartsWith("Failed to create reward delivery for repair order ", StringComparison.Ordinal));
            sawmill.AddHandler(handler);
            try
            {
                var exception = Assert.Throws<InvalidOperationException>(() =>
                    delivery.TryDeliver(station.Owner, 1, console, prototype, rewards, out _));
                Assert.That(exception!.Message, Is.EqualTo(ThrowingRepairLogHandler.Failure));
                Assert.That(handler.Calls, Is.EqualTo(1));
                spawned = entMan.GetEntities().Except(before).ToArray();
                var owned = spawned.Where(uid =>
                {
                    var id = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
                    return id == pool.DeliveryContainer.Id || id == reward.Entity.Id;
                }).ToArray();
                Assert.That(owned, Has.Length.EqualTo(2), "Failure must occur after both the container and a reward exist.");
                foreach (var uid in owned)
                    Assert.That(entMan.IsQueuedForDeletion(uid), Is.True, $"Uncommitted delivery leaked {uid}.");
            }
            finally
            {
                sawmill.RemoveHandler(handler);
                entMan.DeleteEntity(station.Owner);
            }
        });
        await pair.RunTicksSync(2);
        await server.WaitPost(() =>
        {
            foreach (var uid in spawned)
                Assert.That(entMan.EntityExists(uid), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    private sealed class ThrowingRepairLogHandler(Func<string, bool> matches) : ILogHandler
    {
        public const string Failure = "Expected repair lifecycle log sink failure";
        public int Calls;

        public void Log(string sawmillName, LogEvent message)
        {
            if (!matches(message.RenderMessage()))
                return;

            Calls++;
            throw new InvalidOperationException(Failure);
        }
    }
}
