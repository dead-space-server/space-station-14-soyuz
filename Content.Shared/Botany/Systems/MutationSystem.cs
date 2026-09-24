using JetBrains.Annotations;
using System.Linq;
using Content.Shared.Atmos;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Events; // DS-14 Soyuz
using Content.Shared.Botany.Traits.Components;
using Content.Shared.Chemistry.Components; // DS-14 Soyuz
using Content.Shared.Chemistry.EntitySystems; // DS-14 Soyuz
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint; // DS-14 Soyuz
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager;

namespace Content.Shared.Botany.Systems;

/// <summary>
/// Handles plant mutations, including random mutation effects, crossbreeding, and
/// inheritance of plant properties and traits from pollen.
/// </summary>
public sealed partial class PlantMutationSystem : EntitySystem
{
    private static readonly ProtoId<RandomPlantMutationListPrototype> RandomPlantMutations = "RandomPlantMutations";
    private RandomPlantMutationListPrototype _randomMutations = default!;

    // DS14-start
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly BotanySystem _botany = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly PlantSystem _plant = default!;
    [Dependency] private readonly PlantTraySystem _plantTray = default!;
    [Dependency] private readonly SharedEntityEffectsSystem _entityEffects = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!; // DS-14 Soyuz

    private EntityQuery<PlantChemicalsComponent> _chemicalsQuery;
    private EntityQuery<PlantComponent> _plantQuery;
    // DS14-end

    public override void Initialize()
    {
        // DS14-start: initialize EntityQuery explicitly on the current engine.
        base.Initialize();
        _randomMutations = _prototypeManager.Index(RandomPlantMutations);
        _chemicalsQuery = GetEntityQuery<PlantChemicalsComponent>();
        _plantQuery = GetEntityQuery<PlantComponent>();
        // DS-14 Soyuz start: validate the directed graph on load and reload.
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnMutationPrototypesReloaded);
        ValidateSpeciesMutationGraph();
        // DS-14 Soyuz end
        // DS14-end
    }

    /// <summary>
    /// For each random mutation, see if it occurs on this plant this check.
    /// </summary>
    [PublicAPI]
    public void CheckRandomMutations(Entity<PlantComponent?> ent, float severity)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        foreach (var mutation in _randomMutations.Mutations)
        {
            if (Random(Math.Min(mutation.BaseOdds * severity, 1.0f))) // DS14
            {
                if (mutation.AppliesToPlant)
                    _entityEffects.TryApplyEffect(ent, mutation.Effect);

                // Stat adjustments do not persist by being an attached effect, they just change the stat.
                if (mutation.Persists && ent.Comp.Mutations.All(m => m.Name != mutation.Name))
                    ent.Comp.Mutations.Add(mutation);
            }
        }
    }

    // DS-14 Soyuz start: conditional directed species mutations
    /// <summary>
    /// Runs exactly once after the existing ChangeSpecies effect succeeds. Requirements
    /// are checked before independent chance rolls; weight only breaks multiple successes.
    /// </summary>
    [PublicAPI]
    public void TrySpeciesChange(Entity<PlantDataComponent?> plant)
    {
        if (!_net.IsServer || !Resolve(plant, ref plant.Comp, false) || plant.Comp.Mutations.Count == 0)
            return;

        if (!_plantQuery.TryComp(plant.Owner, out var genetics))
            return;

        Entity<PlantTrayComponent>? tray = _plant.TryGetTray(plant.Owner, out var trayEnt) ? trayEnt : null;

        var environment = new PlantMutationEnvironmentEvent();
        RaiseLocalEvent(plant.Owner, ref environment);
        var passed = new List<PlantSpeciesMutation>();
        foreach (var mutation in plant.Comp.Mutations)
        {
            if (RequirementsMet(plant.Owner, genetics, tray, environment, mutation.Requirements)
                && Random(mutation.Chance))
                passed.Add(mutation);
        }

        if (passed.Count == 0)
            return;

        var totalWeight = passed.Sum(m => m.Weight);
        var roll = _random.NextFloat() * totalWeight;
        var selected = passed[^1];
        foreach (var mutation in passed)
        {
            roll -= mutation.Weight;
            if (roll > 0f)
                continue;

            selected = mutation;
            break;
        }

        // Charge only after the selected species was actually spawned.
        if (SpeciesChange(plant, selected.Target) && tray is { } selectedTray)
            ConsumeReagents(selectedTray, selected.Requirements.Reagents);
    }

    private bool RequirementsMet(
        EntityUid plantUid,
        PlantComponent genetics,
        Entity<PlantTrayComponent>? tray,
        PlantMutationEnvironmentEvent environment,
        PlantSpeciesMutationRequirements requirements)
    {
        if (!Within(genetics.Potency, requirements.MinPotency, requirements.MaxPotency)
            || !Within(genetics.GeneticInstability, requirements.MinGeneticInstability, requirements.MaxGeneticInstability))
            return false;

        if (requirements.MinWater != null || requirements.MaxWater != null
            || requirements.MinNutrients != null || requirements.MaxNutrients != null)
        {
            if (tray is not { } plantedTray
                || !Within(plantedTray.Comp.WaterLevel, requirements.MinWater, requirements.MaxWater)
                || !Within(plantedTray.Comp.NutritionLevel, requirements.MinNutrients, requirements.MaxNutrients))
                return false;
        }

        foreach (var trait in requirements.RequiredTraits)
        {
            if (!Factory.TryGetRegistration(trait, out var registration) || !HasComp(plantUid, registration.Type))
                return false;
        }

        if (requirements.Reagents.Count > 0)
        {
            if (tray is not { } plantedTray
                || !_solutions.TryGetSolution(plantedTray.Owner, plantedTray.Comp.SoilSolutionName, out _, out var solution))
                return false;

            foreach (var reagent in requirements.Reagents)
            {
                var amount = solution.Contents
                    .Where(entry => entry.Reagent.Prototype == reagent.Id.Id)
                    .Sum(entry => entry.Quantity.Float());
                if (amount < reagent.MinAmount)
                    return false;
            }
        }

        if (requirements.Environment is not { } external)
            return true;

        if ((external.MinTemperature != null || external.MaxTemperature != null || external.Gases.Count > 0)
            && environment.Atmosphere is not { } atmosphere)
            return false;

        if (external.MinTemperature != null || external.MaxTemperature != null)
        {
            if (!Within(environment.Atmosphere!.Temperature, external.MinTemperature, external.MaxTemperature))
                return false;
        }

        foreach (var gas in external.Gases)
        {
            if (!Within(environment.Atmosphere!.GetMoles(gas.Id), gas.MinAmount, gas.MaxAmount))
                return false;
        }

        if (external.MinLight != null || external.MaxLight != null)
        {
            if (environment.Light is not { } light || !Within(light, external.MinLight, external.MaxLight))
                return false;
        }

        return true;
    }

    private void ConsumeReagents(Entity<PlantTrayComponent> tray, List<PlantMutationReagentRequirement> reagents)
    {
        if (reagents.All(r => r.ConsumeAmount <= 0f)
            || !_solutions.TryGetSolution(tray.Owner, tray.Comp.SoilSolutionName, out var solutionEnt, out var solution))
            return;

        foreach (var reagent in reagents)
        {
            var remaining = FixedPoint2.New(reagent.ConsumeAmount);
            if (remaining <= FixedPoint2.Zero)
                continue;

            foreach (var entry in solution.Contents.ToArray())
            {
                if (entry.Reagent.Prototype != reagent.Id.Id)
                    continue;

                var removed = _solutions.RemoveReagent(solutionEnt.Value, entry.Reagent, FixedPoint2.Min(remaining, entry.Quantity));
                remaining -= removed;
                if (remaining <= FixedPoint2.Zero)
                    break;
            }
        }
    }

    private static bool Within(float value, float? min, float? max) =>
        (min == null || value >= min.Value) && (max == null || value <= max.Value);

    /// <summary>
    /// Replaces the current plant species with a new one from prototype,
    /// preserving lifecycle state.
    /// </summary>
    private bool SpeciesChange(Entity<PlantDataComponent?> oldPlant, EntProtoId newPlantProto)
    {
        if (!Resolve(oldPlant, ref oldPlant.Comp, false))
            return false;

        // Clone state via snapshot and apply to new plant.
        var snapshot = _botany.ClonePlantSnapshotData(oldPlant.Owner, cloneLifecycle: true);
        if (snapshot == null)
            return false;

        var newPlantUid = SpawnAtPosition(newPlantProto, Transform(oldPlant.Owner).Coordinates);
        _botany.ApplyPlantSnapshotData(snapshot, newPlantUid, cloneLifecycle: true);
        _botany.DeletePlantSnapshot(snapshot);

        ChemicalsSpeciesChange(newPlantUid, newPlantProto);

        if (_plant.TryGetTray(oldPlant.Owner, out var trayEnt))
            _plantTray.PlantingPlantInTray(trayEnt.AsNullable(), newPlantUid);
        else
            _plant.PlantingPlant(newPlantUid);

        _plant.ForceUpdate(newPlantUid);
        QueueDel(oldPlant);
        return true;
    }
    // DS-14 Soyuz end

    private void ChemicalsSpeciesChange(EntityUid plantUid, EntProtoId plantProto)
    {
        if (!_botany.TryGetPlantComponent<PlantChemicalsComponent>(null, plantProto, out var newPlantChemicals)
            || !_chemicalsQuery.TryComp(plantUid, out var oldPlantChemicals)) // DS14
            return;

        var oldPlant = oldPlantChemicals.Chemicals;
        var newPlant = newPlantChemicals.Chemicals;

        // Adding the new chemicals from the new species.
        foreach (var otherChem in newPlant)
        {
            oldPlant.TryAdd(otherChem.Key, otherChem.Value);
        }

        // Removing the inherent chemicals from the old species. Leaving mutated/crossbred ones intact.
        foreach (var originalChem in oldPlant)
        {
            if (!newPlant.ContainsKey(originalChem.Key) && originalChem.Value.Inherent)
                oldPlant.Remove(originalChem.Key);
        }

        Dirty(plantUid, oldPlantChemicals);
    }

    /// <summary>
    /// Combines mutations from the pollen and target plants.
    /// </summary>
    [PublicAPI]
    public void CrossMutations(EntityUid pollenPlant, EntProtoId? pollenProtoId, EntityUid targetPlant)
    {
        if (!_botany.TryGetPlantComponent<PlantComponent>(pollenPlant, pollenProtoId, out var pollenCore) ||
            !_plantQuery.TryComp(targetPlant, out var targetCore)) // DS14
            return;

        // LINQ Explanation
        // For the list of mutation effects on both plants, use a 50% chance to pick each one.
        // Union all of the chosen mutations into one list, and pick ones with a Distinct (unique) name.
        targetCore.Mutations = targetCore.Mutations.Where(_ => Random(0.5f)).Union(pollenCore.Mutations.Where(_ => Random(0.5f))).DistinctBy(m => m.Name).ToList(); // DS14

        // Hybrids have a high chance of being seedless. Balances very
        // effective hybrid crossings.
        if (pollenProtoId != null
            && pollenProtoId != MetaData(targetPlant).EntityPrototype?.ID
            && Random(0.7f)) // DS14
        {
            EnsureComp<PlantTraitSeedlessComponent>(targetPlant);
        }
    }

    /// <summary>
    /// Combines chemical properties from the pollen and target plants.
    /// </summary>
    [PublicAPI]
    public void CrossChemicals(EntityUid uid, ref Dictionary<ProtoId<ReagentPrototype>, PlantChemQuantity> val, Dictionary<ProtoId<ReagentPrototype>, PlantChemQuantity> other)
    {
        // Go through chemicals from the pollen in swab
        foreach (var otherChem in other)
        {
            // if both have same chemical, randomly pick potency ratio from the two.
            if (val.TryGetValue(otherChem.Key, out var value))
            {
                val[otherChem.Key] = Random(0.5f) ? otherChem.Value : value; // DS14
            }
            // if target plant doesn't have this chemical, has 50% chance to add it.
            else
            {
                if (Random(0.5f)) // DS14
                {
                    var fixedChem = otherChem.Value;
                    fixedChem.Inherent = false;
                    val.Add(otherChem.Key, fixedChem);
                }
            }
        }

        // if the target plant has chemical that the pollen in swab does not, 50% chance to remove it.
        foreach (var thisChem in val)
        {
            if (!other.ContainsKey(thisChem.Key))
            {
                if (Random(0.5f)) // DS14
                {
                    if (val.Count > 1)
                    {
                        val.Remove(thisChem.Key);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Combines gas properties from the pollen and target plants.
    /// </summary>
    [PublicAPI]
    public void CrossGasses(EntityUid uid, ref Dictionary<Gas, float> val, Dictionary<Gas, float> other)
    {
        // Go through gasses from the pollen in swab
        foreach (var otherGas in other)
        {
            // if both have same gas, randomly pick ammount from the two.
            if (val.TryGetValue(otherGas.Key, out var value))
            {
                val[otherGas.Key] = Random(0.5f) ? otherGas.Value : value; // DS14
            }
            // if target plant doesn't have this gas, has 50% chance to add it.
            else
            {
                if (Random(0.5f)) // DS14
                {
                    val.Add(otherGas.Key, otherGas.Value);
                }
            }
        }
        // if the target plant has gas that the pollen in swab does not, 50% chance to remove it.
        foreach (var thisGas in val)
        {
            if (!other.ContainsKey(thisGas.Key))
            {
                if (Random(0.5f)) // DS14
                {
                    val.Remove(thisGas.Key);
                }
            }
        }
    }

    /// <summary>
    /// Selects a floating value from the plant or pollen.
    /// </summary>
    [PublicAPI]
    // DS14-start
    public void CrossFloat(ref float val, float other)
    {
        val = Random(0.5f) ? val : other;
    }
    // DS14-end

    /// <summary>
    /// Selects an integer value from the plant or pollen.
    /// </summary>
    [PublicAPI]
    // DS14-start
    public void CrossInt(ref int val, int other)
    {
        val = Random(0.5f) ? val : other;
    }
    // DS14-end

    /// <summary>
    /// Selects a Boolean value from the plant or pollen.
    /// </summary>
    [PublicAPI]
    // DS14-start
    public void CrossBool(ref bool val, bool other)
    {
        val = Random(0.5f) ? val : other;
    }
    // DS14-end

    /// <summary>
    /// Crosses plant trait components from pollen to the target plant.
    /// </summary>
    [PublicAPI]
    public void CrossTrait(EntityUid val, EntityUid pollenData)
    {
        foreach (var component in AllComps(pollenData))
        {
            if (component is not PlantTraitsComponent)
                continue;

            if (HasComp(val, component.GetType()))
                continue;

            if (Random(0.5f)) // DS14
                AddComp(val, _serialization.CreateCopy(component, notNullableOverride: true));
        }
    }

    // DS14-start - plant mutations are authoritative and each check needs an independent roll.
    private bool Random(float p)
    {
        if (!_net.IsServer)
            return false;

        return _random.Prob(p);
    }
    // DS14-end
}
