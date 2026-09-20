// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Content.Server.Station.Systems;
using Content.Shared.Access.Components;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Serilog.Events;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.RepairOrders;

public sealed partial class RepairOrderRegressionTest
{
    [Test]
    public void OfferSelectionRespectsWeightIntervals()
    {
        var light = new RepairOrderPrototype { Weight = 1 };
        var heavy = new RepairOrderPrototype { Weight = 3 };
        var candidates = new[] { light, heavy };
        Assert.That(RepairOrderSystem.SelectWeightedOffer(candidates, 0), Is.SameAs(light));
        Assert.That(RepairOrderSystem.SelectWeightedOffer(candidates, .249), Is.SameAs(light));
        Assert.That(RepairOrderSystem.SelectWeightedOffer(candidates, .25), Is.SameAs(heavy));
        Assert.That(RepairOrderSystem.SelectWeightedOffer(candidates, .999), Is.SameAs(heavy));
        Assert.That(RepairOrderSystem.SelectWeightedOffer(new[] { light }, .999), Is.SameAs(light));
    }

    [TestPrototypes]
    private const string ImpossibleDamageProfile = """
        - type: repairDamageProfile
          id: RepairDamageTestImpossible
          events: [RepairDamageFloorCollapse]
          minEvents: 1
          maxEvents: 1
          maxGenerationAttempts: 2
          maxEventRolls: 2
          minDamageFraction: 0.1
          maxDamageFraction: 0.2
          maxRemovedFloorFraction: 0
          maxRemovedAnchoredEntityFraction: 0
          minChangedRequirements: 1
          minRemainingFloor: 2
          minimumEventSeparation: 0
        """;

    [Test]
    public async Task OffersFillConfiguredLimitAndHaveIndependentLifetime()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var station = CreateStation(server.EntMan);
            var orders = server.System<RepairOrderSystem>();
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            try
            {
                station.Comp.PoolInitialized = false;
                orders.Update(0);
                var eligible = server.ProtoMan.EnumeratePrototypes<RepairOrderPrototype>().Count(p => float.IsFinite(p.Weight) && p.Weight > 0);
                var expected = Math.Min(eligible, station.Comp.AvailableOfferCount);
                Assert.That(station.Comp.AvailableOfferCount, Is.EqualTo(RepairOrderStationComponent.MaximumAvailableOffers));
                Assert.That(station.Comp.Available.Count, Is.EqualTo(expected));
                var initial = station.Comp.Available.Values.OrderBy(o => o.RuntimeId).ToArray();
                Assert.That(initial.Select(o => o.RuntimeId).Distinct().Count(), Is.EqualTo(initial.Length));
                Assert.That(initial.Select(o => o.Prototype).Distinct().Count(), Is.EqualTo(initial.Length));
                Assert.That(initial.Select(o => o.DamageSeed).Distinct().Count(), Is.EqualTo(initial.Length));
                orders.GenerateOffer(station);
                Assert.That(station.Comp.Available.Values, Is.EquivalentTo(initial), "A full pool must not change.");
                initial[0].ExpiresAt = now;
                station.Comp.NextOffer = now + station.Comp.OfferInterval;
                orders.Update(0);
                Assert.That(station.Comp.Available.ContainsKey(initial[0].RuntimeId), Is.False);
                Assert.That(station.Comp.Available.Count, Is.EqualTo(expected), "Vacant slots refill immediately.");
                station.Comp.NextOffer = now;
                orders.Update(0);
                Assert.That(station.Comp.Available.Count, Is.EqualTo(expected));
                foreach (var surviving in initial.Skip(1))
                    Assert.That(station.Comp.Available[surviving.RuntimeId], Is.SameAs(surviving));
                Assert.That(station.Comp.Available.Values.Select(o => o.Prototype).Distinct().Count(), Is.EqualTo(station.Comp.Available.Count));
                station.Comp.Available.Clear();
                station.Comp.AvailableOfferCount = 1;
                orders.GenerateOffer(station);
                orders.GenerateOffer(station);
                Assert.That(station.Comp.Available.Count, Is.EqualTo(1));
                station.Comp.Available.Clear();
                station.Comp.AvailableOfferCount = 100;
                orders.GenerateOffer(station);
                Assert.That(station.Comp.Available.Count, Is.EqualTo(Math.Min(eligible, RepairOrderStationComponent.MaximumAvailableOffers)), "The hard cap also applies to oversized configuration.");
            }
            finally { server.EntMan.DeleteEntity(station.Owner); }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AcceptPreservesOtherOffersAndGenerationFailureIsAtomic(bool failGeneration)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        bool ExpectedLog(string sawmill, LogEvent log) => IsAbortLog(sawmill, log) ||
            sawmill == "repair_orders" && log.Level == LogEventLevel.Warning &&
            (log.RenderMessage().Contains("Repair damage rejected:") || log.RenderMessage().Contains("station already has an active order") ||
             log.RenderMessage().Contains("failed: DamageFailed"));
        pair.ServerLogHandler.JudgeLog += ExpectedLog;
        try
        {
            await server.WaitAssertion(() =>
            {
                var station = CreateStation(server.EntMan);
                server.System<StationSystem>().AddGridToStation(station.Owner, map.Grid);
                var console = server.EntMan.SpawnEntity("RepairOrdersConsole", map.GridCoords);
                server.EntMan.RemoveComponent<AccessReaderComponent>(console);
                var actor = server.EntMan.SpawnEntity(null, map.GridCoords);
                var orders = server.System<RepairOrderSystem>();
                orders.GenerateOffer(station);
                var available = station.Comp.Available.Values.OrderBy(o => o.RuntimeId).ToArray();
                var offer = available[0];
                var prototype = server.ProtoMan.Index(offer.Prototype);
                var originalProfile = prototype.DamageProfile;
                var beforeGrids = server.EntMan.EntityQuery<MapGridComponent>().Select(c => c.Owner).ToHashSet();
                var beforeMaps = server.EntMan.EntityQuery<MapComponent>().Select(c => c.Owner).ToHashSet();
                var beforeBlueprints = server.EntMan.EntityQuery<RepairBlueprintComponent>().Select(c => c.Owner).ToHashSet();
                EntityUid generatedGrid = default;
                try
                {
                    if (failGeneration) prototype.DamageProfile = "RepairDamageTestImpossible";
                    var seed = offer.DamageSeed;
                    var request = new RepairOrderAcceptMessage(offer.RuntimeId) { Actor = actor, UiKey = RepairOrderUiKey.Key };
                    server.EntMan.EventBus.RaiseLocalEvent(console, request);
                    Assert.That(station.Comp.Accepting, Is.False);
                    if (failGeneration)
                    {
                        Assert.That(station.Comp.Active, Is.Null);
                        Assert.That(station.Comp.Available.Values, Is.EquivalentTo(available));
                        Assert.That(offer.DamageSeed, Is.EqualTo(seed));
                        Assert.That(server.EntMan.EntityQuery<MapGridComponent>().Select(c => c.Owner), Is.EquivalentTo(beforeGrids));
                        Assert.That(server.EntMan.EntityQuery<MapComponent>().Select(c => c.Owner), Is.EquivalentTo(beforeMaps));
                        Assert.That(server.EntMan.EntityQuery<RepairBlueprintComponent>().Select(c => c.Owner), Is.EquivalentTo(beforeBlueprints));
                        return;
                    }
                    Assert.That(station.Comp.Active, Is.Not.Null);
                    var active = station.Comp.Active!;
                    generatedGrid = active.GridUid;
                    Assert.That(active.DamageSeed, Is.EqualTo(seed));
                    Assert.That(active.DamageGeneration!.SelectedEvents, Is.Not.Empty);
                    foreach (var retained in available.Skip(1))
                        Assert.That(station.Comp.Available[retained.RuntimeId], Is.SameAs(retained));
                    Assert.That(station.Comp.Available.Count, Is.EqualTo(available.Length));
                    var afterAccept = station.Comp.Available.Values.ToArray();
                    server.EntMan.EventBus.RaiseLocalEvent(console,
                        new RepairOrderAcceptMessage(available[1].RuntimeId) { Actor = actor, UiKey = RepairOrderUiKey.Key });
                    Assert.That(station.Comp.Active, Is.SameAs(active));
                    Assert.That(station.Comp.Available.Values, Is.EquivalentTo(afterAccept));
                    var completed = new CompletedRepairOrder(active.RuntimeId, active.Prototype, active.CompletedTasks,
                        active.TotalTasks, active.CurrentPoints, active.MaxPoints, 0, RepairOrderResult.Completed, false, null,
                        Array.Empty<RepairOrderRewardResult>());
                    Assert.That(orders.TryCommitCompletion(station.Owner, active, completed, out _), Is.True);
                    Assert.That(completed.DamageGeneration, Is.SameAs(active.DamageGeneration));
                    Assert.That(station.Comp.Available.Values, Is.EquivalentTo(afterAccept));
                    Assert.That(server.System<SharedUserInterfaceSystem>().TryGetUiState<RepairOrderBoundUserInterfaceState>(
                        console, RepairOrderUiKey.Key, out var ui), Is.True);
                    Assert.That(ui!.Available.All(e => e.DamageEvents.Length == 0), Is.True, "No damage diagnosis exists before Accept.");
                }
                finally
                {
                    prototype.DamageProfile = originalProfile;
                    if (generatedGrid.IsValid())
                    {
                        station.Comp.Active = null;
                        server.System<RepairOrderValidationSystem>().DiscardPreparedSession(generatedGrid);
                        server.EntMan.DeleteEntity(generatedGrid);
                    }
                    server.EntMan.DeleteEntity(station.Owner);
                }
            });
        }
        finally { pair.ServerLogHandler.JudgeLog -= ExpectedLog; }
        await pair.CleanReturnAsync();
    }
}
