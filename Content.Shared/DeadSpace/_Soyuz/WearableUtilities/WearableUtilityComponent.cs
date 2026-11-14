// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using Content.Shared.Actions;
using Content.Shared.Chemistry.Components;
using Content.Shared.DeadSpace.UniformAccessories.Components;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.WearableUtilities;

[RegisterComponent, NetworkedComponent]
public sealed partial class WearableUtilityComponent : Component
{
    public const int BindingCount = 6;
    public const string SolutionName = "injector";
    public const string SwapContainer = "tool_swap";

    [DataField(required: true)] public WearableUtilityKind Kind;
    [DataField(required: true)] public EntProtoId ActionPrototype;

    public EntityUid? Wearer;
    public EntityUid? LastWearer;
    public EntityUid? Host;
    public EntityUid? Action;
    public bool Armed;
    public bool Busy;
    public Solution? ArmedSolution;
    public readonly EntityUid?[] Bindings = new EntityUid?[BindingCount];
    public readonly Dictionary<EntityUid, EntityUid> Origins = new();
    public TimeSpan NextUse;
}

public enum WearableUtilityKind : byte
{
    Injector,
    Bracelet,
}

[Serializable, NetSerializable]
public enum WearableUtilityVisuals : byte
{
    State,
    Off,
    Ready,
    Empty,
}

[Serializable, NetSerializable]
public enum ToolBraceletUiKey : byte
{
    Key,
}

public sealed partial class WearableUtilityActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed record ToolBraceletEntry(NetEntity Item, string Name, bool Available);

[Serializable, NetSerializable]
public sealed class ToolBraceletUiState(ToolBraceletEntry?[] bindings, ToolBraceletEntry[] tools)
    : BoundUserInterfaceState
{
    public readonly ToolBraceletEntry?[] Bindings = bindings;
    public readonly ToolBraceletEntry[] Tools = tools;
}

[Serializable, NetSerializable]
public sealed class ToolBraceletMessage(int slot, NetEntity? item = null, bool select = false,
    NetEntity? expectedBinding = null)
    : BoundUserInterfaceMessage
{
    public readonly int Slot = slot;
    public readonly NetEntity? Item = item;
    public readonly bool Select = select;
    public readonly NetEntity? ExpectedBinding = expectedBinding;
}

public sealed class WearableUtilityAccessSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WearableUtilityComponent, AccessibleOverrideEvent>(OnAccessible);
    }

    private void OnAccessible(Entity<WearableUtilityComponent> ent, ref AccessibleOverrideEvent args)
    {
        if (args.Target != ent.Owner || !TryGetWearer(ent, out var wearer, out _) || wearer != args.User)
            return;
        args.Handled = true;
        args.Accessible = true;
    }

    public bool TryGetWearer(Entity<WearableUtilityComponent> ent, out EntityUid wearer, out EntityUid host)
    {
        wearer = default;
        host = ent.Owner;
        if (TerminatingOrDeleted(ent) || !_containers.TryGetContainingContainer(ent.Owner, out var container))
            return false;
        if (container.ID == UniformAccessoryHolderComponent.ContainerId)
        {
            host = container.Owner;
            if (!_containers.TryGetContainingContainer(host, out container))
                return false;
        }
        else if (ent.Comp.Kind != WearableUtilityKind.Bracelet)
            return false;

        var slot = ent.Comp.Kind == WearableUtilityKind.Injector ? "jumpsuit" : "gloves";
        wearer = container.Owner;
        return !TerminatingOrDeleted(host) && !TerminatingOrDeleted(wearer) &&
            _inventory.TryGetSlotEntity(wearer, slot, out var equipped) && equipped == host;
    }
}
