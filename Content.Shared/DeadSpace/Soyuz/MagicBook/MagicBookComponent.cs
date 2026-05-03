namespace Content.Shared.DeadSpace.Soyuz.MagicBook;

[RegisterComponent]
public sealed partial class MagicBookComponent : Component
{
    [DataField]
    public bool PagesUnlocked;

    [DataField]
    public List<MagicBookPageData> Pages = new();

    [DataField]
    public int MaxPages = 64;

    [DataField]
    public int ActivePage = -1;

    [DataField]
    public HashSet<int> KnownRunes = new();

    [DataField]
    public List<MagicBookSpellData> SavedSpells = new();

    [DataField]
    public bool KnowAllRunes = true;

    [DataField]
    public string SpellActionPrototype = "DS14SoyuzMagicBookSpellAction";
}

