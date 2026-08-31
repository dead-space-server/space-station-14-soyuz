using Content.Shared.DeadSpace._Soyuz.RepairOrders;
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
