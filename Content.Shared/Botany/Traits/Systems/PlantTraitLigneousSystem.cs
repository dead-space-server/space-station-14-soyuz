using Content.Shared.Botany.Components;
using Content.Shared.Botany.Events;
using Content.Shared.Botany.Systems;
using Content.Shared.Botany.Traits.Components;
using Content.Shared.Interaction;
using Content.Shared.Kitchen.Components; // DS14-Soyuz
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;

namespace Content.Shared.Botany.Traits.Systems;

/// <inheritdoc cref="PlantTraitLigneousComponent"/>
public sealed partial class PlantTraitLigneousSystem : EntitySystem
{
    // DS14-start
    [Dependency] private readonly PlantHarvestSystem _plantHarvest = default!;
    [Dependency] private readonly PlantHolderSystem _plantHolder = default!;
    [Dependency] private readonly PlantTraySystem _plantTray = default!; // DS14-Soyuz
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;

    private EntityQuery<PlantHolderComponent> _holderQuery;
    // DS14-end

    public override void Initialize()
    {
        // DS14-start: current engine uses explicit event subscriptions and query initialization.
        base.Initialize();
        SubscribeLocalEvent<PlantTraitLigneousComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<PlantTrayComponent, InteractUsingEvent>(OnTrayInteractUsing); // DS14-Soyuz
        _holderQuery = GetEntityQuery<PlantHolderComponent>();
        // DS14-end

        SubscribeLocalEvent<PlantTraitLigneousComponent, DoHarvestEvent>(OnDoHarvest, before: [typeof(PlantHarvestSystem)]);
    }

    private void OnInteractUsing(Entity<PlantTraitLigneousComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!_holderQuery.TryComp(ent.Owner, out var holder)) // DS14
            return;

        TryHarvestWithTool(ent.Owner, ent.Comp, holder, ref args);
    }

    // DS14-Soyuz start: allow sharp tools to harvest ligneous plants through their tray.
    private void OnTrayInteractUsing(Entity<PlantTrayComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_plantTray.TryGetPlant(ent.AsNullable(), out var plantUid) ||
            !TryComp<PlantTraitLigneousComponent>(plantUid, out var ligneous) ||
            !_holderQuery.TryComp(plantUid, out var holder))
            return;

        TryHarvestWithTool(plantUid.Value, ligneous, holder, ref args);
    }

    private void TryHarvestWithTool(EntityUid plant, PlantTraitLigneousComponent ligneous,
        PlantHolderComponent holder, ref InteractUsingEvent args)
    {
        if (!holder.ReadyForHarvest)
            return;

        if (_plantHolder.IsDead(plant))
        {
            _popup.PopupCursor(Loc.GetString("plant-component-dead-plant-message"), args.User);
            return;
        }

        // Hatchets and scythes use Sharp, while saws expose the configured tool quality.
        var harvestToolQuality = ligneous.HarvestToolQuality;
        if (!HasComp<SharpComponent>(args.Used) &&
            (!harvestToolQuality.HasValue || !_tool.HasQuality(args.Used, harvestToolQuality.Value)))
        {
            _popup.PopupCursor(Loc.GetString("plant-component-ligneous-cant-harvest-message"), args.User);
            return;
        }

        _plantHarvest.TryHandleHarvest(plant, args.User);
        args.Handled = true;
    }
    // DS14-Soyuz end

    private void OnDoHarvest(Entity<PlantTraitLigneousComponent> ent, ref DoHarvestEvent args)
    {
        _popup.PopupCursor(Loc.GetString("plant-component-ligneous-cant-harvest-message"), args.User);
        args.Cancel();
    }
}
