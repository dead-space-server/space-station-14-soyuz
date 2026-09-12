#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Station.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Serilog.Events;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.RepairOrders;

[TestFixture]
public sealed partial class RepairOrderRegressionTest
{
    private const string SubscriberFailure = "Expected repair activation subscriber failure";

    [TestCase(false)]
    [TestCase(true)]
    public async Task ThrowingActivationSubscriberPreservesCommitAndCleanup(bool throwingLogSink)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var orders = server.System<RepairOrderSystem>();
        var listener = server.System<RepairOrderActivationTestSystem>();
        var loggedFailures = 0;
        EntityUid repairGrid = default;
        bool JudgeLog(string sawmill, LogEvent log)
        {
            if (sawmill != "repair_orders")
                return false;

            var message = log.RenderMessage();
            if (message.Contains("post-commit activation event failed:") && message.Contains(SubscriberFailure))
            {
                loggedFailures++;
                if (throwingLogSink)
                    throw new InvalidOperationException("Expected post-commit log sink failure");
                return true;
            }

            return IsAbortLog(sawmill, log);
        }

        pair.ServerLogHandler.JudgeLog += JudgeLog;
        try
        {
            await server.WaitPost(() =>
            {
                var station = CreateStation(entMan);
                server.System<StationSystem>().AddGridToStation(station.Owner, map.Grid);
                var console = entMan.SpawnEntity("RepairOrdersConsole", map.GridCoords);
                // This test exercises activation, with console access already granted.
                entMan.RemoveComponent<AccessReaderComponent>(console);
                var actor = entMan.SpawnEntity(null, map.GridCoords);
                var prototype = SelectOrder(server.ProtoMan);
                var now = server.ResolveDependency<IGameTiming>().CurTime;
                var runtimeId = station.Comp.NextRuntimeId++;
                station.Comp.Available.Clear();
                station.Comp.Available.Add(runtimeId, new AvailableRepairOrder(runtimeId, prototype.ID, now + TimeSpan.FromMinutes(1)));
                var called = 0;
                var observedCompleteCommit = false;
                listener.OnActivated = ev =>
                {
                    called++;
                    repairGrid = ev.GridUid;
                    var active = station.Comp.Active;
                    observedCompleteCommit = ev.Station == station.Owner && active != null &&
                        active.GridUid == repairGrid && active.BlueprintReady && active.TotalTasks > 0 &&
                        active.ActivationConsole == console && active.ExpiresAt == active.StartedAt + prototype.RepairTime &&
                        station.Comp.LastRepairConsoleCoordinates?.EntityId == map.Grid.Owner &&
                        !station.Comp.Available.ContainsKey(runtimeId) && !station.Comp.Accepting;
                    throw new InvalidOperationException(SubscriberFailure);
                };

                try
                {
                    var message = new RepairOrderAcceptMessage(runtimeId) { Actor = actor, UiKey = RepairOrderUiKey.Key };
                    Assert.DoesNotThrow(() => entMan.EventBus.RaiseLocalEvent(console, message));
                    Assert.That(called, Is.EqualTo(1));
                    Assert.That(loggedFailures, Is.EqualTo(1));
                    Assert.That(observedCompleteCommit, Is.True, "Subscriber must observe a fully committed session.");
                    Assert.That(station.Comp.Active, Is.Not.Null);
                    Assert.That(station.Comp.Active!.GridUid, Is.EqualTo(repairGrid));
                    Assert.That(station.Comp.Available.ContainsKey(runtimeId), Is.False);
                    Assert.That(station.Comp.Accepting, Is.False);
                    Assert.That(entMan.EntityExists(repairGrid), Is.True);
                    Assert.That(entMan.GetComponent<RepairBlueprintComponent>(repairGrid).Ready, Is.True);
                    Assert.That(server.System<SharedUserInterfaceSystem>().TryGetUiState<RepairOrderBoundUserInterfaceState>(
                        console, RepairOrderUiKey.Key, out var ui), Is.True);
                    Assert.That(ui!.Active?.RuntimeId, Is.EqualTo(runtimeId));
                    Assert.That(ui.Accepting, Is.False, "The final UI refresh must still run after a subscriber failure.");

                    Assert.That(orders.AbortActiveOrder(station.Owner, repairGrid, RepairOrderAbortReason.ValidationRuntimeLost), Is.True);
                    Assert.That(station.Comp.Active, Is.Null);
                    Assert.That(entMan.HasComponent<RepairBlueprintComponent>(repairGrid), Is.False);
                    Assert.That(entMan.IsQueuedForDeletion(repairGrid), Is.True);
                    Assert.That(orders.AbortActiveOrder(station.Owner, repairGrid, RepairOrderAbortReason.ValidationRuntimeLost), Is.False);
                }
                finally
                {
                    listener.OnActivated = null;
                    entMan.DeleteEntity(station.Owner);
                }
            });
            await pair.RunTicksSync(2);
            await server.WaitPost(() => Assert.That(entMan.EntityExists(repairGrid), Is.False));
        }
        finally
        {
            pair.ServerLogHandler.JudgeLog -= JudgeLog;
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DirtyCellBatchPreservesUnchangedProgressAndRecalculatesRepairs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var stationMap = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var validation = server.System<RepairOrderValidationSystem>();

        await server.WaitPost(() =>
        {
            var station = CreateStation(entMan);
            server.System<StationSystem>().AddGridToStation(station.Owner, stationMap.Grid);
            var console = entMan.SpawnEntity("RepairOrdersConsole", stationMap.GridCoords);
            var uiSystem = server.System<SharedUserInterfaceSystem>();
            maps.CreateMap(out var mapId);
            EntityUid repairGrid = default;
            try
            {
                var prototype = SelectOrder(server.ProtoMan);
                Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(mapId, prototype.TargetGridPath, out var loaded), Is.True);
                var grid = loaded!.Value;
                repairGrid = grid.Owner;
                Assert.That(validation.TryPrepareSession(station.Owner, station.Comp.NextRuntimeId++, prototype.ID, repairGrid, out var active), Is.True);
                active.ExpiresAt = TimeSpan.MaxValue;
                station.Comp.Active = active;
                server.System<RepairOrderSystem>().RefreshStationUis(station.Owner);
                Assert.That(uiSystem.TryGetUiState<RepairOrderBoundUserInterfaceState>(console, RepairOrderUiKey.Key, out var initialUi), Is.True);
                var blueprint = entMan.GetComponent<RepairBlueprintComponent>(repairGrid);
                var cells = blueprint.ExpectedCells.Where(entry => entry.Value.Tile is { Points: > 0 }).Take(2).ToArray();
                Assert.That(cells, Has.Length.EqualTo(2));
                Assert.That(blueprint.FullyMatchesTarget, Is.True);
                var initial = (active.CompletedTasks, active.TotalTasks, active.CurrentPoints, active.MaxPoints);
                var tiles = cells.Select(entry => maps.GetTileRef(repairGrid, grid.Comp, entry.Key).Tile).ToArray();

                // Cosmetic tile rotations dirty both cells without changing any validation requirement.
                for (var i = 0; i < cells.Length; i++)
                    maps.SetTile(repairGrid, grid.Comp, cells[i].Key,
                        new Tile(tiles[i].TypeId, tiles[i].Flags, tiles[i].Variant, (byte) (tiles[i].RotationMirroring ^ 1)));
                validation.Update(0f);
                Assert.That((active.CompletedTasks, active.TotalTasks, active.CurrentPoints, active.MaxPoints), Is.EqualTo(initial));
                Assert.That(blueprint.FullyMatchesTarget, Is.True);
                Assert.That(uiSystem.TryGetUiState<RepairOrderBoundUserInterfaceState>(console, RepairOrderUiKey.Key, out var unchangedUi), Is.True);
                Assert.That(unchangedUi, Is.SameAs(initialUi), "An unchanged dirty batch must not publish another UI state.");

                // Replace two tiles without removing support or splitting the grid.
                var definitions = server.ResolveDependency<ITileDefinitionManager>();
                IReadOnlyDictionary<int, int> tileIdentities = blueprint.TileIdentityIds;
                for (var i = 0; i < cells.Length; i++)
                {
                    var canonical = cells[i].Value.Tile!.CanonicalTileId;
                    var wrongTile = definitions.First(definition => definition.TileId != Tile.Empty.TypeId &&
                        (tileIdentities.TryGetValue(definition.TileId, out var id) ? id : definition.TileId) != canonical);
                    maps.SetTile(repairGrid, grid.Comp, cells[i].Key, new Tile(wrongTile.TileId));
                }

                validation.Update(0f);
                var penalty = cells.Sum(entry => entry.Value.Tile!.Points);
                Assert.That(active.CompletedTasks, Is.EqualTo(initial.CompletedTasks - cells.Length));
                Assert.That(active.TotalTasks, Is.EqualTo(initial.TotalTasks));
                Assert.That(active.CurrentPoints, Is.EqualTo(initial.CurrentPoints - penalty));
                Assert.That(active.MaxPoints, Is.EqualTo(initial.MaxPoints));
                Assert.That(blueprint.FullyMatchesTarget, Is.False);
                Assert.That(uiSystem.TryGetUiState<RepairOrderBoundUserInterfaceState>(console, RepairOrderUiKey.Key, out var changedUi), Is.True);
                Assert.That(changedUi, Is.Not.SameAs(initialUi));
                Assert.That(changedUi!.Active!.CurrentPoints, Is.EqualTo(active.CurrentPoints));
                Assert.That(validation.TryRevalidateForCompletion(repairGrid, out var matches), Is.True);
                Assert.That(matches, Is.False);

                for (var i = 0; i < cells.Length; i++)
                    maps.SetTile(repairGrid, grid.Comp, cells[i].Key, tiles[i]);
                validation.Update(0f);
                Assert.That((active.CompletedTasks, active.TotalTasks, active.CurrentPoints, active.MaxPoints), Is.EqualTo(initial));
                Assert.That(uiSystem.TryGetUiState<RepairOrderBoundUserInterfaceState>(console, RepairOrderUiKey.Key, out var restoredUi), Is.True);
                Assert.That(validation.TryRevalidateForCompletion(repairGrid, out matches), Is.True);
                Assert.That(matches, Is.True);
                Assert.That(validation.TryRevalidateForCompletion(repairGrid, out matches), Is.True);
                Assert.That(matches, Is.True);
                Assert.That(uiSystem.TryGetUiState<RepairOrderBoundUserInterfaceState>(console, RepairOrderUiKey.Key, out var revalidatedUi), Is.True);
                Assert.That(revalidatedUi, Is.SameAs(restoredUi), "Unchanged full validation must not publish another UI state.");
                Assert.That(blueprint.CompletedTasks, Is.EqualTo(active.CompletedTasks));
                Assert.That(blueprint.CurrentPoints, Is.EqualTo(active.CurrentPoints));
            }
            finally
            {
                station.Comp.Active = null;
                if (repairGrid.IsValid())
                    validation.DiscardPreparedSession(repairGrid);
                maps.DeleteMap(mapId);
                entMan.DeleteEntity(station.Owner);
            }
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ShuttleControlsFollowRepairLifecycleWithoutAffectingOrdinaryShuttles(bool complete)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var controls = server.System<ShuttleControlSystem>();
        var orders = server.System<RepairOrderSystem>();
        pair.ServerLogHandler.JudgeLog += IsAbortLog;
        try
        {
            await server.WaitPost(() =>
            {
                var station = CreateStation(entMan);
                var repairGrid = server.MapMan.CreateGridEntity(map.MapId);
                entMan.EnsureComponent<ShuttleComponent>(repairGrid.Owner);
                entMan.EnsureComponent<ShuttleComponent>(map.Grid.Owner);
                try
                {
                    AssertControls(map.Grid.Owner, true);
                    AssertControls(repairGrid.Owner, true);
                    var prototype = SelectOrder(server.ProtoMan);
                    var active = new ActiveRepairOrder(station.Comp.NextRuntimeId++, prototype.ID, repairGrid.Owner)
                    {
                        ExpiresAt = TimeSpan.MaxValue,
                    };
                    station.Comp.Active = active;
                    AssertControls(repairGrid.Owner, false);
                    AssertControls(map.Grid.Owner, true);

                    if (complete)
                    {
                        var result = new CompletedRepairOrder(active.RuntimeId, active.Prototype, 0, 0, 0, 0, 0,
                            RepairOrderResult.Completed, true, null, Array.Empty<RepairOrderRewardResult>());
                        Assert.That(orders.TryCommitCompletion(station.Owner, active, result, out var cleanupGrid), Is.True);
                        orders.CleanupTerminalGrid(station.Owner, cleanupGrid);
                        Assert.That(station.Comp.PendingCleanupGrids, Does.Contain(repairGrid.Owner));
                        // Terminal grids stay locked until deletion, including the deferred-cleanup interval.
                        AssertControls(repairGrid.Owner, false);
                    }
                    else
                    {
                        Assert.That(orders.AbortActiveOrder(station.Owner, repairGrid.Owner, RepairOrderAbortReason.ValidationRuntimeLost), Is.True);
                        AssertControls(repairGrid.Owner, true);
                    }

                    Assert.That(station.Comp.Active, Is.Null);
                    Assert.That(entMan.IsQueuedForDeletion(repairGrid.Owner), Is.True);
                    entMan.DeleteEntity(repairGrid.Owner);
                    orders.Update(0f);
                    Assert.That(station.Comp.PendingCleanupGrids, Is.Empty);
                    // The policy retains no stale ownership after cleanup. This does not resurrect the grid.
                    AssertControls(repairGrid.Owner, true);
                    AssertControls(map.Grid.Owner, true);
                }
                finally
                {
                    entMan.DeleteEntity(station.Owner);
                }
            });
        }
        finally
        {
            pair.ServerLogHandler.JudgeLog -= IsAbortLog;
        }

        await pair.CleanReturnAsync();
        return;

        void AssertControls(EntityUid grid, bool allowed)
        {
            foreach (var type in Enum.GetValues<ShuttleControlType>())
            {
                Assert.That(controls.CanControl(grid, type, out var reason), Is.EqualTo(allowed), $"{grid}: {type}");
                Assert.That(reason, allowed ? Is.Null : Is.Not.Null.And.Not.Empty);
                var attempt = new ShuttleControlAttemptEvent(grid, type, null);
                entMan.EventBus.RaiseLocalEvent(grid, ref attempt, true);
                Assert.That(attempt.Cancelled, Is.EqualTo(!allowed), $"Event contract: {type}");
            }
        }
    }

    private static Entity<RepairOrderStationComponent> CreateStation(IEntityManager entMan)
    {
        var uid = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
        entMan.AddComponent<StationDataComponent>(uid);
        var state = entMan.AddComponent<RepairOrderStationComponent>(uid);
        state.PoolInitialized = true;
        state.NextOffer = TimeSpan.MaxValue;
        return (uid, state);
    }

    private static RepairOrderPrototype SelectOrder(IPrototypeManager prototypes)
    {
        return prototypes.EnumeratePrototypes<RepairOrderPrototype>()
            .OrderBy(order => order.Difficulty)
            .ThenBy(order => order.ID, StringComparer.Ordinal)
            .First();
    }

    private static bool IsAbortLog(string sawmill, LogEvent log)
    {
        return sawmill == "repair_orders" && log.Level == LogEventLevel.Warning &&
            log.RenderMessage().StartsWith("Aborted repair order ", StringComparison.Ordinal);
    }
}
