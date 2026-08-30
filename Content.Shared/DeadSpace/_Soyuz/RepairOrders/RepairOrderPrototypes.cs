using Content.Shared.Maps;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Describes a repair job and the damaged/reference grids associated with it.
/// </summary>
[Prototype]
public sealed partial class RepairOrderPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name = string.Empty;

    [DataField(required: true)]
    public LocId Description = string.Empty;

    [DataField]
    public int Difficulty = 1;

    [DataField]
    public float Weight = 1f;

    /// <summary>
    /// Reference grid loaded only on a temporary paused map while building a repair blueprint.
    /// </summary>
    [DataField(required: true)]
    public ResPath TargetGridPath;

    [DataField(required: true)]
    public ResPath DamagedGridPath;

    [DataField(required: true)]
    public ProtoId<RepairScoreProfilePrototype> ScoreProfile;

    [DataField(required: true)]
    public ProtoId<RepairRewardPoolPrototype> RewardPool;
}

/// <summary>
/// Data-driven point values and rotation requirements for repair blueprint requirements.
/// </summary>
[Prototype]
public sealed partial class RepairScoreProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Point value used by every non-empty target tile without an exact override.
    /// </summary>
    [DataField]
    public int DefaultTilePoints;

    /// <summary>
    /// Point value used by every anchored target entity which did not match an exact value or rule.
    /// </summary>
    [DataField]
    public int DefaultEntityPoints;

    /// <summary>
    /// Optional exact overrides. These take precedence over entity rules and defaults.
    /// </summary>
    [DataField]
    public List<RepairScoreValue> Values = new();

    /// <summary>
    /// Ordered entity scoring rules. The first matching rule is used.
    /// </summary>
    [DataField]
    public List<RepairScoreRule> Rules = new();

    /// <summary>
    /// Ordered entity identity rules. Matching prototypes are compared as the configured canonical prototype.
    /// This is used for map-only variants such as filled/empty power machines.
    /// </summary>
    [DataField]
    public List<RepairEntityIdentityRule> IdentityRules = new();

    /// <summary>
    /// Ordered entity rotation rules. The first matching rule is used.
    /// Entities which match no rule ignore rotation.
    /// </summary>
    [DataField]
    public List<RepairRotationRule> RotationRules = new();
}

[DataDefinition]
public sealed partial class RepairScoreValue
{
    /// <summary>
    /// Exactly one of Tile or Entity must be configured.
    /// </summary>
    [DataField]
    public ProtoId<ContentTileDefinition>? Tile;

    [DataField]
    public EntProtoId? Entity;

    [DataField(required: true)]
    public int Points;
}

/// <summary>
/// Selects entity prototypes without requiring every derived prototype to be listed explicitly.
/// Non-empty selector groups are ANDed; entries inside a group are ORed, except AllTags and
/// AllComponents which require every listed value.
/// </summary>
[DataDefinition]
public sealed partial class RepairEntitySelector
{
    /// <summary>
    /// Exact entity prototypes accepted by this selector.
    /// </summary>
    [DataField]
    public List<EntProtoId> Entities = new();

    /// <summary>
    /// Entity prototype families accepted by this selector. The family root itself also matches.
    /// </summary>
    [DataField]
    public List<EntProtoId> Parents = new();

    [DataField]
    public List<ProtoId<TagPrototype>> AllTags = new();

    /// <summary>
    /// YAML component names which must all be present on the fully inherited entity prototype.
    /// </summary>
    [DataField]
    public List<string> AllComponents = new();
}

[DataDefinition]
public sealed partial class RepairScoreRule
{
    [DataField(required: true)]
    public RepairEntitySelector Selector = new();

    [DataField(required: true)]
    public int Points;
}

[DataDefinition]
public sealed partial class RepairEntityIdentityRule
{
    [DataField(required: true)]
    public RepairEntitySelector Selector = new();

    [DataField(required: true)]
    public EntProtoId Canonical;
}

[DataDefinition]
public sealed partial class RepairRotationRule
{
    [DataField(required: true)]
    public RepairEntitySelector Selector = new();

    [DataField(required: true)]
    public RepairRotationMode Mode;
}

/// <summary>
/// Determines how an entity's local rotation is compared with the target blueprint.
/// </summary>
public enum RepairRotationMode : byte
{
    None,

    /// <summary>
    /// All four cardinal rotations are distinct.
    /// </summary>
    Exact,

    /// <summary>
    /// Opposite directions are equivalent. Used for straight pipes: horizontal and vertical matter,
    /// but rotating a straight pipe by 180 degrees does not.
    /// </summary>
    Axis,
}

/// <summary>
/// One data-driven reward candidate used both for calculation and physical delivery.
/// </summary>
[Prototype]
public sealed partial class RepairRewardPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public EntProtoId Entity;

    [DataField(required: true)]
    public int Cost;

    [DataField]
    public float Weight = 1f;

    [DataField]
    public int MaxCount = 1;

    [DataField]
    public int MinimumDifficulty = 1;
}

/// <summary>
/// Reusable set of reward candidates available to one or more repair orders.
/// </summary>
[Prototype]
public sealed partial class RepairRewardPoolPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Physical container spawned beside the console that submits a completed order.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId DeliveryContainer;

    [DataField(required: true)]
    public List<ProtoId<RepairRewardPrototype>> Rewards = new();
}
