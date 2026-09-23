// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

// DS14-Soyuz start
using Content.Server.DeadSpace._Soyuz.MeteorDefense;
using Content.Server.Radio;
using Content.Server.Station.Systems;
using Content.Shared.DeadSpace._Soyuz.MeteorDefense;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
// DS14-Soyuz end

namespace Content.IntegrationTests.Tests.DeadSpace.MeteorDefense;

// DS14-Soyuz start
[TestFixture]
public sealed class MeteorDefenseInterceptTest
{
    private sealed class RadioSpy : IEntityEventSubscriber
    {
        public EntityUid Beacon;
        public int EngineeringMessages;

        public void OnSend(ref RadioSendAttemptEvent args)
        {
            if (args.MessageSource == Beacon && args.Channel.ID == "Engineering")
                EngineeringMessages++;
        }
    }

    [Test]
    public async Task OnlySuccessfulInterceptConsumesChargeAndSendsRadio()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var spy = new RadioSpy();

        await server.WaitAssertion(() =>
        {
            var station = em.SpawnEntity("TestStation", MapCoordinates.Nullspace);
            server.System<StationSystem>().AddGridToStation(station, map.Grid);
            var beacon = em.SpawnEntity("MeteorDefenseBeacon", map.GridCoords);
            spy.Beacon = beacon;
            server.System<SharedTransformSystem>().AnchorEntity(beacon, em.GetComponent<TransformComponent>(beacon));
            Assert.That(em.GetComponent<TransformComponent>(beacon).Anchored, Is.True);
            var battery = em.GetComponent<BatteryComponent>(beacon);
            var batterySystem = server.System<SharedBatterySystem>();
            var defense = server.System<MeteorDefenseSystem>();
            em.EventBus.SubscribeEvent<RadioSendAttemptEvent>(EventSource.Local, spy, spy.OnSend);

            batterySystem.SetCharge((beacon, battery), 16_000_000);
            var meteor = new EntProtoId("Meteor");
            var disabled = new MeteorInterceptAttemptEvent(map.Grid.Owner, meteor);
            em.EventBus.RaiseEvent(EventSource.Local, ref disabled);
            Assert.That(disabled.Cancelled, Is.False);
            Assert.That(spy.EngineeringMessages, Is.Zero);

            Assert.That(defense.TrySetEnabled(beacon, true, out _), Is.True);
            var cancelled = new MeteorInterceptAttemptEvent(map.Grid.Owner, meteor, true);
            em.EventBus.RaiseEvent(EventSource.Local, ref cancelled);
            Assert.That(batterySystem.GetCharge((beacon, battery)), Is.EqualTo(16_000_000));
            Assert.That(spy.EngineeringMessages, Is.Zero);

            batterySystem.SetCharge((beacon, battery), 0);
            var empty = new MeteorInterceptAttemptEvent(map.Grid.Owner, meteor);
            em.EventBus.RaiseEvent(EventSource.Local, ref empty);
            Assert.That(empty.Cancelled, Is.False);
            Assert.That(spy.EngineeringMessages, Is.Zero);

            batterySystem.SetCharge((beacon, battery), 16_000_000);
            var invalidTarget = new MeteorInterceptAttemptEvent(map.MapUid, meteor);
            em.EventBus.RaiseEvent(EventSource.Local, ref invalidTarget);
            Assert.That(invalidTarget.Cancelled, Is.False);
            Assert.That(spy.EngineeringMessages, Is.Zero);

            var success = new MeteorInterceptAttemptEvent(map.Grid.Owner, meteor);
            em.EventBus.RaiseEvent(EventSource.Local, ref success);
            Assert.That(success.Cancelled, Is.True);
            Assert.That(batterySystem.GetCharge((beacon, battery)), Is.EqualTo(8_000_000));
            Assert.That(spy.EngineeringMessages, Is.EqualTo(1));
            Assert.That(server.System<UserInterfaceSystem>().TryGetUiState<MeteorDefenseBoundUserInterfaceState>(
                beacon, MeteorDefenseUiKey.Key, out var uiState), Is.True);
            Assert.That(uiState!.CurrentCharge, Is.EqualTo(8_000_000));
            Assert.That(uiState.ReadyIntercepts, Is.EqualTo(1));

            em.EventBus.UnsubscribeEvents(spy);
        });

        await pair.CleanReturnAsync();
    }
}
// DS14-Soyuz end
