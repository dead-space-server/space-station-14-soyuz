using Content.Client.DeadSpace._Soyuz.Overlays;
using Robust.Client.Graphics;
using Robust.Client.Player; // DS14-Soyuz
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Overlays;

public sealed partial class BlackAndWhiteOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "GreyscaleFullscreen";

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!; // DS14-Soyuz
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;
    private readonly ShaderInstance _greyscaleShader;

    public BlackAndWhiteOverlay()
    {
        IoCManager.InjectDependencies(this);
        _greyscaleShader = _prototypeManager.Index(Shader).InstanceUnique();
        ZIndex = 10; // draw this over the DamageOverlay, RainbowOverlay etc.
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args) // DS14-Soyuz
    {
        return SoyuzOverlayViewport.IsPrimary(args, _entityManager, _playerManager);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        _greyscaleShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        handle.UseShader(_greyscaleShader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
