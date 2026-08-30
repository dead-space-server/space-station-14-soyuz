using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Fixed repair requirements generated from the complete target reference for one specific grid.
/// </summary>
[RegisterComponent]
[Access(typeof(RepairOrderValidationSystem))]
public sealed partial class RepairBlueprintComponent : Component
{
    [ViewVariables]
    public EntityUid Station;

    [ViewVariables]
    public ProtoId<RepairOrderPrototype> OrderPrototype;

    [ViewVariables]
    public readonly Dictionary<Vector2i, List<RepairTask>> TasksByCell = new();

    /// <summary>
    /// Every anchored signature accepted by the target, including entities which were already present in the damaged grid.
    /// This lets validation distinguish a legitimate co-located target entity from a wrong replacement.
    /// </summary>
    [ViewVariables]
    public readonly HashSet<RepairTargetEntitySignature> TargetEntitySignatures = new();

    /// <summary>
    /// Runtime copy of validated identity rules from the score profile. Used to compare filled/empty
    /// mapping variants as the same repair target without changing the actual entity prototypes.
    /// </summary>
    [ViewVariables]
    public readonly List<RepairEntityIdentityRule> EntityIdentityRules = new();

    [ViewVariables]
    public int TotalTasks;

    [ViewVariables]
    public int CompletedTasks;

    [ViewVariables]
    public int MaxPoints;

    [ViewVariables]
    public int CurrentPoints;

    [ViewVariables]
    public bool Ready;

    /// <summary>
    /// True after the damaged grid's initial state has been captured. Initially correct requirements become
    /// preservation tasks: they award no points while intact and subtract their full value while broken.
    /// </summary>
    [ViewVariables]
    public bool BaselineInitialized;
}

/// <summary>
/// One immutable target requirement and its mutable validation state.
/// </summary>
public sealed class RepairTask
{
    [ViewVariables]
    public RepairTaskType Type;

    [ViewVariables]
    public Vector2i Cell;

    [ViewVariables]
    public int ExpectedTileId;

    [ViewVariables]
    public string? ExpectedTilePrototype;

    [ViewVariables]
    public string? ExpectedEntityPrototype;

    [ViewVariables]
    public Vector2 ExpectedLocalPosition;

    [ViewVariables]
    public Angle ExpectedLocalRotation;

    /// <summary>
    /// Original mapped rotation used only for analyzer ghost rendering. Validation uses ExpectedLocalRotation
    /// after applying RotationMode, so objects with RotationMode.None still ignore rotation for scoring.
    /// </summary>
    [ViewVariables]
    public Angle DisplayLocalRotation;

    /// <summary>
    /// Determines whether rotation is ignored, exact, or compared only by horizontal/vertical axis.
    /// </summary>
    [ViewVariables]
    public RepairRotationMode RotationMode;

    /// <summary>
    /// Distinguishes identical target entities if a map contains more than one at the same transform.
    /// For removal tasks this is the live count at which the extra entity is still present.
    /// </summary>
    [ViewVariables]
    public int RequiredMatchingCount = 1;

    [ViewVariables]
    public int Points;

    /// <summary>
    /// Whether this requirement was correct when the damaged grid first entered the playable map.
    /// Such tasks protect existing construction instead of awarding repair points.
    /// </summary>
    [ViewVariables]
    public bool InitiallyCorrect;

    [ViewVariables]
    public RepairTaskState State;
}

public readonly record struct RepairTargetEntitySignature(
    string Prototype,
    Vector2 LocalPosition,
    Angle LocalRotation,
    RepairRotationMode RotationMode);
