using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Immutable expected target state plus derived validation results for one specific repair grid.
/// </summary>
[RegisterComponent]
[Access(typeof(RepairOrderValidationSystem), typeof(RepairStructuralAnalyzerSystem))]
public sealed partial class RepairBlueprintComponent : Component
{
    [ViewVariables]
    public EntityUid Station;

    [ViewVariables]
    public ProtoId<RepairOrderPrototype> OrderPrototype;

    [ViewVariables]
    public readonly Dictionary<Vector2i, List<RepairTask>> TasksByCell = new();

    /// <summary>
    /// Source of truth copied from the target grid. An absent cell means an empty tile with no anchored entities.
    /// </summary>
    [ViewVariables]
    public readonly Dictionary<Vector2i, RepairExpectedCellState> ExpectedCells = new();

    /// <summary>
    /// Initial actual-only state retained solely for the existing score baseline. Current validation is always
    /// derived from ExpectedCells versus the live grid and does not treat this snapshot as accepted target state.
    /// </summary>
    [ViewVariables]
    public readonly Dictionary<Vector2i, RepairUnexpectedCellBaseline> UnexpectedBaselineCells = new();

    /// <summary>
    /// Runtime copy of validated identity rules from the score profile. Used to compare filled/empty
    /// mapping variants as the same repair target without changing the actual entity prototypes.
    /// </summary>
    [ViewVariables]
    public readonly List<RepairEntityIdentityRule> EntityIdentityRules = new();

    /// <summary>
    /// Runtime tile id canonicalization map generated from the score profile.
    /// This lets validation compare equivalent tile definitions without changing analyzer visuals.
    /// </summary>
    [ViewVariables]
    public readonly Dictionary<int, int> TileIdentityIds = new();

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

    [ViewVariables]
    public bool FullyMatchesTarget;
}

public sealed class RepairExpectedCellState
{
    [ViewVariables]
    public RepairExpectedTileState? Tile;

    [ViewVariables]
    public readonly List<RepairExpectedEntityState> Entities = new();
}

public sealed class RepairExpectedTileState
{
    [ViewVariables]
    public int TileId;

    [ViewVariables]
    public string TilePrototype = string.Empty;

    [ViewVariables]
    public int CanonicalTileId;

    [ViewVariables]
    public string CanonicalTilePrototype = string.Empty;

    [ViewVariables]
    public int Points;

    [ViewVariables]
    public bool InitiallyCorrect;
}

public sealed class RepairExpectedEntityState
{
    [ViewVariables]
    public bool Anchored = true;

    [ViewVariables]
    public RepairAnchoredEntitySignature Signature;

    [ViewVariables]
    public Angle DisplayLocalRotation;

    [ViewVariables]
    public int Count;

    [ViewVariables]
    public int InitiallyCorrectCount;

    [ViewVariables]
    public int Points;
}

public sealed class RepairUnexpectedCellBaseline
{
    [ViewVariables]
    public RepairUnexpectedTileBaseline? Tile;

    [ViewVariables]
    public readonly List<RepairUnexpectedEntityBaseline> Entities = new();
}

public sealed class RepairUnexpectedTileBaseline
{
    [ViewVariables]
    public string TilePrototype = string.Empty;

    [ViewVariables]
    public int Points;
}

public sealed class RepairUnexpectedEntityBaseline
{
    [ViewVariables]
    public RepairAnchoredEntitySignature Signature;

    [ViewVariables]
    public Angle DisplayLocalRotation;

    [ViewVariables]
    public int Count;

    [ViewVariables]
    public int Points;
}

/// <summary>
/// Derived presentation and scoring state for one current expected/actual comparison result.
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
    public int ExpectedCanonicalTileId;

    [ViewVariables]
    public string? ExpectedCanonicalTilePrototype;

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

public readonly record struct RepairAnchoredEntitySignature(
    string Prototype,
    Vector2 LocalPosition,
    Angle LocalRotation,
    RepairRotationMode RotationMode);
