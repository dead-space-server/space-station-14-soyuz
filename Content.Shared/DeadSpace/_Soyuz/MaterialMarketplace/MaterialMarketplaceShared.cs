// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.MaterialMarketplace;

[Serializable, NetSerializable]
public enum MarketplaceLineKind : byte
{
    Material,
    Item
}

[Serializable, NetSerializable]
public enum MarketplacePayment : byte
{
    Department,
    Personal,
    Cash
}

/// <summary>
/// Предмет (не материал), лежащий в торгомате. Price == null означает, что цена не выставлена.
/// </summary>
[NetSerializable, Serializable]
public sealed class MarketplaceItemEntry(string prototypeId, string name, int count, double? price)
{
    public readonly string PrototypeId = prototypeId;
    public readonly string Name = name;
    public readonly int Count = count;
    public readonly double? Price = price;
}

[NetSerializable, Serializable]
public sealed class MaterialMarketplaceState(
    Dictionary<string, int> availableMaterials,
    Dictionary<string, double> prices,
    List<MarketplaceItemEntry> items,
    int depositedCash)
    : BoundUserInterfaceState
{
    public readonly Dictionary<string, int> AvailableMaterials = availableMaterials;
    public readonly Dictionary<string, double> Prices = prices;
    public readonly List<MarketplaceItemEntry> Items = items;
    public readonly int DepositedCash = depositedCash;
}

[NetSerializable, Serializable]
public sealed class MarketplaceCartLine(MarketplaceLineKind kind, string id, int amount)
{
    public readonly MarketplaceLineKind Kind = kind;
    public readonly string Id = id;
    public readonly int Amount = amount;
}

/// <summary>
/// Клиент -> сервер: оформить корзину выбранным способом оплаты.
/// </summary>
[NetSerializable, Serializable]
public sealed class MaterialMarketplaceCheckoutMessage(List<MarketplaceCartLine> lines, MarketplacePayment payment)
    : BoundUserInterfaceMessage
{
    public readonly List<MarketplaceCartLine> Lines = lines;
    public readonly MarketplacePayment Payment = payment;
}

/// <summary>
/// Клиент -> сервер: узнать, какие способы оплаты доступны этому игроку.
/// </summary>
[NetSerializable, Serializable]
public sealed class MaterialMarketplaceRequestPaymentMessage : BoundUserInterfaceMessage;

/// <summary>
/// Клиент -> сервер: вернуть внесённую наличку.
/// </summary>
[NetSerializable, Serializable]
public sealed class MaterialMarketplaceEjectCashMessage : BoundUserInterfaceMessage;

/// <summary>
/// Сервер -> клиент: доступные игроку способы оплаты.
/// DepartmentName == null - нет доступа главы отдела; PersonalBalance == null - нет банковского счёта.
/// </summary>
[NetSerializable, Serializable]
public sealed class MaterialMarketplacePaymentOptionsMessage(
    string? departmentName,
    int? departmentBalance,
    int? personalBalance)
    : BoundUserInterfaceMessage
{
    public readonly string? DepartmentName = departmentName;
    public readonly int? DepartmentBalance = departmentBalance;
    public readonly int? PersonalBalance = personalBalance;
}

/// <summary>
/// Сервер -> клиент: результат оформления покупки.
/// </summary>
[NetSerializable, Serializable]
public sealed class MaterialMarketplaceCheckoutResultMessage(bool success) : BoundUserInterfaceMessage
{
    public readonly bool Success = success;
}

[Serializable, NetSerializable]
public enum MaterialMarketplaceUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum MaterialMarketplaceVisuals : byte
{
    FillLevel
}

[Prototype]
public sealed partial class MaterialMarketplaceCategoryPrototype : IPrototype
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    [DataField("order")]
    public int Order { get; private set; }

    [DataField("materials")]
    public HashSet<string> Materials { get; private set; } = new();
}
