// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Robust.Shared.Serialization;
using System.Linq;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

[Serializable, NetSerializable]
public enum RepairOrderStatus : byte
{
    Available,
    Active,
    Completed,
}

[Serializable, NetSerializable]
public enum RepairOrderResult : byte
{
    Completed,
    Expired,
}

[Serializable, NetSerializable]
public enum RepairOrderUiKey : byte
{
    Key,
}

public static class RepairOrderProgress
{
    public static float CalculateFraction(int completedTasks, int totalTasks)
    {
        return totalTasks <= 0
            ? 1f
            : Math.Clamp((float) completedTasks / totalTasks, 0f, 1f);
    }

    public static int CalculatePercent(int completedTasks, int totalTasks)
    {
        return (int) MathF.Round(CalculateFraction(completedTasks, totalTasks) * 100f);
    }
}

[Serializable, NetSerializable]
public sealed class RepairOrderBuiEntry
{
    public readonly int RuntimeId;
    public readonly string PrototypeId;
    public readonly RepairOrderStatus Status;
    public readonly TimeSpan? ExpiresAt;
    public readonly int CompletedTasks;
    public readonly int TotalTasks;
    public readonly bool BlueprintReady;
    public readonly int CurrentPoints;
    public readonly int MaxPoints;
    public readonly string[] DamageEvents;
    public readonly RepairExclusionTotals Exclusions;

    public RepairOrderBuiEntry(
        int runtimeId,
        string prototypeId,
        RepairOrderStatus status,
        TimeSpan? expiresAt = null,
        int completedTasks = 0,
        int totalTasks = 0,
        bool blueprintReady = false,
        int currentPoints = 0,
        int maxPoints = 0,
        string[]? damageEvents = null,
        RepairExclusionTotals exclusions = default)
    {
        RuntimeId = runtimeId;
        PrototypeId = prototypeId;
        Status = status;
        ExpiresAt = expiresAt;
        CompletedTasks = completedTasks;
        TotalTasks = totalTasks;
        BlueprintReady = blueprintReady;
        CurrentPoints = currentPoints;
        MaxPoints = maxPoints;
        DamageEvents = damageEvents?.ToArray() ?? Array.Empty<string>();
        Exclusions = exclusions;
    }
}

[Serializable, NetSerializable]
public sealed class RepairOrderCompletedBuiEntry
{
    public readonly string[] DamageEvents;
    public readonly RepairExclusionTotals Exclusions;
    public readonly int RuntimeId;
    public readonly string PrototypeId;
    public readonly int CompletedTasks;
    public readonly int TotalTasks;
    public readonly int FinalPoints;
    public readonly int MaxPoints;
    public readonly int RepairPercent;
    public readonly int RewardBudget;
    public readonly RepairOrderResult Result;
    public readonly bool Delivered;
    public readonly List<RepairOrderRewardBuiEntry> Rewards;

    public RepairOrderCompletedBuiEntry(
        int runtimeId,
        string prototypeId,
        int completedTasks,
        int totalTasks,
        int finalPoints,
        int maxPoints,
        int repairPercent,
        int rewardBudget,
        RepairOrderResult result,
        bool delivered,
        List<RepairOrderRewardBuiEntry> rewards,
        string[]? damageEvents = null,
        RepairExclusionTotals exclusions = default)
    {
        DamageEvents = damageEvents?.ToArray() ?? Array.Empty<string>();
        Exclusions = exclusions;
        RuntimeId = runtimeId;
        PrototypeId = prototypeId;
        CompletedTasks = completedTasks;
        TotalTasks = totalTasks;
        FinalPoints = finalPoints;
        MaxPoints = maxPoints;
        RepairPercent = repairPercent;
        RewardBudget = rewardBudget;
        Result = result;
        Delivered = delivered;
        Rewards = rewards;
    }
}

[Serializable, NetSerializable]
public sealed class RepairOrderRewardBuiEntry
{
    public readonly string RewardPrototypeId;
    public readonly int Count;

    public RepairOrderRewardBuiEntry(string rewardPrototypeId, int count)
    {
        RewardPrototypeId = rewardPrototypeId;
        Count = count;
    }
}

[Serializable, NetSerializable]
public sealed class RepairOrderBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly List<RepairOrderBuiEntry> Available;
    public readonly RepairOrderBuiEntry? Active;
    public readonly RepairOrderCompletedBuiEntry? Completed;
    public readonly TimeSpan NextOffer;
    public readonly TimeSpan OfferInterval;
    public readonly bool Accepting;
    public readonly bool Completing;

    public RepairOrderBoundUserInterfaceState(
        List<RepairOrderBuiEntry> available,
        RepairOrderBuiEntry? active,
        RepairOrderCompletedBuiEntry? completed,
        TimeSpan nextOffer,
        TimeSpan offerInterval,
        bool accepting,
        bool completing)
    {
        Available = available;
        Active = active;
        Completed = completed;
        NextOffer = nextOffer;
        OfferInterval = offerInterval;
        Accepting = accepting;
        Completing = completing;
    }
}

/// <summary>
/// Requests activation by station-local runtime ID. All authoritative checks are server-side.
/// </summary>
[Serializable, NetSerializable]
public sealed class RepairOrderAcceptMessage : BoundUserInterfaceMessage
{
    public readonly int RuntimeId;

    public RepairOrderAcceptMessage(int runtimeId)
    {
        RuntimeId = runtimeId;
    }
}

/// <summary>
/// Requests finalization of the current station order by its runtime ID.
/// </summary>
[Serializable, NetSerializable]
public sealed class RepairOrderCompleteMessage : BoundUserInterfaceMessage
{
    public readonly int RuntimeId;

    public RepairOrderCompleteMessage(int runtimeId)
    {
        RuntimeId = runtimeId;
    }
}

/// <summary>
/// Requests a physical server-authored report for a station order by its runtime ID.
/// </summary>
[Serializable, NetSerializable]
public sealed class RepairOrderPrintReportMessage : BoundUserInterfaceMessage
{
    public readonly int RuntimeId;

    public RepairOrderPrintReportMessage(int runtimeId)
    {
        RuntimeId = runtimeId;
    }
}
