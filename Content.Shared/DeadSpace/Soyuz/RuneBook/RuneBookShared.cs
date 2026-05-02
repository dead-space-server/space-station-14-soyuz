using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Soyuz.RuneBook;

[Serializable, NetSerializable]
public enum RuneBookUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum RuneBookCheckResult : byte
{
    None,
    Success,
    Failure,
    PageRipped,
    InvalidRune
}

[Serializable, NetSerializable]
public struct RuneBookSegment : IEquatable<RuneBookSegment>
{
    public Vector2i Start;
    public Vector2i End;

    public RuneBookSegment(Vector2i start, Vector2i end)
    {
        if (Compare(start, end) <= 0)
        {
            Start = start;
            End = end;
        }
        else
        {
            Start = end;
            End = start;
        }
    }

    public RuneBookSegment Normalized => new(Start, End);

    public bool IsPoint => Start == End;

    public bool Equals(RuneBookSegment other)
    {
        return Start == other.Start && End == other.End;
    }

    public override bool Equals(object? obj)
    {
        return obj is RuneBookSegment other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Start, End);
    }

    private static int Compare(Vector2i left, Vector2i right)
    {
        var x = left.X.CompareTo(right.X);
        return x != 0 ? x : left.Y.CompareTo(right.Y);
    }
}

[Serializable, NetSerializable]
public sealed class RuneBookBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly int CurrentPage;
    public readonly int PageCount;
    public readonly int RuneCount;
    public readonly int[] RippedPages;
    public readonly int[] VerifiedRunes; // DS14-Soyuz
    public readonly RuneBookCheckResult LastResult;
    public readonly int LastCheckedRune;
    public readonly int LastScore;
    public readonly int LastMissingSegments;
    public readonly int LastExtraSegments;

    public RuneBookBoundUserInterfaceState(
        int currentPage,
        int pageCount,
        int runeCount,
        int[] rippedPages,
        int[] verifiedRunes,
        RuneBookCheckResult lastResult,
        int lastCheckedRune,
        int lastScore,
        int lastMissingSegments,
        int lastExtraSegments)
    {
        CurrentPage = currentPage;
        PageCount = pageCount;
        RuneCount = runeCount;
        RippedPages = rippedPages;
        VerifiedRunes = verifiedRunes;
        LastResult = lastResult;
        LastCheckedRune = lastCheckedRune;
        LastScore = lastScore;
        LastMissingSegments = lastMissingSegments;
        LastExtraSegments = lastExtraSegments;
    }
}

[Serializable, NetSerializable]
public sealed class RuneBookSetPageMessage : BoundUserInterfaceMessage
{
    public readonly int Page;

    public RuneBookSetPageMessage(int page)
    {
        Page = page;
    }
}

[Serializable, NetSerializable]
public sealed class RuneBookCheckMessage : BoundUserInterfaceMessage
{
    public readonly int RuneId;
    public readonly RuneBookSegment[] Segments;

    public RuneBookCheckMessage(int runeId, RuneBookSegment[] segments)
    {
        RuneId = runeId;
        Segments = segments;
    }
}

[Serializable, NetSerializable]
public sealed class RuneBookRipPageMessage : BoundUserInterfaceMessage
{
    public readonly int Page;
    public readonly int RuneId;

    public RuneBookRipPageMessage(int page, int runeId)
    {
        Page = page;
        RuneId = runeId;
    }
}
