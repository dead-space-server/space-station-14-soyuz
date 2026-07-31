using Content.Client.DeadSpace._Soyuz.Overlays;
using Robust.Client.Graphics;
using Robust.Client.Player; // DS14-Soyuz
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Overlays;

public sealed partial class NoirOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "Noir";

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!; // DS14-Soyuz
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;
    private readonly ShaderInstance _noirShader;

    public NoirOverlay()
    {
        IoCManager.InjectDependencies(this);
        _noirShader = _prototypeManager.Index(Shader).InstanceUnique();
        ZIndex = 9; // draw this over the DamageOverlay, RainbowOverlay etc, but before the black and white shader
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
        _noirShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        handle.UseShader(_noirShader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
