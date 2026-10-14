// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.MedicalOrders;

[Serializable, NetSerializable]
public enum MedicalSpecialPatientStatus : byte
{
    Pending,
    Issued,
    Submitted,
    Lost,
}

[Serializable, NetSerializable]
public sealed class MedicalSpecialPatientView
{
    public readonly MedicalOrderView Order;
    public readonly MedicalSpecialPatientStatus Status;
    public readonly float? CurrentDamage;
    public readonly bool CanSubmit;
    public readonly string Name;

    public MedicalSpecialPatientView(MedicalOrderView order, MedicalSpecialPatientStatus status,
        float? currentDamage, bool canSubmit, string name)
    {
        Order = order;
        Status = status;
        CurrentDamage = currentDamage;
        CanSubmit = canSubmit;
        Name = name;
    }
}

[Serializable, NetSerializable]
public sealed class MedicalSpecialContractView
{
    public readonly int RuntimeId;
    public readonly int Difficulty;
    public readonly string Title;
    public readonly string Description;
    public readonly string Site;
    public readonly TimeSpan TimeLimit;
    public readonly TimeSpan? Deadline;
    public readonly long MaximumPoints;
    public readonly int Reputation;
    public readonly int PenaltyPercent;
    public readonly MedicalOrderLineView[] Reagents;
    public readonly MedicalSpecialPatientView[] Patients;
    public readonly bool CanIssue;

    public MedicalSpecialContractView(int runtimeId, int difficulty, string title, string description,
        string site, TimeSpan timeLimit, TimeSpan? deadline, long maximumPoints, int reputation,
        int penaltyPercent, MedicalOrderLineView[] reagents, MedicalSpecialPatientView[] patients, bool canIssue)
    {
        RuntimeId = runtimeId;
        Difficulty = difficulty;
        Title = title;
        Description = description;
        Site = site;
        TimeLimit = timeLimit;
        Deadline = deadline;
        MaximumPoints = maximumPoints;
        Reputation = reputation;
        PenaltyPercent = penaltyPercent;
        Reagents = reagents;
        Patients = patients;
        CanIssue = canIssue;
    }
}

[Serializable, NetSerializable]
public sealed class MedicalSpecialResultView
{
    public readonly string Title;
    public readonly string Site;
    public readonly bool Expired;
    public readonly long Points;
    public readonly long Reputation;

    public MedicalSpecialResultView(string title, string site, bool expired, long points, long reputation)
    {
        Title = title;
        Site = site;
        Expired = expired;
        Points = points;
        Reputation = reputation;
    }
}
