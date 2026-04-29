using Content.Shared.Inventory.Events;
// using Content.Shared.Tag; // DS14-Soyuz
using Content.Shared.Humanoid;
using Content.Shared._NF.Clothing.Components; // DS14-Soyuz

namespace Content.Shared._DV.Harpy;

public sealed class HarpyWingVisibilitySystem : EntitySystem
{
    // [Dependency] private readonly TagSystem _tagSystem = default!; // DS14-Soyuz
    [Dependency] private readonly SharedHumanoidAppearanceSystem _humanoidSystem = default!;

    //    [ValidatePrototypeId<TagPrototype>] // DS14-Soyuz
    //    private const string HarpyWingsTag = "HidesHarpyWings"; // DS14-Soyuz

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HarpyMidiSingerComponent, DidEquipEvent>(OnDidEquip);
        SubscribeLocalEvent<HarpyMidiSingerComponent, DidUnequipEvent>(OnDidUnequip);
    }

    private void OnDidEquip(EntityUid uid, HarpyMidiSingerComponent component, DidEquipEvent args)
    {
        if (args.Slot != "outerClothing" || !HasComp<HarpyWingOccluderComponent>(args.Equipment)) // DS14-Soyuz: Swap tag to comp
            return;

        SetWingVisibility(uid, false);
    }

    private void OnDidUnequip(EntityUid uid, HarpyMidiSingerComponent component, DidUnequipEvent args)
    {
        if (args.Slot != "outerClothing" || !HasComp<HarpyWingOccluderComponent>(args.Equipment)) // DS14-Soyuz: Swap tag to comp
            return;

        SetWingVisibility(uid, true);
    }

    private void SetWingVisibility(EntityUid uid, bool visible)
    {
        _humanoidSystem.SetLayerVisibility(uid, HumanoidVisualLayers.RArm, visible); // DS14-Soyuz
        _humanoidSystem.SetLayerVisibility(uid, HumanoidVisualLayers.Tail, visible);
    }
}
