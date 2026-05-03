using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Soyuz.RitualAltar;

[Serializable, NetSerializable]
public sealed class RitualEffectMessage : EntityEventArgs
{
    public NetEntity Altar;
    public float Radius;
    public float MaxDarkness;
    public float DurationSeconds;
    public string RunePrototype = "RitualRuneBookRuneEffect";

    public RitualEffectMessage(NetEntity altar, float radius, float maxDarkness, float durationSeconds, string runePrototype = "RitualRuneBookRuneEffect")
    {
        Altar = altar;
        Radius = radius;
        MaxDarkness = maxDarkness;
        DurationSeconds = durationSeconds;
        RunePrototype = runePrototype;
    }
}
