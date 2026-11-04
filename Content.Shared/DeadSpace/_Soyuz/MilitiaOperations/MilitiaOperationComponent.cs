// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared.DeadSpace._Soyuz.MilitiaOperations;

[RegisterComponent, NetworkedComponent]
public sealed partial class MilitiaOperationConsoleComponent : Component
{
    [DataField(required: true)] public ProtoId<MilitiaOperationConfigPrototype> Config;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class MilitiaOperationTargetComponent : Component;

[RegisterComponent, NetworkedComponent]
public sealed partial class MilitiaOperationDocumentComponent : Component
{
    public EntityUid Station;
    public Guid Operation;
}

[Prototype]
public sealed partial class MilitiaOperationPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public LocId Name;
    [DataField(required: true)] public LocId Description;
    [DataField(required: true)] public ResPath Map;
    [DataField(required: true)] public int Points;
    [DataField(required: true)] public int Reputation;
    [DataField] public int MinimumCrew = 4;
    [DataField] public int MaximumCrew = 8;
    [DataField] public TimeSpan Duration = TimeSpan.FromMinutes(60);
    [DataField] public int Medics = 1;
}

[Prototype]
public sealed partial class MilitiaOperationConfigPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public List<ProtoId<MilitiaOperationPrototype>> Operations = new();
    [DataField(required: true)] public List<EntProtoId> Crew = new();
    [DataField(required: true)] public EntProtoId Medic;
    [DataField] public EntProtoId DocumentGuard = "MilitiaOperationRaiderCustodian";
    [DataField(required: true)] public EntProtoId Document;
    [DataField(required: true)] public ProtoId<RepairRewardPoolPrototype> Shop;
    [DataField] public bool ShopEnabled;
    [DataField] public TimeSpan Cooldown = TimeSpan.FromMinutes(5);
}

[Prototype]
public sealed partial class MilitiaEquipmentPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField, NeverPushInheritance] public string ID { get; private set; } = default!;
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<MilitiaEquipmentPrototype>)), NeverPushInheritance]
    public string[]? Parents { get; private set; }
    [AbstractDataField, NeverPushInheritance] public bool Abstract { get; private set; }
    [DataField, AlwaysPushInheritance] public Dictionary<string, List<EntProtoId>> Equipment = new();
    [DataField] public List<MilitiaProtectionSet> Protection = new();
    [DataField] public List<MilitiaWeaponSet> Weapons = new();
    [DataField] public List<EntProtoId> Supplies = new();
    [DataField] public List<EntProtoId> Grenades = new();
    [DataField] public TimeSpan ReloadTime = TimeSpan.FromSeconds(2.5);
}

[DataDefinition]
public sealed partial class MilitiaProtectionSet
{
    [DataField(required: true)] public EntProtoId Suit;
    [DataField(required: true)] public EntProtoId Helmet;
}

[DataDefinition]
public sealed partial class MilitiaWeaponSet
{
    [DataField(required: true)] public EntProtoId Weapon;
    [DataField(required: true)] public EntProtoId Magazine;
    [DataField] public int MinimumMagazines = 3;
    [DataField] public int MaximumMagazines = 3;
}

[Serializable, NetSerializable]
public enum MilitiaOperationUiKey : byte { Key }

[Serializable, NetSerializable]
public enum MilitiaOperationAction : byte { Accept, Submit, Abort, Purchase }

[Serializable, NetSerializable]
public sealed class MilitiaOperationRequest : BoundUserInterfaceMessage
{
    public readonly MilitiaOperationAction Action;
    public readonly Guid Token;
    public readonly List<RepairOrderRewardBuiEntry> Lines;

    public MilitiaOperationRequest(MilitiaOperationAction action, Guid token, List<RepairOrderRewardBuiEntry>? lines = null)
    {
        Action = action;
        Token = token;
        Lines = lines ?? new();
    }
}

[Serializable, NetSerializable]
public sealed class MilitiaOperationOffer
{
    public readonly Guid Token;
    public readonly string Prototype;
    public MilitiaOperationOffer(Guid token, string prototype) { Token = token; Prototype = prototype; }
}

[Serializable, NetSerializable]
public sealed class MilitiaOperationUiState : BoundUserInterfaceState
{
    public readonly List<MilitiaOperationOffer> Offers;
    public readonly MilitiaOperationOffer? Active;
    public readonly string Coordinates;
    public readonly int Remaining;
    public readonly TimeSpan? Deadline;
    public readonly TimeSpan AvailableAt;
    public readonly long Points;
    public readonly long Reputation;
    public readonly int Level;
    public readonly string Shop;
    public readonly string Status;
    public readonly string[] History;
    public readonly bool DocumentInserted;
    public readonly bool CleanupPending;
    public readonly bool EvidenceReceived;
    public readonly bool ShopEnabled;

    public MilitiaOperationUiState(List<MilitiaOperationOffer> offers, MilitiaOperationOffer? active,
        string coordinates, int remaining, TimeSpan? deadline, TimeSpan availableAt, long points,
        long reputation, int level, string shop, string status, string[] history, bool documentInserted, bool cleanupPending, bool evidenceReceived, bool shopEnabled)
    {
        Offers = offers; Active = active; Coordinates = coordinates; Remaining = remaining;
        Deadline = deadline; AvailableAt = availableAt; Points = points; Reputation = reputation;
        Level = level; Shop = shop; Status = status; History = history;
        DocumentInserted = documentInserted; CleanupPending = cleanupPending; EvidenceReceived = evidenceReceived;
        ShopEnabled = shopEnabled;
    }
}

[Serializable, NetSerializable]
public sealed class MilitiaOperationResult : BoundUserInterfaceMessage
{
    public readonly bool Success;
    public readonly string Message;
    public readonly Guid Token;
    public MilitiaOperationResult(bool success, string message, Guid token)
    { Success = success; Message = message; Token = token; }
}
