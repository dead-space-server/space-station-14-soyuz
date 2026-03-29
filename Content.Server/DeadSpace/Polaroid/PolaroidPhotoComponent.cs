namespace Content.Server.DeadSpace.Polaroid;

[RegisterComponent]
public sealed partial class PolaroidPhotoComponent : Component
{
    public const int MaxSignatureLength = 48;

    [ViewVariables]
    public byte[] PngData = Array.Empty<byte>();

    [ViewVariables]
    public string? Photographer;

    [ViewVariables]
    public DateTime? TakenAt;

    [ViewVariables]
    public string? Signature;
}
