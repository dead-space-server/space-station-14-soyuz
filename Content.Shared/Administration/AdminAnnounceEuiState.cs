using Content.Shared.DeadSpace.Languages.Prototypes; // DS14
using Content.Shared.Eui; // DS14
using Robust.Shared.Prototypes; // DS14
using Robust.Shared.Serialization; // DS14

namespace Content.Shared.Administration
{
    public enum AdminAnnounceType
    {
        Station,
        Server,
    }

    [Serializable, NetSerializable]
    public sealed class AdminAnnounceEuiState : EuiStateBase
    {
    }

    public static class AdminAnnounceEuiMsg
    {
        [Serializable, NetSerializable]
        public sealed class DoAnnounce : EuiMessageBase
        {
            public bool CloseAfter;
            public string Announcer = default!;
            public string Announcement = default!;
            public AdminAnnounceType AnnounceType;
            public ProtoId<LanguagePrototype> LanguageId = default!; // DS14-Languages
            public string Voice = default!; // Corvax-TTS
            public bool EnableTTS = default!; // Corvax-TTS
            public bool CustomTTS = default!; // Corvax-TTS
            public string ColorHex = "b84444"; // DS14-value: b64444 -> b84444
            public string SoundPath = "/Audio/_DeadSpace/_Soyuz/Announcements/centcomm.ogg"; // DS14-announce-audio
            public float SoundVolume = 5f; // DS14-announce-volume
            public string Sender = ""; // DS14-announce-sender
        }
    }
}
