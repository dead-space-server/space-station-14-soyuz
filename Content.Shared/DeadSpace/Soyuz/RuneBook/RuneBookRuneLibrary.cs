using System.Linq;

namespace Content.Shared.DeadSpace.Soyuz.RuneBook;

public sealed class RuneBookRuneDefinition
{
    public readonly int Id;
    public readonly RuneBookSegment[] Segments;

    public RuneBookRuneDefinition(int id, RuneBookSegment[] segments)
    {
        Id = id;
        Segments = segments;
    }
}

public static class RuneBookRuneLibrary
{
    public const int GridSize = 16;
    public const int PageCount = 40;
    public const int RuneCount = 84;
    public const int MaxSubmittedSegments = 96;

    private static readonly RuneBookRuneDefinition[] Runes = BuildRunes();

    public static RuneBookRuneDefinition GetRune(int runeId)
    {
        return Runes[Math.Clamp(runeId, 0, RuneCount - 1)];
    }

    public static int[] GetRunesForPage(int page)
    {
        page = Math.Clamp(page, 0, PageCount - 1);
        var count = GetRuneCountForPage(page);
        var start = GetFirstRuneForPage(page);
        var runes = new int[count];

        for (var i = 0; i < count; i++)
            runes[i] = start + i;

        return runes;
    }

    public static bool IsRuneOnPage(int runeId, int page)
    {
        foreach (var pageRune in GetRunesForPage(page))
        {
            if (pageRune == runeId)
                return true;
        }

        return false;
    }

    private static int GetRuneCountForPage(int page)
    {
        return page < 4 ? 3 : 2;
    }

    private static int GetFirstRuneForPage(int page)
    {
        return page < 4 ? page * 3 : 12 + (page - 4) * 2;
    }

    private static RuneBookRuneDefinition[] BuildRunes()
    {
        var runes = new RuneBookRuneDefinition[RuneCount];

        for (var i = 0; i < RuneCount; i++)
            runes[i] = BuildRune(i);

        return runes;
    }

    private static RuneBookRuneDefinition BuildRune(int id)
    {
        var segments = new HashSet<RuneBookSegment>();
        var motif = id % 7;
        var variant = id / 7;

        var top = new Vector2i(7, 1);
        var upper = new Vector2i(7, 3);
        var bottom = new Vector2i(7, 14);
        var lower = new Vector2i(7, 12);
        var left = new Vector2i(1, 7);
        var innerLeft = new Vector2i(3, 7);
        var right = new Vector2i(14, 7);
        var innerRight = new Vector2i(12, 7);
        var tl = new Vector2i(3, 3);
        var tr = new Vector2i(12, 3);
        var bl = new Vector2i(3, 12);
        var br = new Vector2i(12, 12);
        var center = new Vector2i(7, 7);
        var offCenter = new Vector2i(8, 8);

        switch (motif)
        {
            case 0:
                AddPath(segments, top, right, bottom, left, top);
                break;
            case 1:
                AddPath(segments, tl, tr, br, bl, tl);
                break;
            case 2:
                AddPath(segments, top, br, bl, top);
                break;
            case 3:
                AddPath(segments, bottom, tl, tr, bottom);
                break;
            case 4:
                Add(segments, top, bottom);
                Add(segments, left, right);
                Add(segments, tl, br);
                Add(segments, tr, bl);
                break;
            case 5:
                AddPath(segments, tl, center, tr);
                AddPath(segments, bl, center, br);
                Add(segments, top, bottom);
                break;
            default:
                AddPath(segments, bl, top, br);
                AddPath(segments, left, center, right);
                Add(segments, upper, lower);
                break;
        }

        if ((variant & 1) != 0)
        {
            Add(segments, center, top);
            Add(segments, center, bottom);
        }

        if ((variant & 2) != 0)
        {
            Add(segments, center, left);
            Add(segments, center, right);
        }

        if ((variant & 4) != 0)
        {
            Add(segments, innerLeft, tr);
            Add(segments, innerRight, bl);
        }

        if ((variant & 8) != 0)
        {
            Add(segments, tl, innerRight);
            Add(segments, br, innerLeft);
        }

        if ((variant & 16) != 0)
        {
            AddPath(segments, upper, offCenter, lower);
        }

        var spokeA = GetSpoke((id * 3 + variant) % 8);
        var spokeB = GetSpoke((id * 5 + variant + 2) % 8);
        Add(segments, center, spokeA);

        if (spokeA != spokeB)
            Add(segments, spokeA, spokeB);

        return new RuneBookRuneDefinition(id, segments.ToArray());
    }

    private static Vector2i GetSpoke(int index)
    {
        return index switch
        {
            0 => new Vector2i(7, 2),
            1 => new Vector2i(12, 3),
            2 => new Vector2i(13, 7),
            3 => new Vector2i(12, 12),
            4 => new Vector2i(7, 13),
            5 => new Vector2i(3, 12),
            6 => new Vector2i(2, 7),
            _ => new Vector2i(3, 3)
        };
    }

    private static void AddPath(HashSet<RuneBookSegment> segments, params Vector2i[] points)
    {
        for (var i = 0; i < points.Length - 1; i++)
            Add(segments, points[i], points[i + 1]);
    }

    private static void Add(HashSet<RuneBookSegment> segments, Vector2i start, Vector2i end)
    {
        if (!IsValidNode(start) || !IsValidNode(end) || start == end)
            return;

        segments.Add(new RuneBookSegment(start, end));
    }

    private static bool IsValidNode(Vector2i node)
    {
        return node.X >= 0 &&
               node.Y >= 0 &&
               node.X < GridSize &&
               node.Y < GridSize;
    }
}
