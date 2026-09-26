// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

public static class RepairTechnicalExclusion
{
    // A cost cap and a penalty for each full ten waived requirements are intentionally independent.
    public static int MaxWaivedPoints(int maxPoints) => Math.Max(0, maxPoints) / 2;
    public static bool CanAdd(int waivedPoints, int maxWaivedPoints, int points)
        => points > 0 && waivedPoints >= 0 && (long) waivedPoints + points <= maxWaivedPoints;
    public static int PenaltyPercent(int count) => Math.Min(100, Math.Max(0, count) / 10);
    public static int FinalPoints(int rawPoints, int count)
        => (int) ((long) Math.Max(0, rawPoints) * (100 - PenaltyPercent(count)) / 100);
}

[Serializable, NetSerializable]
public readonly record struct RepairExclusionTotals(int Count, int WaivedPoints, int MaxWaivedPoints, int RawPoints)
{
    public int PenaltyPercent => RepairTechnicalExclusion.PenaltyPercent(Count);
    public int FinalPoints => RepairTechnicalExclusion.FinalPoints(RawPoints, Count);
    public int PenaltyPoints => Math.Max(0, RawPoints) - FinalPoints;
}

/// <summary>Only opaque order/requirement identifiers cross the client-to-server boundary.</summary>
[Serializable, NetSerializable]
public sealed class RepairAnalyzerWaiverRequest : EntityEventArgs
{
    public readonly NetEntity Grid;
    public readonly int RuntimeId;
    public readonly int RequirementId;
    public readonly bool Cancel;
    public RepairAnalyzerWaiverRequest(NetEntity grid, int runtimeId, int requirementId, bool cancel)
    {
        Grid = grid;
        RuntimeId = runtimeId;
        RequirementId = requirementId;
        Cancel = cancel;
    }
}
