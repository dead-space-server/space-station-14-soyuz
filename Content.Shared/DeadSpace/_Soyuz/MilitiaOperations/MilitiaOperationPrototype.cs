// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;
using Robust.Shared.Utility;

namespace Content.Shared.DeadSpace._Soyuz.MilitiaOperations;

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
