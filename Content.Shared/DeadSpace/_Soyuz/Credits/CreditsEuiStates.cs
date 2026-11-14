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

    /// <summary>Заполняется только для окна администратора.</summary>
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
    /// <summary>Цена к оплате с учётом скидки.</summary>
    public int Price;

    /// <summary>Цена без скидки (равна Price, если скидки нет).</summary>
    public int OriginalPrice;
    public int DiscountPercent;
    public DateTime? SaleUntilUtc;

    /// <summary>Сколько штук осталось (-1 = без тиража).</summary>
    public int StockLeft = -1;

    /// <summary>Сколько раз товар куплен на сервере (для сортировки по популярности).</summary>
    public int Sold;

    public DateTime? AvailableUntilUtc;

    /// <summary>Уже локализованная причина, по которой игрок не может купить (пусто = может).</summary>
    public string LockReason = "";
    public bool Owned;
    public bool Equipped;
    public bool Permanent;
    public DateTime? ExpiresAtUtc;
    public int? DurationDays;
    public bool GivesItem;
    public List<string> Jobs = new();

    /// <summary>Id категории (null = без категории).</summary>
    public string? Category;

    /// <summary>Достигнут лимит категории: новый товар купить нельзя (уже действующий можно продлить).</summary>
    public bool LimitReached;
}

[Serializable, NetSerializable]
public sealed class CreditCategoryDto
{
    public string Id = "";
    public string Name = "";
    public string? Description;

    /// <summary>0 = без лимита.</summary>
    public int MaxOwned;

    /// <summary>Сколько товаров этой категории у игрока сейчас действует.</summary>
    public int OwnedCount;
}

// ---------- Окно игрока ----------

[Serializable, NetSerializable]
public sealed class CreditsEuiState : EuiStateBase
{
    public long Balance;
    public int PlaytimeHours;
    public List<CreditProductDto> Products = new();
    public List<CreditCategoryDto> Categories = new();
    public List<CreditLogEntryDto> History = new();
    public int Page;
    public bool HasMore;
    public string ReferralCode = "";
    public bool ReferralApplied;
    public bool ReferralRewarded;
    /// <summary>Реферал отклонён (совпали IP или HWID с пригласившим).</summary>
    public bool ReferralRejected;
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
public sealed class CreditsPromoRedeemMessage : EuiMessageBase
{
    public string Code = "";
}

// ---------- Окно администратора ----------

[Serializable, NetSerializable]
public sealed class CreditsAdminState : EuiStateBase
{
    /// <summary>Готовый текст статистики экономики (пусто = не запрашивалась).</summary>
    public string Stats = "";
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
    /// <summary>-1 = любой тип.</summary>
    public int TypeFilter = -1;
    public string ActorFilter = "";
    public string ReasonFilter = "";
}

[Serializable, NetSerializable]
public sealed class CreditsAdminTakeMessage : EuiMessageBase
{
    public string Target = "";
    public long Amount;
    public string Reason = "";
}

[Serializable, NetSerializable]
public sealed class CreditsAdminRefundMessage : EuiMessageBase
{
    public string Target = "";
    public string ProductId = "";
    public string Reason = "";
}

[Serializable, NetSerializable]
public sealed class CreditsAdminStatsMessage : EuiMessageBase
{
}

[Serializable, NetSerializable]
public sealed class CreditsAdminGiveMessage : EuiMessageBase
{
    public string Target = "";
    public long Amount;
    public string Reason = "";
    public CreditTransactionType Type = CreditTransactionType.AdminGive;
}