using System.Linq;
using Content.Shared.DeadSpace.UniformAccessories;
using Content.Shared.DeadSpace.UniformAccessories.Components;
using Robust.Shared.Containers;

namespace Content.Server.DeadSpace.UniformAccessories;

public sealed class UniformAccessorySystem : SharedUniformAccessorySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<UniformAccessoryHolderComponent, EntityTerminatingEvent>(OnTerminating);
    }

    private void OnTerminating(EntityUid holder,
        UniformAccessoryHolderComponent holderComp,
        ref EntityTerminatingEvent args)
    {
        var container = holderComp.AccessoryContainer;
        if (container == null || container.ContainedEntities.Count == 0)
            return;

        // DS14-Soyuz-start
        foreach (var accessory in container.ContainedEntities.ToArray())
        {
            if (!TerminatingOrDeleted(accessory))
                QueueDel(accessory);
        }
        // DS14-Soyuz-end
    }

}
