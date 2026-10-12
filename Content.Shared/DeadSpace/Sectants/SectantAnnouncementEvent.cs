using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Sectants;

[Serializable, NetSerializable]
public sealed class SectantAnnouncementEvent : EntityEventArgs
{
    public string Message = string.Empty;
    public Color Color = Color.Red;
    public float FlashDuration = 2.5f;
    public float ShakeIntensity = 6f;
    public float VignetteDuration = 5f;
    public float TextDuration = 4f;
    public bool Whisper = true;
    public float WhisperDuration = 6f;
}
