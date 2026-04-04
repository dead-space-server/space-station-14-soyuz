// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT
using Content.Shared.DeadSpace.Polaroid;

namespace Content.Server.DeadSpace.Polaroid;

[RegisterComponent]
public sealed partial class PolaroidPhotoComponent : Component
{
    public const int MaxSignatureLength = PolaroidSharedConstants.MaxPhotoSignatureLength;

    [ViewVariables]
    public byte[] PngData = Array.Empty<byte>();

    [ViewVariables]
    public string? Photographer;

    [ViewVariables]
    public DateTime? TakenAt;

    [ViewVariables]
    public string? Signature;
}
