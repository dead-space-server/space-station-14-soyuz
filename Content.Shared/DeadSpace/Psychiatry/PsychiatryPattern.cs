// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

namespace Content.Shared.DeadSpace.Psychiatry;

public enum PsychiatryRemapPool : byte
{
    Animal,
    Monster,
    Item,
}

public static class PsychiatryPattern
{
    public static bool ShouldRemapMob(int entityHash, int seed, SchizophreniaStage stage, float latent = 0.45f, float simple = 0.65f, float acute = 0.9f)
    {
        _ = latent;
        _ = simple;
        if (stage < SchizophreniaStage.Acute)
            return false;

        var v = (HashCode.Combine(entityHash, seed) & 255) / 255f;
        return v <= acute;
    }

    public static bool ShouldRemapItem(int entityHash, int seed, SchizophreniaStage stage, float latent = 0.35f, float simple = 0.45f, float acute = 0.55f)
    {
        _ = latent;
        _ = simple;
        if (stage < SchizophreniaStage.Acute)
            return false;

        var v = (HashCode.Combine(entityHash, seed, 23) & 255) / 255f;
        return v <= acute;
    }

    public static PsychiatryRemapPool PickPool(SchizophreniaStage stage, int entityHash, int seed, float monsterChance = 200f / 255f)
    {
        if (stage >= SchizophreniaStage.Acute)
        {
            return (HashCode.Combine(entityHash, seed, 9) & 255) / 255f < monsterChance
                ? PsychiatryRemapPool.Monster
                : PsychiatryRemapPool.Animal;
        }

        return PsychiatryRemapPool.Animal;
    }

    public static bool PreferCow(int entityHash, int seed) =>
        (HashCode.Combine(entityHash, seed, 3) & 1) == 0;
}
