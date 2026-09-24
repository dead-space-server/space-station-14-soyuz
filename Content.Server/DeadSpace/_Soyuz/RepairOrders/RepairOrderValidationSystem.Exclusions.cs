// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

public sealed partial class RepairOrderValidationSystem
{
    [Dependency] private readonly IGameTiming _waiverTiming = default!;

    /// <summary>Called only after analyzer ownership, range and engineering-access authorization.</summary>
    public bool TrySetTechnicalExclusion(EntityUid grid, int runtimeId, int requirementId, bool cancel, out string error)
    {
        error = "repair-orders-waiver-unavailable";
        if (!TryComp<RepairBlueprintComponent>(grid, out var blueprint) || !blueprint.Ready ||
            !TryComp<RepairOrderStationComponent>(blueprint.Station, out var station) ||
            station.Active is not { } active || active.GridUid != grid || active.RuntimeId != runtimeId ||
            active.Prototype != blueprint.OrderPrototype || station.Accepting || station.Completing ||
            active.ExpirationFrozen || _waiverTiming.CurTime >= active.ExpiresAt || !RevalidateAll(grid)) return false;

        if (cancel)
        {
            if (!blueprint.WaivedRequirements.Remove(requirementId)) return false;
        }
        else
        {
            var task = blueprint.TasksByCell.Values.SelectMany(tasks => tasks).FirstOrDefault(t => t.RequirementId == requirementId);
            if (task == null || task.State == RepairTaskState.Correct || blueprint.WaivedRequirements.ContainsKey(requirementId)) return false;
            if (task.Points <= 0)
            {
                _sawmill.Error($"Cannot waive unresolved requirement {requirementId} of repair order {runtimeId} on {grid}: {task.Points} points.");
                return false;
            }
            if (!RepairTechnicalExclusion.CanAdd(blueprint.WaivedRequirements.Values.Sum(r => r.Points), blueprint.MaxWaivedPoints, task.Points))
            {
                error = "repair-orders-waiver-limit";
                return false;
            }
            blueprint.WaivedRequirements.Add(requirementId, new RepairWaivedRequirement(requirementId, task.Type,
                task.Cell, task.ExpectedEntityPrototype ?? task.ExpectedTilePrototype ?? string.Empty, task.Points));
        }
        // Explicit decisions survive normal revalidation; only this endpoint changes the overlay.
        active.PendingRewards = null;
        RevalidateAll(grid);
        _repairOrders.RefreshStationUis(blueprint.Station);
        error = string.Empty;
        return true;
    }
}
