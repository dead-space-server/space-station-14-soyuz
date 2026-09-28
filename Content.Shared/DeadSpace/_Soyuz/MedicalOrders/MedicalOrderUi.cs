// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.MedicalOrders;

[Serializable, NetSerializable]
public enum MedicalOrderMachineKind : byte
{
    Reagent,
    PatientReceiver,
    PatientSender,
}

[Serializable, NetSerializable]
public enum MedicalOrderUiKey : byte
{
    Key,
}

[RegisterComponent, NetworkedComponent]
public sealed partial class MedicalOrderMachineComponent : Component
{
    [DataField] public MedicalOrderMachineKind Kind;
    [DataField(required: true)] public ProtoId<MedicalOrderConfigPrototype> Config;
}

/// <summary>Only the created patient carries this identity. The client cannot forge it.</summary>
[RegisterComponent]
public sealed partial class MedicalOrderPatientComponent : Component
{
    public EntityUid Station;
    public int RuntimeId;
}

[Serializable, NetSerializable]
public sealed class MedicalOrderLineView
{
    public readonly string ID;
    public readonly int Required;
    public readonly float Submitted;
    public readonly int PointsPerUnit;

    public MedicalOrderLineView(string id, int required, float submitted, int pointsPerUnit)
    {
        ID = id;
        Required = required;
        Submitted = submitted;
        PointsPerUnit = pointsPerUnit;
    }
}

[Serializable, NetSerializable]
public sealed class MedicalOrderView
{
    public readonly int RuntimeId;
    public readonly MedicalOrderLineView[] Lines;
    public readonly int MaximumScore;
    public readonly int CurrentScore;
    public readonly int Difficulty;
    public readonly TimeSpan TimeLimit;
    public readonly TimeSpan? Deadline;

    public MedicalOrderView(int runtimeId, MedicalOrderLineView[] lines, int maximumScore,
        int currentScore, int difficulty, TimeSpan timeLimit, TimeSpan? deadline = null)
    {
        RuntimeId = runtimeId;
        Lines = lines;
        MaximumScore = maximumScore;
        CurrentScore = currentScore;
        Difficulty = difficulty;
        TimeLimit = timeLimit;
        Deadline = deadline;
    }
}

[Serializable, NetSerializable]
public sealed class MedicalOrderShopItemView
{
    public readonly string ID;
    public readonly string Entity;
    public readonly int Cost;
    public readonly int MaxCount;
    public readonly int MinimumShopLevel;
    public readonly bool Classified;

    public MedicalOrderShopItemView(string id, string entity, int cost, int maxCount,
        int minimumShopLevel, bool classified)
    {
        ID = id;
        Entity = entity;
        Cost = cost;
        MaxCount = maxCount;
        MinimumShopLevel = minimumShopLevel;
        Classified = classified;
    }
}

[Serializable, NetSerializable]
public sealed class MedicalOrderShopCartLine
{
    public readonly string ID;
    public readonly int Count;

    public MedicalOrderShopCartLine(string id, int count)
    {
        ID = id;
        Count = count;
    }
}

[Serializable, NetSerializable]
public sealed class MedicalOrderUiState : BoundUserInterfaceState
{
    public readonly MedicalOrderMachineKind Kind;
    public readonly MedicalOrderView[] Offers;
    public readonly MedicalOrderView? Active;
    public readonly MedicalOrderView? LastCompleted;
    public readonly int LastAwardedPoints;
    public readonly int LastAwardedReputation;
    public readonly bool LastExpired;
    public readonly TimeSpan NextRefresh;
    public readonly long Points;
    public readonly long Reputation;
    public readonly int ShopLevel;
    public readonly int? NextShopLevelThreshold;
    public readonly MedicalOrderShopItemView[] Shop;
    public readonly float? PatientInitialDamage;
    public readonly float? PatientCurrentDamage;
    public readonly int PatientCompletionThreshold;
    public readonly bool PatientInserted;
    public readonly bool PatientAlive;
    public readonly bool PatientCritical;
    public readonly string? ReagentBeakerName;

    public MedicalOrderUiState(MedicalOrderMachineKind kind, MedicalOrderView[] offers,
        MedicalOrderView? active, MedicalOrderView? lastCompleted, TimeSpan nextRefresh,
        long points, long reputation, int shopLevel, int? nextShopLevelThreshold,
        MedicalOrderShopItemView[] shop, int lastAwardedPoints, int lastAwardedReputation,
        bool lastExpired, float? patientInitialDamage, float? patientCurrentDamage,
        int patientCompletionThreshold, bool patientInserted, bool patientAlive, bool patientCritical,
        string? reagentBeakerName)
    {
        Kind = kind;
        Offers = offers;
        Active = active;
        LastCompleted = lastCompleted;
        LastAwardedPoints = lastAwardedPoints;
        LastAwardedReputation = lastAwardedReputation;
        LastExpired = lastExpired;
        NextRefresh = nextRefresh;
        Points = points;
        Reputation = reputation;
        ShopLevel = shopLevel;
        NextShopLevelThreshold = nextShopLevelThreshold;
        Shop = shop;
        PatientInitialDamage = patientInitialDamage;
        PatientCurrentDamage = patientCurrentDamage;
        PatientCompletionThreshold = patientCompletionThreshold;
        PatientInserted = patientInserted;
        PatientAlive = patientAlive;
        PatientCritical = patientCritical;
        ReagentBeakerName = reagentBeakerName;
    }
}

[Serializable, NetSerializable]
public enum MedicalOrderAction : byte
{
    Accept,
    Complete,
    Purchase,
    TransferReagents,
}

/// <summary>All fields are untrusted; the server derives station, machine and prices itself.</summary>
[Serializable, NetSerializable]
public sealed class MedicalOrderRequestMessage : BoundUserInterfaceMessage
{
    public readonly MedicalOrderAction Action;
    public readonly int RuntimeId;
    public readonly string RequestId;
    public readonly MedicalOrderShopCartLine[] Cart;

    public MedicalOrderRequestMessage(MedicalOrderAction action, int runtimeId = 0,
        string requestId = "", MedicalOrderShopCartLine[]? cart = null)
    {
        Action = action;
        RuntimeId = runtimeId;
        RequestId = requestId;
        Cart = cart ?? Array.Empty<MedicalOrderShopCartLine>();
    }
}

[Serializable, NetSerializable]
public sealed class MedicalOrderResultMessage : BoundUserInterfaceMessage
{
    public readonly string RequestId;
    public readonly bool Success;
    public readonly string Text;

    public MedicalOrderResultMessage(string requestId, bool success, string text)
    {
        RequestId = requestId;
        Success = success;
        Text = text;
    }
}
