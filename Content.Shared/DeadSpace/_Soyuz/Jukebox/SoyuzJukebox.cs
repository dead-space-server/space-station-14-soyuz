// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks
using Robust.Shared.GameStates;
using Robust.Shared.Configuration;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.Jukebox
{
    public static class JukeboxVolume
    {
        public const float MinValue = 0f;
        public const float MaxValue = 1f;
        public const float DefaultValue = 0.85f;
        private const float MinDb = -24f;

        public static float Clamp(float value) => Math.Clamp(value, MinValue, MaxValue);

        public static float ToDb(float value)
        {
            value = Clamp(value);
            return value <= 0.001f ? float.NegativeInfinity : MinDb * (1f - value);
        }
    }
}

namespace Content.Shared.Audio.Jukebox
{
    using Content.Shared.DeadSpace._Soyuz.Jukebox;

    public sealed partial class JukeboxComponent
    {
        [DataField, AutoNetworkedField]
        public float Volume = JukeboxVolume.DefaultValue;

        [DataField, AutoNetworkedField]
        public bool ShuffleEnabled;

        [DataField, AutoNetworkedField]
        public bool RepeatEnabled;
    }

    [Serializable, NetSerializable]
    public sealed class JukeboxSetVolumeMessage(float volume) : BoundUserInterfaceMessage
    {
        public float Volume { get; } = volume;
    }

    [Serializable, NetSerializable]
    public sealed class JukeboxNextMessage : BoundUserInterfaceMessage;

    [Serializable, NetSerializable]
    public sealed class JukeboxPreviousMessage : BoundUserInterfaceMessage;

    [Serializable, NetSerializable]
    public sealed class JukeboxShuffleMessage(bool enabled) : BoundUserInterfaceMessage
    {
        public bool Enabled { get; } = enabled;
    }

    [Serializable, NetSerializable]
    public sealed class JukeboxRepeatMessage(bool enabled) : BoundUserInterfaceMessage
    {
        public bool Enabled { get; } = enabled;
    }
}

namespace Content.Shared.DeadSpace.CCCCVars
{
    public sealed partial class CCCCVars
    {
        public static readonly CVarDef<bool> JukeboxMusicMute =
            CVarDef.Create("jukebox.mute", false, CVar.CLIENTONLY | CVar.ARCHIVE);
    }
}
