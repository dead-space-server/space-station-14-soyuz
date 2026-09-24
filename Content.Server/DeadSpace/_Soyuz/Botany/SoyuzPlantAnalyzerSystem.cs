using Content.Shared.Botany.Components;
using Content.Shared.Botany.Items.Components;
using Content.Shared.Botany.Systems;
using Content.Shared.Botany.Traits.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.PowerCell;
using Content.Shared.Atmos;
using Content.Shared.Atmos.EntitySystems;
using Content.Shared.DeadSpace._Soyuz.Botany;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Server.DeadSpace._Soyuz.Botany;

public sealed class SoyuzPlantAnalyzerSystem : EntitySystem
{
    [Dependency] private readonly BotanySystem _botany = default!;
    [Dependency] private readonly SharedAtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly PowerCellSystem _power = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SoyuzPlantAnalyzerComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<SoyuzPlantAnalyzerComponent, SoyuzPlantAnalyzerDoAfterEvent>(OnScanFinished);
        SubscribeLocalEvent<SoyuzPlantAnalyzerComponent, SoyuzPlantAnalyzerSetMode>(OnModeChanged);
    }

    private void OnInteract(Entity<SoyuzPlantAnalyzerComponent> analyzer, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target == null || !args.CanReach || !_power.HasActivatableCharge(analyzer.Owner, user: args.User))
            return;

        var target = args.Target.Value;
        var valid = TryGetPlant(target, out _, out _);
        if (!valid)
            return;

        _doAfter.Cancel(analyzer.Comp.CurrentScan);
        var delay = analyzer.Comp.AdvancedScan ? analyzer.Comp.AdvancedScanDelay : analyzer.Comp.ScanDelay;
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            args.User,
            delay,
            new SoyuzPlantAnalyzerDoAfterEvent(),
            analyzer,
            target: target,
            used: analyzer)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        }, out analyzer.Comp.CurrentScan);
    }

    private void OnScanFinished(Entity<SoyuzPlantAnalyzerComponent> analyzer, ref SoyuzPlantAnalyzerDoAfterEvent args)
    {
        analyzer.Comp.CurrentScan = null;
        if (args.Cancelled || args.Handled || args.Args.Target == null)
            return;

        if (!_power.TryUseActivatableCharge(analyzer.Owner, user: args.Args.User))
            return;

        if (!TryGetPlant(args.Args.Target.Value, out var plantProtoId, out var snapshot, out var isPlant))
            return;

        if (!_botany.TryGetPlantComponent<PlantComponent>(snapshot, plantProtoId, out var plant) ||
            !_botany.TryGetPlantComponent<PlantDataComponent>(snapshot, plantProtoId, out var plantData))
            return;

        if (!_ui.HasUi(analyzer, SoyuzPlantAnalyzerUiKey.Key))
            return;

        if (HasComp<ActorComponent>(args.Args.User))
            _ui.OpenUi(analyzer.Owner, SoyuzPlantAnalyzerUiKey.Key, args.Args.User);

        _ui.ServerSendUiMessage(analyzer.Owner, SoyuzPlantAnalyzerUiKey.Key,
            BuildReport(plant, plantData, plantProtoId, snapshot, args.Args.Target.Value, isPlant, analyzer.Comp.AdvancedScan));
        args.Handled = true;
    }

    private void OnModeChanged(Entity<SoyuzPlantAnalyzerComponent> analyzer, ref SoyuzPlantAnalyzerSetMode args)
    {
        analyzer.Comp.AdvancedScan = args.Advanced;
    }

    private bool TryGetPlant(EntityUid target, out EntProtoId plantProtoId, out EntityUid? snapshot)
    {
        return TryGetPlant(target, out plantProtoId, out snapshot, out _);
    }

    private bool TryGetPlant(EntityUid target, out EntProtoId plantProtoId, out EntityUid? snapshot, out bool isPlant)
    {
        isPlant = false;
        plantProtoId = default;
        snapshot = null;
        if (TryComp<SeedComponent>(target, out var seedComponent))
        {
            plantProtoId = seedComponent.PlantProtoId;
            snapshot = seedComponent.PlantData;
            return true;
        }

        if (HasComp<PlantComponent>(target) && MetaData(target).EntityPrototype is { } prototype)
        {
            plantProtoId = prototype.ID;
            snapshot = target;
            isPlant = true;
            return true;
        }

        return false;
    }

    private SoyuzPlantAnalyzerReport BuildReport(
        PlantComponent plant,
        PlantDataComponent data,
        EntProtoId plantProtoId,
        EntityUid? snapshot,
        EntityUid target,
        bool isPlant,
        bool advanced)
    {
        _botany.TryGetPlantComponent<PlantChemicalsComponent>(snapshot, plantProtoId, out var chemicals);
        _botany.TryGetPlantComponent<PlantConsumeExudeGasComponent>(snapshot, plantProtoId, out var gases);
        _botany.TryGetPlantComponent<PlantHarvestComponent>(snapshot, plantProtoId, out var harvest);
        _botany.TryGetPlantComponent<PlantGrowthComponent>(snapshot, plantProtoId, out var growth);
        _botany.TryGetPlantComponent<PlantAtmosphericComponent>(snapshot, plantProtoId, out var atmosphere);
        _botany.TryGetPlantComponent<PlantToxinsComponent>(snapshot, plantProtoId, out var toxins);
        _botany.TryGetPlantComponent<PlantWeedPestComponent>(snapshot, plantProtoId, out var weeds);
        var harvestType = harvest?.HarvestRepeat ?? HarvestType.NoRepeat;

        return new SoyuzPlantAnalyzerReport
        {
            Target = GetNetEntity(target),
            IsPlant = isPlant,
            SeedNameKey = data.Name.ToString(),
            Chemicals = chemicals?.Chemicals.Keys.Select(ReagentName).ToArray() ?? Array.Empty<string>(),
            ConsumedGases = gases?.ConsumeGasses.Keys.Select(GasName).ToArray() ?? Array.Empty<string>(),
            EmittedGases = gases?.ExudeGasses.Keys.Select(GasName).ToArray() ?? Array.Empty<string>(),
            PossibleMutations = data.MutationPrototypes
                .Where(id => _prototypes.TryIndex(id, out EntityPrototype? _))
                .Select(id => _prototypes.Index(id).Name)
                .ToArray(),
            HarvestType = harvestType.ToString(),
            Endurance = plant.Endurance,
            Yield = plant.Yield,
            Potency = plant.Potency,
            Lifespan = plant.Lifespan,
            Maturation = plant.Maturation,
            Production = plant.Production,
            GrowthStages = plant.GrowthStages,
            Advanced = advanced,
            NutrientConsumption = growth?.NutrientConsumption ?? 0,
            WaterConsumption = growth?.WaterConsumption ?? 0,
            IdealHeat = atmosphere is null ? 0 : (atmosphere.LowHeatTolerance + atmosphere.HighHeatTolerance) / 2,
            HeatTolerance = atmosphere is null ? 0 : (atmosphere.HighHeatTolerance - atmosphere.LowHeatTolerance) / 2,
            ToxinsTolerance = toxins?.ToxinsTolerance ?? 0,
            LowPressureTolerance = atmosphere?.LowPressureTolerance ?? 0,
            HighPressureTolerance = atmosphere?.HighPressureTolerance ?? 0,
            PestTolerance = weeds?.PestTolerance ?? 0,
            WeedTolerance = weeds?.WeedTolerance ?? 0,
            Seedless = _botany.TryGetPlantComponent<PlantTraitSeedlessComponent>(snapshot, plantProtoId, out _),
            Ligneous = _botany.TryGetPlantComponent<PlantTraitLigneousComponent>(snapshot, plantProtoId, out _),
            CanScream = _botany.TryGetPlantComponent<PlantTraitScreamComponent>(snapshot, plantProtoId, out _),
        };
    }

    private string ReagentName(ProtoId<ReagentPrototype> reagent)
    {
        return _prototypes.TryIndex(reagent, out ReagentPrototype? prototype)
            ? prototype.LocalizedName
            : reagent.Id;
    }

    private string GasName(Gas gas)
    {
        return Loc.GetString(_atmosphere.GetGas(gas).Name);
    }
}
