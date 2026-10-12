using Robust.Client.Graphics;
using Robust.Shared.Timing;
using Robust.Shared.Enums;

namespace Content.Client.DeadSpace.Sectants;

/// <summary>
/// Ползущая тьма по краям экрана + пульсирующий тёмно-красный фильтр.
/// Не мешает игре: центр остаётся прозрачным.
/// </summary>
public sealed class SectantVignetteOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly Color _color;
    private readonly float _duration;
    private readonly IOverlayManager _overlayManager;
    private float _timeLeft;
    private float _elapsed;

    public SectantVignetteOverlay(Color color, float duration, IOverlayManager overlayManager)
    {
        _color = color;
        _duration = duration;
        _timeLeft = duration;
        _overlayManager = overlayManager;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _elapsed += args.DeltaSeconds;
        _timeLeft -= args.DeltaSeconds;
        if (_timeLeft <= 0)
            _overlayManager.RemoveOverlay(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Кривая появления/затухания: быстро набегает, медленно уходит
        var t = Math.Clamp(_timeLeft / _duration, 0f, 1f);
        var strength = t * t * (3f - 2f * t); // smoothstep
        if (strength < 0.01f) return;

        var handle = args.ScreenHandle;
        var vp = args.ViewportBounds;

        // Пульсация — как биение сердца
        var pulse = 0.7f + 0.3f * MathF.Sin(_elapsed * 3.5f);
        var alpha = strength * pulse;

        // Верхняя и нижняя кромки — толще, чем боковые (эффект "объятия")
        const float thick = 140f;
        const float side = 90f;

        var top = _color.WithAlpha(alpha * 0.85f);
        var mid = _color.WithAlpha(alpha * 0.35f);
        var sideCol = _color.WithAlpha(alpha * 0.65f);

        // Верх: три слоя для мягкого градиента
        handle.DrawRect(new UIBox2(vp.Left, vp.Top, vp.Right, vp.Top + thick), top);
        handle.DrawRect(new UIBox2(vp.Left, vp.Top + thick, vp.Right, vp.Top + thick * 1.8f), mid);
        // Низ
        handle.DrawRect(new UIBox2(vp.Left, vp.Bottom - thick, vp.Right, vp.Bottom), top);
        handle.DrawRect(new UIBox2(vp.Left, vp.Bottom - thick * 1.8f, vp.Right, vp.Bottom - thick), mid);
        // Боковые
        handle.DrawRect(new UIBox2(vp.Left, vp.Top, vp.Left + side, vp.Bottom), sideCol);
        handle.DrawRect(new UIBox2(vp.Right - side, vp.Top, vp.Right, vp.Bottom), sideCol);
        handle.DrawRect(new UIBox2(vp.Left + side, vp.Top, vp.Left + side * 1.7f, vp.Bottom), mid);
        handle.DrawRect(new UIBox2(vp.Right - side * 1.7f, vp.Top, vp.Right - side, vp.Bottom), mid);

        // Общий лёгкий фильтр на весь экран
        handle.DrawRect(vp, _color.WithAlpha(alpha * 0.08f));
    }
}
