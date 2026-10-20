// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

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
    public const int MaximumAvailableOffers = 15;

    [DataField]
    public int AvailableOfferCount = MaximumAvailableOffers;

    [DataField]
    public TimeSpan OfferInterval = TimeSpan.FromMinutes(10);

    [ViewVariables]
    public TimeSpan NextOffer;

    [ViewVariables]
    public Dictionary<int, AvailableRepairOrder> Available = new();

    [ViewVariables]
    public ActiveRepairOrder? Active;

    [ViewVariables]
    public CompletedRepairOrder? Completed;

    /// <summary>
    /// Terminal repair grids which are waiting for their remaining players to leave before deletion.
    /// </summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> PendingCleanupGrids = new();

    /// <summary>Last known usable repair-orders console position on its station grid.</summary>
    [ViewVariables]
    public EntityCoordinates? LastRepairConsoleCoordinates;

    [ViewVariables]
    public bool Accepting;

    [ViewVariables]
    public bool Completing;

    [ViewVariables]
    public long RepairPoints;

    [ViewVariables]
    public long EngineeringReputation;

    [ViewVariables]
    public bool ShopPurchaseInProgress;

    /// <summary>Idempotency keys of purchases committed during this round.</summary>
    public readonly HashSet<Guid> CompletedShopRequests = new();

    [ViewVariables]
    public bool PoolInitialized;

    [ViewVariables]
    public int NextRuntimeId = 1;

}

[DataDefinition]
public sealed partial class AvailableRepairOrder
{
    [ViewVariables]
    public int RuntimeId;

    [ViewVariables]
    public ProtoId<RepairOrderPrototype> Prototype;

    [ViewVariables]
    public readonly int DamageSeed;

    public AvailableRepairOrder(int runtimeId, ProtoId<RepairOrderPrototype> prototype, int damageSeed = 0)
    {
        RuntimeId = runtimeId;
        Prototype = prototype;
        DamageSeed = damageSeed;
    }
}

[DataDefinition]
public sealed partial class ActiveRepairOrder
{
    public RepairTechnicalExclusionSnapshot? Exclusions;
    public int FinalPoints => RepairTechnicalExclusion.FinalPoints(CurrentPoints, Exclusions?.Requirements.Length ?? 0);

    [ViewVariables]
    public RepairDamageGenerationInfo? DamageGeneration;

    public int DamageSeed => DamageGeneration?.Seed ?? 0;

    [ViewVariables]
    public int RuntimeId;

    [ViewVariables]
    public ProtoId<RepairOrderPrototype> Prototype;

    [ViewVariables]
    public EntityUid GridUid;

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
    /// Retries must never recalculate progress or earned currency after this point.
    /// </summary>
    [ViewVariables]
    public bool ExpirationFrozen;

    [ViewVariables]
    public int ExpiredRewardBudget;

    [ViewVariables]
    public int ExpiredReputation;

    [ViewVariables]
    public TimeSpan NextExpirationAttempt;

    [ViewVariables]
    public readonly HashSet<EntityUid> ExpirationAdditionalGrids = new();

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
    public RepairTechnicalExclusionSnapshot? Exclusions;

    [ViewVariables]
    public RepairDamageGenerationInfo? DamageGeneration;

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
    public int EarnedReputation;

    [ViewVariables]
    public RepairOrderResult Result;

    public CompletedRepairOrder(
        int runtimeId,
        ProtoId<RepairOrderPrototype> prototype,
        int completedTasks,
        int totalTasks,
        int finalPoints,
        int maxPoints,
        int rewardBudget,
        int earnedReputation,
        RepairOrderResult result)
    {
        RuntimeId = runtimeId;
        Prototype = prototype;
        CompletedTasks = completedTasks;
        TotalTasks = totalTasks;
        FinalPoints = finalPoints;
        MaxPoints = maxPoints;
        RepairPercent = RepairOrderProgress.CalculatePercent(completedTasks, totalTasks);
        RewardBudget = rewardBudget;
        EarnedReputation = earnedReputation;
        Result = result;
    }

    // Keep the constructor used by existing lifecycle tests; physical rewards are no longer issued on completion.
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
        : this(runtimeId, prototype, completedTasks, totalTasks, finalPoints, maxPoints, rewardBudget, 0, result)
    {
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
