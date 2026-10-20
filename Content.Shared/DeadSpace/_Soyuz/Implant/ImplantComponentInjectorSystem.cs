// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT
using Content.Shared.Implants;
using Robust.Shared.Containers;

namespace Content.Shared._Soyuz.Implant;

public sealed class ComponentInjectorImplantSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ComponentInjectorImplantComponent, ImplantImplantedEvent>(OnImplantInserted);
        SubscribeLocalEvent<ComponentInjectorImplantComponent, EntGotRemovedFromContainerMessage>(OnImplantExtracted);
    }

    private void OnImplantInserted(Entity<ComponentInjectorImplantComponent> implant, ref ImplantImplantedEvent args)
    {
        var targetEntity = args.Implanted;
        if (targetEntity == EntityUid.Invalid || Deleted(targetEntity))
            return;

        var injector = implant.Comp;

        foreach (var entry in injector.TargetComponents)
        {
            var componentType = _componentFactory.GetComponent(entry.Key).GetType();

            if (HasComp(targetEntity, componentType))
                continue;

            EntityManager.AddComponent(targetEntity, entry.Value);
            injector.InstalledComponents.Add(entry.Key, entry.Value);
        }
    }

    private void OnImplantExtracted(Entity<ComponentInjectorImplantComponent> implant, ref EntGotRemovedFromContainerMessage args)
    {
        var injector = implant.Comp;

        if (injector.InstalledComponents.Count == 0)
            return;

        EntityManager.RemoveComponents(args.Container.Owner, injector.InstalledComponents);

        injector.InstalledComponents.Clear();
    }
}