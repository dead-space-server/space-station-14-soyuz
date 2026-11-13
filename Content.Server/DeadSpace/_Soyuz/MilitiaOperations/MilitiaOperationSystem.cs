// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Globalization;
using System.Linq;
using Content.Server.DeadSpace._Soyuz.RepairOrders;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.ActionBlocker;
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
using Robust.Server.GameObjects;
using Robust.Server.Physics;
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

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(Content.Server.NPC.Systems.NPCSteeringSystem));
        _log = _logs.GetSawmill("militia_operations");
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _nextUpdate = TimeSpan.Zero;
            _nextSquadUpdate = TimeSpan.Zero;
            _nextSquadSafety = TimeSpan.Zero;
            _nextSquadGrenade = TimeSpan.Zero;
            _preparationMaps.Clear();
        });
        SubscribeLocalEvent<MilitiaOperationGridComponent, GridSplitEvent>(OnSplit);
        SubscribeLocalEvent<MilitiaOperationTargetComponent, EntityTerminatingEvent>(OnTargetTerminating);
        SubscribeLocalEvent<MilitiaOperationActorComponent, DamageChangedEvent>(OnActorDamaged);
        SubscribeLocalEvent<MilitiaOperationActorComponent, ComponentShutdown>(OnActorShutdown);
        SubscribeLocalEvent<MilitiaOperationActorComponent, MobStateChangedEvent>(OnActorMobStateChanged);
        SubscribeLocalEvent<MilitiaOperationConsoleComponent, EntInsertedIntoContainerMessage>(OnDocumentInserted);
        SubscribeLocalEvent<MilitiaOperationActorComponent, Content.Shared.Weapons.Ranged.Events.ShotAttemptedEvent>(OnSquadShot);
        SubscribeLocalEvent<MilitiaOperationActorComponent, MapInitEvent>(OnActorMapInit,
            after: [typeof(SharedBodySystem), typeof(LoadoutSystem)]);
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
        var scan = UpdateSquadSafety();
        UpdateSupportSafety(scan);
        UpdateSquads();
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);

        var consoles = EntityQueryEnumerator<MilitiaOperationConsoleComponent>();
        while (consoles.MoveNext(out var uid, out var console))
        {
            if (EnsureStation(uid, console) is not { } state || state.Busy)
                continue;
            try
            {
                if (_slots.TryGetSlot(uid, DocumentSlot, out var evidenceSlot) && evidenceSlot.Item is { } evidence)
                    TryReceiveEvidence(uid, state, evidence);
                CheckActive(state);
                UpdateUi(uid, console, state);
            }
            catch (Exception exception)
            {
                _log.Error($"Operation maintenance at {uid} failed: {exception}");
            }
        }

        var states = EntityQueryEnumerator<MilitiaOperationStationComponent>();
        while (states.MoveNext(out _, out var state))
        {
            if (!state.Busy)
            {
                try { CheckActive(state); }
                catch (Exception exception) { _log.Error($"Operation lifecycle failed: {exception}"); }
            }
        }
        MaintainOwnedEntities();
        foreach (var map in _preparationMaps.ToArray())
            CleanupPreparationMap(map);
    }

    private MilitiaOperationStationComponent? EnsureStation(EntityUid console, MilitiaOperationConsoleComponent component)
    {
        if (_stations.GetOwningStation(console) is not { } station || Terminating(station) ||
            !_prototypes.TryIndex(component.Config, out var config))
            return null;
        var state = EnsureComp<MilitiaOperationStationComponent>(station);
        if (state.Config.Length == 0)
        {
            state.Config = config.ID;
            InitializeOffers(state, config);
        }
        return state.Config == config.ID ? state : null;
    }

    private void InitializeOffers(MilitiaOperationStationComponent state, MilitiaOperationConfigPrototype config)
    {
        state.Offers.Clear();
        foreach (var id in config.Operations.Distinct())
        {
            if (_prototypes.TryIndex(id, out _))
                state.Offers.Add(new MilitiaOperationOffer(Guid.NewGuid(), id.Id));
        }
    }

    private void OnOpened(Entity<MilitiaOperationConsoleComponent> console, ref BoundUIOpenedEvent args)
    {
        if (EnsureStation(console.Owner, console.Comp) is { } state)
            UpdateUi(console.Owner, console.Comp, state);
    }

    private void OnRequest(Entity<MilitiaOperationConsoleComponent> console, ref MilitiaOperationRequest args)
    {
        if (!CanUseConsole(console.Owner, args.Actor) ||
            _stations.GetOwningStation(console.Owner) is not { } station ||
            EnsureStation(console.Owner, console.Comp) is not { } state || state.Busy ||
            args.Action == MilitiaOperationAction.Purchase && _timing.CurTime < state.Requests.GetValueOrDefault(args.Actor) || args.Token == Guid.Empty ||
            !_prototypes.TryIndex(console.Comp.Config, out var config))
        {
            _ui.ServerSendUiMessage(console.Owner, MilitiaOperationUiKey.Key,
                new MilitiaOperationResult(false, "militia-operation-unavailable", args.Token), args.Actor);
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
                    success = Accept(station, console.Owner, state, config, args.Token);
                    message = success ? "militia-operation-accepted" : "militia-operation-spawn-failed";
                    break;
                case MilitiaOperationAction.Submit:
                    success = Submit(console.Owner, state, args.Token);
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
                    success = config.ShopEnabled && Purchase(station, console.Owner, state, config, args);
                    message = !config.ShopEnabled ? "militia-operation-shop-maintenance" : success ? "militia-operation-purchased" : "militia-operation-purchase-failed";
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
        _ui.ServerSendUiMessage(console.Owner, MilitiaOperationUiKey.Key,
            new MilitiaOperationResult(success, message, args.Token), args.Actor);
        UpdateUi(console.Owner, console.Comp, state);
    }

    private bool CanUseConsole(EntityUid console, EntityUid actor)
        => Exists(console) && !Terminating(console) && this.IsPowered(console, EntityManager) &&
           !HasComp<GhostComponent>(actor) && _blocker.CanInteract(actor, console) &&
           _ui.IsUiOpen(console, MilitiaOperationUiKey.Key, actor) &&
           _interaction.InRangeUnobstructed(actor, console) && _access.IsAllowed(actor, console);

    private bool Accept(EntityUid station, EntityUid console, MilitiaOperationStationComponent state,
        MilitiaOperationConfigPrototype config, Guid token)
    {
        state.Cleanup.RemoveWhere(uid => !Exists(uid));
        if (state.Active != null || state.Cleanup.Count != 0 || _timing.CurTime < state.AvailableAt)
            return false;
        var offer = state.Offers.FirstOrDefault(o => o.Token == token);
        if (offer == null || !_prototypes.TryIndex<MilitiaOperationPrototype>(offer.Prototype, out var prototype) ||
            !TryPrepareOperation(station, console, config, prototype, out var active))
            return false;

        state.Active = active;
        state.Status = "militia-operation-in-progress";
        state.Offers.Remove(offer);
        _log.Info($"Operation activated: Station={station}, Operation={active.Id}, Prototype={prototype.ID}, Seed={active.Seed}, Crew={active.Crew.Count}");
        return true;
    }

    private int Remaining(MilitiaActiveOperation active)
        => active.Crew.Count(uid => Exists(uid) && !Terminating(uid) && !EntityManager.IsQueuedForDeletion(uid) &&
            HasComp<MilitiaOperationActorComponent>(uid) && !_mobs.IsDead(uid));

    private void CheckActive(MilitiaOperationStationComponent state)
    {
        state.Cleanup.RemoveWhere(uid => !Exists(uid));
        if (state.Active is not { } active)
            return;
        if (_timing.CurTime >= active.Deadline)
            Finish(state, active, false, "militia-operation-expired");
        else if (active.EvidenceReceived && Remaining(active) == 0)
            Finish(state, active, true, "militia-operation-success");
        else if (!active.EvidenceReceived && (!Exists(active.Document) || Terminating(active.Document) || EntityManager.IsQueuedForDeletion(active.Document)))
            Finish(state, active, false, "militia-operation-document-lost");
    }

    private void OnDocumentInserted(Entity<MilitiaOperationConsoleComponent> console, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == DocumentSlot && EnsureStation(console.Owner, console.Comp) is { } state)
            TryReceiveEvidence(console.Owner, state, args.Entity);
    }

    private void TryReceiveEvidence(EntityUid console, MilitiaOperationStationComponent state, EntityUid evidence)
    {
        if (state.Busy || !this.IsPowered(console, EntityManager) || state.Active is not { } active || active.EvidenceReceived ||
            evidence != active.Document || !TryComp<MilitiaOperationDocumentComponent>(evidence, out var document) ||
            document.Operation != active.Id || document.Station != _stations.GetOwningStation(console) ||
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

    private bool Submit(EntityUid console, MilitiaOperationStationComponent state, Guid token)
    {
        if (state.Active is not { } active || active.Id != token || Remaining(active) != 0 ||
            !_slots.TryGetSlot(console, DocumentSlot, out var slot) || slot.Item != active.Document ||
            !TryComp<MilitiaOperationDocumentComponent>(active.Document, out var document) ||
            document.Operation != active.Id || document.Station != _stations.GetOwningStation(console) ||
            Terminating(active.Document) || EntityManager.IsQueuedForDeletion(active.Document))
            return false;
        Finish(state, active, true, "militia-operation-success");
        return true;
    }

    private void Finish(MilitiaOperationStationComponent state, MilitiaActiveOperation active, bool success, string status)
    {
        if (!ReferenceEquals(state.Active, active))
            return;
        var points = success ? checked(state.SupplyPoints + active.Points) : state.SupplyPoints;
        var reputation = success ? checked(state.Reputation + active.Reputation) : state.Reputation;
        var history = Loc.GetString("militia-operation-history-entry", ("name", Loc.GetString(active.Name)),
            ("result", Loc.GetString(status)), ("points", success ? active.Points : 0));
        state.Cleanup.UnionWith(active.Grids);
        state.History.Insert(0, history);
        if (state.History.Count > 30)
            state.History.RemoveAt(state.History.Count - 1);
        state.SupplyPoints = points;
        state.Reputation = reputation;
        state.Status = status;
        state.AvailableAt = _timing.CurTime + active.Cooldown;
        state.Active = null;
    }

    private bool Purchase(EntityUid station, EntityUid console, MilitiaOperationStationComponent state,
        MilitiaOperationConfigPrototype config, MilitiaOperationRequest request)
    {
        if (!config.ShopEnabled || state.Purchases.Count >= 2048 || state.Purchases.Contains(request.Token) ||
            !_prototypes.TryIndex(config.Shop, out var pool) ||
            !_rewards.TryBuildPurchase(pool, request.Lines, RepairOrderRewardSystem.GetShopLevel(pool, state.Reputation),
                out var rewards, out var cost, out _) || state.SupplyPoints < cost)
            return false;
        RepairOrderDelivery? delivery = null;
        var committed = false;
        try
        {
            if (!_delivery.TryDeliver(station, request.Token, console, pool, rewards, out delivery) ||
                state.SupplyPoints < cost || state.Purchases.Contains(request.Token) ||
                !CanUseConsole(console, request.Actor) || _stations.GetOwningStation(console) != station)
                return false;
            state.Purchases.Add(request.Token);
            state.SupplyPoints -= cost;
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
            MarkGrid(fragment, grid.Comp.Station, grid.Comp.Operation);
            if (TryComp<MilitiaOperationStationComponent>(grid.Comp.Station, out var station))
            {
                if (station.Active is { } active && active.Id == grid.Comp.Operation)
                    active.Grids.Add(fragment);
                else
                    station.Cleanup.Add(fragment);
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

    private bool IsActive(EntityUid station, Guid operation)
        => TryComp<MilitiaOperationStationComponent>(station, out var state) &&
           !Terminating(station) && state.Active?.Id == operation;

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
            if (!IsActive(owner.Station, owner.Operation))
                EnsureComp<RepairOrderCleanupComponent>(grid).Station = owner.Station;
        }
        var documents = EntityQueryEnumerator<MilitiaOperationDocumentComponent>();
        while (documents.MoveNext(out var uid, out var owner))
        {
            if (owner.Operation != Guid.Empty && !IsActive(owner.Station, owner.Operation))
                QueueDel(uid);
        }
        var enemies = EntityQueryEnumerator<MilitiaOperationEnemyComponent>();
        var remove = new HashSet<EntityUid>();
        while (enemies.MoveNext(out var uid, out var owner))
        {
            if (!IsActive(owner.Station, owner.Operation))
                remove.Add(uid);
        }
        var equipment = EntityQueryEnumerator<MilitiaOperationEquipmentComponent>();
        var preserve = new List<EntityUid>();
        while (equipment.MoveNext(out var uid, out var owner))
        {
            if (Exists(owner.Enemy) && _mobs.IsDead(owner.Enemy))
            {
                preserve.Add(uid);
                continue;
            }
            if (owner.Operation != Guid.Empty && !IsActive(owner.Station, owner.Operation) ||
                !Exists(owner.Enemy))
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

    private void MarkGrid(EntityUid grid, EntityUid station, Guid operation)
    {
        var marker = EnsureComp<MilitiaOperationGridComponent>(grid);
        marker.Station = station;
        marker.Operation = operation;
        EnsureComp<MilitiaOperationTargetComponent>(grid);
        _shuttles.AddIFFFlag(grid, IFFFlags.Hide | IFFFlags.HideLabel);
    }

    private void UpdateUi(EntityUid console, MilitiaOperationConsoleComponent component, MilitiaOperationStationComponent state)
    {
        if (!_prototypes.TryIndex(component.Config, out var config) || !_prototypes.TryIndex(config.Shop, out var pool))
            return;
        var active = state.Active;
        var position = string.Empty;
        if (active != null)
        {
            var grid = active.Grids.FirstOrDefault(uid => Exists(uid) && !Terminating(uid));
            if (grid.IsValid())
            {
                var p = _transform.GetWorldPosition(grid);
                position = string.Create(CultureInfo.InvariantCulture, $"X: {p.X:F0}   Y: {p.Y:F0}");
            }
        }
        _ui.SetUiState(console, MilitiaOperationUiKey.Key, new MilitiaOperationUiState(
            state.Offers.ToList(), active == null ? null : new MilitiaOperationOffer(active.Id, active.Prototype.Id),
            position, active == null ? 0 : Remaining(active), active?.Deadline, state.AvailableAt,
            state.SupplyPoints, state.Reputation, RepairOrderRewardSystem.GetShopLevel(pool, state.Reputation),
            pool.ID, state.Status, state.History.ToArray(),
            _slots.TryGetSlot(console, DocumentSlot, out var slot) && slot.Item != null, state.Cleanup.Count != 0,
            active?.EvidenceReceived == true, config.ShopEnabled));
    }
}

[RegisterComponent]
public sealed partial class MilitiaOperationStationComponent : Component
{
    public string Config = string.Empty;
    [DataField] public long SupplyPoints;
    [DataField] public long Reputation;
    public bool Busy;
    public TimeSpan AvailableAt;
    public readonly Dictionary<EntityUid, TimeSpan> Requests = new();
    public MilitiaActiveOperation? Active;
    public readonly List<MilitiaOperationOffer> Offers = new();
    public readonly List<string> History = new();
    public readonly HashSet<Guid> Purchases = new();
    public readonly HashSet<EntityUid> Cleanup = new();
    public string Status = "militia-operation-ready";
}

public sealed class MilitiaActiveOperation
{
    public required ProtoId<MilitiaOperationPrototype> Prototype;
    public required LocId Name;
    public required int Points;
    public required int Reputation;
    public required TimeSpan Cooldown;
    public required Guid Id;
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
    public EntityUid Station;
    public Guid Operation;
}

[RegisterComponent]
public sealed partial class MilitiaOperationEnemyComponent : Component
{
    public EntityUid Station;
    public Guid Operation;
}

[RegisterComponent]
public sealed partial class MilitiaOperationEquipmentComponent : Component
{
    public EntityUid Enemy;
    public EntityUid Station;
    public Guid Operation;
}
