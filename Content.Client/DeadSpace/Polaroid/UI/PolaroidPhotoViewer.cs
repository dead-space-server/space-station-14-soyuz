// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.Client.DeadSpace.Polaroid.UI;

public sealed class PolaroidPhotoViewer : Control
{
    private const float MinZoom = 1f;
    private const float MaxZoom = 8f;
    private const float ZoomStep = 0.15f;

    private Texture? _texture;
    private float _zoom = MinZoom;
    private Vector2 _panOffset = Vector2.Zero;
    private bool _dragging;

    public PolaroidPhotoViewer()
    {
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    public void SetTexture(Texture? texture, bool resetView = true)
    {
        _texture = texture;

        if (resetView)
        {
            _zoom = MinZoom;
            _panOffset = Vector2.Zero;
        }

        ClampPan();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_texture == null)
            return;

        handle.DrawTextureRect(_texture, GetDrawRect());
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (_texture == null || args.Delta.Y == 0f)
            return;

        var oldRect = GetDrawRect();
        if (oldRect.Width <= 0f || oldRect.Height <= 0f)
            return;

        var anchor = oldRect.Contains(args.RelativePosition) ? args.RelativePosition : oldRect.Center;
        var imagePosition = (anchor - oldRect.TopLeft) / oldRect.Size;
        var oldZoom = _zoom;

        _zoom = Math.Clamp(_zoom * MathF.Pow(1f + ZoomStep, args.Delta.Y), MinZoom, MaxZoom);

        if (MathHelper.CloseToPercent(_zoom, oldZoom))
            return;

        var newSize = GetDisplaySize();
        var centeredTopLeft = (PixelSize - newSize) / 2f;
        var newTopLeft = anchor - imagePosition * newSize;
        _panOffset = newTopLeft - centeredTopLeft;
        ClampPan();

        args.Handle();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick || !CanPan())
            return;

        _dragging = true;
        DefaultCursorShape = CursorShape.Move;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _dragging = false;
        UpdateCursor();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (!_dragging)
            return;

        _panOffset += args.Relative;
        ClampPan();
        args.Handle();
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();
        UpdateCursor();
    }

    protected override void MouseExited()
    {
        base.MouseExited();

        if (!_dragging)
            DefaultCursorShape = CursorShape.Arrow;
    }

    protected override void Resized()
    {
        base.Resized();
        ClampPan();
    }

    private UIBox2 GetDrawRect()
    {
        var size = GetDisplaySize();
        var position = (PixelSize - size) / 2f + _panOffset;
        return UIBox2.FromDimensions(position, size);
    }

    private Vector2 GetDisplaySize()
    {
        if (_texture == null || PixelSize.X <= 0f || PixelSize.Y <= 0f)
            return Vector2.Zero;

        var textureSize = (Vector2) _texture.Size;

        if (textureSize.X <= 0f || textureSize.Y <= 0f)
            return Vector2.Zero;

        var fitScale = Math.Min(PixelSize.X / textureSize.X, PixelSize.Y / textureSize.Y);
        return textureSize * fitScale * _zoom;
    }

    private bool CanPan()
    {
        var size = GetDisplaySize();
        return size.X > PixelSize.X || size.Y > PixelSize.Y;
    }

    private void ClampPan()
    {
        var size = GetDisplaySize();
        var overflow = size - PixelSize;

        _panOffset = new Vector2(
            overflow.X > 0f ? Math.Clamp(_panOffset.X, -overflow.X / 2f, overflow.X / 2f) : 0f,
            overflow.Y > 0f ? Math.Clamp(_panOffset.Y, -overflow.Y / 2f, overflow.Y / 2f) : 0f);

        UpdateCursor();
    }

    private void UpdateCursor()
    {
        DefaultCursorShape = CanPan() ? CursorShape.Move : CursorShape.Arrow;
    }
}
