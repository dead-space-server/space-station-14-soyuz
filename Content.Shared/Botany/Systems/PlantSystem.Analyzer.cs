using System.Linq;
using Content.Shared.Botany.Components;
using Content.Shared.Botany.Events;
using Content.Shared.Botany.Traits.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.Botany.Systems;

public sealed partial class PlantSystem
{
    private void OnPlantAnalyzerAttempt(Entity<PlantComponent> ent, ref PlantAnalyzerAttemptEvent args)
    {
        if (args.Handled)
            return;

        args.PlantData = ent.Owner;
        args.PlantProtoId = MetaData(ent.Owner).EntityPrototype?.ID;
        args.Handled = true;
    }

    public int GetNextHarvestAge(Entity<PlantComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp, false)
            || !_holderQuery.TryComp(ent.Owner, out var holder))
            return 0;

        if (holder.ReadyForHarvest)
            return holder.Age;

        var productionTicks = (int)MathF.Floor(ent.Comp.Production);
        var productionStartAge = holder.Age < ent.Comp.Maturation
            ? (int)MathF.Ceiling(ent.Comp.Maturation)
            : holder.LastHarvest + 1;

        return Math.Max(holder.Age, productionStartAge + productionTicks);
    }

    public string GetPlantExamineMarkup(Entity<PlantComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !_holderQuery.TryComp(ent.Owner, out var holder))
            return string.Empty;

        var markup = new List<string>();
        if (_plantHolder.IsDead(ent.Owner))
            markup.Add(Loc.GetString("plant-component-dead-plant-matter-message"));

        if (_plantHolder.GetHealthThreshold(ent.Owner))
        {
            markup.Add(Loc.GetString(
                "plant-component-something-already-growing-low-health-message",
                ("healthState",
                    Loc.GetString(holder.Age > ent.Comp.Lifespan
                        ? "plant-component-plant-old-adjective"
                        : "plant-component-plant-unhealthy-adjective"))));
        }

        return string.Join("\n", markup);
    }

    public IEnumerable<LocId> GetPlantMutationDescriptions(EntityUid uid)
    {
        return AllComps<PlantTraitsComponent>(uid)
            .OrderBy(trait => trait.GetType().FullName)
            .Select(trait => trait.TraitState)
            .OfType<LocId>();
    }

}

public sealed partial class PlantTraySystem
{
    private void OnPlantAnalyzerAttempt(Entity<PlantTrayComponent> ent, ref PlantAnalyzerAttemptEvent args)
    {
        if (!TryGetPlant(ent.AsNullable(), out var plant))
            return;

        RaiseLocalEvent(plant.Value, ref args);
        args.Target ??= plant.Value;
    }
}
