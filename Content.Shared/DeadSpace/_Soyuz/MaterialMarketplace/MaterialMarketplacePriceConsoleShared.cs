// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
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

    /// <summary>
    /// Динамическая цена на материалы: цена = базовая * множитель, множитель зависит от спроса.
    /// </summary>
    [DataField]
    public bool DynamicPricing = true;

    /// <summary>
    /// Текущий множитель цены по материалам (1.0 = базовая цена).
    /// </summary>
    [DataField]
    public Dictionary<string, double> MaterialMultipliers = new();

    /// <summary>
    /// Сколько единиц каждого материала купили за текущий период.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, int> PurchasedThisPeriod = new();

    /// <summary>
    /// Как часто пересчитываются множители.
    /// </summary>
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Сколько единиц материала нужно купить за период, чтобы цель множителя стала максимальной (200%).
    /// Меньше покупок - цель ближе к минимальной (50%).
    /// </summary>
    [DataField]
    public int SaturationPerPeriod = 20;

    [DataField]
    public double MinMultiplier = 0.5;

    [DataField]
    public double MaxMultiplier = 2.0;

    /// <summary>
    /// Какую долю расстояния до цели множитель проходит за один пересчёт (чем меньше, тем медленнее).
    /// </summary>
    [DataField]
    public double Smoothing = 0.1;

    [ViewVariables]
    public TimeSpan NextUpdate;

    /// <summary>
    /// Комиссия общего рынка, % от цены лота. Удерживается с продавца и уходит на счёт почты
    /// </summary>
    [DataField]
    public int MarketCommissionPercent;
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
public sealed class MarketplacePriceEntry(
    string id,
    string name,
    bool isItem,
    double? customPrice,
    double? defaultPrice,
    double multiplier)
{
    public readonly string Id = id;
    public readonly string Name = name;
    public readonly bool IsItem = isItem;
    public readonly double? CustomPrice = customPrice;
    public readonly double? DefaultPrice = defaultPrice;

    /// <summary>
    /// Текущий динамический множитель (1.0 для предметов или при выключенной динамической цене).
    /// </summary>
    public readonly double Multiplier = multiplier;
}

[NetSerializable, Serializable]
public sealed class MaterialMarketplacePriceConsoleState(
    List<MarketplacePriceEntry> entries,
    bool dynamicPricing,
    int marketCommissionPercent)
    : BoundUserInterfaceState
{
    public readonly List<MarketplacePriceEntry> Entries = entries;
    public readonly bool DynamicPricing = dynamicPricing;
    public readonly int MarketCommissionPercent = marketCommissionPercent;
}

/// <summary>
/// Задать комиссию общего рынка (в процентах)
/// </summary>
[NetSerializable, Serializable]
public sealed class MaterialMarketplaceSetCommissionMessage(int percent) : BoundUserInterfaceMessage
{
    public readonly int Percent = percent;
}

/// <summary>
/// Включить или выключить динамическую цену. При любом переключении множители сбрасываются к 100%.
/// </summary>
[NetSerializable, Serializable]
public sealed class MaterialMarketplaceSetDynamicPricingMessage(bool enabled) : BoundUserInterfaceMessage
{
    public readonly bool Enabled = enabled;
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
