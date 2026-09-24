using System.Linq;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Traits.Components;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared.Botany.Systems;

public sealed partial class PlantMutationSystem
{
    private void OnMutationPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            ValidateSpeciesMutationGraph();
    }

    /// <summary>
    /// Validate the complete graph after prototype loading, including edges across files.
    /// A bad edge must fail loading instead of becoming a silent, unreachable mutation.
    /// </summary>
    private void ValidateSpeciesMutationGraph()
    {
        var graph = new Dictionary<string, List<string>>();
        foreach (var prototype in _prototypeManager.EnumeratePrototypes<EntityPrototype>())
        {
            if (prototype.Abstract ||
                !prototype.TryGetComponent<PlantDataComponent>(out var data, EntityManager.ComponentFactory))
                continue;

            var edges = new List<string>();
            var targets = new HashSet<string>();
            foreach (var mutation in data.Mutations)
            {
                var targetId = mutation.Target.Id;
                if (targetId == prototype.ID || !targets.Add(targetId))
                    throw Invalid(prototype.ID, $"self-link or duplicate target {targetId}");

                if (!_prototypeManager.TryIndex(mutation.Target, out EntityPrototype? target)
                    || target.Abstract
                    || !target.TryGetComponent<PlantDataComponent>(out _, EntityManager.ComponentFactory))
                    throw Invalid(prototype.ID, $"target {targetId} is not an existing plant");

                if (!float.IsFinite(mutation.Chance) || mutation.Chance <= 0f || mutation.Chance > 1f)
                    throw Invalid(prototype.ID, $"invalid chance for {targetId}");

                if (!float.IsFinite(mutation.Weight) || mutation.Weight <= 0f)
                    throw Invalid(prototype.ID, $"invalid weight for {targetId}");

                ValidateRequirements(prototype.ID, targetId, mutation.Requirements);
                edges.Add(targetId);
            }

            graph.Add(prototype.ID, edges);
        }

        var state = new Dictionary<string, byte>();
        foreach (var node in graph.Keys)
            Visit(node);

        void Visit(string node)
        {
            if (state.TryGetValue(node, out var seen))
            {
                if (seen == 1)
                    throw Invalid(node, "cycle in species mutation graph");
                return;
            }

            state[node] = 1;
            foreach (var target in graph[node])
                Visit(target);
            state[node] = 2;
        }
    }

    private void ValidateRequirements(string source, string target, PlantSpeciesMutationRequirements requirements)
    {
        CheckRange(source, target, "potency", requirements.MinPotency, requirements.MaxPotency);
        CheckRange(source, target, "geneticInstability", requirements.MinGeneticInstability, requirements.MaxGeneticInstability);
        CheckRange(source, target, "water", requirements.MinWater, requirements.MaxWater);
        CheckRange(source, target, "nutrients", requirements.MinNutrients, requirements.MaxNutrients);

        foreach (var trait in requirements.RequiredTraits)
        {
            if (!Factory.TryGetRegistration(trait, out var registration)
                || !typeof(PlantTraitsComponent).IsAssignableFrom(registration.Type))
                throw Invalid(source, $"unknown plant trait {trait} for {target}");
        }

        var reagents = new HashSet<string>();
        foreach (var reagent in requirements.Reagents)
        {
            if (!reagents.Add(reagent.Id.Id) || !_prototypeManager.HasIndex<ReagentPrototype>(reagent.Id.Id))
                throw Invalid(source, $"duplicate or unknown reagent {reagent.Id.Id} for {target}");

            if (!float.IsFinite(reagent.MinAmount) || reagent.MinAmount < 0f
                || !float.IsFinite(reagent.ConsumeAmount) || reagent.ConsumeAmount < 0f
                || reagent.ConsumeAmount > reagent.MinAmount)
                throw Invalid(source, $"invalid reagent amounts for {target}");
        }

        if (requirements.Environment is not { } environment)
            return;

        CheckRange(source, target, "temperature", environment.MinTemperature, environment.MaxTemperature);
        CheckRange(source, target, "light", environment.MinLight, environment.MaxLight);

        var gases = new HashSet<Content.Shared.Atmos.Gas>();
        foreach (var gas in environment.Gases)
        {
            if (!gases.Add(gas.Id)
                || !_prototypeManager.HasIndex<GasPrototype>(gas.Id.ToString()))
                throw Invalid(source, $"duplicate or unknown gas {gas.Id} for {target}");

            CheckRange(source, target, $"gas {gas.Id}", gas.MinAmount, gas.MaxAmount);
            if (gas.MinAmount < 0f)
                throw Invalid(source, $"negative gas requirement for {target}");
        }
    }

    private static void CheckRange(string source, string target, string name, float? min, float? max)
    {
        if ((min is { } lo && !float.IsFinite(lo))
            || (max is { } hi && !float.IsFinite(hi))
            || (min != null && max != null && min > max))
            throw Invalid(source, $"invalid {name} range for {target}");
    }

    private static InvalidOperationException Invalid(string source, string message) =>
        new($"Plant species mutation {source}: {message}");
}
