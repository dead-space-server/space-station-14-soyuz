#nullable enable
using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.GameObjects;
using Robust.Shared.Reflection;

namespace Content.IntegrationTests.Tests.DeadSpace._Soyuz.RepairOrders;

// Registered only on the server: the subscribed component does not exist on the client.
[Reflect(false)]
public sealed class RepairOrderActivationTestSystem : EntitySystem
{
    public Action<RepairOrderActivatedEvent>? OnActivated;

    public override void Initialize()
    {
        SubscribeLocalEvent<RepairOrderStationComponent, RepairOrderActivatedEvent>(OnActivation);
    }

    private void OnActivation(Entity<RepairOrderStationComponent> station, ref RepairOrderActivatedEvent args)
    {
        OnActivated?.Invoke(args);
    }
}
