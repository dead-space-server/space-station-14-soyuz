// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using System.Linq;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Database;
using Content.Shared.DeadSpace.Chemistry.Components;
using Content.Shared.DeadSpace._Soyuz.WearableUtilities;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Medical.Healing;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tag;
using Content.Shared.Tools.Components;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.WearableUtilities;

public sealed class WearableUtilitySystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly ISharedAdminLogManager _admin = default!;
    [Dependency] private readonly ReactiveSystem _reactive = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly WearableUtilityAccessSystem _wearables = default!;

    private static readonly SoundSpecifier InjectSound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");
    private static readonly ProtoId<TagPrototype> SurgeryToolTag = "SurgeryTool";
    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WearableUtilityComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<WearableUtilityComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<WearableUtilityComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WearableUtilityComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<WearableUtilityComponent, WearableUtilityActionEvent>(OnAction);
        SubscribeLocalEvent<WearableUtilityComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<WearableUtilityComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<WearableUtilityUserComponent, DidUnequipEvent>(OnUnequipped);
        SubscribeLocalEvent<WearableUtilityUserComponent, MobStateChangedEvent>(OnMobState);
        Subs.BuiEvents<WearableUtilityComponent>(ToolBraceletUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<ToolBraceletMessage>(OnMessage);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextRefresh)
            return;
        _nextRefresh = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        var query = EntityQueryEnumerator<WearableUtilityComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Busy || TerminatingOrDeleted(uid))
                continue;
            Refresh((uid, comp));
            if (comp.Wearer is { } wearer && comp.Kind == WearableUtilityKind.Bracelet &&
                _ui.IsUiOpen(uid, ToolBraceletUiKey.Key, wearer))
                UpdateUi((uid, comp), wearer);
        }
    }

    private void OnInserted(Entity<WearableUtilityComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        Refresh(ent);
    }

    private void OnRemoved(Entity<WearableUtilityComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        Release(ent);
    }

    private void OnShutdown(Entity<WearableUtilityComponent> ent, ref ComponentShutdown args)
    {
        Release(ent);
    }

    private void OnUnequipped(Entity<WearableUtilityUserComponent> user, ref DidUnequipEvent args)
    {
        var query = EntityQueryEnumerator<WearableUtilityComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Wearer == user.Owner && comp.Host == args.Equipment)
                Release((uid, comp));
        }
    }

    private void Refresh(Entity<WearableUtilityComponent> ent)
    {
        if (!_wearables.TryGetWearer(ent, out var wearer, out var host))
        {
            if (ent.Comp.Wearer != null)
                Release(ent);
            return;
        }
        if (ent.Comp.Wearer == wearer && ent.Comp.Host == host)
            return;
        Release(ent);
        if (ent.Comp.LastWearer != wearer)
        {
            Array.Clear(ent.Comp.Bindings);
            ent.Comp.Origins.Clear();
        }
        ent.Comp.LastWearer = wearer;
        ent.Comp.Wearer = wearer;
        ent.Comp.Host = host;
        EnsureComp<WearableUtilityUserComponent>(wearer);
        _actions.AddAction(wearer, ref ent.Comp.Action, ent.Comp.ActionPrototype, ent.Owner);
        UpdateVisuals(ent);
    }

    private void Release(Entity<WearableUtilityComponent> ent)
    {
        Disarm(ent);
        _actions.RemoveAction(ent.Comp.Action);
        if (ent.Comp.Kind == WearableUtilityKind.Bracelet)
            _ui.CloseUi(ent.Owner, ToolBraceletUiKey.Key);
        ent.Comp.Wearer = null;
        ent.Comp.Host = null;
    }

    private bool CanUse(Entity<WearableUtilityComponent> ent, EntityUid user)
    {
        Refresh(ent);
        return IsLive(ent.Owner) && !ent.Comp.Busy && ent.Comp.Wearer == user &&
            _mobs.IsAlive(user) && _blocker.CanInteract(user, ent);
    }

    private void OnAction(Entity<WearableUtilityComponent> ent, ref WearableUtilityActionEvent args)
    {
        if (args.Handled || !CanUse(ent, args.Performer))
            return;
        args.Handled = true;
        Use(ent, args.Performer);
    }

    private void OnActivate(Entity<WearableUtilityComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !CanUse(ent, args.User))
            return;
        args.Handled = true;
        Use(ent, args.User);
    }

    private void Use(Entity<WearableUtilityComponent> ent, EntityUid user)
    {
        if (ent.Comp.Kind == WearableUtilityKind.Bracelet)
        {
            UpdateUi(ent, user);
            _ui.TryOpenUi(ent.Owner, ToolBraceletUiKey.Key, user);
            return;
        }
        if (ent.Comp.Armed)
            Disarm(ent);
        else if (!HasComp<InjectionBlockerComponent>(user) &&
            _solutions.TryGetInjectableSolution(user, out _, out _) &&
            _solutions.TryGetSolution(ent.Owner, WearableUtilityComponent.SolutionName, out _, out var solution) &&
            solution.Volume > 0)
        {
            ent.Comp.ArmedSolution = solution.Clone();
            ent.Comp.Armed = true;
            _actions.SetToggled(ent.Comp.Action, true);
            UpdateVisuals(ent);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("soyuz-utility-injector-not-ready"), user, user);
            return;
        }
        _popup.PopupEntity(Loc.GetString(ent.Comp.Armed ? "soyuz-utility-injector-armed" : "soyuz-utility-injector-off"), user, user);
    }

    private void Disarm(Entity<WearableUtilityComponent> ent)
    {
        ent.Comp.Armed = false;
        ent.Comp.ArmedSolution = null;
        _actions.SetToggled(ent.Comp.Action, false);
        if (!TerminatingOrDeleted(ent))
            UpdateVisuals(ent);
    }

    private static bool SameContents(Solution first, Solution second)
    {
        return first.Volume == second.Volume && first.Contents.Count == second.Contents.Count &&
            first.Contents.All(r => second.GetReagentQuantity(r.Reagent) == r.Quantity);
    }

    private void OnSolutionChanged(Entity<WearableUtilityComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (ent.Comp.Kind != WearableUtilityKind.Injector || args.SolutionId != WearableUtilityComponent.SolutionName)
            return;
        if (ent.Comp.ArmedSolution is { } armed && !SameContents(armed, args.Solution))
            Disarm(ent);
        UpdateVisuals(ent);
    }

    private void UpdateVisuals(Entity<WearableUtilityComponent> ent)
    {
        if (ent.Comp.Kind != WearableUtilityKind.Injector)
            return;
        var state = ent.Comp.Armed ? WearableUtilityVisuals.Ready : WearableUtilityVisuals.Off;
        if (!_solutions.TryGetSolution(ent.Owner, WearableUtilityComponent.SolutionName, out _, out var solution) || solution.Volume <= 0)
            state = WearableUtilityVisuals.Empty;
        _appearance.SetData(ent.Owner, WearableUtilityVisuals.State, state);
    }

    private void OnExamine(Entity<WearableUtilityComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Kind == WearableUtilityKind.Injector)
            args.PushMarkup(Loc.GetString(ent.Comp.Armed ? "soyuz-utility-injector-armed" : "soyuz-utility-injector-off"));
    }

    private void OnMobState(Entity<WearableUtilityUserComponent> user, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Critical || args.OldMobState == MobState.Critical)
            return;
        var query = EntityQueryEnumerator<WearableUtilityComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Kind == WearableUtilityKind.Injector && comp.Armed && comp.Wearer == user.Owner)
            {
                Inject((uid, comp), user.Owner);
                break;
            }
        }
    }

    private void Inject(Entity<WearableUtilityComponent> ent, EntityUid wearer)
    {
        if (ent.Comp.Busy || !_wearables.TryGetWearer(ent, out var current, out var host) || current != wearer || host != ent.Comp.Host)
        {
            Disarm(ent);
            return;
        }
        ent.Comp.Busy = true;
        try
        {
            if (HasComp<InjectionBlockerComponent>(wearer) ||
                !_solutions.TryGetSolution(ent.Owner, WearableUtilityComponent.SolutionName, out var source, out var solution) ||
                ent.Comp.ArmedSolution is not { } armed || solution.Volume <= 0 || !SameContents(armed, solution) ||
                !_solutions.TryGetInjectableSolution(wearer, out var target, out var bloodstream) ||
                bloodstream.AvailableVolume < solution.Volume)
                return;

            var remaining = solution.Clone();
            remaining.Name = solution.Name;
            var dose = remaining.SplitSolution(remaining.Volume);
            var injected = bloodstream.Clone();
            injected.Name = bloodstream.Name;
            injected.AddSolution(dose, _prototypes);
            Disarm(ent);
            source.Value.Comp.Solution = remaining;
            target.Value.Comp.Solution = injected;
            try
            {
                _solutions.UpdateChemicals(source.Value);
                _reactive.DoEntityReaction(wearer, dose, ReactionMethod.Injection);
            }
            finally
            {
                _solutions.UpdateChemicals(target.Value);
            }
            _admin.Add(LogType.ForceFeed, $"{ToPrettyString(wearer):user} received {SharedSolutionContainerSystem.ToPrettyString(dose):solution} from {ToPrettyString(ent.Owner):injector}");
            _audio.PlayPvs(InjectSound, ent.Owner);
            _popup.PopupEntity(Loc.GetString("soyuz-utility-injector-fired", ("amount", dose.Volume)), wearer, wearer);
        }
        finally
        {
            try
            {
                Disarm(ent);
            }
            finally
            {
                ent.Comp.Busy = false;
            }
        }
    }

    private IEnumerable<Entity<StorageComponent>> WornStorage(EntityUid user)
    {
        foreach (var slot in new[] { "belt", "back" })
        {
            if (_inventory.TryGetSlotEntity(user, slot, out var uid) && TryComp<StorageComponent>(uid, out var storage) &&
                IsLive(uid.Value))
                yield return (uid.Value, storage);
        }
    }

    private bool TryGetToolStorage(EntityUid user, EntityUid tool, out Entity<StorageComponent> storage)
    {
        foreach (var candidate in WornStorage(user))
        {
            if (candidate.Comp.Container.Contains(tool))
            {
                storage = candidate;
                return true;
            }
        }
        storage = default;
        return false;
    }

    private bool IsLive(EntityUid uid) => !TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid);

    private bool IsSelectableItem(EntityUid item) => IsLive(item) &&
        (!TryComp<StackComponent>(item, out var stack) || stack.Count > 0) &&
        (HasComp<ToolComponent>(item) || HasComp<MultipleToolComponent>(item) ||
            HasComp<HealingComponent>(item) || _tags.HasTag(item, SurgeryToolTag));

    private void OnUiOpened(Entity<WearableUtilityComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (ent.Comp.Kind != WearableUtilityKind.Bracelet || !CanUse(ent, args.Actor))
        {
            _ui.CloseUi(ent.Owner, ToolBraceletUiKey.Key, args.Actor);
            return;
        }
        UpdateUi(ent, args.Actor);
    }

    private void UpdateUi(Entity<WearableUtilityComponent> ent, EntityUid user)
    {
        var tools = WornStorage(user).SelectMany(s => s.Comp.Container.ContainedEntities)
            .Where(IsSelectableItem).Distinct().OrderBy(uid => Name(uid)).ToArray();
        var bindings = new ToolBraceletEntry?[WearableUtilityComponent.BindingCount];
        for (var i = 0; i < bindings.Length; i++)
        {
            if (ent.Comp.Bindings[i] is not { } uid || !IsLive(uid))
                continue;
            bindings[i] = new ToolBraceletEntry(GetNetEntity(uid), Name(uid), tools.Contains(uid));
        }
        _ui.SetUiState(ent.Owner, ToolBraceletUiKey.Key, new ToolBraceletUiState(bindings,
            tools.Select(uid => new ToolBraceletEntry(GetNetEntity(uid), Name(uid), true)).ToArray()));
    }

    private void OnMessage(Entity<WearableUtilityComponent> ent, ref ToolBraceletMessage args)
    {
        if (ent.Comp.Kind != WearableUtilityKind.Bracelet || args.Slot is < 0 or >= WearableUtilityComponent.BindingCount ||
            !CanUse(ent, args.Actor) || _timing.CurTime < ent.Comp.NextUse)
            return;
        ent.Comp.NextUse = _timing.CurTime + TimeSpan.FromSeconds(0.2);
        var binding = ent.Comp.Bindings[args.Slot] is { } bound && IsLive(bound)
            ? GetNetEntity(bound) : (NetEntity?) null;
        if (binding != args.ExpectedBinding)
        {
            _popup.PopupEntity(Loc.GetString("soyuz-utility-bracelet-changed"), args.Actor, args.Actor);
            UpdateUi(ent, args.Actor);
            return;
        }
        if (args.Select)
        {
            if (ent.Comp.Bindings[args.Slot] is { } tool && !SwapTool(ent, args.Actor, tool))
                _popup.PopupEntity(Loc.GetString("soyuz-utility-bracelet-swap-failed"), args.Actor, args.Actor);
        }
        else if (args.Item == null)
            ent.Comp.Bindings[args.Slot] = null;
        else if (TryGetEntity(args.Item.Value, out var uid) && IsSelectableItem(uid.Value) &&
            TryGetToolStorage(args.Actor, uid.Value, out var storage))
        {
            for (var i = 0; i < ent.Comp.Bindings.Length; i++)
            {
                if (ent.Comp.Bindings[i] == uid)
                    ent.Comp.Bindings[i] = null;
            }
            ent.Comp.Bindings[args.Slot] = uid;
            ent.Comp.Origins[uid.Value] = storage.Owner;
        }
        foreach (var tool in ent.Comp.Origins.Keys.ToArray())
        {
            if (!ent.Comp.Bindings.Contains(tool) && _hands.GetActiveItem(args.Actor) != tool)
                ent.Comp.Origins.Remove(tool);
        }
        UpdateUi(ent, args.Actor);
    }

    private bool SwapTool(Entity<WearableUtilityComponent> ent, EntityUid user, EntityUid tool)
    {
        var activeHand = _hands.GetActiveHand(user);
        if (!IsSelectableItem(tool) || !TryGetToolStorage(user, tool, out var source) ||
            !source.Comp.StoredItems.TryGetValue(tool, out var location) ||
            !_containers.TryGetContainer(ent.Owner, WearableUtilityComponent.SwapContainer, out var staging) ||
            staging.ContainedEntities.Count != 0 ||
            !_hands.TryGetHand(user, activeHand, out _) ||
            !_containers.TryGetContainer(user, activeHand, out var handContainer))
            return false;

        var held = _hands.GetActiveItem(user);
        if (held == tool)
            return true;
        if (held is { } previous && (!IsSelectableItem(previous) || !_hands.CanDrop(user, previous) ||
            TryComp<WieldableComponent>(previous, out var wieldable) && wieldable.Wielded))
            return false;
        if (Transform(tool).Anchored || !_blocker.CanPickup(user, tool) ||
            !_containers.CanRemove(tool, source.Comp.Container) ||
            !_containers.CanInsert(tool, handContainer, assumeEmpty: true))
            return false;

        ent.Comp.Busy = true;
        var success = false;
        var moved = false;
        try
        {
            if (!CanStillUse(ent, user) || _hands.GetActiveHand(user) != activeHand ||
                _hands.GetActiveItem(user) != held || !source.Comp.Container.Contains(tool))
                return false;
            moved = true;
            if (!_containers.Insert(tool, staging) || !staging.Contains(tool))
                return false;
            if (held is { } old)
            {
                var destinations = WornStorage(user).OrderByDescending(s =>
                    ent.Comp.Origins.TryGetValue(old, out var origin) && s.Owner == origin).ToArray();
                var stored = false;
                foreach (var destination in destinations)
                {
                    if (_storage.CanInsert(destination, old, out _, destination.Comp, ignoreStacks: true) &&
                        _storage.Insert(destination, old, out _, user, destination.Comp, playSound: false, stackAutomatically: false) &&
                        destination.Comp.Container.Contains(old))
                    {
                        stored = true;
                        break;
                    }
                }
                if (!stored)
                    return false;
            }
            if (!CanStillUse(ent, user) || _hands.GetActiveHand(user) != activeHand ||
                !_hands.TryPickup(user, tool, activeHand, animate: false) || _hands.GetActiveItem(user) != tool ||
                !CanStillUse(ent, user) || !WornStorage(user).Any(s => s.Owner == source.Owner))
                return false;
            ent.Comp.Origins[tool] = source.Owner;
            success = true;
            return true;
        }
        finally
        {
            try
            {
                if (!success && moved)
                {
                    if (!source.Comp.Container.Contains(tool))
                        Restore(tool, staging);
                    if (held is { } old)
                        Restore(old, handContainer);
                    Restore(tool, source.Comp.Container, location);
                }
            }
            finally
            {
                ent.Comp.Busy = false;
            }
        }
    }

    private bool CanStillUse(Entity<WearableUtilityComponent> ent, EntityUid user) =>
        IsLive(ent.Owner) && _wearables.TryGetWearer(ent, out var wearer, out var host) && wearer == user && host == ent.Comp.Host &&
        _mobs.IsAlive(user) && _blocker.CanInteract(user, ent);

    private void Restore(EntityUid item, BaseContainer destination, ItemStorageLocation? location = null)
    {
        if (TerminatingOrDeleted(item) || TerminatingOrDeleted(destination.Owner) || destination.Contains(item))
            return;
        if (_containers.TryGetContainingContainer(item, out var current) && !_containers.Remove(item, current, force: true))
        {
            Log.Error($"Cannot remove {ToPrettyString(item)} during bracelet rollback.");
            return;
        }
        if (location is { } position && TryComp<StorageComponent>(destination.Owner, out var storage) &&
            _storage.InsertAt((destination.Owner, storage), item, position, out _, playSound: false, stackAutomatically: false) &&
            destination.Contains(item))
            return;
        if (!_containers.Insert(item, destination, force: true) || !destination.Contains(item))
            Log.Error($"Cannot restore {ToPrettyString(item)} to {ToPrettyString(destination.Owner)} after bracelet swap.");
    }
}

[RegisterComponent]
public sealed partial class WearableUtilityUserComponent : Component;
