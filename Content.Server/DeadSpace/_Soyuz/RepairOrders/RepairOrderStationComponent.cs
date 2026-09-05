using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Runtime order pool shared by every repair order console belonging to a station.
/// </summary>
[RegisterComponent]
public sealed partial class RepairOrderStationComponent : Component
{
    [DataField]
    public TimeSpan OfferInterval = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan OfferLifetime = TimeSpan.FromMinutes(5);

    [ViewVariables]
    public TimeSpan NextOffer;

    [ViewVariables]
    public readonly Dictionary<int, AvailableRepairOrder> Available = new();

    [ViewVariables]
    public ActiveRepairOrder? Active;

    [ViewVariables]
    public CompletedRepairOrder? Completed;

    /// <summary>
    /// Terminal repair grids which are waiting for their remaining players to leave before deletion.
    /// </summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> PendingCleanupGrids = new();

    /// <summary>
    /// Last known usable repair-orders console position, stored relative to its station grid so it remains useful
    /// after the console entity itself is deleted.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates? LastRepairConsoleCoordinates;

    [ViewVariables]
    public bool Accepting;

    [ViewVariables]
    public bool Completing;

    [ViewVariables]
    public bool PoolInitialized;

    [ViewVariables]
    public int NextRuntimeId = 1;

    [ViewVariables]
    public ProtoId<RepairOrderPrototype>? LastGeneratedPrototype;
}

[DataDefinition]
public sealed partial class AvailableRepairOrder
{
    [ViewVariables]
    public int RuntimeId;

    [ViewVariables]
    public ProtoId<RepairOrderPrototype> Prototype;

    [ViewVariables]
    public TimeSpan ExpiresAt;

    public AvailableRepairOrder(int runtimeId, ProtoId<RepairOrderPrototype> prototype, TimeSpan expiresAt)
    {
        RuntimeId = runtimeId;
        Prototype = prototype;
        ExpiresAt = expiresAt;
    }
}

[DataDefinition]
public sealed partial class ActiveRepairOrder
{
    [ViewVariables]
    public int RuntimeId;

    [ViewVariables]
    public ProtoId<RepairOrderPrototype> Prototype;

    [ViewVariables]
    public EntityUid GridUid;

    /// <summary>
    /// Console through which this order was activated. Its UID is only a preferred live anchor; the station also
    /// retains grid-local coordinates for use after the console is deleted.
    /// </summary>
    [ViewVariables]
    public EntityUid? ActivationConsole;

    [ViewVariables]
    public TimeSpan StartedAt;

    [ViewVariables]
    public TimeSpan ExpiresAt;

    [ViewVariables]
    public int CompletedTasks;

    [ViewVariables]
    public int TotalTasks;

    [ViewVariables]
    public bool BlueprintReady;

    [ViewVariables]
    public int CurrentPoints;

    [ViewVariables]
    public int MaxPoints;

    /// <summary>
    /// True once deadline revalidation has frozen the terminal Expired snapshot.
    /// Delivery retries must never recalculate progress or rewards after this point.
    /// </summary>
    [ViewVariables]
    public bool ExpirationFrozen;

    [ViewVariables]
    public int ExpiredRewardBudget;

    [ViewVariables]
    public TimeSpan NextExpirationAttempt;

    [ViewVariables]
    public readonly HashSet<EntityUid> ExpirationAdditionalGrids = new();

    /// <summary>
    /// Reward selection is frozen on the first valid completion attempt so a failed physical delivery can retry
    /// the same earned result instead of rolling a different reward set.
    /// </summary>
    [ViewVariables]
    public List<RepairOrderRewardResult>? PendingRewards;

    public ActiveRepairOrder(int runtimeId, ProtoId<RepairOrderPrototype> prototype, EntityUid gridUid)
    {
        RuntimeId = runtimeId;
        Prototype = prototype;
        GridUid = gridUid;
    }
}

[DataDefinition]
public sealed partial class CompletedRepairOrder
{
    [ViewVariables]
    public int RuntimeId;

    [ViewVariables]
    public ProtoId<RepairOrderPrototype> Prototype;

    [ViewVariables]
    public int CompletedTasks;

    [ViewVariables]
    public int TotalTasks;

    [ViewVariables]
    public int FinalPoints;

    [ViewVariables]
    public int MaxPoints;

    [ViewVariables]
    public int RepairPercent;

    [ViewVariables]
    public int RewardBudget;

    [ViewVariables]
    public RepairOrderResult Result;

    [ViewVariables]
    public bool Delivered;

    [ViewVariables]
    public readonly List<EntityUid> DeliveryContainers = new();

    [ViewVariables]
    public readonly List<RepairOrderRewardResult> Rewards = new();

    public CompletedRepairOrder(
        int runtimeId,
        ProtoId<RepairOrderPrototype> prototype,
        int completedTasks,
        int totalTasks,
        int finalPoints,
        int maxPoints,
        int rewardBudget,
        RepairOrderResult result,
        bool delivered,
        IEnumerable<EntityUid>? deliveryContainers,
        IEnumerable<RepairOrderRewardResult> rewards)
    {
        RuntimeId = runtimeId;
        Prototype = prototype;
        CompletedTasks = completedTasks;
        TotalTasks = totalTasks;
        FinalPoints = finalPoints;
        MaxPoints = maxPoints;
        RepairPercent = RepairOrderProgress.CalculatePercent(completedTasks, totalTasks);
        RewardBudget = rewardBudget;
        Result = result;
        Delivered = delivered;
        if (deliveryContainers != null)
            DeliveryContainers.AddRange(deliveryContainers);
        Rewards.AddRange(rewards);
    }
}

[DataDefinition]
public sealed partial class RepairOrderRewardResult
{
    [ViewVariables]
    public ProtoId<RepairRewardPrototype> Reward;

    [ViewVariables]
    public int Count;

    public RepairOrderRewardResult(ProtoId<RepairRewardPrototype> reward, int count)
    {
        Reward = reward;
        Count = count;
    }
}
