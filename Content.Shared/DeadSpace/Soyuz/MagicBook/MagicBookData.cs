using System.Linq;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Soyuz.MagicBook;

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MagicBookSpellData
{
    public const int RuneSlotCount = 5;

    [DataField]
    public string Id = string.Empty;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public int[] Runes = EmptyRunes();

    [DataField]
    public int FormRune = -1;

    [DataField]
    public int[] Modifiers = Array.Empty<int>();

    [DataField]
    public int[] Effects = Array.Empty<int>();

    [DataField]
    public int ManaCost;

    [DataField]
    public float Stability;

    [DataField]
    public float Risk;

    [DataField]
    public bool IsValid;

    [DataField]
    public string[] ValidationErrors = Array.Empty<string>();

    [DataField]
    public float PowerMultiplier = 1f;

    [DataField]
    public float Radius;

    [DataField]
    public float ProjectileSpeed = 1f;

    [DataField]
    public float CastSpeed = 1f;

    [DataField]
    public int PenetrationCount;

    [DataField]
    public int ProjectileCount = 1;

    [DataField]
    public int ChainTargets;

    [DataField]
    public int BounceCount;

    [DataField]
    public bool DelayedActivation;

    [DataField]
    public bool PersistentArea;

    [DataField]
    public bool OrbitAroundCaster;

    [DataField]
    public bool RandomEffect;

    public static int[] EmptyRunes()
    {
        return Enumerable.Repeat(-1, RuneSlotCount).ToArray();
    }

    public static MagicBookSpellData Empty()
    {
        return new MagicBookSpellData
        {
            Runes = EmptyRunes(),
            ValidationErrors = Array.Empty<string>(),
            Modifiers = Array.Empty<int>(),
            Effects = Array.Empty<int>(),
            ProjectileCount = 1,
            PowerMultiplier = 1f,
            ProjectileSpeed = 1f,
            CastSpeed = 1f,
        };
    }

    public MagicBookSpellData Clone()
    {
        return new MagicBookSpellData
        {
            Id = Id,
            Name = Name,
            Runes = Runes.ToArray(),
            FormRune = FormRune,
            Modifiers = Modifiers.ToArray(),
            Effects = Effects.ToArray(),
            ManaCost = ManaCost,
            Stability = Stability,
            Risk = Risk,
            IsValid = IsValid,
            ValidationErrors = ValidationErrors.ToArray(),
            PowerMultiplier = PowerMultiplier,
            Radius = Radius,
            ProjectileSpeed = ProjectileSpeed,
            CastSpeed = CastSpeed,
            PenetrationCount = PenetrationCount,
            ProjectileCount = ProjectileCount,
            ChainTargets = ChainTargets,
            BounceCount = BounceCount,
            DelayedActivation = DelayedActivation,
            PersistentArea = PersistentArea,
            OrbitAroundCaster = OrbitAroundCaster,
            RandomEffect = RandomEffect,
        };
    }
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MagicBookPageData
{
    [DataField]
    public string Id = Guid.NewGuid().ToString("N");

    [DataField]
    public bool Inserted;

    [DataField]
    public MagicBookSpellData Spell = MagicBookSpellData.Empty();

    [DataField]
    public MagicBookPageState PageState = MagicBookPageState.Empty;

    public MagicBookPageData Clone()
    {
        return new MagicBookPageData
        {
            Id = Id,
            Inserted = Inserted,
            Spell = Spell.Clone(),
            PageState = PageState,
        };
    }
}
