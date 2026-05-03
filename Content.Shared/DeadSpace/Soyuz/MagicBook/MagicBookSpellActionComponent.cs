namespace Content.Shared.DeadSpace.Soyuz.MagicBook;

[RegisterComponent]
public sealed partial class MagicBookSpellActionComponent : Component
{
    [DataField]
    public MagicBookSpellData Spell = MagicBookSpellData.Empty();
}

