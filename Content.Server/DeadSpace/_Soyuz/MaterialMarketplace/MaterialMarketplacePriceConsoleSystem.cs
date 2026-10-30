// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.DeadSpace._Soyuz.MaterialMarketplace;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.MaterialMarketplace;

/// <summary>
/// Завхоз выставляет цены на материалы и предметы для всех торгоматов станции.
/// </summary>
public sealed class MaterialMarketplacePriceConsoleSystem : EntitySystem
{
    private const double MaxPrice = 10000;

    [Dependency] private readonly MaterialMarketplaceSystem _marketplace = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MaterialMarketplacePriceConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<MaterialMarketplacePriceConsoleComponent, MaterialMarketplaceSetPriceMessage>(OnSetPrice);
        SubscribeLocalEvent<MaterialMarketplacePriceConsoleComponent, MaterialMarketplaceSetDynamicPricingMessage>(OnSetDynamicPricing);
    }

    private void OnSetDynamicPricing(
        EntityUid uid,
        MaterialMarketplacePriceConsoleComponent comp,
        MaterialMarketplaceSetDynamicPricingMessage args)
    {
        if (!_access.IsAllowed(args.Actor, uid))
            return;

        var station = _station.GetOwningStation(uid);
        if (station == null)
            return;

        var prices = EnsureComp<MaterialMarketplacePricesComponent>(station.Value);
        prices.DynamicPricing = args.Enabled;

        prices.MaterialMultipliers.Clear();
        prices.PurchasedThisPeriod.Clear();
        prices.NextUpdate = TimeSpan.Zero;

        _marketplace.RefreshStation(station.Value);
        UpdateUI(uid);
    }

    private void OnOpened(EntityUid uid, MaterialMarketplacePriceConsoleComponent comp, BoundUIOpenedEvent args)
    {
        if (!_access.IsAllowed(args.Actor, uid))
        {
            _popup.PopupEntity("Доступ запрещён.", uid, args.Actor);
            _ui.CloseUi(uid, MaterialMarketplacePriceConsoleUiKey.Key, args.Actor);
            return;
        }

        UpdateUI(uid);
    }

    private void OnSetPrice(EntityUid uid, MaterialMarketplacePriceConsoleComponent comp, MaterialMarketplaceSetPriceMessage args)
    {
        if (!_access.IsAllowed(args.Actor, uid))
            return;

        var station = _station.GetOwningStation(uid);
        if (station == null)
            return;

        if (args.Price is { } price && (!double.IsFinite(price) || price < 0 || price > MaxPrice))
        {
            _popup.PopupEntity($"Цена должна быть от 0 до {MaxPrice:F0}.", uid, args.Actor);
            return;
        }

        if (args.IsItem)
        {
            if (!_prototype.HasIndex<EntityPrototype>(args.Id))
                return;
        }
        else if (!_prototype.HasIndex<Content.Shared.Materials.MaterialPrototype>(args.Id))
        {
            return;
        }

        var prices = EnsureComp<MaterialMarketplacePricesComponent>(station.Value);
        var dict = args.IsItem ? prices.ItemPrices : prices.MaterialPrices;

        if (args.Price is { } value)
            dict[args.Id] = Math.Round(value, 2);
        else
            dict.Remove(args.Id);

        _marketplace.RefreshStation(station.Value);
        UpdateUI(uid);
    }

    private void UpdateUI(EntityUid uid)
    {
        var entries = new List<MarketplacePriceEntry>();
        var station = _station.GetOwningStation(uid);
        var dynamicPricing = true;

        if (station != null)
        {
            var prices = CompOrNull<MaterialMarketplacePricesComponent>(station.Value);
            dynamicPricing = prices?.DynamicPricing ?? true;
            var seenMaterials = new HashSet<string>();
            var seenItems = new HashSet<string>();

            var query = EntityQueryEnumerator<MaterialMarketplaceComponent>();
            while (query.MoveNext(out var marketUid, out var marketComp))
            {
                if (_station.GetOwningStation(marketUid) != station)
                    continue;

                foreach (var mat in _marketplace.GetFilteredMaterials(marketComp))
                {
                    if (!seenMaterials.Add(mat.ID))
                        continue;

                    var perEntity = Math.Max(1, _marketplace.GetUnitsPerEntity(mat));
                    double? custom = prices != null && prices.MaterialPrices.TryGetValue(mat.ID, out var c) ? c : null;

                    entries.Add(new MarketplacePriceEntry(
                        mat.ID,
                        _marketplace.GetMaterialDisplayName(mat),
                        false,
                        custom,
                        mat.Price * perEntity,
                        _marketplace.GetMaterialMultiplier(prices, mat.ID)));
                }

                foreach (var protoId in _marketplace.GetStoredItems(marketUid, marketComp).Keys)
                {
                    seenItems.Add(protoId);
                }
            }

            // Предметы с уже выставленной ценой остаются в списке, даже если их сейчас нет в торгоматах.
            if (prices != null)
            {
                foreach (var protoId in prices.ItemPrices.Keys)
                {
                    seenItems.Add(protoId);
                }
            }

            foreach (var protoId in seenItems)
            {
                double? custom = prices != null && prices.ItemPrices.TryGetValue(protoId, out var c) ? c : null;
                entries.Add(new MarketplacePriceEntry(protoId, _marketplace.GetItemName(protoId), true, custom, null, 1.0));
            }
        }

        _ui.SetUiState(uid, MaterialMarketplacePriceConsoleUiKey.Key, new MaterialMarketplacePriceConsoleState(entries, dynamicPricing));
    }
}
