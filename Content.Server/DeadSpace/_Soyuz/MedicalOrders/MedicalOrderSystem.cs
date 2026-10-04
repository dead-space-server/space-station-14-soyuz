// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Content.Server.Humanoid.Systems;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server.Station.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Access.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DeadSpace._Soyuz.MedicalOrders;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.MedicalOrders;

/// <summary>Owns patient orders, the medicine exchange and their station-wide economy.</summary>
public sealed partial class MedicalOrderSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly StationSystem _stations = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly RandomHumanoidSystem _randomHumanoids = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly EntityStorageSystem _storage = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly RepairOrderRewardDeliverySystem _delivery = default!;

    private ISawmill _sawmill = default!;
    private TimeSpan _nextDiscovery;
    private TimeSpan _nextUiRefresh;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("medical_orders");
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _marketBasePrices.Clear();
            _marketBaseReputations.Clear();
        });
        SubscribeLocalEvent<MedicalOrderMachineComponent, ComponentStartup>(OnMachineStartup);
        SubscribeLocalEvent<MedicalOrderMachineComponent, ComponentShutdown>(OnMachineShutdown);
        SubscribeLocalEvent<MedicalOrderStationComponent, ComponentShutdown>(OnStationShutdown);
        SubscribeLocalEvent<MedicalOrderMachineComponent, EntInsertedIntoContainerMessage>(OnBeakerInserted);
        SubscribeLocalEvent<MedicalOrderMachineComponent, EntRemovedFromContainerMessage>(OnBeakerRemoved);
        Subs.BuiEvents<MedicalOrderMachineComponent>(MedicalOrderUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<MedicalOrderRequestMessage>(OnRequest);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var refreshUi = now >= _nextUiRefresh;
        if (refreshUi)
            _nextUiRefresh = now + TimeSpan.FromSeconds(1);
        if (now >= _nextDiscovery)
        {
            DiscoverMachines();
            _nextDiscovery = now + TimeSpan.FromSeconds(1);
        }

        var query = EntityQueryEnumerator<MedicalOrderStationComponent>();
        while (query.MoveNext(out var uid, out var state))
        {
            if (state.Busy)
                continue;
            if (!_prototypes.TryIndex<MedicalOrderConfigPrototype>(state.ConfigId, out var config))
                continue;

            if (now >= state.NextRefresh)
            {
                RefreshOffers(state, config);
                UpdateStationUis(state, config);
            }

            if (state.PatientActive is { } patient)
            {
                if (!Exists(patient.Terminal) || Terminating(patient.Terminal) ||
                    patient.Patient is not { } body || !Exists(body) || Terminating(body) || now >= patient.Deadline)
                    FinalizeOrder(uid, state, config, patient, expired: true);
            }

            if (refreshUi)
                UpdateStationUis(state, config, marketOnly: state.PatientActive == null);
        }
    }

    private void DiscoverMachines()
    {
        var query = EntityQueryEnumerator<MedicalOrderMachineComponent>();
        while (query.MoveNext(out var uid, out var machine))
            EnsureStation(uid, machine);
    }

    private void OnMachineStartup(Entity<MedicalOrderMachineComponent> machine, ref ComponentStartup args)
    {
        EnsureStation(machine.Owner, machine.Comp);
    }

    private void OnMachineShutdown(Entity<MedicalOrderMachineComponent> machine, ref ComponentShutdown args)
    {
        if (_stations.GetOwningStation(machine.Owner) is not { } station ||
            !TryComp<MedicalOrderStationComponent>(station, out var state))
            return;

        state.Machines.Remove(machine.Owner);
        if (state.PatientActive is { } patient && patient.Terminal == machine.Owner &&
            _prototypes.TryIndex<MedicalOrderConfigPrototype>(state.ConfigId, out var patientConfig))
            FinalizeOrder(station, state, patientConfig, patient, expired: true);
    }

    private void OnStationShutdown(Entity<MedicalOrderStationComponent> station, ref ComponentShutdown args)
    {
        var active = station.Comp.PatientActive;
        if (active?.Patient is { } body && Exists(body) &&
            TryComp<MedicalOrderPatientComponent>(body, out var marker) &&
            marker.Station == station.Owner &&
            marker.RuntimeId == active.Offer.RuntimeId)
            RemComp<MedicalOrderPatientComponent>(body);
        station.Comp.Machines.Clear();
    }

    private MedicalOrderStationComponent? EnsureStation(EntityUid machineUid, MedicalOrderMachineComponent machine)
    {
        if (_stations.GetOwningStation(machineUid) is not { } station)
            return null;

        var state = EnsureComp<MedicalOrderStationComponent>(station);
        if (state.ConfigId.Length == 0)
        {
            state.ConfigId = machine.Config.Id;
            if (!_prototypes.TryIndex<MedicalOrderConfigPrototype>(state.ConfigId, out var config))
                return null;
            RefreshOffers(state, config);
        }

        if (state.ConfigId != machine.Config.Id)
        {
            _sawmill.Error($"Medical order machine {machineUid} has a different config on station {station}.");
            return null;
        }

        state.Machines.Add(machineUid);
        return state;
    }

    private void RefreshOffers(MedicalOrderStationComponent state, MedicalOrderConfigPrototype config)
    {
        state.PatientOffers.Clear();
        var totalWeight = config.Difficulties.Sum(d => d.Weight);
        for (var i = 0; i < config.PatientOfferCount; i++)
        {
            var selected = SelectWeighted(config.Difficulties, d => d.Weight, _random.Next(totalWeight));
            var difficulty = config.Difficulties.IndexOf(selected);
            if (GeneratePatientOffer(state, config, difficulty) is { } offer)
                state.PatientOffers.Add(offer.RuntimeId, offer);
        }

        state.NextRefresh = _timing.CurTime + config.OfferRefreshInterval;
    }

    private MedicalOrderOffer? GeneratePatientOffer(MedicalOrderStationComponent state,
        MedicalOrderConfigPrototype config, int difficulty)
    {
        var candidates = config.Damages.Where(d => d.CanGenerate).ToList();
        if (candidates.Count < config.MinDamageEntries)
            return null;

        var definition = config.Difficulties[difficulty];
        var ordered = new List<MedicalOrderDamage>();
        var possibleCounts = new List<(int Count, int Minimum, int Maximum)>();
        var minimum = 0;
        var maximum = 0;
        var maxCount = Math.Min(config.MaxDamageEntries, candidates.Count);
        for (var i = 0; i < maxCount; i++)
        {
            var totalWeight = candidates.Sum(d => d.Weight);
            var roll = _random.Next(totalWeight);
            var selected = SelectWeighted(candidates, d => d.Weight, roll);

            candidates.Remove(selected);
            ordered.Add(selected);
            minimum += selected.MinAmount;
            maximum += selected.MaxAmount;
            var lower = Math.Max(minimum,
                (int) (((long) definition.MinScore + config.PointsPerDamage - 1) / config.PointsPerDamage));
            var upper = Math.Min(maximum, definition.MaxScore / config.PointsPerDamage);
            if (i + 1 >= config.MinDamageEntries && lower <= upper)
                possibleCounts.Add((i + 1, lower, upper));
        }

        if (possibleCounts.Count == 0)
        {
            _sawmill.Error($"No medical damage combination covers difficulty {difficulty + 1}.");
            return null;
        }

        var choice = possibleCounts[_random.Next(possibleCounts.Count)];
        var selectedTypes = ordered.Take(choice.Count).ToArray();
        var remaining = _random.Next(choice.Minimum, choice.Maximum + 1);
        var futureMinimum = selectedTypes.Sum(d => d.MinAmount);
        var futureMaximum = selectedTypes.Sum(d => d.MaxAmount);
        var offer = new MedicalOrderOffer
        {
            RuntimeId = state.NextPatientRuntimeId++,
            Patient = true,
            Difficulty = difficulty,
            TimeLimit = definition.TimeLimit,
        };
        foreach (var selected in selectedTypes)
        {
            futureMinimum -= selected.MinAmount;
            futureMaximum -= selected.MaxAmount;
            // Reserve enough damage for the remaining types, so every roll reaches this difficulty.
            var lower = Math.Max(selected.MinAmount, remaining - futureMaximum);
            var upper = Math.Min(selected.MaxAmount, remaining - futureMinimum);
            var amount = _random.Next(lower, upper + 1);
            remaining -= amount;
            offer.Lines.Add(new MedicalOrderRequirement
            {
                ID = selected.DamageType.Id,
                Amount = amount,
                PointsPerUnit = config.PointsPerDamage,
            });
            offer.MaximumScore = checked(offer.MaximumScore + amount * config.PointsPerDamage);
        }

        return offer;
    }

    public static T SelectWeighted<T>(IReadOnlyList<T> candidates, Func<T, int> weight, int roll)
    {
        foreach (var candidate in candidates)
        {
            roll -= weight(candidate);
            if (roll < 0)
                return candidate;
        }

        throw new ArgumentOutOfRangeException(nameof(roll));
    }

    private void OnUiOpened(Entity<MedicalOrderMachineComponent> machine, ref BoundUIOpenedEvent args)
    {
        if (EnsureStation(machine.Owner, machine.Comp) is { } state &&
            _prototypes.TryIndex<MedicalOrderConfigPrototype>(state.ConfigId, out var config))
            UpdateUi(machine.Owner, machine.Comp, state, config);
    }

    private void OnRequest(Entity<MedicalOrderMachineComponent> machine, ref MedicalOrderRequestMessage args)
    {
        if (!this.IsPowered(machine.Owner, EntityManager) ||
            !_access.IsAllowed(args.Actor, machine.Owner) ||
            _stations.GetOwningStation(machine.Owner) is not { } station ||
            EnsureStation(machine.Owner, machine.Comp) is not { } state ||
            state.Busy ||
            !_prototypes.TryIndex<MedicalOrderConfigPrototype>(state.ConfigId, out var config))
        {
            Reject(machine.Owner, args.Actor, "medical-orders-error-unavailable");
            if (args.Action == MedicalOrderAction.Purchase)
                _ui.ServerSendUiMessage(machine.Owner, MedicalOrderUiKey.Key,
                    new MedicalOrderResultMessage(args.RequestId, false, "medical-orders-error-unavailable"), args.Actor);
            return;
        }

        switch (args.Action)
        {
            case MedicalOrderAction.Accept:
                if (machine.Comp.Kind == MedicalOrderMachineKind.PatientReceiver)
                    TryAcceptPatient(station, machine.Owner, args.Actor, state, config, args.RuntimeId);
                break;
            case MedicalOrderAction.Complete:
                if (machine.Comp.Kind == MedicalOrderMachineKind.PatientSender)
                    TrySubmitPatient(station, machine.Owner, args.Actor, state, config, args.RuntimeId);
                else
                    Reject(machine.Owner, args.Actor, "medical-orders-error-not-ready");
                break;
            case MedicalOrderAction.Purchase:
                if (machine.Comp.Kind != MedicalOrderMachineKind.PatientReceiver)
                    TryPurchase(station, machine.Owner, machine.Comp.Kind, state, config, args);
                break;
            case MedicalOrderAction.SellReagents:
                if (machine.Comp.Kind == MedicalOrderMachineKind.Reagent)
                    TrySellReagents(machine.Owner, args.Actor, state, config);
                break;
        }

        UpdateStationUis(state, config);
    }

    private void OnBeakerInserted(Entity<MedicalOrderMachineComponent> machine, ref EntInsertedIntoContainerMessage args)
    {
        UpdateBeakerUi(machine);
    }

    private void OnBeakerRemoved(Entity<MedicalOrderMachineComponent> machine, ref EntRemovedFromContainerMessage args)
    {
        UpdateBeakerUi(machine);
    }

    private void UpdateBeakerUi(Entity<MedicalOrderMachineComponent> machine)
    {
        if (machine.Comp.Kind != MedicalOrderMachineKind.Reagent ||
            _stations.GetOwningStation(machine.Owner) is not { } station ||
            !TryComp<MedicalOrderStationComponent>(station, out var state) ||
            !_prototypes.TryIndex<MedicalOrderConfigPrototype>(state.ConfigId, out var config))
            return;

        UpdateUi(machine.Owner, machine.Comp, state, config);
    }

    private static MedicalOrderOffer CopyOffer(MedicalOrderOffer original)
    {
        var copy = new MedicalOrderOffer
        {
            RuntimeId = original.RuntimeId,
            Patient = original.Patient,
            MaximumScore = original.MaximumScore,
            Difficulty = original.Difficulty,
            TimeLimit = original.TimeLimit,
        };
        copy.Lines.AddRange(original.Lines.Select(line => line.Copy()));
        return copy;
    }

    private void TryAcceptPatient(EntityUid station, EntityUid receiver, EntityUid actor,
        MedicalOrderStationComponent state,
        MedicalOrderConfigPrototype config, int runtimeId)
    {
        if (state.Busy || state.PatientActive != null || state.PatientAccepting || state.NextRefresh <= _timing.CurTime ||
            !state.PatientOffers.TryGetValue(runtimeId, out var offer) ||
            !TryComp<EntityStorageComponent>(receiver, out var storage) || storage.Open ||
            storage.Contents.ContainedEntities.Count != 0)
        {
            Reject(receiver, actor, "medical-orders-error-unavailable");
            return;
        }

        EntityUid? body = null;
        EntityUid? gown = null;
        state.Busy = true;
        state.PatientAccepting = true;
        try
        {
            body = _randomHumanoids.SpawnRandomHumanoid(config.PatientRandomHumanoidSettings.Id,
                Transform(receiver).Coordinates, string.Empty);
            gown = Spawn(config.PatientGown, Transform(receiver).Coordinates);
            if (!_inventory.TryEquip(body.Value, gown.Value, "outerClothing", silent: true, force: true))
            {
                Reject(receiver, actor, "medical-orders-error-patient-create");
                return;
            }

            if (!HasComp<DamageableComponent>(body.Value) || !HasComp<Content.Shared.Mobs.Components.MobStateComponent>(body.Value))
            {
                Reject(receiver, actor, "medical-orders-error-patient-create");
                return;
            }

            var damage = new DamageSpecifier();
            foreach (var line in offer.Lines)
                damage.DamageDict[line.ID] = FixedPoint2.New(line.Amount);
            if (!_damageable.TryChangeDamage(body.Value, damage, ignoreResistances: true))
            {
                Reject(receiver, actor, "medical-orders-error-patient-create");
                return;
            }

            _mobState.ChangeMobState(body.Value, MobState.Dead);
            if (!_storage.CanInsert(body.Value, receiver, storage) ||
                !_storage.Insert(body.Value, receiver, storage))
            {
                Reject(receiver, actor, "medical-orders-error-patient-create");
                return;
            }

            var marker = EnsureComp<MedicalOrderPatientComponent>(body.Value);
            marker.Station = station;
            marker.RuntimeId = runtimeId;
            var actualDamage = Comp<DamageableComponent>(body.Value).TotalDamage;
            var active = new MedicalOrderActive
            {
                Offer = CopyOffer(offer),
                Terminal = receiver,
                AcceptedAt = _timing.CurTime,
                Deadline = _timing.CurTime + offer.TimeLimit,
                Patient = body,
                InitialDamage = actualDamage,
            };
            if (!Exists(station) || Terminating(station) || EntityManager.IsQueuedForDeletion(station) ||
                !Exists(receiver) || Terminating(receiver) || EntityManager.IsQueuedForDeletion(receiver) ||
                !Exists(body.Value) || Terminating(body.Value) || EntityManager.IsQueuedForDeletion(body.Value) ||
                _stations.GetOwningStation(receiver) != station ||
                !this.IsPowered(receiver, EntityManager) || !_access.IsAllowed(actor, receiver) ||
                storage.Open || storage.Contents.ContainedEntities.Count != 1 ||
                !storage.Contents.ContainedEntities.Contains(body.Value) ||
                state.PatientActive != null || !state.PatientOffers.Remove(runtimeId))
            {
                Reject(receiver, actor, "medical-orders-error-unavailable");
                return;
            }
            state.PatientActive = active;
            body = null;
            gown = null;
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Creating medical order patient {runtimeId} failed: {exception}");
            Reject(receiver, actor, "medical-orders-error-patient-create");
        }
        finally
        {
            try
            {
                if (body is { } failedBody && Exists(failedBody))
                    QueueDel(failedBody);
                if (gown is { } failedGown && Exists(failedGown))
                    QueueDel(failedGown);
            }
            finally
            {
                state.PatientAccepting = false;
                state.Busy = false;
            }
        }
    }

    private void TrySubmitPatient(EntityUid station, EntityUid sender, EntityUid actor,
        MedicalOrderStationComponent state,
        MedicalOrderConfigPrototype config, int runtimeId)
    {
        if (state.Busy || state.PatientActive is not { } active || active.Finalizing || active.Offer.RuntimeId != runtimeId)
        {
            Reject(sender, actor, "medical-orders-error-unavailable");
            return;
        }

        if (!TryComp<EntityStorageComponent>(sender, out var storage) || storage.Open ||
            storage.Contents.ContainedEntities.Count != 1)
        {
            Reject(sender, actor, "medical-orders-error-no-patient");
            return;
        }

        if (_timing.CurTime >= active.Deadline)
        {
            FinalizeOrder(station, state, config, active, expired: true);
            return;
        }

        var body = storage.Contents.ContainedEntities.First();
        if (active.Patient != body ||
            !TryComp<MedicalOrderPatientComponent>(body, out var marker) ||
            marker.Station != station || marker.RuntimeId != runtimeId)
        {
            Reject(sender, actor, "medical-orders-error-wrong-patient");
            return;
        }

        if (!_mobState.IsAlive(body) || _mobState.IsCritical(body))
        {
            Reject(sender, actor, "medical-orders-error-not-alive");
            return;
        }

        if (
            !TryComp<DamageableComponent>(body, out var damageable) ||
            damageable.TotalDamage > FixedPoint2.New(config.PatientCompletionDamageThreshold))
        {
            Reject(sender, actor, "medical-orders-error-damage");
            return;
        }

        if (FinalizeOrder(station, state, config, active, expired: false) && Exists(body))
            QueueDel(body);
    }

    private int GetScore(MedicalOrderActive active, MedicalOrderConfigPrototype config)
    {
        if (active.Patient is not { } body || !TryComp<DamageableComponent>(body, out var damageable))
            return 0;
        return ((active.InitialDamage - damageable.TotalDamage) *
            FixedPoint2.New(config.PointsPerDamage)).Int();
    }

    private bool FinalizeOrder(EntityUid station, MedicalOrderStationComponent state,
        MedicalOrderConfigPrototype config, MedicalOrderActive active, bool expired)
    {
        if (state.Busy || active.Finalizing ||
            state.PatientActive != active)
            return false;

        state.Busy = true;
        try
        {
            active.Finalizing = true;
            MedicalOrderResult result;
            try
            {
                expired |= _timing.CurTime >= active.Deadline;
                var score = GetScore(active, config);
                var effective = Math.Max(0, score);
                var points = (int) Math.Min(int.MaxValue,
                    ((long) effective * (expired ? config.ExpiredRewardMultiplierPercent : 100) + 50) / 100);
                var maximum = active.Offer.MaximumScore;
                var quality = maximum <= 0 ? 0 : Math.Clamp((int) ((long) effective * 100 / maximum), 0, 100);
                var band = config.QualityBands.First(b => quality >= b.MinPercent && quality <= b.MaxPercent);
                var baseReputation = config.Difficulties[active.Offer.Difficulty].BaseReputation;
                // A successfully dispatched patient earns the advertised fixed reward.
                // Partial treatment after expiry cannot pay more reputation than timely completion.
                var reputation = expired
                    ? (int) Math.Min(int.MaxValue,
                        ((long) baseReputation * Math.Min(band.MultiplierPercent, 100) *
                            config.ExpiredRewardMultiplierPercent + 5000) / 10000)
                    : baseReputation;
                var creditedPoints = (int) Math.Min((long) points, long.MaxValue - state.Points);
                var creditedReputation = (int) Math.Min((long) reputation, long.MaxValue - state.Reputation);
                result = new MedicalOrderResult
                {
                    Offer = active.Offer,
                    FinalScore = score,
                    AwardedPoints = creditedPoints,
                    AwardedReputation = creditedReputation,
                    Expired = expired,
                };
            }
            catch (Exception exception)
            {
                active.Finalizing = false;
                _sawmill.Error($"Finalizing medical order {active.Offer.RuntimeId} failed: {exception}");
                return false;
            }

            // This commit contains no entity operations or UI callbacks. The active reference is cleared
            // alongside both balances, so a second finish or timeout cannot credit the same order.
            state.Points += result.AwardedPoints;
            state.Reputation += result.AwardedReputation;
            state.LastPatient = result;
            state.PatientActive = null;

            if (active.Offer.Patient)
            {
                try
                {
                    if (active.Patient is { } body && Exists(body) &&
                        TryComp<MedicalOrderPatientComponent>(body, out var marker) &&
                        marker.Station == station && marker.RuntimeId == active.Offer.RuntimeId)
                        RemComp<MedicalOrderPatientComponent>(body);
                }
                catch (Exception exception)
                {
                    _sawmill.Error($"Cleaning up medical order patient {active.Offer.RuntimeId} failed: {exception}");
                }
            }

            try
            {
                UpdateStationUis(state, config);
            }
            catch (Exception exception)
            {
                _sawmill.Error($"Updating medical order UI after {active.Offer.RuntimeId} failed: {exception}");
            }
            return true;
        }
        finally
        {
            state.Busy = false;
        }
    }

    private void TryPurchase(EntityUid station, EntityUid machine, MedicalOrderMachineKind kind,
        MedicalOrderStationComponent state, MedicalOrderConfigPrototype config,
        MedicalOrderRequestMessage request)
    {
        var success = false;
        var acquired = false;
        var purchaseReserved = false;
        var debited = false;
        var message = "medical-orders-shop-error-invalid";
        RepairOrderDelivery? delivery = null;
        Guid purchaseId = default;
        long total = 0;
        try
        {
            if (!Guid.TryParse(request.RequestId, out purchaseId) || purchaseId == Guid.Empty)
                return;

            if (state.Busy)
                return;

            if (request.Cart is not { Length: > 0 and <= 64 })
                return;

            var normalized = new Dictionary<string, int>();
            foreach (var line in request.Cart)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.ID) || line.Count <= 0 ||
                    !normalized.TryAdd(line.ID, line.Count))
                    return;
            }

            if (state.CommittedPurchases.TryGetValue(purchaseId, out var receipt))
            {
                if (receipt.Kind == kind && receipt.Cart.Count == normalized.Count &&
                    normalized.All(line => receipt.Cart.TryGetValue(line.Key, out var count) && count == line.Value))
                {
                    success = true;
                    message = "medical-orders-shop-success";
                }
                return;
            }

            state.Busy = true;
            acquired = true;
            var pool = config.Shop;
            var level = GetShopLevel(state.Reputation, config);

            var items = new List<EntProtoId>();
            foreach (var (id, count) in normalized)
            {
                var entry = pool.FirstOrDefault(e => e.ID == id);
                if (entry == null || !entry.Enabled || level < entry.MinimumShopLevel ||
                    count > entry.MaxCount || !_prototypes.TryIndex<EntityPrototype>(entry.Entity, out _))
                    return;

                var lineCost = (long) entry.Cost * count;
                if (lineCost > long.MaxValue - total)
                    return;
                total += lineCost;
                for (var i = 0; i < count; i++)
                    items.Add(entry.Entity);
            }

            if (total <= 0)
                return;

            if (state.Points < total)
            {
                message = "medical-orders-shop-error-funds";
                return;
            }

            if (!_delivery.TryDeliverMedical(station, purchaseId, machine, config.DeliveryContainer,
                    items, out delivery))
            {
                message = "medical-orders-shop-error-delivery";
                return;
            }

            if (!Exists(machine) || Terminating(machine) || EntityManager.IsQueuedForDeletion(machine) ||
                !Exists(station) || Terminating(station) || EntityManager.IsQueuedForDeletion(station) ||
                _stations.GetOwningStation(machine) != station || !this.IsPowered(machine, EntityManager) ||
                !_access.IsAllowed(request.Actor, machine))
            {
                message = "medical-orders-error-unavailable";
                return;
            }

            // Reserve the id before debiting. If any remaining step fails, finally restores
            // the balance and reservation before rolling back the prepared physical delivery.
            if (!state.CommittedPurchases.TryAdd(purchaseId, (kind, normalized)))
                return;
            purchaseReserved = true;
            state.Points -= total;
            debited = true;
            _delivery.Commit(delivery);
            success = true;
            message = "medical-orders-shop-success";
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Medical shop purchase {request.RequestId} failed: {exception}");
            message = "medical-orders-shop-error-delivery";
        }
        finally
        {
            try
            {
                if (!success)
                {
                    if (debited)
                        state.Points += total;
                    if (purchaseReserved)
                        state.CommittedPurchases.Remove(purchaseId);
                    _delivery.Rollback(delivery);
                }
            }
            finally
            {
                if (acquired)
                    state.Busy = false;
            }
            _ui.ServerSendUiMessage(machine, MedicalOrderUiKey.Key,
                new MedicalOrderResultMessage(request.RequestId, success, message), request.Actor);
        }
    }

    private void Reject(EntityUid machine, EntityUid actor, string key)
    {
        if (!Exists(machine))
            return;
        _popup.PopupEntity(Loc.GetString(key), machine, actor, PopupType.SmallCaution);
    }

    private void UpdateStationUis(MedicalOrderStationComponent state, MedicalOrderConfigPrototype config,
        bool marketOnly = false)
    {
        foreach (var uid in state.Machines.ToArray())
        {
            if (!Exists(uid) || !TryComp<MedicalOrderMachineComponent>(uid, out var machine))
            {
                state.Machines.Remove(uid);
                continue;
            }

            if (!marketOnly || machine.Kind == MedicalOrderMachineKind.Reagent)
                UpdateUi(uid, machine, state, config);
        }
    }

    private void UpdateUi(EntityUid uid, MedicalOrderMachineComponent machine,
        MedicalOrderStationComponent state, MedicalOrderConfigPrototype config)
    {
        var patient = machine.Kind != MedicalOrderMachineKind.Reagent;
        var offers = patient ? state.PatientOffers.Values.ToArray() : Array.Empty<MedicalOrderOffer>();
        var active = patient ? state.PatientActive : null;
        var completed = patient ? state.LastPatient : null;
        var level = GetShopLevel(state.Reputation, config);
        var pool = machine.Kind == MedicalOrderMachineKind.PatientReceiver ? null : config.Shop;
        var shop = pool?.Where(i => i.Enabled).Select((i, index) => new MedicalOrderShopItemView(
            level < i.MinimumShopLevel ? $"classified-{index}" : i.ID,
            level < i.MinimumShopLevel ? string.Empty : i.Entity.Id,
            level < i.MinimumShopLevel ? 0 : i.Cost,
            level < i.MinimumShopLevel ? 0 : i.MaxCount,
            i.MinimumShopLevel, level < i.MinimumShopLevel)).ToArray() ??
            Array.Empty<MedicalOrderShopItemView>();
        float? initialDamage = null;
        float? currentDamage = null;
        var insertedPatient = false;
        var patientAlive = false;
        var patientCritical = false;
        if (patient && active?.Patient is { } body)
        {
            initialDamage = active.InitialDamage.Float();
            if (TryComp<DamageableComponent>(body, out var damageable))
                currentDamage = damageable.TotalDamage.Float();
            if (Exists(body))
            {
                patientAlive = _mobState.IsAlive(body);
                patientCritical = _mobState.IsCritical(body);
            }
            insertedPatient = machine.Kind == MedicalOrderMachineKind.PatientSender &&
                TryComp<EntityStorageComponent>(uid, out var storage) &&
                storage.Contents.ContainedEntities.Contains(body);
        }
        long marketPoints = 0;
        long marketReputation = 0;
        float acceptedVolume = 0;
        float rejectedVolume = 0;
        var market = machine.Kind == MedicalOrderMachineKind.Reagent
            ? BuildMarketView(uid, state, config, out marketPoints, out marketReputation,
                out acceptedVolume, out rejectedVolume)
            : Array.Empty<MedicalReagentMarketView>();
        _ui.SetUiState(uid, MedicalOrderUiKey.Key, new MedicalOrderUiState(
            machine.Kind,
            offers.OrderBy(o => o.RuntimeId)
                .Select(o => View(o, null, 0, config)).ToArray(),
            active == null ? null : View(active.Offer, active, GetScore(active, config), config),
            completed == null ? null : View(completed.Offer, null, completed.FinalScore, config),
            state.NextRefresh, state.Points, state.Reputation, level,
            level < config.ShopLevelThresholds.Count ? config.ShopLevelThresholds[level] : null,
            shop, completed?.AwardedPoints ?? 0, completed?.AwardedReputation ?? 0,
            completed?.Expired ?? false, initialDamage, currentDamage,
            config.PatientCompletionDamageThreshold, insertedPatient, patientAlive, patientCritical,
            machine.Kind == MedicalOrderMachineKind.Reagent &&
            _itemSlots.GetItemOrNull(uid, "beakerSlot") is { } beaker ? Name(beaker) : null,
            market, marketPoints, marketReputation, acceptedVolume, rejectedVolume,
            state.LastMarketPoints, state.LastMarketReputation));
    }

    private static MedicalOrderView View(MedicalOrderOffer offer, MedicalOrderActive? active, int score,
        MedicalOrderConfigPrototype config)
    {
        var lines = offer.Lines.Select(line => new MedicalOrderLineView(line.ID, line.Amount,
            active != null && active.Submitted.TryGetValue(line.ID, out var submitted) ? submitted.Float() : 0,
            line.PointsPerUnit)).ToArray();
        return new MedicalOrderView(offer.RuntimeId, lines, offer.MaximumScore, score,
            offer.Difficulty, offer.TimeLimit, config.Difficulties[offer.Difficulty].BaseReputation,
            active?.Deadline);
    }

    private static int GetShopLevel(long reputation, MedicalOrderConfigPrototype config)
    {
        var level = 1;
        for (var i = 1; i < config.ShopLevelThresholds.Count; i++)
        {
            if (reputation < config.ShopLevelThresholds[i])
                break;
            level = i + 1;
        }
        return level;
    }
}
