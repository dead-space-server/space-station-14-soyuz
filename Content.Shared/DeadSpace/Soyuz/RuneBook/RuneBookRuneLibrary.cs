using System.Linq;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;

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
    public const int MaxSubmittedSegments = 96;

    private const string ConfigId = "DS14SoyuzRuneBookConfig";

    private static readonly object InitLock = new();
    private static bool _initialized;
    private static RuneBookRuneDefinition[] _runes = Array.Empty<RuneBookRuneDefinition>();
    private static string[] _runePrototypeIds = Array.Empty<string>();
    private static int _runesPerPage = 2;
    private static ISawmill? _sawmill;

    public static int RuneCount
    {
        get
        {
            EnsureInitialized();
            return _runes.Length;
        }
    }

    public static int PageCount
    {
        get
        {
            EnsureInitialized();
            var count = RuneCount;
            if (count <= 0)
                return 1;

            var perPage = Math.Max(_runesPerPage, 1);
            return Math.Max(1, (count + perPage - 1) / perPage);
        }
    }

    public static RuneBookRuneDefinition GetRune(int runeId)
    {
        EnsureInitialized();

        if (_runes.Length == 0)
            return new RuneBookRuneDefinition(0, Array.Empty<RuneBookSegment>());

        return _runes[Math.Clamp(runeId, 0, _runes.Length - 1)];
    }

    public static bool TryGetRunePrototypeId(int runeId, out string prototypeId)
    {
        EnsureInitialized();
        prototypeId = string.Empty;

        if (runeId < 0 || runeId >= _runePrototypeIds.Length)
            return false;

        prototypeId = _runePrototypeIds[runeId];
        return !string.IsNullOrWhiteSpace(prototypeId);
    }

    public static int[] GetRunesForPage(int page)
    {
        EnsureInitialized();

        if (_runes.Length == 0)
            return Array.Empty<int>();

        var perPage = Math.Max(_runesPerPage, 1);
        page = Math.Clamp(page, 0, PageCount - 1);
        var start = page * perPage;
        var count = Math.Clamp(_runes.Length - start, 0, perPage);
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

    private static void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (InitLock)
        {
            if (_initialized)
                return;

            var deps = IoCManager.Instance;
            if (deps == null)
            {
                _runes = Array.Empty<RuneBookRuneDefinition>();
                _initialized = true;
                return;
            }

            if (_sawmill == null &&
                deps.TryResolveType(typeof(ILogManager), out var logObj) &&
                logObj is ILogManager logMan)
            {
                _sawmill = logMan.GetSawmill("rune-book");
            }

            if (!deps.TryResolveType(typeof(IPrototypeManager), out var protoObj) ||
                protoObj is not IPrototypeManager proto)
            {
                _sawmill?.Error("Failed to resolve IPrototypeManager; rune library will be empty.");
                _runes = Array.Empty<RuneBookRuneDefinition>();
                _initialized = true;
                return;
            }

            RuneBookConfigPrototype? config = null;
            if (proto.TryIndex<RuneBookConfigPrototype>(ConfigId, out var indexed))
                config = indexed;

            _runesPerPage = Math.Max(config?.RunesPerPage ?? 2, 1);

            var runeProtos = proto.EnumeratePrototypes<RuneBookRunePrototype>()
                .OrderBy(p => p.Index)
                .ToArray();

            var inferredCount = runeProtos.Length > 0 ? runeProtos.Max(p => p.Index) + 1 : 0;
            var runeCount = config?.ExpectedRuneCount ?? inferredCount;

            if (runeCount < 0)
                runeCount = 0;

            var runes = new RuneBookRuneDefinition[Math.Max(runeCount, 0)];
            var runeIds = new string[runes.Length];
            var seen = new bool[runes.Length];

            foreach (var rune in runeProtos)
            {
                if (rune.Index < 0 || rune.Index >= runes.Length)
                {
                    _sawmill?.Warning($"Rune prototype '{rune.ID}' has out-of-range index {rune.Index} (0..{Math.Max(runes.Length - 1, 0)}).");
                    continue;
                }

                var segments = new HashSet<RuneBookSegment>();
                foreach (var segmentDef in rune.Segments)
                {
                    var seg = segmentDef.ToSegment().Normalized;
                    if (!IsValidNode(seg.Start) || !IsValidNode(seg.End) || seg.IsPoint)
                        continue;

                    segments.Add(seg);
                }

                runes[rune.Index] = new RuneBookRuneDefinition(rune.Index, segments.ToArray());
                runeIds[rune.Index] = rune.ID;
                seen[rune.Index] = true;
            }

            for (var i = 0; i < runes.Length; i++)
            {
                if (seen[i])
                    continue;

                _sawmill?.Error($"Missing rune definition for index {i}. Add a 'ds14SoyuzRuneBookRune' prototype with index={i}.");
                runes[i] = new RuneBookRuneDefinition(i, Array.Empty<RuneBookSegment>());
            }

            if (config?.ExpectedRuneCount != null && runeProtos.Length != config.ExpectedRuneCount.Value)
            {
                _sawmill?.Warning(
                    $"Rune pool size mismatch: expected {config.ExpectedRuneCount.Value} prototypes, found {runeProtos.Length}. " +
                    "The book will still use expectedRuneCount for indexing.");
            }

            _runes = runes;
            _runePrototypeIds = runeIds;
            _initialized = true;
        }
    }

    private static bool IsValidNode(Vector2i node)
    {
        return node.X >= 0 &&
               node.Y >= 0 &&
               node.X < GridSize &&
               node.Y < GridSize;
    }
}
