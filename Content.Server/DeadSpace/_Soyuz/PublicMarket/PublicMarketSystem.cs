// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Content.Server.Cargo.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Backmen.Economy;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.DeadSpace._Soyuz.MaterialMarketplace;
using Content.Shared.DeadSpace._Soyuz.PublicMarket;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.PublicMarket;

/// <summary>
/// Каждый сотрудник отдела выставляет предметы, деньги от продажи идут на счёт его отдела (за вычетом комиссии, которая задаётся в консоли цен и уходит на счёт снабжения).
/// </summary>
public sealed class PublicMarketSystem : EntitySystem
{
    private const int MaxCartLots = 64;
    private const string DepartmentLocPrefix = "department-";

    [Dependency] private readonly CargoSystem _cargo = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PublicMarketComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<PublicMarketComponent, BoundUIOpenedEvent>(OnUIOpened);
        SubscribeLocalEvent<PublicMarketComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<PublicMarketComponent, PublicMarketSetPriceMessage>(OnSetPrice);
        SubscribeLocalEvent<PublicMarketComponent, PublicMarketWithdrawMessage>(OnWithdraw);
        SubscribeLocalEvent<PublicMarketComponent, PublicMarketBuyMessage>(OnBuy);
        SubscribeLocalEvent<PublicMarketComponent, MaterialMarketplaceRequestPaymentMessage>(OnRequestPayment);
        SubscribeLocalEvent<PublicMarketComponent, MaterialMarketplaceEjectCashMessage>(OnEjectCash);
    }

    private void OnInit(EntityUid uid, PublicMarketComponent comp, ComponentInit args)
    {
        _container.EnsureContainer<Container>(uid, comp.ItemContainerId);
    }

    private void OnUIOpened(EntityUid uid, PublicMarketComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUI(uid, comp);
    }

    /// <summary>
    /// Обновить интерфейс всех рынков станции (после смены комиссии).
    /// </summary>
    public void RefreshStation(EntityUid station)
    {
        var query = EntityQueryEnumerator<PublicMarketComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_station.GetOwningStation(uid) != station)
                continue;

            UpdateUI(uid, comp);
        }
    }

    private int GetCommission(EntityUid market)
    {
        var station = _station.GetOwningStation(market);
        if (station == null)
            return 0;

        var prices = CompOrNull<MaterialMarketplacePricesComponent>(station.Value);
        return Math.Clamp(prices?.MarketCommissionPercent ?? 0, 0, 100);
    }

    private string GetAccountName(ProtoId<CargoAccountPrototype> account)
    {
        return _prototype.TryIndex(account, out var proto) ? Loc.GetString(proto.Name) : account.Id;
    }

    private void UpdateUI(EntityUid uid, PublicMarketComponent comp)
    {
        var lots = new List<PublicMarketLotEntry>();

        if (_container.TryGetContainer(uid, comp.ItemContainerId, out var container))
        {
            foreach (var item in container.ContainedEntities)
            {
                if (!TryComp<PublicMarketLotComponent>(item, out var lot))
                    continue;

                var meta = MetaData(item);
                lots.Add(new PublicMarketLotEntry(
                    GetNetEntity(item),
                    meta.EntityPrototype?.ID ?? string.Empty,
                    meta.EntityName,
                    lot.Price,
                    lot.SellerUserId,
                    lot.SellerName,
                    GetAccountName(lot.SellerAccount)));
            }
        }

        _ui.SetUiState(uid, PublicMarketUiKey.Key,
            new PublicMarketState(lots, comp.DepositedCash, GetCommission(uid)));
    }

    #region Выставление предметов и наличка

    private void OnInteractUsing(EntityUid uid, PublicMarketComponent comp, InteractUsingEvent args)
    {
        if (args.Handled || !_power.IsPowered(uid))
            return;

        var used = args.Used;
        var user = args.User;

        if (TryComp<StackComponent>(used, out var stack))
        {
            if (stack.StackTypeId != comp.CashStackType)
                return;

            var amount = _stack.GetCount(used);
            comp.DepositedCash += amount;
            QueueDel(used);
            args.Handled = true;

            Say(uid, user, $"Внесено {amount}¢. На балансе: {comp.DepositedCash}¢.");
            UpdateUI(uid, comp);
            return;
        }

        if (!CanStoreItem(used))
            return;

        args.Handled = true;

        var userId = GetUserId(user);
        if (userId == null)
            return;

        if (!TryGetSellerAccount(comp, user, out var account))
        {
            Say(uid, user, "Ваш отдел не может продавать на общем рынке (или нет ID-карты).");
            return;
        }

        if (!_container.TryGetContainer(uid, comp.ItemContainerId, out var container))
            return;

        var lot = AddComp<PublicMarketLotComponent>(used);
        lot.SellerUserId = userId;
        lot.SellerName = MetaData(user).EntityName;
        lot.SellerAccount = account;
        lot.Price = null;

        if (!_container.Insert(used, container))
        {
            RemComp<PublicMarketLotComponent>(used);
            return;
        }

        Say(uid, user, "Предмет помещён на рынок. Задайте цену в разделе «Мои лоты».");
        UpdateUI(uid, comp);
    }

    private bool CanStoreItem(EntityUid item)
    {
        return HasComp<ItemComponent>(item) &&
               !HasComp<StackComponent>(item) &&
               MetaData(item).EntityPrototype != null;
    }

    private string? GetUserId(EntityUid user)
    {
        return TryComp<ActorComponent>(user, out var actor)
            ? actor.PlayerSession.UserId.UserId.ToString()
            : null;
    }

    /// <summary>
    /// Отдел продавца берётся из JobDepartments его ID-карты: первый отдел, который есть в SellerDepartments.
    /// </summary>
    private bool TryGetSellerAccount(
        PublicMarketComponent comp,
        EntityUid user,
        out ProtoId<CargoAccountPrototype> account)
    {
        account = default;

        if (!_idCard.TryFindIdCard(user, out var idCard))
            return false;

        foreach (var department in idCard.Comp.JobDepartments)
        {
            string id = department;
            if (id.StartsWith(DepartmentLocPrefix, StringComparison.Ordinal))
                id = id.Substring(DepartmentLocPrefix.Length);

            if (comp.SellerDepartments.TryGetValue(id, out var found))
            {
                account = found;
                return true;
            }
        }

        return false;
    }

    private void OnEjectCash(EntityUid uid, PublicMarketComponent comp, MaterialMarketplaceEjectCashMessage args)
    {
        if (!_power.IsPowered(uid) || comp.DepositedCash <= 0)
            return;

        var amount = comp.DepositedCash;
        comp.DepositedCash = 0;
        SpawnCash(uid, comp, amount);
        UpdateUI(uid, comp);
    }

    private void SpawnCash(EntityUid uid, PublicMarketComponent comp, int amount)
    {
        if (amount <= 0)
            return;

        var stackProto = _prototype.Index(comp.CashStackType);
        var cash = Spawn(stackProto.Spawn, Transform(uid).Coordinates);
        _stack.SetCount(cash, amount);
    }

    #endregion

    #region Управление своими лотами

    private bool TryGetOwnLot(
        EntityUid uid,
        PublicMarketComponent comp,
        EntityUid actor,
        NetEntity netLot,
        out EntityUid item,
        out PublicMarketLotComponent lot,
        out BaseContainer container)
    {
        item = default;
        lot = default!;
        container = default!;

        if (!TryGetEntity(netLot, out var found))
            return false;

        if (!_container.TryGetContainer(uid, comp.ItemContainerId, out var foundContainer) ||
            !foundContainer.Contains(found.Value))
            return false;

        if (!TryComp<PublicMarketLotComponent>(found.Value, out var foundLot))
            return false;

        var userId = GetUserId(actor);
        if (userId == null || userId != foundLot.SellerUserId)
            return false;

        item = found.Value;
        lot = foundLot;
        container = foundContainer;
        return true;
    }

    private void OnSetPrice(EntityUid uid, PublicMarketComponent comp, PublicMarketSetPriceMessage args)
    {
        if (!_power.IsPowered(uid))
            return;

        if (!TryGetOwnLot(uid, comp, args.Actor, args.Lot, out _, out var lot, out _))
            return;

        if (args.Price is { } price)
        {
            if (price < 0 || price > comp.MaxLotPrice)
            {
                Say(uid, args.Actor, $"Цена должна быть от 0 до {comp.MaxLotPrice}.");
                return;
            }

            lot.Price = price;
        }
        else
        {
            lot.Price = null;
        }

        UpdateUI(uid, comp);
    }

    private void OnWithdraw(EntityUid uid, PublicMarketComponent comp, PublicMarketWithdrawMessage args)
    {
        if (!_power.IsPowered(uid))
            return;

        if (!TryGetOwnLot(uid, comp, args.Actor, args.Lot, out var item, out _, out var container))
            return;

        _container.Remove(item, container);
        RemComp<PublicMarketLotComponent>(item);

        Say(uid, args.Actor, "Лот снят с продажи.");
        UpdateUI(uid, comp);
    }

    #endregion

    #region Оплата

    private void OnRequestPayment(EntityUid uid, PublicMarketComponent comp, MaterialMarketplaceRequestPaymentMessage args)
    {
        var actor = args.Actor;
        var station = _station.GetOwningStation(uid);

        string? departmentName = null;
        int? departmentBalance = null;
        int? personalBalance = null;

        if (station != null && TryGetPayerDepartmentAccount(comp, actor, out var account))
        {
            departmentName = GetAccountName(account);
            departmentBalance = _cargo.GetBalanceFromAccount(station.Value, account);
        }

        if (TryGetPersonalAccount(actor, out _, out var bank))
            personalBalance = bank.IsInfinite ? int.MaxValue : bank.Balance.Int();

        _ui.ServerSendUiMessage(uid, PublicMarketUiKey.Key,
            new MaterialMarketplacePaymentOptionsMessage(departmentName, departmentBalance, personalBalance), actor);
    }

    private bool TryGetPayerDepartmentAccount(
        PublicMarketComponent comp,
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

    #endregion

    #region Покупка

    private void OnBuy(EntityUid uid, PublicMarketComponent comp, PublicMarketBuyMessage args)
    {
        var actor = args.Actor;
        var success = TryBuy(uid, comp, actor, args.Lots, args.Payment);

        _ui.ServerSendUiMessage(uid, PublicMarketUiKey.Key,
            new MaterialMarketplaceCheckoutResultMessage(success), actor);

        UpdateUI(uid, comp);
    }

    private bool TryBuy(
        EntityUid uid,
        PublicMarketComponent comp,
        EntityUid actor,
        List<NetEntity> lots,
        MarketplacePayment payment)
    {
        if (!_power.IsPowered(uid))
            return false;

        if (lots.Count == 0 || lots.Count > MaxCartLots)
            return false;

        var station = _station.GetOwningStation(uid);
        if (station == null)
        {
            Say(uid, actor, "Рынок не подключён к станции.");
            return false;
        }

        if (!_container.TryGetContainer(uid, comp.ItemContainerId, out var container))
            return false;

        var plan = new List<(EntityUid Item, PublicMarketLotComponent Lot, int Price)>();
        var seen = new HashSet<EntityUid>();
        var total = 0;

        foreach (var netLot in lots)
        {
            if (!TryGetEntity(netLot, out var found) ||
                !seen.Add(found.Value) ||
                !container.Contains(found.Value) ||
                !TryComp<PublicMarketLotComponent>(found.Value, out var lot))
            {
                Say(uid, actor, "Один из лотов уже недоступен.");
                return false;
            }

            if (lot.Price is not { } price)
            {
                Say(uid, actor, "Продавец ещё не назначил цену на один из лотов.");
                return false;
            }

            plan.Add((found.Value, lot, price));
            total += price;
        }

        var change = 0;

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
                if (!TryGetPayerDepartmentAccount(comp, actor, out var account))
                {
                    Say(uid, actor, "Нужен доступ главы отдела.");
                    return false;
                }

                if (_cargo.GetBalanceFromAccount(station.Value, account) < total)
                {
                    Say(uid, actor, "Недостаточно средств на счёте отдела.");
                    return false;
                }

                _cargo.UpdateBankAccount(station.Value, -total, account);
                break;

            default:
                return false;
        }

        var commissionPercent = GetCommission(uid);
        foreach (var (_, sellerLot, price) in plan)
        {
            var commission = (int) Math.Round(price * commissionPercent / 100.0);
            var share = price - commission;

            if (share > 0)
                _cargo.UpdateBankAccount(station.Value, share, sellerLot.SellerAccount);

            if (commission > 0)
                _cargo.UpdateBankAccount(station.Value, commission, comp.CommissionAccount);
        }

        foreach (var (item, _, _) in plan)
        {
            _container.Remove(item, container);
            RemComp<PublicMarketLotComponent>(item);
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