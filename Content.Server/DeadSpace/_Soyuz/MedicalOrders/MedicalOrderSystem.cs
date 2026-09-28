// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Content.Server.Humanoid.Systems;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server.Station.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Access.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DeadSpace._Soyuz.MedicalOrders;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
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

/// <summary>Generates runtime medical orders and owns their station-scoped lifecycle.</summary>
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
    private TimeSpan _nextPatientUiRefresh;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("medical_orders");
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
        var refreshPatientUi = now >= _nextPatientUiRefresh;
        if (refreshPatientUi)
            _nextPatientUiRefresh = now + TimeSpan.FromSeconds(1);
        if (now >= _nextDiscovery)
        {
            DiscoverMachines();
            _nextDiscovery = now + TimeSpan.FromSeconds(1);
        }

        var query = EntityQueryEnumerator<MedicalOrderStationComponent>();
        while (query.MoveNext(out var uid, out var state))
        {
            if (!_prototypes.TryIndex<MedicalOrderConfigPrototype>(state.ConfigId, out var config))
                continue;

            if (now >= state.NextRefresh)
            {
                RefreshOffers(state, config);
                UpdateStationUis(state, config);
            }

            if (state.ReagentActive is { } reagent)
            {
                if (!Exists(reagent.Terminal) || Terminating(reagent.Terminal) || now >= reagent.Deadline)
                    FinalizeOrder(uid, state, config, reagent, expired: true);
            }

            if (state.PatientActive is { } patient)
            {
                if (!Exists(patient.Terminal) || Terminating(patient.Terminal) ||
                    patient.Patient is not { } body || !Exists(body) || Terminating(body) || now >= patient.Deadline)
                    FinalizeOrder(uid, state, config, patient, expired: true);
            }

            if (refreshPatientUi && state.PatientActive != null)
                UpdateStationUis(state, config);
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
        if (state.ReagentActive is { } active && active.Terminal == machine.Owner &&
            _prototypes.TryIndex<MedicalOrderConfigPrototype>(state.ConfigId, out var config))
            FinalizeOrder(station, state, config, active, expired: true);
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
        state.ReagentOffers.Clear();
        state.PatientOffers.Clear();
        for (var i = 0; i < config.ReagentOfferCount; i++)
        {
            if (GenerateReagentOffer(state, config) is { } offer)
                state.ReagentOffers.Add(offer.RuntimeId, offer);
        }

        for (var i = 0; i < config.PatientOfferCount; i++)
        {
            if (GeneratePatientOffer(state, config) is { } offer)
                state.PatientOffers.Add(offer.RuntimeId, offer);
        }

        state.NextRefresh = _timing.CurTime + config.OfferRefreshInterval;
    }

    private MedicalOrderOffer? GenerateReagentOffer(MedicalOrderStationComponent state, MedicalOrderConfigPrototype config)
    {
        var candidates = config.Reagents.Where(r => r.CanGenerate).ToList();
        if (candidates.Count < config.MinReagentEntries)
            return null;

        var count = Math.Min(_random.Next(config.MinReagentEntries, config.MaxReagentEntries + 1), candidates.Count);
        var offer = new MedicalOrderOffer { RuntimeId = state.NextReagentRuntimeId++ };
        for (var i = 0; i < count; i++)
        {
            var totalWeight = candidates.Sum(r => r.Weight);
            var roll = _random.Next(totalWeight);
            var selected = SelectWeighted(candidates, r => r.Weight, roll);

            candidates.Remove(selected);
            var amount = _random.Next(selected.MinAmount, selected.MaxAmount + 1);
            offer.Lines.Add(new MedicalOrderRequirement
            {
                ID = selected.Reagent.Id,
                Amount = amount,
                PointsPerUnit = selected.PointsPerUnit,
            });
            offer.MaximumScore = checked(offer.MaximumScore + amount * selected.PointsPerUnit);
        }

        return SetDifficulty(offer, config);
    }

    private MedicalOrderOffer? GeneratePatientOffer(MedicalOrderStationComponent state, MedicalOrderConfigPrototype config)
    {
        var candidates = config.Damages.Where(d => d.CanGenerate).ToList();
        if (candidates.Count < config.MinDamageEntries)
            return null;

        var count = Math.Min(_random.Next(config.MinDamageEntries, config.MaxDamageEntries + 1), candidates.Count);
        var offer = new MedicalOrderOffer { RuntimeId = state.NextPatientRuntimeId++, Patient = true };
        for (var i = 0; i < count; i++)
        {
            var totalWeight = candidates.Sum(d => d.Weight);
            var roll = _random.Next(totalWeight);
            var selected = SelectWeighted(candidates, d => d.Weight, roll);

            candidates.Remove(selected);
            var amount = _random.Next(selected.MinAmount, selected.MaxAmount + 1);
            offer.Lines.Add(new MedicalOrderRequirement
            {
                ID = selected.DamageType.Id,
                Amount = amount,
                PointsPerUnit = config.PointsPerDamage,
            });
            offer.MaximumScore = checked(offer.MaximumScore + amount * config.PointsPerDamage);
        }

        return SetDifficulty(offer, config);
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

    private MedicalOrderOffer? SetDifficulty(MedicalOrderOffer offer, MedicalOrderConfigPrototype config)
    {
        for (var i = 0; i < config.Difficulties.Count; i++)
        {
            var definition = config.Difficulties[i];
            if (offer.MaximumScore < definition.MinScore || offer.MaximumScore > definition.MaxScore)
                continue;

            offer.Difficulty = i;
            offer.TimeLimit = definition.TimeLimit;
            return offer;
        }

        _sawmill.Error($"No medical order difficulty covers generated score {offer.MaximumScore}.");
        return null;
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
                if (machine.Comp.Kind == MedicalOrderMachineKind.Reagent)
                    TryAcceptReagent(machine.Owner, args.Actor, state, args.RuntimeId);
                else if (machine.Comp.Kind == MedicalOrderMachineKind.PatientReceiver)
                    TryAcceptPatient(station, machine.Owner, args.Actor, state, config, args.RuntimeId);
                break;
            case MedicalOrderAction.Complete:
                if (machine.Comp.Kind == MedicalOrderMachineKind.Reagent &&
                    state.ReagentActive is { } reagent && reagent.Offer.RuntimeId == args.RuntimeId &&
                    reagent.Terminal == machine.Owner && IsReagentReady(reagent))
                    FinalizeOrder(station, state, config, reagent, expired: false);
                else if (machine.Comp.Kind == MedicalOrderMachineKind.PatientSender)
                    TrySubmitPatient(station, machine.Owner, args.Actor, state, config, args.RuntimeId);
                else
                    Reject(machine.Owner, args.Actor, "medical-orders-error-not-ready");
                break;
            case MedicalOrderAction.Purchase:
                if (machine.Comp.Kind != MedicalOrderMachineKind.PatientReceiver)
                    TryPurchase(station, machine.Owner, machine.Comp.Kind, state, config, args);
                break;
            case MedicalOrderAction.TransferReagents:
                if (machine.Comp.Kind == MedicalOrderMachineKind.Reagent)
                    TryTransferReagents(machine.Owner, args.Actor, station, state, config, args.RuntimeId);
                break;
        }

        UpdateStationUis(state, config);
    }

    private void TryAcceptReagent(EntityUid terminal, EntityUid actor,
        MedicalOrderStationComponent state,
        int runtimeId)
    {
        if (state.ReagentActive != null || state.NextRefresh <= _timing.CurTime ||
            !state.ReagentOffers.TryGetValue(runtimeId, out var offer))
        {
            Reject(terminal, actor, "medical-orders-error-unavailable");
            return;
        }

        var active = new MedicalOrderActive
        {
            Offer = CopyOffer(offer),
            Terminal = terminal,
            AcceptedAt = _timing.CurTime,
            Deadline = _timing.CurTime + offer.TimeLimit,
        };
        if (!state.ReagentOffers.Remove(runtimeId))
        {
            Reject(terminal, actor, "medical-orders-error-unavailable");
            return;
        }
        state.ReagentActive = active;
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

    private void TryTransferReagents(EntityUid terminal, EntityUid actor, EntityUid station,
        MedicalOrderStationComponent state, MedicalOrderConfigPrototype config, int runtimeId)
    {
        if (state.ReagentActive is not { } active || active.Finalizing ||
            active.Offer.RuntimeId != runtimeId || active.Terminal != terminal)
        {
            Reject(terminal, actor, "medical-orders-error-unavailable");
            return;
        }

        if (_timing.CurTime >= active.Deadline)
        {
            FinalizeOrder(station, state, config, active, expired: true);
            return;
        }

        if (_itemSlots.GetItemOrNull(terminal, "beakerSlot") is not { } beaker ||
            !_solutions.TryGetFitsInDispenser(beaker, out var solutionEntity, out var solution) ||
            solutionEntity == null)
        {
            Reject(terminal, actor, "medical-orders-error-no-beaker");
            return;
        }

        var changed = false;
        foreach (var line in active.Offer.Lines)
        {
            var required = FixedPoint2.New(line.Amount);
            active.Submitted.TryGetValue(line.ID, out var submitted);
            var remaining = required - submitted;
            if (remaining <= FixedPoint2.Zero)
                continue;

            var reagent = new ProtoId<ReagentPrototype>(line.ID);
            var available = solution.GetTotalPrototypeQuantity(reagent);
            var amount = FixedPoint2.Min(available, remaining);
            if (amount <= FixedPoint2.Zero)
                continue;

            var removed = _solutions.RemoveReagent(solutionEntity.Value, line.ID, amount);
            if (removed <= FixedPoint2.Zero)
                continue;

            active.Submitted[line.ID] = submitted + removed;
            changed = true;
        }

        if (changed)
        {
            _popup.PopupEntity(Loc.GetString("medical-orders-reagent-submitted"), terminal,
                actor, PopupType.Small);
            UpdateStationUis(state, config);
        }
        else
            Reject(terminal, actor, "medical-orders-error-no-matching-reagents");
    }

    private static bool IsReagentReady(MedicalOrderActive active)
    {
        return active.Offer.Lines.All(line => active.Submitted.TryGetValue(line.ID, out var submitted) &&
            submitted >= FixedPoint2.New(line.Amount));
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
        if (state.PatientActive != null || state.PatientAccepting || state.NextRefresh <= _timing.CurTime ||
            !state.PatientOffers.TryGetValue(runtimeId, out var offer) ||
            !TryComp<EntityStorageComponent>(receiver, out var storage) || storage.Open ||
            storage.Contents.ContainedEntities.Count != 0)
        {
            Reject(receiver, actor, "medical-orders-error-unavailable");
            return;
        }

        EntityUid? body = null;
        EntityUid? gown = null;
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
            if (!Exists(receiver) || Terminating(receiver) ||
                _stations.GetOwningStation(receiver) != station ||
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
            state.PatientAccepting = false;
            if (body is { } failedBody && Exists(failedBody))
                QueueDel(failedBody);
            if (gown is { } failedGown && Exists(failedGown))
                QueueDel(failedGown);
        }
    }

    private void TrySubmitPatient(EntityUid station, EntityUid sender, EntityUid actor,
        MedicalOrderStationComponent state,
        MedicalOrderConfigPrototype config, int runtimeId)
    {
        if (state.PatientActive is not { } active || active.Finalizing || active.Offer.RuntimeId != runtimeId)
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
        if (active.Offer.Patient)
        {
            if (active.Patient is not { } body || !TryComp<DamageableComponent>(body, out var damageable))
                return 0;
            return ((active.InitialDamage - damageable.TotalDamage) *
                FixedPoint2.New(config.PointsPerDamage)).Int();
        }

        var result = FixedPoint2.Zero;
        foreach (var line in active.Offer.Lines)
        {
            if (active.Submitted.TryGetValue(line.ID, out var submitted))
                result += submitted * FixedPoint2.New(line.PointsPerUnit);
        }
        return result.Int();
    }

    private bool FinalizeOrder(EntityUid station, MedicalOrderStationComponent state,
        MedicalOrderConfigPrototype config, MedicalOrderActive active, bool expired)
    {
        if (active.Finalizing ||
            (active.Offer.Patient ? state.PatientActive != active : state.ReagentActive != active))
            return false;

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
            var reputation = (int) Math.Min(int.MaxValue,
                ((long) baseReputation * band.MultiplierPercent + 50) / 100);
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
        if (active.Offer.Patient)
        {
            state.LastPatient = result;
            state.PatientActive = null;
        }
        else
        {
            state.LastReagent = result;
            state.ReagentActive = null;
        }

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
            var pool = kind == MedicalOrderMachineKind.Reagent ? config.ReagentShop : config.PatientShop;
            var level = GetShopLevel(state.Reputation, config);

            var items = new List<EntProtoId>();
            foreach (var (id, count) in normalized)
            {
                var entry = pool.FirstOrDefault(e => e.ID == id);
                if (entry == null || !entry.Enabled || level < entry.MinimumShopLevel ||
                    count > entry.MaxCount || !_prototypes.TryIndex<EntityPrototype>(entry.Entity, out _))
                    return;

                total = checked(total + (long) entry.Cost * count);
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

            if (!Exists(machine) || Terminating(machine) || !Exists(station) || Terminating(station) ||
                _stations.GetOwningStation(machine) != station || !this.IsPowered(machine, EntityManager))
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
        catch (OverflowException)
        {
            message = "medical-orders-shop-error-invalid";
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Medical shop purchase {request.RequestId} failed: {exception}");
            message = "medical-orders-shop-error-delivery";
        }
        finally
        {
            if (!success)
            {
                if (debited)
                    state.Points += total;
                if (purchaseReserved)
                    state.CommittedPurchases.Remove(purchaseId);
                _delivery.Rollback(delivery);
            }
            if (acquired)
                state.Busy = false;
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

    private void UpdateStationUis(MedicalOrderStationComponent state, MedicalOrderConfigPrototype config)
    {
        foreach (var uid in state.Machines.ToArray())
        {
            if (!Exists(uid) || !TryComp<MedicalOrderMachineComponent>(uid, out var machine))
            {
                state.Machines.Remove(uid);
                continue;
            }

            UpdateUi(uid, machine, state, config);
        }
    }

    private void UpdateUi(EntityUid uid, MedicalOrderMachineComponent machine,
        MedicalOrderStationComponent state, MedicalOrderConfigPrototype config)
    {
        var patient = machine.Kind != MedicalOrderMachineKind.Reagent;
        var offers = patient ? state.PatientOffers.Values : state.ReagentOffers.Values;
        var active = patient ? state.PatientActive : state.ReagentActive;
        var completed = patient ? state.LastPatient : state.LastReagent;
        var level = GetShopLevel(state.Reputation, config);
        var pool = machine.Kind == MedicalOrderMachineKind.Reagent ? config.ReagentShop :
            machine.Kind == MedicalOrderMachineKind.PatientSender ? config.PatientShop : null;
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
        _ui.SetUiState(uid, MedicalOrderUiKey.Key, new MedicalOrderUiState(
            machine.Kind,
            offers.Select(o => View(o, null, 0)).ToArray(),
            active == null ? null : View(active.Offer, active, GetScore(active, config)),
            completed == null ? null : View(completed.Offer, null, completed.FinalScore),
            state.NextRefresh, state.Points, state.Reputation, level,
            level < config.ShopLevelThresholds.Count ? config.ShopLevelThresholds[level] : null,
            shop, completed?.AwardedPoints ?? 0, completed?.AwardedReputation ?? 0,
            completed?.Expired ?? false, initialDamage, currentDamage,
            config.PatientCompletionDamageThreshold, insertedPatient, patientAlive, patientCritical,
            machine.Kind == MedicalOrderMachineKind.Reagent &&
            _itemSlots.GetItemOrNull(uid, "beakerSlot") is { } beaker ? Name(beaker) : null));
    }

    private static MedicalOrderView View(MedicalOrderOffer offer, MedicalOrderActive? active, int score)
    {
        var lines = offer.Lines.Select(line => new MedicalOrderLineView(line.ID, line.Amount,
            active != null && active.Submitted.TryGetValue(line.ID, out var submitted) ? submitted.Float() : 0,
            line.PointsPerUnit)).ToArray();
        return new MedicalOrderView(offer.RuntimeId, lines, offer.MaximumScore, score,
            offer.Difficulty, offer.TimeLimit, active?.Deadline);
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
