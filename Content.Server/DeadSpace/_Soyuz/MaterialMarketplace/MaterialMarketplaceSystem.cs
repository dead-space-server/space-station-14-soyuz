// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using System.Linq;
using Content.Server.Cargo.Systems;
using Content.Server.Materials;
using Content.Server.Power.EntitySystems;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Backmen.Economy;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.DeadSpace._Soyuz.MaterialMarketplace;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Materials;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.MaterialMarketplace
{
    /// <summary>
    /// Серверная система маркетплейса материалов.
    /// Отвечает за подсчёт материалов и предметов, цены, оплату и выдачу покупок.
    /// </summary>
    public sealed class MaterialMarketplaceSystem : EntitySystem
    {
        private const int MaxCartLines = 64;
        private const int MaxLineAmount = 1000;

        [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
        [Dependency] private readonly CargoSystem _cargo = default!;
        [Dependency] private readonly StationSystem _stationSystem = default!;
        [Dependency] private readonly IPrototypeManager _prototype = default!;
        [Dependency] private readonly UserInterfaceSystem _ui = default!;
        [Dependency] private readonly SharedContainerSystem _container = default!;
        [Dependency] private readonly StackSystem _stack = default!;
        [Dependency] private readonly AccessReaderSystem _access = default!;
        [Dependency] private readonly SharedIdCardSystem _idCard = default!;
        [Dependency] private readonly SharedPopupSystem _popup = default!;
        [Dependency] private readonly PowerReceiverSystem _power = default!;
        [Dependency] private readonly IGameTiming _timing = default!;

        private List<MaterialPrototype> _cachedMaterials = new();

        public override void Initialize()
        {
            base.Initialize();
            _cachedMaterials = _prototype.EnumeratePrototypes<MaterialPrototype>().ToList();

            SubscribeLocalEvent<MaterialMarketplaceComponent, ComponentInit>(OnInit);
            SubscribeLocalEvent<MaterialMarketplaceComponent, BoundUIOpenedEvent>(OnUIOpened);
            SubscribeLocalEvent<MaterialMarketplaceComponent, InteractUsingEvent>(OnInteractUsing,
                before: new[] { typeof(MaterialStorageSystem) });
            SubscribeLocalEvent<MaterialMarketplaceComponent, MaterialMarketplaceCheckoutMessage>(OnCheckout);
            SubscribeLocalEvent<MaterialMarketplaceComponent, MaterialMarketplaceRequestPaymentMessage>(OnRequestPayment);
            SubscribeLocalEvent<MaterialMarketplaceComponent, MaterialMarketplaceEjectCashMessage>(OnEjectCash);
            SubscribeLocalEvent<MaterialStorageComponent, MaterialAmountChangedEvent>(OnMaterialChanged);
        }

        private void OnInit(EntityUid uid, MaterialMarketplaceComponent comp, ComponentInit args)
        {
            _container.EnsureContainer<Container>(uid, comp.ItemContainerId);
        }

        private void OnUIOpened(EntityUid uid, MaterialMarketplaceComponent comp, BoundUIOpenedEvent args)
        {
            UpdateUI(uid, comp);
        }

        private void OnMaterialChanged(EntityUid uid, MaterialStorageComponent comp, MaterialAmountChangedEvent args)
        {
            if (TryComp<MaterialMarketplaceComponent>(uid, out var marketplace))
                UpdateUI(uid, marketplace);
        }

        #region Публичные методы для консоли цен

        public IEnumerable<MaterialPrototype> GetFilteredMaterials(MaterialMarketplaceComponent comp)
        {
            return _cachedMaterials.Where(mat =>
                (comp.WhitelistMaterials.Count == 0 || comp.WhitelistMaterials.Contains(mat.ID)) &&
                !comp.BlacklistMaterials.Contains(mat.ID) &&
                IsAllowedByTags(mat, comp.WhitelistTags, comp.BlacklistTags));
        }

        public int GetUnitsPerEntity(MaterialPrototype material)
        {
            if (!string.IsNullOrEmpty(material.StackEntity) &&
                _prototype.TryIndex<EntityPrototype>(material.StackEntity, out var stackProto) &&
                stackProto.TryGetComponent<PhysicalCompositionComponent>(out var compo) &&
                compo.MaterialComposition.TryGetValue(material.ID, out var units))
            {
                if (units <= 0)
                    return 1;
                return units;
            }

            return 1;
        }

        public string GetMaterialDisplayName(MaterialPrototype material)
        {
            return Loc.GetString(material.Name);
        }

        public string GetItemName(string prototypeId)
        {
            return _prototype.TryIndex<EntityPrototype>(prototypeId, out var proto) ? proto.Name : prototypeId;
        }

        public Dictionary<string, List<EntityUid>> GetStoredItems(EntityUid uid, MaterialMarketplaceComponent comp)
        {
            var result = new Dictionary<string, List<EntityUid>>();
            if (!_container.TryGetContainer(uid, comp.ItemContainerId, out var container))
                return result;

            foreach (var item in container.ContainedEntities)
            {
                var protoId = MetaData(item).EntityPrototype?.ID;
                if (protoId == null)
                    continue;

                if (!result.TryGetValue(protoId, out var list))
                    result[protoId] = list = new List<EntityUid>();

                list.Add(item);
            }

            return result;
        }

        /// <summary>
        /// Обновить интерфейс всех торгоматов станции (после смены цен).
        /// </summary>
        public void RefreshStation(EntityUid station)
        {
            var query = EntityQueryEnumerator<MaterialMarketplaceComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                if (_stationSystem.GetOwningStation(uid) != station)
                    continue;

                UpdateUI(uid, comp);
            }
        }

        #endregion

        #region Цены

        private MaterialMarketplacePricesComponent? GetPrices(EntityUid marketplace)
        {
            var station = _stationSystem.GetOwningStation(marketplace);
            return station == null ? null : CompOrNull<MaterialMarketplacePricesComponent>(station.Value);
        }

        /// <summary>
        /// Текущий множитель динамической цены (1.0, если динамическая цена выключена).
        /// </summary>
        public double GetMaterialMultiplier(MaterialMarketplacePricesComponent? prices, string materialId)
        {
            if (prices == null || !prices.DynamicPricing)
                return 1.0;

            return prices.MaterialMultipliers.GetValueOrDefault(materialId, 1.0);
        }

        private double GetMaterialPrice(MaterialPrototype mat, int perEntity, MaterialMarketplacePricesComponent? prices)
        {
            var basePrice = prices != null && prices.MaterialPrices.TryGetValue(mat.ID, out var custom)
                ? custom
                : mat.Price * perEntity;

            return basePrice * GetMaterialMultiplier(prices, mat.ID);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var query = EntityQueryEnumerator<MaterialMarketplacePricesComponent>();
            while (query.MoveNext(out var station, out var prices))
            {
                if (!prices.DynamicPricing)
                    continue;

                // Первый запуск (или после переключения): отсчитываем период с текущего момента.
                if (prices.NextUpdate == TimeSpan.Zero)
                {
                    prices.NextUpdate = _timing.CurTime + prices.UpdateInterval;
                    continue;
                }

                if (_timing.CurTime < prices.NextUpdate)
                    continue;

                prices.NextUpdate = _timing.CurTime + prices.UpdateInterval;
                UpdateDynamicPrices(prices);
                RefreshStation(station);
            }
        }

        /// <summary>
        /// Раз в период двигаем множитель каждого материала к цели:
        /// много покупок - цель растёт к максимуму (200%), мало - падает к минимуму (50%).
        /// Движение плавное: за один пересчёт проходится только доля Smoothing пути.
        /// </summary>
        private void UpdateDynamicPrices(MaterialMarketplacePricesComponent prices)
        {
            var saturation = Math.Max(1, prices.SaturationPerPeriod);

            foreach (var material in _cachedMaterials)
            {
                var bought = prices.PurchasedThisPeriod.GetValueOrDefault(material.ID);
                var demand = Math.Clamp(bought / (double) saturation, 0.0, 1.0);
                var target = prices.MinMultiplier + (prices.MaxMultiplier - prices.MinMultiplier) * demand;

                var current = prices.MaterialMultipliers.GetValueOrDefault(material.ID, 1.0);
                var next = current + (target - current) * prices.Smoothing;

                prices.MaterialMultipliers[material.ID] = Math.Clamp(next, prices.MinMultiplier, prices.MaxMultiplier);
            }

            prices.PurchasedThisPeriod.Clear();
        }

        #endregion

        private void UpdateUI(EntityUid uid, MaterialMarketplaceComponent comp)
        {
            if (!TryComp<MaterialStorageComponent>(uid, out _))
                return;

            var prices = GetPrices(uid);
            var available = new Dictionary<string, int>();
            var priceDict = new Dictionary<string, double>();

            foreach (var mat in GetFilteredMaterials(comp))
            {
                var units = _materialStorage.GetMaterialAmount(uid, mat.ID);
                var perEntity = Math.Max(1, GetUnitsPerEntity(mat));
                var entitiesCount = units / perEntity;

                available[mat.ID] = entitiesCount;
                priceDict[mat.ID] = GetMaterialPrice(mat, perEntity, prices);
            }

            var items = new List<MarketplaceItemEntry>();
            foreach (var (protoId, list) in GetStoredItems(uid, comp))
            {
                double? price = null;
                if (prices != null && prices.ItemPrices.TryGetValue(protoId, out var custom))
                    price = custom;

                items.Add(new MarketplaceItemEntry(protoId, GetItemName(protoId), list.Count, price));
            }

            _ui.SetUiState(uid, MaterialMarketplaceUiKey.Key,
                new MaterialMarketplaceState(available, priceDict, items, comp.DepositedCash));
        }

        private bool IsAllowedByTags(MaterialPrototype mat, HashSet<string> whitelistTags, HashSet<string> blacklistTags)
        {
            if (mat.StackEntity == null)
                return true;

            if (!_prototype.TryIndex<EntityPrototype>(mat.StackEntity, out var stackProto))
                return true;

            if (whitelistTags.Count > 0)
            {
                if (!whitelistTags.Any(tag =>
                        stackProto.ID.Contains(tag, StringComparison.OrdinalIgnoreCase) ||
                        stackProto.Name.Contains(tag, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            if (blacklistTags.Count > 0)
            {
                if (blacklistTags.Any(tag =>
                        stackProto.ID.Contains(tag, StringComparison.OrdinalIgnoreCase) ||
                        stackProto.Name.Contains(tag, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            return true;
        }

        #region Вставка наличных и предметов

        private void OnInteractUsing(EntityUid uid, MaterialMarketplaceComponent comp, InteractUsingEvent args)
        {
            if (args.Handled || !_power.IsPowered(uid))
                return;

            var used = args.Used;

            if (TryComp<StackComponent>(used, out var stack))
            {
                if (stack.StackTypeId != comp.CashStackType)
                    return;

                var amount = _stack.GetCount(used);
                comp.DepositedCash += amount;
                QueueDel(used);
                args.Handled = true;

                Say(uid, args.User, $"Внесено {amount}¢. На балансе: {comp.DepositedCash}¢.");
                UpdateUI(uid, comp);
                return;
            }

            if (!comp.AllowItems || !CanStoreItem(used))
                return;

            if (!_container.TryGetContainer(uid, comp.ItemContainerId, out var container))
                return;

            if (!_container.Insert(used, container))
                return;

            args.Handled = true;
            Say(uid, args.User, "Предмет помещён в торгомат.");
            UpdateUI(uid, comp);
        }

        private bool CanStoreItem(EntityUid item)
        {
            return HasComp<ItemComponent>(item) &&
                   !HasComp<StackComponent>(item) &&
                   MetaData(item).EntityPrototype != null;
        }

        #endregion

        #region Оплата

        private void OnRequestPayment(EntityUid uid, MaterialMarketplaceComponent comp, MaterialMarketplaceRequestPaymentMessage args)
        {
            var actor = args.Actor;
            var station = _stationSystem.GetOwningStation(uid);

            string? departmentName = null;
            int? departmentBalance = null;
            int? personalBalance = null;

            if (station != null && TryGetDepartmentAccount(comp, actor, out var account))
            {
                departmentName = Loc.GetString(_prototype.Index(account).Name);
                departmentBalance = _cargo.GetBalanceFromAccount(station.Value, account);
            }

            if (TryGetPersonalAccount(actor, out _, out var bank))
                personalBalance = bank.IsInfinite ? int.MaxValue : bank.Balance.Int();

            _ui.ServerSendUiMessage(uid, MaterialMarketplaceUiKey.Key,
                new MaterialMarketplacePaymentOptionsMessage(departmentName, departmentBalance, personalBalance), actor);
        }

        private bool TryGetDepartmentAccount(
            MaterialMarketplaceComponent comp,
            EntityUid actor,
            out ProtoId<CargoAccountPrototype> account)
        {
            account = default;
            var tags = _access.FindAccessTags(actor);

            foreach (var (tag, departmentAccount) in comp.DepartmentAccess)
            {
                if (!tags.Contains(tag))
                    continue;

                account = departmentAccount;
                return true;
            }

            return false;
        }

        private bool TryGetPersonalAccount(EntityUid actor, out EntityUid card, out BankAccountComponent bank)
        {
            card = default;
            bank = default!;

            if (!_idCard.TryFindIdCard(actor, out var idCard))
                return false;

            if (!TryComp<BankAccountComponent>(idCard, out var found))
                return false;

            card = idCard.Owner;
            bank = found;
            return true;
        }

        private void OnEjectCash(EntityUid uid, MaterialMarketplaceComponent comp, MaterialMarketplaceEjectCashMessage args)
        {
            if (!_power.IsPowered(uid) || comp.DepositedCash <= 0)
                return;

            var amount = comp.DepositedCash;
            comp.DepositedCash = 0;
            SpawnCash(uid, comp, amount);
            UpdateUI(uid, comp);
        }

        private void SpawnCash(EntityUid uid, MaterialMarketplaceComponent comp, int amount)
        {
            if (amount <= 0)
                return;

            var stackProto = _prototype.Index(comp.CashStackType);
            var cash = Spawn(stackProto.Spawn, Transform(uid).Coordinates);
            _stack.SetCount(cash, amount);
        }

        #endregion

        #region Покупка

        private void OnCheckout(EntityUid uid, MaterialMarketplaceComponent comp, MaterialMarketplaceCheckoutMessage args)
        {
            var actor = args.Actor;
            var success = TryCheckout(uid, comp, actor, args.Lines, args.Payment);

            _ui.ServerSendUiMessage(uid, MaterialMarketplaceUiKey.Key,
                new MaterialMarketplaceCheckoutResultMessage(success), actor);

            UpdateUI(uid, comp);
        }

        private bool TryCheckout(
            EntityUid uid,
            MaterialMarketplaceComponent comp,
            EntityUid actor,
            List<MarketplaceCartLine> lines,
            MarketplacePayment payment)
        {
            if (!_power.IsPowered(uid))
                return false;

            if (lines.Count == 0 || lines.Count > MaxCartLines)
                return false;

            var station = _stationSystem.GetOwningStation(uid);
            if (station == null)
            {
                Say(uid, actor, "Торгомат не подключён к станции.");
                return false;
            }

            var wanted = new Dictionary<(MarketplaceLineKind, string), int>();
            foreach (var line in lines)
            {
                if (line.Amount <= 0 || line.Amount > MaxLineAmount)
                    return false;

                var key = (line.Kind, line.Id);
                wanted[key] = wanted.GetValueOrDefault(key) + line.Amount;
            }

            var prices = GetPrices(uid);
            var materials = GetFilteredMaterials(comp).ToDictionary(m => m.ID);
            var stored = GetStoredItems(uid, comp);

            var materialPlan = new List<(MaterialPrototype Material, int Amount, int PerEntity)>();
            var itemPlan = new List<(string ProtoId, int Amount)>();
            var total = 0;

            foreach (var ((kind, id), amount) in wanted)
            {
                if (kind == MarketplaceLineKind.Material)
                {
                    if (!materials.TryGetValue(id, out var material))
                        return false;

                    var perEntity = Math.Max(1, GetUnitsPerEntity(material));
                    if (_materialStorage.GetMaterialAmount(uid, id) / perEntity < amount)
                    {
                        Say(uid, actor, "Недостаточно материала на складе.");
                        return false;
                    }

                    total += (int)Math.Round(GetMaterialPrice(material, perEntity, prices) * amount);
                    materialPlan.Add((material, amount, perEntity));
                }
                else
                {
                    if (!stored.TryGetValue(id, out var list) || list.Count < amount)
                    {
                        Say(uid, actor, "Недостаточно предметов на складе.");
                        return false;
                    }

                    if (prices == null || !prices.ItemPrices.TryGetValue(id, out var itemPrice))
                    {
                        Say(uid, actor, "Стоимость предмета не выставлена.");
                        return false;
                    }

                    total += (int)Math.Round(itemPrice * amount);
                    itemPlan.Add((id, amount));
                }
            }

            var change = 0;
            var creditSales = true;

            switch (payment)
            {
                case MarketplacePayment.Cash:
                    if (comp.DepositedCash < total)
                    {
                        Say(uid, actor, "Внесено недостаточно наличных.");
                        return false;
                    }

                    change = comp.DepositedCash - total;
                    comp.DepositedCash = 0;
                    break;

                case MarketplacePayment.Personal:
                    if (!TryGetPersonalAccount(actor, out var card, out var bank))
                    {
                        Say(uid, actor, "На вашей ID-карте не найден банковский счёт.");
                        return false;
                    }

                    if (!bank.IsInfinite && bank.Balance < total)
                    {
                        Say(uid, actor, "Недостаточно средств на вашем счёте.");
                        return false;
                    }

                    if (!bank.IsInfinite)
                    {
                        bank.SetBalance(bank.Balance - total);
                        Dirty(card, bank);
                    }

                    break;

                case MarketplacePayment.Department:
                    if (!TryGetDepartmentAccount(comp, actor, out var account))
                    {
                        Say(uid, actor, "Нужен доступ главы отдела.");
                        return false;
                    }

                    if (_cargo.GetBalanceFromAccount(station.Value, account) < total)
                    {
                        Say(uid, actor, "Недостаточно средств на счёте отдела.");
                        return false;
                    }

                    if (account == comp.SalesAccount)
                        creditSales = false;
                    else
                        _cargo.UpdateBankAccount(station.Value, -total, account);

                    break;

                default:
                    return false;
            }

            if (creditSales && total > 0)
                _cargo.UpdateBankAccount(station.Value, total, comp.SalesAccount);

            var coords = Transform(uid).Coordinates;
            foreach (var (material, amount, perEntity) in materialPlan)
            {
                var totalUnits = amount * perEntity;
                if (_materialStorage.TryChangeMaterialAmount(uid, material.ID, -totalUnits))
                    _materialStorage.SpawnMultipleFromMaterial(totalUnits, material.ID, coords);
                else
                    Log.Error($"Торгомат {ToPrettyString(uid)}: не удалось списать {totalUnits} {material.ID} после оплаты.");
            }

            if (materialPlan.Count > 0)
            {
                var pricesComp = EnsureComp<MaterialMarketplacePricesComponent>(station.Value);
                foreach (var (material, amount, _) in materialPlan)
                {
                    pricesComp.PurchasedThisPeriod[material.ID] =
                        pricesComp.PurchasedThisPeriod.GetValueOrDefault(material.ID) + amount;
                }
            }

            // Выдача предметов.
            if (itemPlan.Count > 0 && _container.TryGetContainer(uid, comp.ItemContainerId, out var container))
            {
                foreach (var (protoId, amount) in itemPlan)
                {
                    var list = stored[protoId];
                    for (var i = 0; i < amount; i++)
                    {
                        _container.Remove(list[i], container);
                    }
                }
            }

            SpawnCash(uid, comp, change);
            Say(uid, actor, $"Покупка оформлена: {total}¢.");
            return true;
        }

        #endregion

        private void Say(EntityUid uid, EntityUid actor, string message)
        {
            _popup.PopupEntity(message, uid, actor);
        }
    }
}
