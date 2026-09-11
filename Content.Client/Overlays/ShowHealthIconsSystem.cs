using Content.Shared.Atmos.Rotting;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Overlays;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Damage.Components;

namespace Content.Client.Overlays;

/// <summary>
/// Shows a healthy icon on mobs.
/// </summary>
public sealed class ShowHealthIconsSystem : EquipmentHudSystem<ShowHealthIconsComponent>
{
    [Dependency] private readonly IPrototypeManager _prototypeMan = default!;
    [Dependency] private readonly SharedRottingSystem _rotting = default!;

    [ViewVariables]
    public HashSet<string> DamageContainers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamageableComponent, GetStatusIconsEvent>(OnGetStatusIconsEvent);
        SubscribeLocalEvent<ShowHealthIconsComponent, AfterAutoHandleStateEvent>(OnHandleState);
    }

    protected override void UpdateInternal(RefreshEquipmentHudEvent<ShowHealthIconsComponent> component)
    {
        base.UpdateInternal(component);

        DamageContainers.Clear();
        foreach (var comp in component.Components)
        {
            foreach (var damageContainerId in comp.DamageContainers)
            {
                DamageContainers.Add(damageContainerId);
            }
        }
    }

    protected override void DeactivateInternal()
    {
        base.DeactivateInternal();

        DamageContainers.Clear();
    }

    private void OnHandleState(Entity<ShowHealthIconsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshOverlay();
    }

    private void OnGetStatusIconsEvent(Entity<DamageableComponent> entity, ref GetStatusIconsEvent args)
    {
        if (!IsActive)
            return;

        var healthIcons = DecideHealthIcons(entity);

        args.StatusIcons.AddRange(healthIcons);
    }

private IReadOnlyList<HealthIconPrototype> DecideHealthIcons(Entity<DamageableComponent> entity)
{
    var damageableComponent = entity.Comp;

    if (damageableComponent.DamageContainerID == null ||
        !DamageContainers.Contains(damageableComponent.DamageContainerID))
    {
        return Array.Empty<HealthIconPrototype>();
    }

    var result = new List<HealthIconPrototype>();

    if (damageableComponent?.DamageContainerID == "Biological")
    {
        if (TryComp<MobStateComponent>(entity, out var state))
        {
//DS14-Soyuz-start
            if (state.CurrentState == MobState.Dead)
            {
                int effectiveStage = 1;

                // Проверяем наличие компонента PerishableComponent
                if (TryComp<PerishableComponent>(entity, out var perishableComp))
                {
                    int perishStage = _rotting.PerishStage((entity, perishableComp), 4);
                    effectiveStage = perishStage == 0 ? 1 : perishStage;
                }
                else if (TryComp<RottingComponent>(entity, out var rottingComp))
                {
                    int rotStage = _rotting.RotStage(entity, rottingComp);
                    effectiveStage = rotStage == 0 ? 1 : rotStage;
                }

                if (effectiveStage > 4)
                {
                    if (_prototypeMan.TryIndex<HealthIconPrototype>(damageableComponent.RottingIcon, out var rottingIcon))
                    {
                        result.Add(rottingIcon);
                    }
                    return result;
                }

                effectiveStage = Math.Clamp(effectiveStage, 1, 4);
                int iconIndex = effectiveStage - 1;

                if (iconIndex < damageableComponent.RottingStageIcons.Count)
                {
                    string iconId = damageableComponent.RottingStageIcons[iconIndex];

                    if (_prototypeMan.TryIndex<HealthIconPrototype>(iconId, out var icon))
                    {
                        result.Add(icon);
                    }
                    else if (_prototypeMan.TryIndex<HealthIconPrototype>(damageableComponent.RottingIcon, out var fallbackIcon))
                    {
                        result.Add(fallbackIcon);
                    }
                }
                else if (_prototypeMan.TryIndex<HealthIconPrototype>(damageableComponent.RottingIcon, out var fallbackIcon))
                {
                    result.Add(fallbackIcon);
                }
            }
            else if (damageableComponent.HealthIcons.TryGetValue(state.CurrentState, out var value))
            {
                if (_prototypeMan.TryIndex<HealthIconPrototype>(value, out var icon))
                {
//DS14-Soyuz-end
                    result.Add(icon);
                }
            }
        }
    }

    return result;
}
}
