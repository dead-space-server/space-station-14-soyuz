namespace Content.Shared.DeadSpace.Soyuz.RuneBook;

[RegisterComponent]
public sealed partial class RuneBookComponent : Component
{
    [DataField]
    public int CurrentPage;

    [DataField]
    public int PageCount = RuneBookRuneLibrary.PageCount;

    [DataField]
    public int RuneCount = RuneBookRuneLibrary.RuneCount;

    [DataField]
    public HashSet<int> RippedPages = new();

    public RuneBookCheckResult LastResult = RuneBookCheckResult.None;
    public int LastCheckedRune = -1;
    public int LastScore;
    public int LastMissingSegments;
    public int LastExtraSegments;
}
