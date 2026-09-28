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
    public async Task OffersUseConfiguredCatalogAndRefreshKeepsActiveSnapshot()
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
            try
            {
                var orders = server.System<MedicalOrderSystem>();
                orders.Update(0);
                var config = server.ProtoMan.Index(MedicalOrdersConfig);
                var state = entMan.GetComponent<MedicalOrderStationComponent>(station);

                Assert.That(state.ReagentOffers.Count, Is.EqualTo(config.ReagentOfferCount));
                Assert.That(state.PatientOffers.Count, Is.EqualTo(config.PatientOfferCount));
                Assert.That(state.ReagentOffers.Keys.OrderBy(id => id),
                    Is.EqualTo(Enumerable.Range(1, config.ReagentOfferCount)));
                Assert.That(state.PatientOffers.Keys.OrderBy(id => id),
                    Is.EqualTo(Enumerable.Range(1, config.PatientOfferCount)));

                var allowedReagents = config.Reagents.Where(r => r.CanGenerate)
                    .ToDictionary(r => r.Reagent.Id);
                var allowedDamages = config.Damages.Where(d => d.CanGenerate)
                    .ToDictionary(d => d.DamageType.Id);
                foreach (var offer in state.ReagentOffers.Values.Concat(state.PatientOffers.Values))
                {
                    var minimum = offer.Patient ? config.MinDamageEntries : config.MinReagentEntries;
                    var maximum = offer.Patient ? config.MaxDamageEntries : config.MaxReagentEntries;
                    Assert.That(offer.Lines.Count, Is.InRange(minimum, maximum));
                    Assert.That(offer.Lines.Select(l => l.ID).Distinct().Count(), Is.EqualTo(offer.Lines.Count));
                    Assert.That(offer.MaximumScore, Is.EqualTo(offer.Lines.Sum(l => l.Amount * l.PointsPerUnit)));
                    Assert.That(offer.TimeLimit, Is.EqualTo(config.Difficulties[offer.Difficulty].TimeLimit));
                    foreach (var line in offer.Lines)
                    {
                        if (offer.Patient)
                        {
                            Assert.That(allowedDamages.TryGetValue(line.ID, out var damage), Is.True);
                            Assert.That(line.Amount, Is.InRange(damage!.MinAmount, damage.MaxAmount));
                        }
                        else
                        {
                            Assert.That(allowedReagents.TryGetValue(line.ID, out var reagent), Is.True);
                            Assert.That(line.Amount, Is.InRange(reagent!.MinAmount, reagent.MaxAmount));
                        }
                    }
                }

                var accepted = state.ReagentOffers.Values.First();
                var now = server.ResolveDependency<IGameTiming>().CurTime;
                state.ReagentActive = new MedicalOrderActive
                {
                    Offer = accepted,
                    Terminal = reagentMachine,
                    AcceptedAt = now,
                    Deadline = now + accepted.TimeLimit,
                };
                var oldReagentIds = state.ReagentOffers.Keys.ToHashSet();
                var oldPatientIds = state.PatientOffers.Keys.ToHashSet();
                state.NextRefresh = now;
                orders.Update(0);

                Assert.That(state.ReagentActive, Is.Not.Null);
                Assert.That(state.ReagentActive!.Offer, Is.SameAs(accepted));
                Assert.That(state.ReagentActive.Deadline, Is.EqualTo(now + accepted.TimeLimit));
                Assert.That(state.ReagentOffers.Count, Is.EqualTo(config.ReagentOfferCount));
                Assert.That(state.PatientOffers.Count, Is.EqualTo(config.PatientOfferCount));
                Assert.That(state.ReagentOffers.Keys.Intersect(oldReagentIds), Is.Empty);
                Assert.That(state.PatientOffers.Keys.Intersect(oldPatientIds), Is.Empty);
                Assert.That(state.NextRefresh, Is.EqualTo(now + config.OfferRefreshInterval));
                state.ReagentActive = null;
            }
            finally
            {
                entMan.DeleteEntity(receiver);
                entMan.DeleteEntity(reagentMachine);
                entMan.DeleteEntity(station);
            }
        });

        await pair.CleanReturnAsync();
    }
}
