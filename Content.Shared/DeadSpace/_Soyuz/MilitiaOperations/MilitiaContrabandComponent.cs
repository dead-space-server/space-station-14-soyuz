// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.MilitiaOperations;

[RegisterComponent, NetworkedComponent]
public sealed partial class MilitiaOperativeComponent : Component;

[Serializable, NetSerializable]
public enum MilitiaContrabandKind : byte { Armor, Helmet, Boots, Firearm, Melee, Hypospray }

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MilitiaRestrictedGearComponent : Component
{
    [DataField, AutoNetworkedField] public MilitiaContrabandKind Kind;
    [DataField, AutoNetworkedField] public bool Surrendered;
    [DataField] public Guid Serial;
    public TimeSpan NextDeniedPopup;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class MilitiaCustodyCabinetComponent : Component
{
    [DataField] public ProtoId<MilitiaOperationConfigPrototype> Config = "MilitiaOperations";
    [DataField] public int Capacity = 30;
    [DataField] public TimeSpan SealDuration = TimeSpan.FromSeconds(3);
    public Guid Token = Guid.NewGuid();
    public TimeSpan? Deadline;
    public TimeSpan RetryUntil;
    public EntityUid Station;
    public bool Paid;
    public readonly List<MilitiaCustodyQuote> Pending = new();
    public readonly HashSet<EntityUid> Delivery = new();
    public string Status = "militia-custody-ready";
    public string QuoteSignature = string.Empty;
}

public sealed record MilitiaCustodyQuote(EntityUid Item, EntityUid Root, Guid Serial, MilitiaContrabandKind Kind, int Points, int Reputation);

[Serializable, NetSerializable]
public enum MilitiaCustodyUiKey : byte { Key }

[Serializable, NetSerializable]
public enum MilitiaCustodyAction : byte { Insert, Eject, Submit }

[Serializable, NetSerializable]
public sealed class MilitiaCustodyRequest(MilitiaCustodyAction action, Guid token, NetEntity? item = null) : BoundUserInterfaceMessage
{
    public readonly MilitiaCustodyAction Action = action;
    public readonly Guid Token = token;
    public readonly NetEntity? Item = item;
}

[Serializable, NetSerializable]
public sealed record MilitiaCustodyEntry(NetEntity Item, string Name, string Prototype, long Points, long Reputation, string Reason);

[Serializable, NetSerializable]
public sealed class MilitiaCustodyState(Guid token, List<MilitiaCustodyEntry> entries, long points, long reputation,
    bool busy, bool online, TimeSpan? deadline, string status, int capacity) : BoundUserInterfaceState
{
    public readonly Guid Token = token;
    public readonly List<MilitiaCustodyEntry> Entries = entries;
    public readonly long Points = points;
    public readonly long Reputation = reputation;
    public readonly bool Busy = busy;
    public readonly bool Online = online;
    public readonly TimeSpan? Deadline = deadline;
    public readonly string Status = status;
    public readonly int Capacity = capacity;
}

[Serializable, NetSerializable]
public enum MilitiaCustodyVisuals : byte { State }
