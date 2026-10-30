// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.MaterialMarketplace;

/// <summary>
/// Консоль, с которой квартирмейстер выставляет цены для всех торгоматов станции.
/// Доступ проверяется через AccessReader.
/// </summary>
[RegisterComponent]
public sealed partial class MaterialMarketplacePriceConsoleComponent : Component;

/// <summary>
/// Хранится на сущности станции. Цены, выставленные консолью.
/// Материалы: цена за одну единицу товара (лист/слиток). Предметы: цена за штуку, ключ - id прототипа.
/// </summary>
[RegisterComponent]
public sealed partial class MaterialMarketplacePricesComponent : Component
{
    [DataField]
    public Dictionary<string, double> MaterialPrices = new();

    [DataField]
    public Dictionary<string, double> ItemPrices = new();
}

[Serializable, NetSerializable]
public enum MaterialMarketplacePriceConsoleUiKey : byte
{
    Key
}

/// <summary>
/// CustomPrice == null - цена не переопределена (для материала действует цена по умолчанию, для предмета - "не выставлена").
/// DefaultPrice есть только у материалов.
/// </summary>
[NetSerializable, Serializable]
public sealed class MarketplacePriceEntry(string id, string name, bool isItem, double? customPrice, double? defaultPrice)
{
    public readonly string Id = id;
    public readonly string Name = name;
    public readonly bool IsItem = isItem;
    public readonly double? CustomPrice = customPrice;
    public readonly double? DefaultPrice = defaultPrice;
}

[NetSerializable, Serializable]
public sealed class MaterialMarketplacePriceConsoleState(List<MarketplacePriceEntry> entries) : BoundUserInterfaceState
{
    public readonly List<MarketplacePriceEntry> Entries = entries;
}

/// <summary>
/// Price == null сбрасывает цену (материал вернётся к цене по умолчанию, предмет станет "не выставлен").
/// </summary>
[NetSerializable, Serializable]
public sealed class MaterialMarketplaceSetPriceMessage(bool isItem, string id, double? price) : BoundUserInterfaceMessage
{
    public readonly bool IsItem = isItem;
    public readonly string Id = id;
    public readonly double? Price = price;
}
