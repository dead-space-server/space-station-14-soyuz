using Content.Shared.Inventory.Events;
// using Content.Shared.Tag; // DS14-Soyuz
using Content.Shared.Humanoid;
using Content.Shared._NF.Clothing.Components; // DS14-Soyuz

namespace Content.Shared._DV.Harpy;

public sealed class HarpyVisualsSystem : EntitySystem
{
    // [Dependency] private readonly TagSystem _tagSystem = default!; // DS14-Soyuz
    [Dependency] private readonly SharedHumanoidAppearanceSystem _humanoidSystem = default!;

    //    [ValidatePrototypeId<TagPrototype>] // DS14-Soyuz
    //    private const string HarpyWingsTag = "HidesHarpyWings"; // DS14-Soyuz

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HarpySingerComponent, DidEquipEvent>(OnDidEquipEvent);
        SubscribeLocalEvent<HarpySingerComponent, DidUnequipEvent>(OnDidUnequipEvent);
    }

    private void OnDidEquipEvent(EntityUid uid, HarpySingerComponent component, DidEquipEvent args)
    {
        if (args.Slot == "outerClothing" && HasComp<HarpyHideWingsComponent>(args.Equipment)) // DS14-Soyuz: Swap tag to comp
        {
            _humanoidSystem.SetLayerVisibility(uid, HumanoidVisualLayers.RArm, false); // DS14-Soyuz
            _humanoidSystem.SetLayerVisibility(uid, HumanoidVisualLayers.Tail, false);
        }
    }

    private void OnDidUnequipEvent(EntityUid uid, HarpySingerComponent component, DidUnequipEvent args)
    {
        if (args.Slot == "outerClothing" && HasComp<HarpyHideWingsComponent>(args.Equipment)) // DS14-Soyuz: Swap tag to comp
        {
            _humanoidSystem.SetLayerVisibility(uid, HumanoidVisualLayers.RArm, true); // DS14-Soyuz
            _humanoidSystem.SetLayerVisibility(uid, HumanoidVisualLayers.Tail, true);
        }
    }
}
