// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Server.DeadSpace._Soyuz.MedicalOrders;
using Content.Server.Station.Systems;
using Content.Shared.DeadSpace._Soyuz.MedicalOrders;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.MedicalOrders;

[TestFixture]
public sealed class MedicalOrderTest
{
    private static readonly ProtoId<MedicalOrderConfigPrototype> MedicalOrdersConfig = "SoyuzMedicalOrders";

    [Test]
    public void WeightedSelectionUsesExactIntegerIntervals()
    {
        var candidates = new[] { 1, 3, 2 };
        Assert.That(MedicalOrderSystem.SelectWeighted(candidates, x => x, 0), Is.EqualTo(1));
        Assert.That(MedicalOrderSystem.SelectWeighted(candidates, x => x, 1), Is.EqualTo(3));
        Assert.That(MedicalOrderSystem.SelectWeighted(candidates, x => x, 3), Is.EqualTo(3));
        Assert.That(MedicalOrderSystem.SelectWeighted(candidates, x => x, 4), Is.EqualTo(2));
        Assert.That(MedicalOrderSystem.SelectWeighted(candidates, x => x, 5), Is.EqualTo(2));
    }

    [Test]
    public void MarketPricesIntegrateDeliveriesAndRecoverDemand()
    {
        var entry = new MedicalReagentMarketEntry
        {
            Reagent = "Bicaridine",
            BasePrice = 1m,
            MinimumPrice = 0.1m,
            ReputationPerUnit = 0.2m,
            SaturationVolume = 100m,
            RecoveryPerMinute = 10m,
        };
        var whole = MedicalOrderSystem.GetMarketReturn(entry, 0m, 400m);
        decimal points = 0;
        decimal reputation = 0;
        for (var i = 0; i < 40; i++)
        {
            var part = MedicalOrderSystem.GetMarketReturn(entry, i * 10m, 10m);
            points += part.Points;
            reputation += part.Reputation;
        }

        Assert.Multiple(() =>
        {
            Assert.That(whole.Points, Is.EqualTo(85m));
            Assert.That(whole.Reputation, Is.EqualTo(17m));
            Assert.That(points, Is.EqualTo(whole.Points));
            Assert.That(reputation, Is.EqualTo(whole.Reputation));
            Assert.That(MedicalOrderSystem.GetMarketReturn(entry, 400m, 10m).Points, Is.EqualTo(1m));
            Assert.That(MedicalOrderSystem.RecoverMarketPressure(entry, 100m, TimeSpan.FromMinutes(5)),
                Is.EqualTo(50m));
            Assert.That(MedicalOrderSystem.RecoverMarketPressure(entry, 100m, TimeSpan.FromMinutes(10)),
                Is.Zero);
            Assert.That(MedicalOrderSystem.RecoverMarketPressure(entry, 100m, TimeSpan.FromMinutes(-1)),
                Is.EqualTo(100m));
        });
    }

    [Test]
    public async Task PatientOffersUseConfiguredCatalogAndRefreshKeepsActiveSnapshot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var station = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.AddComponent<StationDataComponent>(station);
            server.System<StationSystem>().AddGridToStation(station, map.Grid);
            var reagentMachine = entMan.SpawnEntity("SoyuzMedicalReagentOrderMachine", map.GridCoords);
            var receiver = entMan.SpawnEntity("SoyuzMedicalPatientReceiver", map.GridCoords);
            var patient = entMan.SpawnEntity("MobHuman", map.GridCoords);
            try
            {
                var orders = server.System<MedicalOrderSystem>();
                orders.Update(0);
                var config = server.ProtoMan.Index(MedicalOrdersConfig);
                var state = entMan.GetComponent<MedicalOrderStationComponent>(station);

                Assert.That(state.PatientOffers.Count, Is.EqualTo(config.PatientOfferCount));
                Assert.That(state.PatientOffers.Keys.OrderBy(id => id),
                    Is.EqualTo(Enumerable.Range(1, config.PatientOfferCount)));
                var roll = 0;
                foreach (var difficulty in config.Difficulties)
                {
                    Assert.That(MedicalOrderSystem.SelectWeighted(config.Difficulties, d => d.Weight, roll),
                        Is.SameAs(difficulty));
                    Assert.That(MedicalOrderSystem.SelectWeighted(config.Difficulties, d => d.Weight,
                        roll + difficulty.Weight - 1), Is.SameAs(difficulty));
                    roll += difficulty.Weight;
                }

                Assert.That(config.MarketReagents.Any(r => r.Enabled), Is.True);
                Assert.That(state.Machines, Does.Contain(reagentMachine));
                var allowedDamages = config.Damages.Where(d => d.CanGenerate)
                    .ToDictionary(d => d.DamageType.Id);
                foreach (var offer in state.PatientOffers.Values)
                {
                    var minimum = config.MinDamageEntries;
                    var maximum = config.MaxDamageEntries;
                    Assert.That(offer.Patient, Is.True);
                    Assert.That(offer.Lines.Count, Is.InRange(minimum, maximum));
                    Assert.That(offer.Lines.Select(l => l.ID).Distinct().Count(), Is.EqualTo(offer.Lines.Count));
                    Assert.That(offer.MaximumScore, Is.EqualTo(offer.Lines.Sum(l => l.Amount * l.PointsPerUnit)));
                    Assert.That(offer.TimeLimit, Is.EqualTo(config.Difficulties[offer.Difficulty].TimeLimit));
                    Assert.That(offer.MaximumScore,
                        Is.InRange(config.Difficulties[offer.Difficulty].MinScore,
                            config.Difficulties[offer.Difficulty].MaxScore));
                    foreach (var line in offer.Lines)
                    {
                        Assert.That(allowedDamages.TryGetValue(line.ID, out var damage), Is.True);
                        Assert.That(line.Amount, Is.InRange(damage!.MinAmount, damage.MaxAmount));
                    }
                }

                var accepted = state.PatientOffers.Values.First();
                var now = server.ResolveDependency<IGameTiming>().CurTime;
                state.PatientActive = new MedicalOrderActive
                {
                    Offer = accepted,
                    Terminal = receiver,
                    Patient = patient,
                    AcceptedAt = now,
                    Deadline = now + accepted.TimeLimit,
                };
                var marketEntry = config.MarketReagents.First(r => r.Enabled);
                var demand = new MedicalReagentMarketDemand { Pressure = 100m, UpdatedAt = now };
                state.MarketDemand[marketEntry.Reagent.Id] = demand;
                state.MarketPointRemainder = 0.25m;
                var oldPatientIds = state.PatientOffers.Keys.ToHashSet();
                state.NextRefresh = now;
                orders.Update(0);

                Assert.That(state.PatientActive, Is.Not.Null);
                Assert.That(state.PatientActive!.Offer, Is.SameAs(accepted));
                Assert.That(state.PatientActive.Deadline, Is.EqualTo(now + accepted.TimeLimit));
                Assert.That(state.PatientOffers.Count, Is.EqualTo(config.PatientOfferCount));
                Assert.That(state.MarketDemand[marketEntry.Reagent.Id], Is.SameAs(demand));
                Assert.That(demand.Pressure, Is.EqualTo(100m));
                Assert.That(state.MarketPointRemainder, Is.EqualTo(0.25m));
                Assert.That(state.PatientOffers.Keys.Intersect(oldPatientIds), Is.Empty);
                Assert.That(state.NextRefresh, Is.EqualTo(now + config.OfferRefreshInterval));
                state.PatientActive = null;
            }
            finally
            {
                entMan.DeleteEntity(patient);
                entMan.DeleteEntity(receiver);
                entMan.DeleteEntity(reagentMachine);
                entMan.DeleteEntity(station);
            }
        });

        await pair.CleanReturnAsync();
    }
}
