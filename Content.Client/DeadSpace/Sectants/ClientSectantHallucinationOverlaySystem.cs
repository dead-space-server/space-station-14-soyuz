using Content.Shared.DeadSpace.Sectants;
using Robust.Client.GameObjects;

namespace Content.Client.DeadSpace.Sectants;

public sealed class ClientSectantHallucinationOverlaySystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    private readonly Dictionary<EntityUid, Color> _originalColors = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<SectantHallucinationAuraComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SectantHallucinationAuraComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(EntityUid uid, SectantHallucinationAuraComponent comp, ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite)) return;
        _originalColors[uid] = sprite.Color;
        _sprite.SetColor((uid, sprite), Color.FromHex("#8899ff").WithAlpha(0.7f));
    }

    private void OnShutdown(EntityUid uid, SectantHallucinationAuraComponent comp, ComponentShutdown args)
    {
        if (!_originalColors.Remove(uid, out var original)) return;
        if (!TryComp<SpriteComponent>(uid, out var sprite)) return;
        _sprite.SetColor((uid, sprite), original);
    }
}
