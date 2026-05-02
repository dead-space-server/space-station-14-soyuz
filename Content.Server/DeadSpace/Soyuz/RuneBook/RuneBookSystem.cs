using System.Linq;
using Content.Shared.DeadSpace.Soyuz.RuneBook;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Paper;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace.Soyuz.RuneBook;

public sealed class RuneBookSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RuneBookComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RuneBookComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<RuneBookComponent, RuneBookSetPageMessage>(OnSetPage);
        SubscribeLocalEvent<RuneBookComponent, RuneBookCheckMessage>(OnCheck);
        SubscribeLocalEvent<RuneBookComponent, RuneBookRipPageMessage>(OnRipPage);
    }

    private void OnMapInit(EntityUid uid, RuneBookComponent component, MapInitEvent args)
    {
        EnsureCounts(component);
        component.CurrentPage = Math.Clamp(component.CurrentPage, 0, Math.Max(component.PageCount - 1, 0));
    }

    private void OnUiOpened(EntityUid uid, RuneBookComponent component, BoundUIOpenedEvent args)
    {
        EnsureCounts(component);
        UpdateUi(uid, component);
    }

    private void OnSetPage(EntityUid uid, RuneBookComponent component, RuneBookSetPageMessage args)
    {
        component.CurrentPage = Math.Clamp(args.Page, 0, Math.Max(component.PageCount - 1, 0));
        ResetResult(component);
        UpdateUi(uid, component);
    }

    private void OnCheck(EntityUid uid, RuneBookComponent component, RuneBookCheckMessage args)
    {
        component.LastCheckedRune = args.RuneId;
        component.LastScore = 0;
        component.LastMissingSegments = 0;
        component.LastExtraSegments = 0;

        if (component.RippedPages.Contains(component.CurrentPage))
        {
            component.LastResult = RuneBookCheckResult.PageRipped;
            UpdateUi(uid, component);
            return;
        }

        if (!RuneBookRuneLibrary.IsRuneOnPage(args.RuneId, component.CurrentPage))
        {
            component.LastResult = RuneBookCheckResult.InvalidRune;
            UpdateUi(uid, component);
            return;
        }

        var expected = ExpandSegments(RuneBookRuneLibrary.GetRune(args.RuneId).Segments);
        var submitted = ExpandSegments(args.Segments);

        var missing = 0;
        foreach (var segment in expected)
        {
            if (!submitted.Contains(segment))
                missing++;
        }

        var extra = 0;
        foreach (var segment in submitted)
        {
            if (!expected.Contains(segment))
                extra++;
        }

        var expectedCount = Math.Max(expected.Count, 1);
        var score = 100 - missing * 100 / expectedCount - extra * 60 / expectedCount;
        component.LastScore = Math.Clamp(score, 0, 100);
        component.LastMissingSegments = missing;
        component.LastExtraSegments = extra;
        component.LastResult = missing == 0 && extra == 0
            ? RuneBookCheckResult.Success
            : RuneBookCheckResult.Failure;

        // DS14-Soyuz: remember successfully verified runes for the "Verified" tab.
        if (component.LastResult == RuneBookCheckResult.Success &&
            args.RuneId >= 0 &&
            args.RuneId < component.RuneCount)
        {
            component.VerifiedRunes.Add(args.RuneId);
        }

        UpdateUi(uid, component);
    }

    private void OnRipPage(EntityUid uid, RuneBookComponent component, RuneBookRipPageMessage args)
    {
        var page = Math.Clamp(args.Page, 0, Math.Max(component.PageCount - 1, 0));

        if (component.RippedPages.Contains(page))
        {
            component.LastResult = RuneBookCheckResult.PageRipped;
            UpdateUi(uid, component);
            return;
        }

        component.RippedPages.Add(page);
        SpawnRuneSheet(args.Actor, page, args.RuneId);

        if (component.CurrentPage == page)
            component.LastResult = RuneBookCheckResult.PageRipped;
        else
            ResetResult(component);

        UpdateUi(uid, component);
    }

    private void SpawnRuneSheet(EntityUid actor, int page, int selectedRune)
    {
        if (!TryComp(actor, out HandsComponent? hands))
            return;

        var runeId = selectedRune;
        if (runeId < 0 || !RuneBookRuneLibrary.IsRuneOnPage(runeId, page))
        {
            var pageRunes = RuneBookRuneLibrary.GetRunesForPage(page);
            runeId = pageRunes.Length > 0 ? pageRunes[0] : -1;
        }

        if (runeId < 0 || runeId >= RuneBookRuneLibrary.RuneCount)
            return;

        if (!RuneBookRuneLibrary.TryGetRunePrototypeId(runeId, out var runePrototypeId))
            return;

        var coords = Transform(actor).Coordinates;
        var sheet = Spawn("DS14SoyuzRuneSheet", coords);

        if (TryComp<RuneBookRuneSheetComponent>(sheet, out var runeSheet))
        {
            runeSheet.RunePrototype = runePrototypeId;
            runeSheet.RuneIndex = runeId;
            Dirty(sheet, runeSheet);
        }

        var runeName = $"Rune {runeId + 1}";
        if (_proto.TryIndex<RuneBookRunePrototype>(runePrototypeId, out var runeProto))
            runeName = runeProto.Name;

        if (TryComp<PaperComponent>(sheet, out var paper))
        {
            _meta.SetEntityName(sheet, runeName);
            _paper.SetContent((sheet, paper), string.Empty); // DS14-Soyuz: show rune visually in UI
            Dirty(sheet, paper);
        }

        _hands.TryPickup(actor, sheet, checkActionBlocker: false, handsComp: hands);
    }

    private static HashSet<RuneBookSegment> ExpandSegments(RuneBookSegment[] segments)
    {
        var result = new HashSet<RuneBookSegment>();
        var count = Math.Min(segments.Length, RuneBookRuneLibrary.MaxSubmittedSegments);

        for (var i = 0; i < count; i++)
        {
            var segment = segments[i].Normalized;

            if (!IsValidNode(segment.Start) ||
                !IsValidNode(segment.End) ||
                segment.IsPoint)
            {
                continue;
            }

            AddExpandedSegment(result, segment);
        }

        return result;
    }

    private static void AddExpandedSegment(HashSet<RuneBookSegment> result, RuneBookSegment segment)
    {
        var deltaX = segment.End.X - segment.Start.X;
        var deltaY = segment.End.Y - segment.Start.Y;
        var steps = GreatestCommonDivisor(Math.Abs(deltaX), Math.Abs(deltaY));

        if (steps <= 1)
        {
            result.Add(segment);
            return;
        }

        var step = new Vector2i(deltaX / steps, deltaY / steps);
        var current = segment.Start;

        for (var i = 0; i < steps; i++)
        {
            var next = current + step;
            result.Add(new RuneBookSegment(current, next));
            current = next;
        }
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            var remainder = left % right;
            left = right;
            right = remainder;
        }

        return Math.Max(left, 1);
    }

    private static bool IsValidNode(Vector2i node)
    {
        return node.X >= 0 &&
               node.Y >= 0 &&
               node.X < RuneBookRuneLibrary.GridSize &&
               node.Y < RuneBookRuneLibrary.GridSize;
    }

    private static void ResetResult(RuneBookComponent component)
    {
        component.LastResult = RuneBookCheckResult.None;
        component.LastCheckedRune = -1;
        component.LastScore = 0;
        component.LastMissingSegments = 0;
        component.LastExtraSegments = 0;
    }

    private void UpdateUi(EntityUid uid, RuneBookComponent component)
    {
        EnsureCounts(component);
        component.CurrentPage = Math.Clamp(component.CurrentPage, 0, Math.Max(component.PageCount - 1, 0));

        _ui.SetUiState(uid,
            RuneBookUiKey.Key,
            new RuneBookBoundUserInterfaceState(
                component.CurrentPage,
                component.PageCount,
                component.RuneCount,
                component.RippedPages.OrderBy(page => page).ToArray(),
                component.VerifiedRunes.OrderBy(r => r).ToArray(), // DS14-Soyuz
                component.LastResult,
                component.LastCheckedRune,
                component.LastScore,
                component.LastMissingSegments,
                component.LastExtraSegments));
    }

    private static void EnsureCounts(RuneBookComponent component)
    {
        component.PageCount = RuneBookRuneLibrary.PageCount;
        component.RuneCount = RuneBookRuneLibrary.RuneCount;
    }
}
