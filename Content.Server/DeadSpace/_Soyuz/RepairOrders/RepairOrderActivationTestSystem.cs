// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

#nullable enable
using Robust.Shared.GameObjects;
using Robust.Shared.Reflection;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

public sealed class RepairOrderActivationTestSystem : EntitySystem
{
    public Action<RepairOrderActivatedEvent>? OnActivated;

    public override void Initialize()
    {
        SubscribeLocalEvent<RepairOrderStationComponent, RepairOrderActivatedEvent>(OnActivation);
    }

    private void OnActivation(
        Entity<RepairOrderStationComponent> station,
        ref RepairOrderActivatedEvent args)
    {
        OnActivated?.Invoke(args);
    }
}
