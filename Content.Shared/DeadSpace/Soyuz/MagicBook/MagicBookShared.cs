using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Soyuz.MagicBook;

[Serializable, NetSerializable]
public enum MagicBookUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum MagicRuneCategory : byte
{
    Form,
    Modifier,
    Effect
}

[Serializable, NetSerializable]
public enum MagicBookPageState : byte
{
    Empty,
    Editing,
    Saved,
    Broken
}

[Serializable, NetSerializable]
public sealed class MagicBookBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly bool PagesUnlocked;
    public readonly int MaxPages;
    public readonly int ActivePage;
    public readonly MagicBookPageUiState[] Pages;
    public readonly MagicBookRuneUiEntry[] KnownRunes;
    public readonly MagicBookSpellData Preview;

    public MagicBookBoundUserInterfaceState(
        bool pagesUnlocked,
        int maxPages,
        int activePage,
        MagicBookPageUiState[] pages,
        MagicBookRuneUiEntry[] knownRunes,
        MagicBookSpellData preview)
    {
        PagesUnlocked = pagesUnlocked;
        MaxPages = maxPages;
        ActivePage = activePage;
        Pages = pages;
        KnownRunes = knownRunes;
        Preview = preview;
    }
}

[Serializable, NetSerializable]
public sealed class MagicBookPageUiState
{
    public readonly string Id;
    public readonly MagicBookPageState PageState;
    public readonly MagicBookSpellData Spell;

    public MagicBookPageUiState(string id, MagicBookPageState pageState, MagicBookSpellData spell)
    {
        Id = id;
        PageState = pageState;
        Spell = spell;
    }
}

[Serializable, NetSerializable]
public sealed class MagicBookRuneUiEntry
{
    public readonly int Index;
    public readonly string PrototypeId;
    public readonly string Name;
    public readonly MagicRuneCategory Category;
    public readonly string[] Tags;
    public readonly int ManaCost;
    public readonly bool Stackable;
    public readonly int MaxStacks;
    public readonly string EffectHandler;

    public MagicBookRuneUiEntry(
        int index,
        string prototypeId,
        string name,
        MagicRuneCategory category,
        string[] tags,
        int manaCost,
        bool stackable,
        int maxStacks,
        string effectHandler)
    {
        Index = index;
        PrototypeId = prototypeId;
        Name = name;
        Category = category;
        Tags = tags;
        ManaCost = manaCost;
        Stackable = stackable;
        MaxStacks = maxStacks;
        EffectHandler = effectHandler;
    }
}

[Serializable, NetSerializable]
public sealed class MagicBookSelectPageMessage : BoundUserInterfaceMessage
{
    public readonly int Page;

    public MagicBookSelectPageMessage(int page)
    {
        Page = page;
    }
}

[Serializable, NetSerializable]
public sealed class MagicBookInsertPageMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class MagicBookSetRuneSlotMessage : BoundUserInterfaceMessage
{
    public readonly int Slot;
    public readonly int RuneIndex;

    public MagicBookSetRuneSlotMessage(int slot, int runeIndex)
    {
        Slot = slot;
        RuneIndex = runeIndex;
    }
}

[Serializable, NetSerializable]
public sealed class MagicBookClearRuneSlotMessage : BoundUserInterfaceMessage
{
    public readonly int Slot;

    public MagicBookClearRuneSlotMessage(int slot)
    {
        Slot = slot;
    }
}

[Serializable, NetSerializable]
public sealed class MagicBookSaveSpellMessage : BoundUserInterfaceMessage
{
    public readonly string Name;

    public MagicBookSaveSpellMessage(string name)
    {
        Name = name;
    }
}

