using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;

namespace Content.Client.Kitchen;

public sealed class CookingPromptOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IResourceCache _resources = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new CookingPromptOverlay(EntityManager, _resources, _players));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<CookingPromptOverlay>();
    }
}
