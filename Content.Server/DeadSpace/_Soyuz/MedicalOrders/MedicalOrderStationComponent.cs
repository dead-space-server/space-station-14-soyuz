// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.FixedPoint;
using Content.Shared.DeadSpace._Soyuz.MedicalOrders;

namespace Content.Server.DeadSpace._Soyuz.MedicalOrders;

/// <summary>Authoritative state shared by the three medical-order machines on one station.</summary>
[RegisterComponent]
public sealed partial class MedicalOrderStationComponent : Component
{
    public string ConfigId = string.Empty;
    public Dictionary<int, MedicalOrderOffer> ReagentOffers = new();
    public Dictionary<int, MedicalOrderOffer> PatientOffers = new();
    public MedicalOrderActive? ReagentActive;
    public MedicalOrderActive? PatientActive;
    public bool PatientAccepting;
    public MedicalOrderResult? LastReagent;
    public MedicalOrderResult? LastPatient;
    public TimeSpan NextRefresh;
    public int NextReagentRuntimeId = 1;
    public int NextPatientRuntimeId = 1;
    public long Points;
    public long Reputation;
    public bool Busy;
    public readonly Dictionary<Guid, (MedicalOrderMachineKind Kind, Dictionary<string, int> Cart)> CommittedPurchases = new();
    public readonly HashSet<EntityUid> Machines = new();
}

public sealed class MedicalOrderOffer
{
    public int RuntimeId;
    public bool Patient;
    public readonly List<MedicalOrderRequirement> Lines = new();
    public int MaximumScore;
    public int Difficulty;
    public TimeSpan TimeLimit;
}

public sealed class MedicalOrderRequirement
{
    public string ID = string.Empty;
    public int Amount;
    public int PointsPerUnit;

    public MedicalOrderRequirement Copy() => new() { ID = ID, Amount = Amount, PointsPerUnit = PointsPerUnit };
}

public sealed class MedicalOrderActive
{
    public MedicalOrderOffer Offer = default!;
    public TimeSpan AcceptedAt;
    public TimeSpan Deadline;
    public EntityUid Terminal;
    public EntityUid? Patient;
    public FixedPoint2 InitialDamage;
    public readonly Dictionary<string, FixedPoint2> Submitted = new();
    public bool Finalizing;
}

public sealed class MedicalOrderResult
{
    public MedicalOrderOffer Offer = default!;
    public int FinalScore;
    public int AwardedPoints;
    public int AwardedReputation;
    public bool Expired;
}
