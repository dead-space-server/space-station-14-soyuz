using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Corvax.TTS;

[Serializable, NetSerializable]
// ReSharper disable once InconsistentNaming
public sealed class PlayTTSEvent : EntityEventArgs
{
    public byte[] Data { get; }
    public NetEntity? SourceUid { get; }
    public bool IsWhisper { get; }
    public bool IsRadio { get; }
    public bool IsLexiconSound { get; } // DS14-Language
    public string LanguageId { get; } // DS14-Language
    // DS14-PoliticalLoudspeaker-start: sync TTS playback modifiers to clients
    public float VolumeMultiplier { get; }
    public float DistanceMultiplier { get; }
    // DS14-PoliticalLoudspeaker-end

    public PlayTTSEvent(
        byte[] data,
        NetEntity? sourceUid = null,
        bool isWhisper = false,
        bool isRadio = false,
        bool isSoundLexicon = false,
        string languageId = "",
        float volumeMultiplier = 1f, // DS14-PoliticalLoudspeaker
        float distanceMultiplier = 1f) // DS14-PoliticalLoudspeaker
    {
        Data = data;
        SourceUid = sourceUid;
        IsWhisper = isWhisper;
        IsRadio = isRadio;
        IsLexiconSound = isSoundLexicon; // DS14-Language
        LanguageId = languageId; // DS14-Language
        // DS14-PoliticalLoudspeaker-start
        VolumeMultiplier = volumeMultiplier;
        DistanceMultiplier = distanceMultiplier;
        // DS14-PoliticalLoudspeaker-end
    }
}
