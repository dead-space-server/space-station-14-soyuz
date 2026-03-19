using Robust.Shared.Utility;

namespace Content.Client.MediaWiki;

[RegisterComponent]
[Access(typeof(MediaWikiBookSystem))]
public sealed partial class MediaWikiBookComponent : Component
{
    [DataField(required: true)]
    public string Title = string.Empty;

    [DataField]
    public string Page = string.Empty;

    [DataField(required: true)]
    public ResPath Text = default!;
}
