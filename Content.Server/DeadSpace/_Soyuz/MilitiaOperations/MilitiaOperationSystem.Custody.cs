// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Shared.Armor;
using Content.Shared.Actions.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.DeadSpace._Soyuz.MilitiaOperations;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Ghost;
using Content.Shared.PowerCell.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    private const string CustodyInput = "custody_input";
    private TimeSpan _nextCustodyUpdate;
    private readonly Dictionary<EntityUid, MilitiaCustodyState> _custodyUiCache = new();
    private readonly Dictionary<(EntityUid Cabinet, EntityUid User), TimeSpan> _custodyRequests = new();

    private void InitializeCustody()
    {
        SubscribeLocalEvent<MilitiaCustodyCabinetComponent, ContainerIsInsertingAttemptEvent>(OnCustodyInsertAttempt);
        SubscribeLocalEvent<MilitiaCustodyCabinetComponent, ContainerIsRemovingAttemptEvent>(OnCustodyRemoveAttempt);
        SubscribeLocalEvent<MilitiaCustodyCabinetComponent, EntInsertedIntoContainerMessage>(OnCustodyContentsChanged);
        SubscribeLocalEvent<MilitiaCustodyCabinetComponent, EntRemovedFromContainerMessage>(OnCustodyContentsRemoved);
        SubscribeLocalEvent<MilitiaCustodyCabinetComponent, ComponentShutdown>(OnCustodyShutdown);
        Subs.BuiEvents<MilitiaCustodyCabinetComponent>(MilitiaCustodyUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnCustodyOpened);
            subs.Event<MilitiaCustodyRequest>(OnCustodyRequest);
        });
    }

    private void OnCustodyOpened(Entity<MilitiaCustodyCabinetComponent> cabinet, ref BoundUIOpenedEvent args)
        => UpdateCustodyUi(cabinet);

    private void RestrictActorEquipment(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        EnsureComp<MilitiaOperativeComponent>(uid);
        var pending = new Stack<EntityUid>(actor.CreatedEquipment);
        var seen = new HashSet<EntityUid>();
        while (pending.TryPop(out var item))
        {
            if (!seen.Add(item) || !Exists(item) || TerminatingOrDeleted(item))
                continue;
            MilitiaContrabandKind? kind = item == actor.Weapon ? MilitiaContrabandKind.Firearm
                : item == actor.Backup ? MilitiaContrabandKind.Melee : null;
            if (HasComp<MagbootsComponent>(item))
                kind = MilitiaContrabandKind.Boots;
            else if (TryComp<ClothingComponent>(item, out var clothing) && HasComp<ArmorComponent>(item))
            {
                if ((clothing.Slots & SlotFlags.HEAD) != 0)
                    kind = MilitiaContrabandKind.Helmet;
                else if ((clothing.Slots & SlotFlags.OUTERCLOTHING) != 0)
                    kind = MilitiaContrabandKind.Armor;
            }
            if (kind is { } category)
            {
                var restricted = EnsureComp<MilitiaRestrictedGearComponent>(item);
                restricted.Kind = category;
                restricted.Serial = Guid.NewGuid();
                Dirty(item, restricted);
            }
            if (!TryComp<ContainerManagerComponent>(item, out var manager))
                continue;
            foreach (var container in _equipmentContainers.GetAllContainers(item, manager))
                foreach (var child in container.ContainedEntities)
                    pending.Push(child);
        }
    }

    private void RegisterContraband(EntityUid station, MilitiaOperationStationComponent state, Guid operation)
    {
        var query = EntityQueryEnumerator<MilitiaRestrictedGearComponent, MilitiaOperationEquipmentComponent>();
        while (query.MoveNext(out var item, out var restricted, out var equipment))
        {
            if (equipment.Station == station && equipment.Operation == operation && restricted.Serial != Guid.Empty)
                state.IssuedContraband.TryAdd(restricted.Serial, item);
        }
    }

    private MilitiaOperationStationComponent? CustodyStation(EntityUid cabinet, MilitiaCustodyCabinetComponent component, out EntityUid station)
    {
        station = EntityUid.Invalid;
        if (_stations.GetOwningStation(cabinet) is not { } owner || TerminatingOrDeleted(owner) ||
            !_prototypes.TryIndex(component.Config, out var config))
            return null;
        station = owner;
        var state = EnsureComp<MilitiaOperationStationComponent>(owner);
        if (state.Config.Length == 0)
        {
            state.Config = config.ID;
            InitializeOffers(state, config);
        }
        return state.Config == config.ID ? state : null;
    }

    private bool CustodyPowered(EntityUid uid)
        => Exists(uid) && !TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid) &&
           Transform(uid).Anchored && this.IsPowered(uid, EntityManager);

    private HashSet<EntityUid>? CustodyPayload(EntityUid item)
    {
        var pending = new Stack<EntityUid>();
        var seen = new HashSet<EntityUid>();
        pending.Push(item);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current) || seen.Count > 256)
                return null;
            if (TryComp(current, out TransformComponent? transform))
            {
                var children = transform.ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    if (HasComp<ItemComponent>(child) && (!_equipmentContainers.TryGetContainingContainer(child, out var owner) || owner.Owner != current))
                        return null;
                }
            }
            if (!TryComp<ContainerManagerComponent>(current, out var manager))
                continue;
            foreach (var container in _equipmentContainers.GetAllContainers(current, manager))
            foreach (var child in container.ContainedEntities)
            {
                if (TerminatingOrDeleted(child) || EntityManager.IsQueuedForDeletion(child) ||
                    !HasComp<CartridgeAmmoComponent>(child) && !HasComp<BallisticAmmoProviderComponent>(child) &&
                    !HasComp<PowerCellComponent>(child) && !HasComp<MilitiaRestrictedGearComponent>(child) &&
                    (HasComp<ItemComponent>(child) || !HasComp<ActionComponent>(child) && !HasComp<SolutionComponent>(child)))
                    return null;
                pending.Push(child);
            }
        }
        return seen;
    }

    private MilitiaCustodyQuote? QuoteContraband(EntityUid item, EntityUid station, MilitiaOperationStationComponent? state,
        MilitiaOperationConfigPrototype config, out string reason)
    {
        reason = "militia-custody-unregistered";
        if (state == null || !Exists(item) || TerminatingOrDeleted(item) || EntityManager.IsQueuedForDeletion(item) ||
            !TryComp<MilitiaRestrictedGearComponent>(item, out var restricted) || restricted.Surrendered ||
            restricted.Serial == Guid.Empty || state.RedeemedContraband.Contains(restricted.Serial) ||
            !state.IssuedContraband.TryGetValue(restricted.Serial, out var issued) || issued != item ||
            !config.ContrabandRewards.TryGetValue(restricted.Kind, out var price) || price.Points < 0 || price.Reputation < 0 ||
            price.Points == 0 && price.Reputation == 0)
            return null;
        reason = string.Empty;
        return new MilitiaCustodyQuote(item, item, restricted.Serial, restricted.Kind, price.Points, price.Reputation);
    }

    private List<MilitiaCustodyQuote>? QuoteCustodyTree(EntityUid root, EntityUid station, MilitiaOperationStationComponent? state,
        MilitiaOperationConfigPrototype config, out string reason)
    {
        if (QuoteContraband(root, station, state, config, out reason) == null)
            return null;
        var payload = CustodyPayload(root);
        if (payload == null)
        {
            reason = "militia-custody-extra-content";
            return null;
        }
        var quotes = new List<MilitiaCustodyQuote>();
        foreach (var item in payload)
        {
            if (!HasComp<MilitiaRestrictedGearComponent>(item))
                continue;
            if (QuoteContraband(item, station, state, config, out reason) is not { } quote)
                return null;
            quotes.Add(quote with { Root = root });
        }
        return quotes;
    }

    private void OnCustodyInsertAttempt(Entity<MilitiaCustodyCabinetComponent> cabinet, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID == CustodyInput && (cabinet.Comp.Deadline != null || cabinet.Comp.Paid ||
            args.Container.ContainedEntities.Count >= cabinet.Comp.Capacity || !HasComp<ItemComponent>(args.EntityUid)))
            args.Cancel();
    }

    private void OnCustodyRemoveAttempt(Entity<MilitiaCustodyCabinetComponent> cabinet, ref ContainerIsRemovingAttemptEvent args)
    {
        if (args.Container.ID == CustodyInput && (cabinet.Comp.Deadline != null || cabinet.Comp.Paid))
            args.Cancel();
    }

    private void OnCustodyContentsChanged(Entity<MilitiaCustodyCabinetComponent> cabinet, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == CustodyInput)
            CustodyContentsChanged(cabinet);
    }

    private void OnCustodyContentsRemoved(Entity<MilitiaCustodyCabinetComponent> cabinet, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == CustodyInput)
            CustodyContentsChanged(cabinet);
    }

    private void CustodyContentsChanged(Entity<MilitiaCustodyCabinetComponent> cabinet)
    {
        cabinet.Comp.Token = Guid.NewGuid();
        if (cabinet.Comp.Deadline != null && !cabinet.Comp.Paid)
            CancelCustody(cabinet);
        else if (!cabinet.Comp.Paid)
            cabinet.Comp.Status = "militia-custody-ready";
        UpdateCustodyUi(cabinet);
    }

    private void OnCustodyRequest(Entity<MilitiaCustodyCabinetComponent> cabinet, ref MilitiaCustodyRequest args)
    {
        if (args.Token != cabinet.Comp.Token || TerminatingOrDeleted(cabinet) || HasComp<GhostComponent>(args.Actor) ||
            !_blocker.CanInteract(args.Actor, cabinet) || !_access.IsAllowed(args.Actor, cabinet) ||
            !_ui.IsUiOpen(cabinet.Owner, MilitiaCustodyUiKey.Key, args.Actor) || !_interaction.InRangeUnobstructed(args.Actor, cabinet.Owner) ||
            cabinet.Comp.Deadline != null || cabinet.Comp.Paid ||
            !_equipmentContainers.TryGetContainer(cabinet, CustodyInput, out var input))
            return;
        var requestKey = (cabinet.Owner, args.Actor);
        if (_custodyRequests.TryGetValue(requestKey, out var next) && _timing.CurTime < next)
            return;
        _custodyRequests[requestKey] = _timing.CurTime + TimeSpan.FromSeconds(0.2);
        UpdateCustodyUi(cabinet);
        if (args.Token != cabinet.Comp.Token)
            return;
        switch (args.Action)
        {
            case MilitiaCustodyAction.Insert:
                if (_hands.GetActiveItem(args.Actor) is not { } held)
                    cabinet.Comp.Status = "militia-custody-hold-item";
                else if (!HasComp<ItemComponent>(held) || !_hands.TryDropIntoContainer(args.Actor, held, input))
                    cabinet.Comp.Status = "militia-custody-cannot-insert";
                break;
            case MilitiaCustodyAction.Eject:
                if (args.Item is { } net && TryGetEntity(net, out var item) && item is { } ejected && input.Contains(ejected))
                {
                    if (!_hands.TryPickupAnyHand(args.Actor, ejected, animate: false))
                        cabinet.Comp.Status = "militia-custody-free-hand";
                }
                break;
            case MilitiaCustodyAction.Submit:
                if (!CustodyPowered(cabinet))
                    break;
                var state = CustodyStation(cabinet, cabinet.Comp, out var station);
                if (state == null || !_prototypes.TryIndex(cabinet.Comp.Config, out var config))
                    break;
                var serials = new HashSet<Guid>();
                var pending = new List<MilitiaCustodyQuote>();
                foreach (var uid in input.ContainedEntities)
                {
                    if (QuoteCustodyTree(uid, station, state, config, out _) is not { } quotes)
                        continue;
                    foreach (var quote in quotes)
                    {
                        if (serials.Add(quote.Serial))
                            pending.Add(quote);
                    }
                }
                if (pending.Count == 0)
                    break;
                cabinet.Comp.Pending.Clear();
                cabinet.Comp.Pending.AddRange(pending);
                cabinet.Comp.Station = station;
                cabinet.Comp.Deadline = _timing.CurTime + cabinet.Comp.SealDuration;
                cabinet.Comp.RetryUntil = cabinet.Comp.Deadline.Value + TimeSpan.FromSeconds(5);
                cabinet.Comp.Status = "militia-custody-sealing";
                cabinet.Comp.Token = Guid.NewGuid();
                break;
        }
        UpdateCustodyUi(cabinet);
    }

    private void CancelCustody(Entity<MilitiaCustodyCabinetComponent> cabinet)
    {
        cabinet.Comp.Deadline = null;
        cabinet.Comp.Pending.Clear();
        cabinet.Comp.Delivery.Clear();
        cabinet.Comp.Token = Guid.NewGuid();
        cabinet.Comp.Status = "militia-custody-cancelled";
    }

    private void UpdateCustody()
    {
        if (_timing.CurTime < _nextCustodyUpdate)
            return;
        _nextCustodyUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        var query = EntityQueryEnumerator<MilitiaCustodyCabinetComponent>();
        while (query.MoveNext(out var uid, out var cabinet))
        {
            if (cabinet.Paid)
                PurgeCustody((uid, cabinet));
            else if (cabinet.Deadline != null)
            {
                if (!CustodyPowered(uid) || _stations.GetOwningStation(uid) != cabinet.Station)
                    CancelCustody((uid, cabinet));
                else if (cabinet.Deadline <= _timing.CurTime)
                    CommitCustody((uid, cabinet));
            }
            UpdateCustodyUi((uid, cabinet));
        }
    }

    private void CommitCustody(Entity<MilitiaCustodyCabinetComponent> cabinet)
    {
        var state = CustodyStation(cabinet, cabinet.Comp, out var station);
        if (state?.Busy == true && _timing.CurTime < cabinet.Comp.RetryUntil)
            return;
        if (state == null || state.Busy || station != cabinet.Comp.Station ||
            !_prototypes.TryIndex(cabinet.Comp.Config, out var config) ||
            !_equipmentContainers.TryGetContainer(cabinet, CustodyInput, out var input))
        {
            CancelCustody(cabinet);
            return;
        }
        state.Busy = true;
        try
        {
            var current = new HashSet<MilitiaCustodyQuote>();
            var delivery = new HashSet<EntityUid>();
            foreach (var root in cabinet.Comp.Pending.Select(quote => quote.Root).Distinct())
            {
                if (!input.Contains(root) || QuoteCustodyTree(root, station, state, config, out _) is not { } quotes ||
                    CustodyPayload(root) is not { } payload)
                    throw new InvalidOperationException("Custody contents changed.");
                current.UnionWith(quotes);
                delivery.UnionWith(payload);
            }
            if (current.Count != cabinet.Comp.Pending.Count || !current.SetEquals(cabinet.Comp.Pending))
                throw new InvalidOperationException("Custody quote changed.");
            var redeemed = new HashSet<Guid>(state.RedeemedContraband);
            long points = 0;
            long reputation = 0;
            foreach (var quote in cabinet.Comp.Pending)
            {
                if (!redeemed.Add(quote.Serial))
                    throw new InvalidOperationException("Duplicate custody receipt.");
                points = checked(points + quote.Points);
                reputation = checked(reputation + quote.Reputation);
            }
            points = checked(state.SupplyPoints + points);
            reputation = checked(state.Reputation + reputation);
            cabinet.Comp.Delivery.Clear();
            cabinet.Comp.Delivery.UnionWith(delivery);
            state.RedeemedContraband = redeemed;
            state.SupplyPoints = points;
            state.Reputation = reputation;
            cabinet.Comp.Paid = true;
            cabinet.Comp.Deadline = null;
            cabinet.Comp.Status = "militia-custody-complete";
            foreach (var quote in cabinet.Comp.Pending)
            {
                if (TryComp<MilitiaRestrictedGearComponent>(quote.Item, out var gear))
                {
                    gear.Surrendered = true;
                    Dirty(quote.Item, gear);
                }
            }
            _log.Info($"Contraband surrendered: Station={station}, Cabinet={cabinet.Owner}, Count={cabinet.Comp.Pending.Count}");
        }
        catch (Exception exception)
        {
            _log.Error($"Contraband handover failed at {cabinet.Owner}: {exception}");
            if (!cabinet.Comp.Paid)
                CancelCustody(cabinet);
        }
        finally
        {
            state.Busy = false;
        }
        if (cabinet.Comp.Paid)
            PurgeCustody(cabinet);
    }

    private void PurgeCustody(Entity<MilitiaCustodyCabinetComponent> cabinet)
    {
        foreach (var item in cabinet.Comp.Delivery)
        {
            if (Exists(item) && !TerminatingOrDeleted(item) && !EntityManager.IsQueuedForDeletion(item))
                QueueDel(item);
        }
        if (cabinet.Comp.Delivery.Any(Exists))
            return;
        cabinet.Comp.Pending.Clear();
        cabinet.Comp.Delivery.Clear();
        cabinet.Comp.Paid = false;
        cabinet.Comp.Token = Guid.NewGuid();
    }

    private void OnCustodyShutdown(Entity<MilitiaCustodyCabinetComponent> cabinet, ref ComponentShutdown args)
    {
        if (cabinet.Comp.Paid)
            PurgeCustody(cabinet);
        if (_equipmentContainers.TryGetContainer(cabinet, CustodyInput, out var input))
        {
            foreach (var item in input.ContainedEntities.ToArray())
            {
                if (!cabinet.Comp.Delivery.Contains(item) && !TerminatingOrDeleted(item))
                    _equipmentContainers.Remove(item, input, force: true, destination: Transform(cabinet).Coordinates);
            }
        }
        _custodyUiCache.Remove(cabinet.Owner);
        foreach (var key in _custodyRequests.Keys.Where(key => key.Cabinet == cabinet.Owner).ToArray())
            _custodyRequests.Remove(key);
    }

    private void UpdateCustodyUi(Entity<MilitiaCustodyCabinetComponent> cabinet)
    {
        if (TerminatingOrDeleted(cabinet))
            return;
        var state = CustodyStation(cabinet, cabinet.Comp, out var station);
        var entries = new List<MilitiaCustodyEntry>();
        if (_prototypes.TryIndex(cabinet.Comp.Config, out var config) &&
            _equipmentContainers.TryGetContainer(cabinet, CustodyInput, out var input))
        {
            foreach (var item in input.ContainedEntities)
            {
                if (!Exists(item) || TerminatingOrDeleted(item))
                    continue;
                var quotes = QuoteCustodyTree(item, station, state, config, out var reason);
                var name = quotes?.Count > 1 ? Loc.GetString("militia-custody-kit-name", ("name", Name(item)), ("count", quotes.Count)) : Name(item);
                entries.Add(new MilitiaCustodyEntry(GetNetEntity(item), name, MetaData(item).EntityPrototype?.ID ?? string.Empty,
                    quotes?.Sum(quote => (long) quote.Points) ?? 0, quotes?.Sum(quote => (long) quote.Reputation) ?? 0, reason));
            }
        }
        var busy = cabinet.Comp.Deadline != null || cabinet.Comp.Paid;
        var online = CustodyPowered(cabinet) && state != null;
        var signature = string.Join(";", entries.Select(entry => $"{entry.Item}:{entry.Name}:{entry.Prototype}:{entry.Points}:{entry.Reputation}:{entry.Reason}"));
        if (signature != cabinet.Comp.QuoteSignature)
        {
            cabinet.Comp.QuoteSignature = signature;
            cabinet.Comp.Token = Guid.NewGuid();
        }
        var view = new MilitiaCustodyState(cabinet.Comp.Token, entries,
            state?.SupplyPoints ?? 0, state?.Reputation ?? 0, busy, online, cabinet.Comp.Deadline,
            !busy && !online ? "militia-custody-no-power" : cabinet.Comp.Status, cabinet.Comp.Capacity);
        _custodyAppearance.SetData(cabinet.Owner, MilitiaCustodyVisuals.State, busy ? 2 : CustodyPowered(cabinet) ? 1 : 0);
        if (_custodyUiCache.TryGetValue(cabinet.Owner, out var previous) && previous.Token == view.Token &&
            previous.Points == view.Points && previous.Reputation == view.Reputation && previous.Busy == view.Busy &&
            previous.Online == view.Online && previous.Deadline == view.Deadline && previous.Status == view.Status && previous.Capacity == view.Capacity)
            return;
        _custodyUiCache[cabinet.Owner] = view;
        _ui.SetUiState(cabinet.Owner, MilitiaCustodyUiKey.Key, view);
    }

    [Dependency] private readonly SharedAppearanceSystem _custodyAppearance = default!;
}
