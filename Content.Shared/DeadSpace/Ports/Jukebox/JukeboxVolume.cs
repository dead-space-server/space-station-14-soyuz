using System;

// DS-14 Start: Shared helpers keep client preview volume and server playback volume on
// the same clamp and decibel conversion rules.
namespace Content.Shared.DeadSpace.Ports.Jukebox;

public static class JukeboxVolume
{
    public const float MinValue = 0f;
    public const float MaxValue = 1f;
    public const float DefaultValue = 0.85f;

    public const float MinDb = -24f;
    public const float MaxDb = 0f;

    public static float Clamp(float value)
    {
        return Math.Clamp(value, MinValue, MaxValue);
    }

    public static float ToDb(float value)
    {
        value = Clamp(value);

        if (value <= 0.001f)
            return float.NegativeInfinity;

        return MinDb + (MaxDb - MinDb) * value;
    }
}
// DS-14 End
