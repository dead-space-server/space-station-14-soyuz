// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

public enum RepairDamagePlacement { AnyOccupied, Interior, Boundary, BoundaryToInterior, RepairValueTarget, DenseRepairableArea, BoundaryCorner }
public enum RepairDamageShape { Disc, Ellipse, Line, Strip, Rectangle, IrregularBlob, EdgeSegment, MultiPoint }
public enum RepairDamageSeverity { Light, Medium, Heavy }
public enum RepairDamageOperation { RemoveTile, RemoveAnchoredEntity, RemoveAnchoredEntitiesByRepairValueCategory, RemoveStructuralEntitiesInArea }

[Prototype]
public sealed partial class RepairDamageProfilePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
    [DataField(required: true)] public List<ProtoId<RepairDamageEventPrototype>> Events = new();
    [DataField(required: true)] public int MinEvents;
    [DataField(required: true)] public int MaxEvents;
    [DataField(required: true)] public int MaxGenerationAttempts;
    [DataField(required: true)] public int MaxEventRolls;
    [DataField(required: true)] public float MinDamageFraction;
    [DataField(required: true)] public float MaxDamageFraction;
    [DataField(required: true)] public float MaxRemovedFloorFraction;
    [DataField(required: true)] public float MaxRemovedAnchoredEntityFraction;
    [DataField(required: true)] public int MinChangedRequirements;
    [DataField(required: true)] public int MinRemainingFloor;
    [DataField(required: true)] public float MinimumEventSeparation;
    [DataField] public int MaxOccurrencesPerEvent = 1;
    [DataField] public RepairDamageSeverity MaxSeverity = RepairDamageSeverity.Heavy;
}

[Prototype]
public sealed partial class RepairDamageEventPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = string.Empty;
    [DataField(required: true)] public LocId Name;
    [DataField(required: true)] public LocId Description;
    [DataField(required: true)] public float Weight;
    [DataField(required: true)] public RepairDamageSeverity Severity;
    [DataField(required: true)] public RepairDamagePlacement Placement;
    /// <summary>Required center categories, also requiring an actual removal of such equipment.</summary>
    [DataField] public List<ProtoId<RepairValueTagPrototype>> Targets = new();
    [DataField] public bool BoundaryTargets;
    [DataField] public bool RequiresFloorRemoval;
    [DataField] public int MinCenters = 1;
    [DataField] public int MaxCenters = 1;
    [DataField] public float MinimumCenterSeparation = 2;
    [DataField] public int? MaxOccurrences;
    [DataField(required: true)] public List<RepairDamageStage> Stages = new();
}

[DataDefinition]
public sealed partial class RepairDamageStage
{
    [DataField(required: true)] public RepairDamageShape Shape;
    [DataField] public RepairDamageShape PointShape = RepairDamageShape.Disc;
    [DataField] public int MinRadius = 1;
    [DataField] public int MaxRadius = 2;
    [DataField] public int MinLength = 2;
    [DataField] public int MaxLength = 4;
    [DataField(required: true)] public List<RepairDamageAction> Operations = new();
}

[DataDefinition]
public sealed partial class RepairDamageAction
{
    [DataField(required: true)] public RepairDamageOperation Type;
    [DataField(required: true)] public float Chance;
    [DataField] public int MaxTargets = int.MaxValue;
    /// <summary>Restrict equipment removal to adjacent target cells reachable from the event center.</summary>
    [DataField] public bool ConnectedTargets;
    [DataField] public List<ProtoId<RepairValueTagPrototype>> Categories = new();
}

/// <summary>Configuration validation shared by generation and prototype integration tests.</summary>
public static class RepairDamageConfiguration
{
    public static List<string> Validate(IPrototypeManager prototypes)
    {
        var errors = new List<string>();
        static bool Fraction(float value) => float.IsFinite(value) && value >= 0 && value <= 1;
        foreach (var profile in prototypes.EnumeratePrototypes<RepairDamageProfilePrototype>())
        {
            if (profile.MinEvents < 1 || profile.MaxEvents < profile.MinEvents || profile.MaxGenerationAttempts < 1 ||
                profile.MaxEventRolls < profile.MaxEvents || profile.MinChangedRequirements < 1 || profile.MinRemainingFloor < 2 ||
                profile.MaxOccurrencesPerEvent < 1 || !float.IsFinite(profile.MinimumEventSeparation) || profile.MinimumEventSeparation < 0 ||
                !Enum.IsDefined(profile.MaxSeverity) || !Fraction(profile.MinDamageFraction) || profile.MinDamageFraction <= 0 ||
                !Fraction(profile.MaxDamageFraction) || profile.MaxDamageFraction >= 1 || profile.MinDamageFraction > profile.MaxDamageFraction ||
                !Fraction(profile.MaxRemovedFloorFraction) || !Fraction(profile.MaxRemovedAnchoredEntityFraction) ||
                profile.Events.Count == 0 || profile.Events.Distinct().Count() != profile.Events.Count)
                errors.Add($"Invalid repair damage profile {profile.ID}.");
            foreach (var id in profile.Events)
                if (!prototypes.HasIndex(id)) errors.Add($"{profile.ID}: missing damage event {id}.");
        }
        foreach (var ev in prototypes.EnumeratePrototypes<RepairDamageEventPrototype>())
        {
            if (!float.IsFinite(ev.Weight) || ev.Weight <= 0 || !Enum.IsDefined(ev.Placement) || !Enum.IsDefined(ev.Severity) ||
                ev.Stages.Count == 0 || ev.MinCenters < 1 || ev.MaxCenters < ev.MinCenters || ev.MaxOccurrences is <= 0 ||
                !float.IsFinite(ev.MinimumCenterSeparation) || ev.MinimumCenterSeparation < 0 ||
                ev.Placement == RepairDamagePlacement.RepairValueTarget && ev.Targets.Count == 0)
                errors.Add($"Invalid repair damage event {ev.ID}.");
            foreach (var stage in ev.Stages)
            {
                if (!Enum.IsDefined(stage.Shape) || !Enum.IsDefined(stage.PointShape) || stage.PointShape == RepairDamageShape.MultiPoint ||
                    stage.MinRadius < 0 || stage.MaxRadius < stage.MinRadius || stage.MinLength < 1 || stage.MaxLength < stage.MinLength ||
                    stage.Operations.Count == 0)
                    errors.Add($"{ev.ID}: invalid shape or empty operations.");
                foreach (var op in stage.Operations)
                {
                    if (!Enum.IsDefined(op.Type) || !Fraction(op.Chance) || op.Chance <= 0 || op.MaxTargets < 1 ||
                        (op.Type is RepairDamageOperation.RemoveAnchoredEntitiesByRepairValueCategory or RepairDamageOperation.RemoveStructuralEntitiesInArea) && op.Categories.Count == 0 ||
                        op.ConnectedTargets && (op.Type == RepairDamageOperation.RemoveTile || op.Chance != 1 || op.MaxTargets != int.MaxValue))
                        errors.Add($"{ev.ID}: invalid operation.");
                }
            }
            foreach (var category in ev.Targets.Concat(ev.Stages.SelectMany(s => s.Operations).SelectMany(o => o.Categories)))
                if (!prototypes.HasIndex(category)) errors.Add($"{ev.ID}: missing repair value category {category}.");
        }
        return errors;
    }
}
