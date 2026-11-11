// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Content.Shared.Access;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.DeadSpace._Soyuz.MaterialMarketplace;
using Content.Shared.Stacks;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.PublicMarket;

/// <summary>
/// Сотрудники отделов выставляют предметы, деньги идут на счёт отдела продавца
/// Предметы лежат в скрытом контейнере, достать их можно только купив (или забрав свой лот)
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PublicMarketComponent : Component
{
    [DataField]
    public string ItemContainerId = "public_market_items";

    /// <summary>
    /// Тип стопки, которая считается наличными
    /// </summary>
    [DataField]
    public ProtoId<StackPrototype> CashStackType = "Credit";

    /// <summary>
    /// Деньги, внесённые наличкой и ещё не потраченные
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public int DepositedCash;

    /// <summary>
    /// Счёт, на который уходит комиссия
    /// </summary>
    [DataField]
    public ProtoId<CargoAccountPrototype> CommissionAccount = "Cargo";

    /// <summary>
    /// Какой доступ (глава отдела) даёт право платить со счёта отдела при покупке
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<AccessLevelPrototype>, ProtoId<CargoAccountPrototype>> DepartmentAccess = new();

    /// <summary>
    /// Отделы, чьи сотрудники могут продавать: id отдела из JobDepartments ID-карты -> счёт отдела
    /// Отделов, которых здесь нет, продавать не могут.
    /// </summary>
    [DataField]
    public Dictionary<string, ProtoId<CargoAccountPrototype>> SellerDepartments = new();

    [DataField]
    public int MaxLotPrice = 100000;
}

/// <summary>
/// Вешается на предмет, пока он лежит на рынке. Хранит, кто и за сколько его продаёт
/// </summary>
[RegisterComponent]
public sealed partial class PublicMarketLotComponent : Component
{
    [DataField]
    public string SellerUserId = string.Empty;

    [DataField]
    public string SellerName = string.Empty;

    [DataField]
    public ProtoId<CargoAccountPrototype> SellerAccount;

    /// <summary>
    /// null - продавец ещё не назначил цену, лот скрыт от покупателей
    /// </summary>
    [DataField]
    public int? Price;
}

[Serializable, NetSerializable]
public enum PublicMarketUiKey : byte
{
    Key
}

[NetSerializable, Serializable]
public sealed class PublicMarketLotEntry(
    NetEntity lot,
    string prototypeId,
    string name,
    int? price,
    string sellerUserId,
    string sellerName,
    string departmentName)
{
    public readonly NetEntity Lot = lot;
    public readonly string PrototypeId = prototypeId;
    public readonly string Name = name;
    public readonly int? Price = price;
    public readonly string SellerUserId = sellerUserId;
    public readonly string SellerName = sellerName;
    public readonly string DepartmentName = departmentName;
}

[NetSerializable, Serializable]
public sealed class PublicMarketState(List<PublicMarketLotEntry> lots, int depositedCash, int commissionPercent)
    : BoundUserInterfaceState
{
    public readonly List<PublicMarketLotEntry> Lots = lots;
    public readonly int DepositedCash = depositedCash;
    public readonly int CommissionPercent = commissionPercent;
}

/// <summary>
/// Купить лоты выбранным способом оплаты
/// </summary>
[NetSerializable, Serializable]
public sealed class PublicMarketBuyMessage(List<NetEntity> lots, MarketplacePayment payment) : BoundUserInterfaceMessage
{
    public readonly List<NetEntity> Lots = lots;
    public readonly MarketplacePayment Payment = payment;
}

/// <summary>
/// Продавец задаёт цену своего лота (null - снять с продажи, оставив предмет в рынке)
/// </summary>
[NetSerializable, Serializable]
public sealed class PublicMarketSetPriceMessage(NetEntity lot, int? price) : BoundUserInterfaceMessage
{
    public readonly NetEntity Lot = lot;
    public readonly int? Price = price;
}

[NetSerializable, Serializable]
public sealed class PublicMarketWithdrawMessage(NetEntity lot) : BoundUserInterfaceMessage
{
    public readonly NetEntity Lot = lot;
}