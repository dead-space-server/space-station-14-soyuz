using Robust.Client.Graphics;
using Robust.Shared.Timing;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using System.Numerics;

namespace Content.Client.DeadSpace.Sectants;

public sealed class SectantFlashOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly Color _color;
    private readonly float _duration;
    private readonly IOverlayManager _overlayManager;
    private float _timeLeft;

    public SectantFlashOverlay(Color color, float duration, IOverlayManager overlayManager)
    {
        _color = color;
        _duration = duration;
        _timeLeft = duration;
        _overlayManager = overlayManager;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _timeLeft -= args.DeltaSeconds;
        if (_timeLeft <= 0)
            _overlayManager.RemoveOverlay(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var t = Math.Clamp(_timeLeft / _duration, 0f, 1f);
        var alpha = t * t * 0.6f;

        var handle = args.ScreenHandle;
        handle.SetTransform(Matrix3x2.Identity);

        var vp = args.ViewportBounds;
        handle.DrawRect(vp, _color.WithAlpha(alpha));

        var vignette = _color.WithAlpha(t * 0.85f);
        const float border = 64f;
        handle.DrawRect(new UIBox2(vp.Left, vp.Top, vp.Right, vp.Top + border), vignette);
        handle.DrawRect(new UIBox2(vp.Left, vp.Bottom - border, vp.Right, vp.Bottom), vignette);
        handle.DrawRect(new UIBox2(vp.Left, vp.Top, vp.Left + border, vp.Bottom), vignette);
        handle.DrawRect(new UIBox2(vp.Right - border, vp.Top, vp.Right, vp.Bottom), vignette);
    }
}
