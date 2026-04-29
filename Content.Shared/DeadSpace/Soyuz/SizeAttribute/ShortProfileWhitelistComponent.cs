// Must be shared, used by character setup UI
namespace Content.Shared._NF.SizeAttribute;

[RegisterComponent, ComponentProtoName("ShortWhitelist")] // DS14: keep old prototype/component id
public sealed partial class ShortProfileWhitelistComponent : Component
{
    [DataField]
    public float Scale;

    [DataField]
    public float Density;

    [DataField]
    public bool PseudoItem = false;

    [DataField]
    public bool CosmeticOnly = true;

    [DataField]
    public List<Box2i>? Shape;

    [DataField]
    public Vector2i? StoredOffset;

    [DataField]
    public float StoredRotation;
}
