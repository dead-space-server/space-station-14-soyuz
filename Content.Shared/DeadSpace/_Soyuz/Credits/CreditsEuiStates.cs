// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using System.Collections.Generic;
using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.Credits;

[Serializable, NetSerializable]
public sealed class CreditLogEntryDto
{
    public long Amount;
    public CreditTransactionType Type;
    public string Reason = "";
    public DateTime CreatedAtUtc;
    public string Target = "";
    public string Actor = "";

    public CreditLogEntryDto() { }

    public CreditLogEntryDto(long amount, CreditTransactionType type, string reason, DateTime createdAtUtc)
    {
        Amount = amount;
        Type = type;
        Reason = reason;
        CreatedAtUtc = createdAtUtc;
    }
}

[Serializable, NetSerializable]
public sealed class CreditProductDto
{
    public string Id = "";
    public string Name = "";
    public string? Description;
    public int Price;
    public bool Owned;
    public bool Equipped;
    public bool Permanent;
    public DateTime? ExpiresAtUtc;
    public int? DurationDays;
    public bool GivesItem;
    public List<string> Jobs = new();
}

[Serializable, NetSerializable]
public sealed class CreditsEuiState : EuiStateBase
{
    public long Balance;
    public int PlaytimeHours;
    public List<CreditProductDto> Products = new();
    public List<CreditLogEntryDto> History = new();
    public int Page;
    public bool HasMore;
    public string ReferralCode = "";
    public bool ReferralApplied;
    public bool ReferralRewarded;
    public int ReferralMinHours;
    public int InviterReward;
    public int InviteeReward;
    public int RefInvited;
    public int RefRewarded;
    public int RefPending;
    public long RefEarned;
    public string Notice = "";
}

[Serializable, NetSerializable]
public sealed class CreditsBuyMessage : EuiMessageBase
{
    public string ProductId = "";
}

[Serializable, NetSerializable]
public sealed class CreditsEquipMessage : EuiMessageBase
{
    public string ProductId = "";
    public bool Equip;
}

[Serializable, NetSerializable]
public sealed class CreditsPageMessage : EuiMessageBase
{
    public int Page;
}

[Serializable, NetSerializable]
public sealed class CreditsReferralApplyMessage : EuiMessageBase
{
    public string Code = "";
}

[Serializable, NetSerializable]
public sealed class CreditsAdminState : EuiStateBase
{
    public string Target = "";
    public bool Resolved;
    public bool Global;
    public long Balance;
    public List<CreditLogEntryDto> History = new();
    public int Page;
    public bool HasMore;
    public int TypeFilter = -1;
    public string ActorFilter = "";
    public string ReasonFilter = "";
    public string Notice = "";
}

[Serializable, NetSerializable]
public sealed class CreditsAdminLookupMessage : EuiMessageBase
{
    public string Target = "";
    public int Page;
    /// <summary>
    /// -1 = любой тип
    /// </summary>
    public int TypeFilter = -1;
    public string ActorFilter = "";
    public string ReasonFilter = "";
}

[Serializable, NetSerializable]
public sealed class CreditsAdminGiveMessage : EuiMessageBase
{
    public string Target = "";
    public long Amount;
    public string Reason = "";
    public CreditTransactionType Type = CreditTransactionType.AdminGive;
}