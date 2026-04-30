using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Soyuz.RuneBook;

[Prototype("ds14SoyuzRuneBookConfig")]
public sealed partial class RuneBookConfigPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("expectedRuneCount")]
    public int? ExpectedRuneCount { get; private set; }

    [DataField("runesPerPage")]
    public int RunesPerPage { get; private set; } = 2;
}

[Prototype("ds14SoyuzRuneBookRune")]
public sealed partial class RuneBookRunePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField("index", required: true)]
    public int Index { get; private set; }

    [DataField("segments")]
    public List<RuneBookSegmentDef> Segments { get; private set; } = new();
}

[Serializable, DataDefinition]
public sealed partial class RuneBookSegmentDef
{
    [DataField("start", required: true)]
    public RuneBookNodeDef Start { get; private set; }

    [DataField("end", required: true)]
    public RuneBookNodeDef End { get; private set; }

    public RuneBookSegment ToSegment() => new(Start.ToVector(), End.ToVector());
}

[Serializable, DataDefinition]
public readonly partial struct RuneBookNodeDef
{
    [DataField("x", required: true)]
    public int X { get; init; }

    [DataField("y", required: true)]
    public int Y { get; init; }

    public Vector2i ToVector() => new(X, Y);
}

