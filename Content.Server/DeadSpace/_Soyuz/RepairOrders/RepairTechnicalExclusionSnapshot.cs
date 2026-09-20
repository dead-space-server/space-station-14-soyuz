// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

public readonly record struct RepairRequirementKey(RepairTaskType Type, Vector2i Cell, string Prototype,
    Vector2 Position, Angle Rotation, int RequiredMatchingCount)
{
    public static RepairRequirementKey For(RepairTask task) => new(task.Type, task.Cell,
        task.ExpectedEntityPrototype ?? string.Empty, task.ExpectedLocalPosition, task.ExpectedLocalRotation,
        task.RequiredMatchingCount);
}

/// <summary>Safe immutable descriptor retained after grid cleanup; contains no entity UID.</summary>
public sealed record RepairWaivedRequirement(int Id, RepairTaskType Type, Vector2i Cell, string Prototype, int Points);

public sealed record RepairTechnicalExclusionSnapshot(ImmutableArray<RepairWaivedRequirement> Requirements,
    int MaxWaivedPoints, int RawPoints)
{
    public RepairExclusionTotals Totals => new(Requirements.Length, Requirements.Sum(r => r.Points), MaxWaivedPoints, RawPoints);
    public static RepairTechnicalExclusionSnapshot Capture(RepairBlueprintComponent blueprint) => new(
        blueprint.WaivedRequirements.Values.OrderBy(r => r.Id).ToImmutableArray(), blueprint.MaxWaivedPoints, blueprint.CurrentPoints);
}
