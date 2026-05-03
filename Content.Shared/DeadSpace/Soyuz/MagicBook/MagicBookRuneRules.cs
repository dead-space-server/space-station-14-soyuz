using Content.Shared.DeadSpace.Soyuz.RuneBook;

namespace Content.Shared.DeadSpace.Soyuz.MagicBook;

public static class MagicBookRuneRules
{
    public const int FormProjectile = 0;
    public const int FormSelf = 1;
    public const int FormTouch = 2;
    public const int FormUnderFeet = 3;
    public const int FormHomingProjectile = 4;

    public const int ModifierAmplify = 5;
    public const int ModifierRandom = 6;
    public const int ModifierSensitivity = 7;
    public const int ModifierArea = 8;
    public const int ModifierAcceleration = 9;
    public const int ModifierWeaken = 10;
    public const int ModifierSlow = 11;
    public const int ModifierShortenDuration = 12;
    public const int ModifierPierce = 13;
    public const int ModifierSplit = 14;
    public const int ModifierArc = 15;
    public const int ModifierBounce = 16;
    public const int ModifierDelay = 17;
    public const int ModifierDispel = 18;
    public const int ModifierReset = 19;
    public const int ModifierDisorient = 20;
    public const int ModifierMist = 21;
    public const int ModifierOrbit = 22;
    public const int ModifierExtendTime = 23;

    public const int EffectCut = 24;
    public const int EffectDamage = 25;
    public const int EffectIgnite = 26;
    public const int EffectKnockback = 27;
    public const int EffectCrush = 28;
    public const int EffectDischarge = 29;
    public const int EffectPoison = 30;
    public const int EffectExplosion = 31;
    public const int EffectPoisonSpores = 32;
    public const int EffectSpike = 33;
    public const int EffectDestroy = 34;
    public const int EffectCreateWalls = 35;
    public const int EffectCreateLight = 36;
    public const int EffectCreate = 37;
    public const int EffectEvaporate = 38;
    public const int EffectChop = 39;
    public const int EffectGather = 40;
    public const int EffectInteract = 41;
    public const int EffectTemporaryWall = 42;
    public const int EffectWindShift = 43;
    public const int EffectJump = 44;
    public const int EffectPull = 45;
    public const int EffectTeleport = 46;
    public const int EffectSlide = 47;
    public const int EffectRewind = 48;
    public const int EffectThrow = 49;
    public const int EffectLuck = 50;
    public const int EffectHeal = 51;
    public const int EffectSatiate = 52;
    public const int EffectInvisibility = 53;
    public const int EffectManaBubble = 54;
    public const int EffectEthereal = 55;
    public const int EffectLifeLink = 56;
    public const int EffectStorageAccess = 57;
    public const int EffectTrap = 58;
    public const int EffectSummonFauna = 59;
    public const int EffectWololo = 60;
    public const int EffectCharm = 61;
    public const int EffectFirework = 62;
    public const int EffectSignalFlare = 63;
    public const int EffectPhantomGrip = 64;
    public const int EffectSummonDecoy = 65;
    public const int EffectSummonUndead = 66;
    public const int EffectSummonVex = 67;

    public static readonly HashSet<int> ProjectileForms = new()
    {
        FormProjectile,
        FormHomingProjectile
    };

    public static readonly HashSet<int> AreaOrDurationEffects = new()
    {
        EffectIgnite,
        EffectPoison,
        EffectExplosion,
        EffectPoisonSpores,
        EffectCreateWalls,
        EffectCreateLight,
        EffectTemporaryWall,
        EffectWindShift,
        EffectManaBubble,
        EffectInvisibility,
        EffectEthereal,
        EffectLifeLink,
        EffectTrap,
        EffectPhantomGrip,
    };

    public static readonly HashSet<int> DirectCombatEffects = new()
    {
        EffectCut,
        EffectDamage,
        EffectIgnite,
        EffectKnockback,
        EffectCrush,
        EffectDischarge,
        EffectPoison,
        EffectExplosion,
        EffectPoisonSpores,
        EffectSpike,
        EffectDestroy,
        EffectPull,
        EffectThrow,
        EffectPhantomGrip,
    };

    public static readonly HashSet<int> RitualOnlyEffects = new()
    {
        EffectCreateLight,
        EffectCreate,
        EffectStorageAccess,
        EffectSummonVex,
    };

    public static MagicRuneCategory GetCategory(RuneBookRunePrototype rune)
    {
        return rune.Category switch
        {
            1 => MagicRuneCategory.Form,
            2 => MagicRuneCategory.Modifier,
            _ => MagicRuneCategory.Effect
        };
    }

    public static int GetManaCost(RuneBookRunePrototype rune)
    {
        if (rune.ManaCost > 0)
            return rune.ManaCost;

        return GetCategory(rune) switch
        {
            MagicRuneCategory.Form => 8 + rune.Tier * 2,
            MagicRuneCategory.Modifier => rune.Index switch
            {
                ModifierAmplify => 8,
                ModifierArea => 9,
                ModifierSplit => 11,
                ModifierPierce => 10,
                ModifierBounce => 9,
                ModifierWeaken => -3,
                ModifierReset => 1,
                _ => 5 + rune.Tier
            },
            _ => 10 + rune.Tier * 2
        };
    }

    public static bool IsStackable(RuneBookRunePrototype rune)
    {
        if (rune.Stackable)
            return true;

        return rune.Index is ModifierAmplify or ModifierArea or ModifierWeaken;
    }

    public static int GetMaxStacks(RuneBookRunePrototype rune)
    {
        if (rune.MaxStacks > 1)
            return rune.MaxStacks;

        return rune.Index switch
        {
            ModifierAmplify => 4,
            ModifierArea => 4,
            ModifierWeaken => 4,
            _ => 1
        };
    }

    public static string GetEffectHandler(RuneBookRunePrototype rune)
    {
        if (!string.IsNullOrWhiteSpace(rune.EffectHandler))
            return rune.EffectHandler;

        return rune.Index switch
        {
            < 5 => "form",
            < 24 => "modifier",
            _ => $"effect-{rune.Index}"
        };
    }

    public static HashSet<string> GetTags(RuneBookRunePrototype rune)
    {
        var tags = new HashSet<string>(rune.Tags);

        switch (rune.Index)
        {
            case FormProjectile:
            case FormHomingProjectile:
                tags.Add("projectile-form");
                break;
            case FormUnderFeet:
                tags.Add("area-form");
                break;
            case ModifierArea:
            case ModifierMist:
            case ModifierArc:
            case ModifierSplit:
                tags.Add("multi-target");
                break;
        }

        if (AreaOrDurationEffects.Contains(rune.Index))
            tags.Add("area-or-duration");

        if (DirectCombatEffects.Contains(rune.Index))
            tags.Add("direct-combat");

        if (RitualOnlyEffects.Contains(rune.Index))
            tags.Add("ritual");

        return tags;
    }
}

