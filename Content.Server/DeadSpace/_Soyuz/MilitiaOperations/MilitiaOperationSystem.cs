// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Globalization;
using System.Linq;
using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Systems;
using Content.Shared.Clothing;
using Content.Shared.Damage.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeadSpace._Soyuz.MilitiaOperations;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Interaction;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Trigger.Components.Effects;
using Robust.Server.GameObjects;
using Robust.Server.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Containers;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem : EntitySystem
{
    public const string DocumentSlot = "operationDocument";
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ILogManager _logs = default!;
    [Dependency] private readonly StationSystem _stations = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly RepairOrderSystem _repair = default!;
    [Dependency] private readonly RepairOrderRewardSystem _rewards = default!;
    [Dependency] private readonly RepairOrderRewardDeliverySystem _delivery = default!;
    [Dependency] private readonly ShuttleSystem _shuttles = default!;

    private ISawmill _log = default!;
    private TimeSpan _nextUpdate;
    // Shop purchases, operation rewards and contraband handovers share one account per round.
    private long _supplyPoints;
    private long _reputation;
    private readonly Dictionary<Guid, (EntityUid Actor, (string Reward, int Count)[] Lines)> _purchases = new();
    private readonly Dictionary<ProtoId<MilitiaOperationConfigPrototype>, EntityUid> _operationControllers = new();

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(Content.Server.NPC.Systems.NPCSteeringSystem));
        _log = _logs.GetSawmill("militia_operations");
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ =>
        {
            _prototypeAncestors.Clear();
            _projectileCollisions.Clear();
        });
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _nextUpdate = TimeSpan.Zero;
            _nextSquadUpdate = TimeSpan.Zero;
            _nextSquadSafety = TimeSpan.Zero;
            _nextSquadGrenade = TimeSpan.Zero;
            _nextCustodyUpdate = TimeSpan.Zero;
            _squadStops.Clear();
            _squadInterrupts.Clear();
            _squadThrowers.Clear();
            _urgentThreatScans.Clear();
            _urgentSquadOrders.Clear();
            _scannedThreats.Clear();
            _supportWork.Clear();
            _breakoutDoorSnapshots.Clear();
            _squadCoverRadii.Clear();
            _squadHostileQueries.Clear();
            _reuseSquadQueries = false;
            _treatmentReservations.Clear();
            _prototypeAncestors.Clear();
            _projectileCollisions.Clear();
            _custodyUiCache.Clear();
            _custodyRequests.Clear();
            _issuedContraband.Clear();
            _redeemedContraband.Clear();
            _supplyPoints = 0;
            _reputation = 0;
            _purchases.Clear();
            _operationControllers.Clear();
            _preparationMaps.Clear();
        });
        SubscribeLocalEvent<MilitiaOperationGridComponent, GridSplitEvent>(OnSplit);
        SubscribeLocalEvent<MilitiaOperationTargetComponent, EntityTerminatingEvent>(OnTargetTerminating);
        SubscribeLocalEvent<MilitiaOperationActorComponent, DamageChangedEvent>(OnActorDamaged);
        SubscribeLocalEvent<DamageOnTriggerComponent, StepTriggerAttemptEvent>(OnActorShardStep);
        SubscribeLocalEvent<Content.Shared.Projectiles.ProjectileComponent, Content.Shared.Projectiles.ProjectileHitEvent>(OnActorProjectileHit);
        SubscribeLocalEvent<MilitiaOperationActorComponent, Content.Shared.Weapons.Hitscan.Events.HitscanRaycastStrikeEvent>(OnActorHitscanHit);
        SubscribeLocalEvent<Content.Shared.Weapons.Hitscan.Events.HitscanRaycastFiredEvent>(OnThreateningHitscan);
        SubscribeLocalEvent<MilitiaOperationActorComponent, TryingToSleepEvent>(OnActorSleepAttempt);
        SubscribeLocalEvent<MilitiaOperationActorComponent, Content.Shared.Stunnable.KnockDownAttemptEvent>(OnActorKnockDownAttempt);
        SubscribeLocalEvent<MilitiaOperationActorComponent, ComponentShutdown>(OnActorShutdown);
        SubscribeLocalEvent<MilitiaOperationActorComponent, MobStateChangedEvent>(OnActorMobStateChanged);
        SubscribeLocalEvent<MilitiaOperationConsoleComponent, EntInsertedIntoContainerMessage>(OnDocumentInserted);
        SubscribeLocalEvent<MilitiaOperationActorComponent, Content.Shared.Weapons.Ranged.Events.ShotAttemptedEvent>(OnSquadShot);
        SubscribeLocalEvent<MilitiaOperationActorComponent, Content.Shared.Weapons.Ranged.Events.GunShotAtTargetEvent>(OnActorTargetedShot);
        SubscribeLocalEvent<MilitiaOperationActorComponent, MapInitEvent>(OnActorMapInit,
            after: [typeof(SharedBodySystem), typeof(LoadoutSystem)]);
        InitializeCustody();
        SubscribeLocalEvent<ShuttleControlAttemptEvent>(OnControl);
        Subs.BuiEvents<MilitiaOperationConsoleComponent>(MilitiaOperationUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<MilitiaOperationRequest>(OnRequest);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateCustody();
        _reuseSquadQueries = true;
        try
        {
            var scan = UpdateSquadSafety();
            UpdateSupportSafety(scan);
            UpdateSquads();
        }
        finally
        {
            _reuseSquadQueries = false;
            _scannedThreats.Clear();
            _squadCoverRadii.Clear();
            _squadHostileQueries.Clear();
        }
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);

        var states = EntityQueryEnumerator<MilitiaOperationStateComponent>();
        while (states.MoveNext(out _, out var state))
        {
            if (!state.Busy)
            {
                try { CheckActive(state); }
                catch (Exception exception) { _log.Error($"Operation lifecycle failed: {exception}"); }
            }
        }

        var consoles = EntityQueryEnumerator<MilitiaOperationConsoleComponent>();
        while (consoles.MoveNext(out var uid, out var console))
        {
            if (EnsureOperationState(console.Config) is not { } state || state.Comp.Busy)
                continue;
            try
            {
                if (_slots.TryGetSlot(uid, DocumentSlot, out var evidenceSlot) && evidenceSlot.Item is { } evidence)
                    TryReceiveEvidence(uid, state, evidence);
                UpdateUi(uid, console, state.Comp);
            }
            catch (Exception exception)
            {
                _log.Error($"Operation maintenance at {uid} failed: {exception}");
            }
        }

        MaintainOwnedEntities();
        foreach (var map in _preparationMaps.ToArray())
            CleanupPreparationMap(map);
    }

    private Entity<MilitiaOperationStateComponent>? EnsureOperationState(ProtoId<MilitiaOperationConfigPrototype> configId)
    {
        if (!_prototypes.TryIndex(configId, out var config))
            return null;
        if (_operationControllers.TryGetValue(configId, out var owner) && !TerminatingOrDeleted(owner) &&
            !EntityManager.IsQueuedForDeletion(owner) && TryComp<MilitiaOperationStateComponent>(owner, out var existing))
            return (owner, existing);

        var offers = ReplenishOffers(Array.Empty<MilitiaOperationOffer>(), config);
        owner = Spawn(null, MapCoordinates.Nullspace);
        var state = EnsureComp<MilitiaOperationStateComponent>(owner);
        state.Config = configId;
        state.Offers.AddRange(offers);
        _operationControllers[configId] = owner;
        return (owner, state);
    }

    private List<MilitiaOperationOffer> ReplenishOffers(IEnumerable<MilitiaOperationOffer> current,
        MilitiaOperationConfigPrototype config)
    {
        var offers = current.ToList();
        var candidates = config.Operations.Distinct()
            .Where(id => _prototypes.TryIndex(id, out _) && offers.All(offer => offer.Prototype != id.Id)).ToList();
        var names = config.OfferNames.Except(offers.Where(offer => offer.Name.HasValue)
            .Select(offer => offer.Name!.Value)).ToList();
        var descriptions = config.OfferDescriptions.Except(offers.Where(offer => offer.Description.HasValue)
            .Select(offer => offer.Description!.Value)).ToList();
        while (offers.Count < 2 && candidates.Count > 0)
        {
            var index = _random.Next(candidates.Count);
            var nameIndex = names.Count > 0 ? _random.Next(names.Count) : -1;
            var descriptionIndex = descriptions.Count > 0 ? _random.Next(descriptions.Count) : -1;
            offers.Add(new MilitiaOperationOffer(Guid.NewGuid(), candidates[index].Id,
                nameIndex >= 0 ? (LocId?) names[nameIndex] : null,
                descriptionIndex >= 0 ? (LocId?) descriptions[descriptionIndex] : null));
            if (nameIndex >= 0)
                names.RemoveAt(nameIndex);
            if (descriptionIndex >= 0)
                descriptions.RemoveAt(descriptionIndex);
            candidates.RemoveAt(index);
        }
        return offers;
    }

    private void OnOpened(Entity<MilitiaOperationConsoleComponent> console, ref BoundUIOpenedEvent args)
    {
        if (EnsureOperationState(console.Comp.Config) is { } state)
        {
            if (!state.Comp.Busy)
                CheckActive(state.Comp);
            UpdateUi(console.Owner, console.Comp, state.Comp);
        }
    }

    private void OnRequest(Entity<MilitiaOperationConsoleComponent> console, ref MilitiaOperationRequest args)
    {
        if (!CanUseConsole(console.Owner, args.Actor) ||
            EnsureOperationState(console.Comp.Config) is not { } controller || args.Token == Guid.Empty ||
            !_prototypes.TryIndex(console.Comp.Config, out var config))
        {
            SendOperationResult(console.Owner, args.Actor, false, "militia-operation-unavailable", args.Token);
            return;
        }
        var state = controller.Comp;
        if (state.Busy || args.Action == MilitiaOperationAction.Purchase &&
            _timing.CurTime < state.Requests.GetValueOrDefault(args.Actor))
        {
            SendOperationResult(console.Owner, args.Actor, false, "militia-operation-busy", args.Token);
            return;
        }

        state.Busy = true;
        if (args.Action == MilitiaOperationAction.Purchase)
            state.Requests[args.Actor] = _timing.CurTime + TimeSpan.FromMilliseconds(100);
        var success = false;
        var message = "militia-operation-unavailable";
        try
        {
            CheckActive(state);
            switch (args.Action)
            {
                case MilitiaOperationAction.Accept:
                    success = Accept(controller, console.Owner, config, args.Token);
                    message = success ? "militia-operation-accepted" : "militia-operation-spawn-failed";
                    break;
                case MilitiaOperationAction.Submit:
                    success = Submit(console.Owner, controller, args.Token);
                    message = success ? "militia-operation-success" : "militia-operation-submit-failed";
                    break;
                case MilitiaOperationAction.Abort:
                    if (state.Active is { } active && active.Id == args.Token)
                    {
                        Finish(state, active, false, "militia-operation-aborted");
                        success = true;
                    }
                    message = success ? "militia-operation-aborted" : message;
                    break;
                case MilitiaOperationAction.Purchase:
                    success = Purchase(controller.Owner, console.Owner, config, args);
                    message = success ? "militia-operation-purchased" : !config.ShopEnabled ? "militia-operation-shop-maintenance" : "militia-operation-purchase-failed";
                    break;
            }
        }
        catch (Exception exception)
        {
            _log.Error($"Operation request {args.Action} at {console.Owner} failed: {exception}");
        }
        finally
        {
            state.Busy = false;
        }
        SendOperationResult(console.Owner, args.Actor, success, message, args.Token);
        UpdateUi(console.Owner, console.Comp, state);
    }

    private void SendOperationResult(EntityUid console, EntityUid actor, bool success, string message, Guid token)
    {
        if (Exists(console) && !TerminatingOrDeleted(console) && !EntityManager.IsQueuedForDeletion(console) &&
            _ui.HasUi(console, MilitiaOperationUiKey.Key))
            _ui.ServerSendUiMessage(console, MilitiaOperationUiKey.Key, new MilitiaOperationResult(success, message, token), actor);
    }

    private bool CanUseConsole(EntityUid console, EntityUid actor)
        => Exists(console) && !TerminatingOrDeleted(console) && !EntityManager.IsQueuedForDeletion(console) &&
           Transform(console) is { GridUid: not null } xform && xform.MapID != MapId.Nullspace &&
           Exists(actor) && !TerminatingOrDeleted(actor) && this.IsPowered(console, EntityManager) &&
           !HasComp<GhostComponent>(actor) && _blocker.CanInteract(actor, console) &&
           _ui.IsUiOpen(console, MilitiaOperationUiKey.Key, actor) &&
           _interaction.InRangeUnobstructed(actor, console) && _access.IsAllowed(actor, console);

    private bool Accept(Entity<MilitiaOperationStateComponent> controller, EntityUid console, MilitiaOperationConfigPrototype config, Guid token)
    {
        var state = controller.Comp;
        state.Cleanup.RemoveWhere(uid => !Exists(uid));
        if (state.Active is { } existing)
            return existing.OfferToken == token;
        if (state.Cleanup.Count != 0 || _timing.CurTime < state.AvailableAt)
            return false;
        var source = Transform(console);
        var sourceGrid = source.GridUid;
        var sourceMap = source.MapID;
        var offer = state.Offers.FirstOrDefault(o => o.Token == token);
        if (offer == null || !_prototypes.TryIndex<MilitiaOperationPrototype>(offer.Prototype, out var prototype) ||
            !TryPrepareOperation(controller, console, config, prototype, out var active))
            return false;

        if (!Exists(console) || TerminatingOrDeleted(console) || EntityManager.IsQueuedForDeletion(console) ||
            !HasComp<MilitiaOperationConsoleComponent>(console) || Transform(console).GridUid != sourceGrid ||
            Transform(console).MapID != sourceMap)
        {
            state.Cleanup.UnionWith(active.Grids);
            return false;
        }
        active.OfferToken = token;
        active.Name = offer.Name ?? active.Name;
        active.Description = offer.Description ?? active.Description;
        state.Active = active;
        state.Status = "militia-operation-in-progress";
        state.Offers.Remove(offer);
        _log.Info($"Operation activated: Console={console}, Operation={active.Id}, Prototype={prototype.ID}, Seed={active.Seed}, Crew={active.Crew.Count}");
        return true;
    }

    private int Remaining(MilitiaActiveOperation active)
        => active.Crew.Count(uid => Exists(uid) && !Terminating(uid) && !EntityManager.IsQueuedForDeletion(uid) &&
            HasComp<MilitiaOperationActorComponent>(uid) && !_mobs.IsDead(uid));

    private bool OperationGridExists(EntityUid grid)
        => HasComp<MapGridComponent>(grid) && !TerminatingOrDeleted(grid) && !EntityManager.IsQueuedForDeletion(grid);

    private void CheckActive(MilitiaOperationStateComponent state)
    {
        state.Cleanup.RemoveWhere(uid => !Exists(uid));
        if (state.Active is not { } active)
            return;
        if (_timing.CurTime >= active.Deadline)
            Finish(state, active, false, "militia-operation-expired");
        else if (!active.Grids.Any(OperationGridExists))
            Finish(state, active, false, "militia-operation-target-lost");
        else if (active.EvidenceReceived && Remaining(active) == 0)
            Finish(state, active, true, "militia-operation-success");
        else if (!active.EvidenceReceived && (!Exists(active.Document) || Terminating(active.Document) || EntityManager.IsQueuedForDeletion(active.Document)))
            Finish(state, active, false, "militia-operation-document-lost");
    }

    private void OnDocumentInserted(Entity<MilitiaOperationConsoleComponent> console, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == DocumentSlot && EnsureOperationState(console.Comp.Config) is { } state)
            TryReceiveEvidence(console.Owner, state, args.Entity);
    }

    private void TryReceiveEvidence(EntityUid console, Entity<MilitiaOperationStateComponent> controller, EntityUid evidence)
    {
        var state = controller.Comp;
        if (state.Busy || !this.IsPowered(console, EntityManager) || state.Active is not { } active || active.EvidenceReceived ||
            evidence != active.Document || !TryComp<MilitiaOperationDocumentComponent>(evidence, out var document) ||
            document.Operation != active.Id || document.Controller != controller.Owner ||
            _timing.CurTime >= active.Deadline || TerminatingOrDeleted(evidence) || EntityManager.IsQueuedForDeletion(evidence))
            return;
        state.Busy = true;
        try
        {
            if (Remaining(active) == 0)
                Finish(state, active, true, "militia-operation-success");
            else
            {
                active.EvidenceReceived = true;
                state.Status = "militia-operation-evidence-received";
            }
            QueueDel(evidence);
        }
        catch (Exception exception) { _log.Error($"Operation evidence transfer failed: {exception}"); }
        finally { state.Busy = false; }
    }

    private bool Submit(EntityUid console, Entity<MilitiaOperationStateComponent> controller, Guid token)
    {
        var state = controller.Comp;
        if (state.Active is not { } active || active.Id != token || Remaining(active) != 0 ||
            !_slots.TryGetSlot(console, DocumentSlot, out var slot) || slot.Item != active.Document ||
            !TryComp<MilitiaOperationDocumentComponent>(active.Document, out var document) ||
            document.Operation != active.Id || document.Controller != controller.Owner ||
            Terminating(active.Document) || EntityManager.IsQueuedForDeletion(active.Document))
            return false;
        Finish(state, active, true, "militia-operation-success");
        return true;
    }

    private void Finish(MilitiaOperationStateComponent state, MilitiaActiveOperation active, bool success, string status)
    {
        if (!ReferenceEquals(state.Active, active))
            return;
        var points = success ? checked(_supplyPoints + active.Points) : _supplyPoints;
        var reputation = success ? checked(_reputation + active.Reputation) : _reputation;
        var history = Loc.GetString("militia-operation-history-entry", ("name", Loc.GetString(active.Name)),
            ("result", Loc.GetString(status)), ("points", success ? active.Points : 0));
        // Prepare replacement offers before committing completion or rewards.
        var offers = _prototypes.TryIndex(state.Config, out var config)
            ? ReplenishOffers(state.Offers, config) : state.Offers.ToList();
        state.Cleanup.UnionWith(active.Grids);
        state.History.Insert(0, history);
        if (state.History.Count > 30)
            state.History.RemoveAt(state.History.Count - 1);
        _supplyPoints = points;
        _reputation = reputation;
        state.Status = status;
        state.AvailableAt = _timing.CurTime + active.Cooldown;
        state.Active = null;
        state.Offers.Clear();
        state.Offers.AddRange(offers);
    }

    private bool Purchase(EntityUid controller, EntityUid console, MilitiaOperationConfigPrototype config,
        MilitiaOperationRequest request)
    {
        if (_purchases.TryGetValue(request.Token, out var receipt))
            return receipt.Actor == request.Actor && receipt.Lines.SequenceEqual(
                request.Lines.Select(line => (line.RewardPrototypeId, line.Count)));
        if (!config.ShopEnabled || _purchases.Count >= 2048 ||
            !_prototypes.TryIndex(config.Shop, out var pool) ||
            !_rewards.TryBuildPurchase(pool, request.Lines, RepairOrderRewardSystem.GetShopLevel(pool, _reputation),
                out var rewards, out var cost, out _) || _supplyPoints < cost)
            return false;
        var purchasedLines = request.Lines.Select(line => (line.RewardPrototypeId, line.Count)).ToArray();
        var source = Transform(console);
        var sourceGrid = source.GridUid;
        var sourceMap = source.MapID;
        RepairOrderDelivery? delivery = null;
        var committed = false;
        try
        {
            if (!_delivery.TryDeliver(controller, request.Token, console, pool, rewards, out delivery) ||
                !CanUseConsole(console, request.Actor) || Transform(console).GridUid != sourceGrid || Transform(console).MapID != sourceMap ||
                _supplyPoints < cost || _purchases.Count >= 2048 || _purchases.ContainsKey(request.Token))
                return false;
            _purchases.Add(request.Token, (request.Actor, purchasedLines));
            _supplyPoints -= cost;
            _delivery.Commit(delivery);
            committed = true;
            return true;
        }
        finally
        {
            if (!committed)
                _delivery.Rollback(delivery);
        }
    }

    private void OnControl(ref ShuttleControlAttemptEvent args)
    {
        if (!HasComp<MilitiaOperationTargetComponent>(args.GridUid) || args.ControlType == ShuttleControlType.Docking)
            return;
        args.Cancelled = true;
        args.Reason = Loc.GetString("militia-operation-controls-locked");
    }

    private void OnSplit(Entity<MilitiaOperationGridComponent> grid, ref GridSplitEvent args)
    {
        foreach (var fragment in args.NewGrids)
        {
            MarkGrid(fragment, grid.Comp.Controller, grid.Comp.Operation);
            if (TryComp<MilitiaOperationStateComponent>(grid.Comp.Controller, out var state))
            {
                if (state.Active is { } active && active.Id == grid.Comp.Operation)
                    active.Grids.Add(fragment);
                else
                    state.Cleanup.Add(fragment);
            }
        }
    }

    private void OnTargetTerminating(Entity<MilitiaOperationTargetComponent> target, ref EntityTerminatingEvent args)
    {
        if (!TryComp(target.Owner, out TransformComponent? transform) || transform.MapUid is not { } map ||
            TerminatingOrDeleted(map) || !_repair.TryFindPlayerOnRepairGrid(target.Owner, out _))
            return;
        var preserve = new List<EntityUid>();
        var children = transform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (_repair.TryFindPlayerInRepairEntity(child, out _))
                preserve.Add(child);
        }
        foreach (var child in preserve)
        {
            if (TryComp(child, out TransformComponent? childTransform) && !TerminatingOrDeleted(child))
                _transform.SetParent(child, childTransform, map);
        }
    }

    private bool IsActive(EntityUid controller, Guid operation)
        => TryComp<MilitiaOperationStateComponent>(controller, out var state) &&
           !Terminating(controller) && state.Active?.Id == operation;

    private void OnActorMobStateChanged(EntityUid uid, MilitiaOperationActorComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;
        var release = new List<EntityUid>();
        var equipment = AllEntityQuery<MilitiaOperationEquipmentComponent>();
        while (equipment.MoveNext(out var item, out var owner))
        {
            if (owner.Enemy == uid)
                release.Add(item);
        }
        foreach (var item in release)
            RemComp<MilitiaOperationEquipmentComponent>(item);
    }

    private void MaintainOwnedEntities()
    {
        var grids = EntityQueryEnumerator<MilitiaOperationGridComponent>();
        while (grids.MoveNext(out var grid, out var owner))
        {
            if (!IsActive(owner.Controller, owner.Operation))
                EnsureComp<RepairOrderCleanupComponent>(grid).Station = owner.Controller;
        }
        var documents = EntityQueryEnumerator<MilitiaOperationDocumentComponent>();
        while (documents.MoveNext(out var uid, out var owner))
        {
            if (owner.Operation != Guid.Empty && !IsActive(owner.Controller, owner.Operation))
                QueueDel(uid);
        }
        var enemies = EntityQueryEnumerator<MilitiaOperationEnemyComponent>();
        var remove = new HashSet<EntityUid>();
        while (enemies.MoveNext(out var uid, out var owner))
        {
            if (!IsActive(owner.Controller, owner.Operation))
                remove.Add(uid);
        }
        var equipment = EntityQueryEnumerator<MilitiaOperationEquipmentComponent>();
        var preserve = new List<EntityUid>();
        while (equipment.MoveNext(out var uid, out var owner))
        {
            if (!Exists(owner.Enemy) || _mobs.IsDead(owner.Enemy))
            {
                preserve.Add(uid);
                continue;
            }
            if (owner.Operation != Guid.Empty && !IsActive(owner.Controller, owner.Operation))
                remove.Add(uid);
        }
        foreach (var uid in preserve)
            RemComp<MilitiaOperationEquipmentComponent>(uid);
        foreach (var uid in remove)
        {
            try
            {
                if (Exists(uid) && !Terminating(uid) && !_repair.TryFindPlayerInRepairEntity(uid, out _))
                    Del(uid);
            }
            catch (Exception exception) { _log.Error($"Operation entity cleanup failed for {uid}: {exception}"); }
        }
    }

    private void MarkGrid(EntityUid grid, EntityUid controller, Guid operation)
    {
        var marker = EnsureComp<MilitiaOperationGridComponent>(grid);
        marker.Controller = controller;
        marker.Operation = operation;
        EnsureComp<MilitiaOperationTargetComponent>(grid);
        if (TryComp<ShuttleComponent>(grid, out var shuttle))
            shuttle.Enabled = false;
        _shuttles.Disable(grid);
        _shuttles.RemoveIFFFlag(grid, IFFFlags.Hide);
        _shuttles.AddIFFFlag(grid, IFFFlags.HideLabel);
    }

    private void UpdateUi(EntityUid console, MilitiaOperationConsoleComponent component, MilitiaOperationStateComponent state)
    {
        if (!Exists(console) || TerminatingOrDeleted(console) || EntityManager.IsQueuedForDeletion(console) ||
            !_ui.HasUi(console, MilitiaOperationUiKey.Key) ||
            !_prototypes.TryIndex(component.Config, out var config) || !_prototypes.TryIndex(config.Shop, out var pool))
            return;
        var active = state.Active;
        var position = string.Empty;
        if (active != null)
        {
            var grid = active.Grids.FirstOrDefault(OperationGridExists);
            if (grid.IsValid())
            {
                var p = _transform.GetWorldPosition(grid);
                position = string.Create(CultureInfo.InvariantCulture, $"X: {p.X:F0}   Y: {p.Y:F0}");
                if (Transform(grid).MapID != Transform(console).MapID)
                    position = Loc.GetString("militia-operation-remote-position", ("map", (int) Transform(grid).MapID), ("position", position));
            }
        }
        _ui.SetUiState(console, MilitiaOperationUiKey.Key, new MilitiaOperationUiState(
            state.Offers.ToList(), active == null ? null : new MilitiaOperationOffer(active.Id, active.Prototype.Id, active.Name, active.Description),
            position, active == null ? 0 : Remaining(active), active?.Deadline, state.AvailableAt,
            _supplyPoints, _reputation, RepairOrderRewardSystem.GetShopLevel(pool, _reputation),
            pool.ID, state.Status, state.History.ToArray(),
            _slots.TryGetSlot(console, DocumentSlot, out var slot) && slot.Item != null, state.Cleanup.Count != 0,
            active?.EvidenceReceived == true, config.ShopEnabled));
    }
}

[RegisterComponent]
public sealed partial class MilitiaOperationStateComponent : Component
{
    public ProtoId<MilitiaOperationConfigPrototype> Config;
    public bool Busy;
    public TimeSpan AvailableAt;
    public readonly Dictionary<EntityUid, TimeSpan> Requests = new();
    public MilitiaActiveOperation? Active;
    public readonly List<MilitiaOperationOffer> Offers = new();
    public readonly List<string> History = new();
    public readonly HashSet<EntityUid> Cleanup = new();
    public string Status = "militia-operation-ready";
}

public sealed class MilitiaActiveOperation
{
    public required ProtoId<MilitiaOperationPrototype> Prototype;
    public required LocId Name;
    public required LocId Description;
    public required int Points;
    public required int Reputation;
    public required TimeSpan Cooldown;
    public required Guid Id;
    public Guid OfferToken;
    public required EntityUid Document;
    public required TimeSpan Deadline;
    public required int Seed;
    public bool EvidenceReceived;
    public readonly HashSet<EntityUid> Grids = new();
    public readonly HashSet<EntityUid> Crew = new();
}

[RegisterComponent]
public sealed partial class MilitiaOperationGridComponent : Component
{
    public EntityUid Controller;
    public Guid Operation;
}

[RegisterComponent]
public sealed partial class MilitiaOperationEnemyComponent : Component
{
    public EntityUid Controller;
    public Guid Operation;
}

[RegisterComponent]
public sealed partial class MilitiaOperationEquipmentComponent : Component
{
    public EntityUid Enemy;
    public EntityUid Controller;
    public Guid Operation;
}
