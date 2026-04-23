// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT
using System.IO;
using System.Numerics;
using Content.Client.Eye;
using Content.Client.Viewport;
using Content.Shared.DeadSpace.Polaroid;
using Robust.Client.Graphics;
using Robust.Client.Input; // DS14
using Robust.Client.Timing;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input; // DS14
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using SixLabors.ImageSharp;

namespace Content.Client.DeadSpace.Polaroid.UI;

public sealed class PolaroidCameraWindow : DefaultWindow
{
    private const float MinPreviewZoom = 0.25f;
    private const float MaxPreviewZoom = 1f;
    private const float PreviewZoomStep = 0.15f;
    private const float DefaultPreviewZoom = (MinPreviewZoom + MaxPreviewZoom) / 2f;
    private const float PreviewPanStrength = 0.35f; // DS14

    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IInputManager _inputManager = default!; // DS14
    [Dependency] private readonly IClientGameTiming _timing = default!;

    private readonly EyeLerpingSystem _eyeLerping = default!;
    private readonly FixedEye _defaultEye = new();
    private PolaroidCameraUiState? _nextState;
    private EntityUid? _currentCamera;
    private bool _disposed;
    private bool _previewPanActive; // DS14
    private float _previewZoom = DefaultPreviewZoom;
    private Vector2 _previewPan = Vector2.Zero; // DS14

    private readonly ScalingViewport _cameraView;
    private readonly Label _chargesLabel;
    private readonly Button _captureButton;
    private readonly Button _printLastButton;

    public event Action<byte[]>? CaptureReady;
    public event Action? PrintLastPressed;

    public PolaroidCameraWindow()
    {
        IoCManager.InjectDependencies(this);
        _eyeLerping = _entManager.System<EyeLerpingSystem>();

        Title = Loc.GetString("polaroid-camera-ui-title");
        MinSize = new Vector2(520f, 420f); // DS14-value: new Vector2(320f, 420f) -> new Vector2(520f, 420f)

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
        };

        Contents.AddChild(root);

        // DS14-start: split preview and controls help into a horizontal layout
        var contentRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        root.AddChild(contentRow);
        // DS14-end

        var viewportFrame = new PanelContainer
        {
            MinSize = new Vector2(256f, 256f),
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Robust.Shared.Maths.Color.FromHex("#151515"),
                BorderColor = Robust.Shared.Maths.Color.FromHex("#d6d0c8"),
                BorderThickness = new Thickness(3),
                ContentMarginLeftOverride = 6,
                ContentMarginTopOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginBottomOverride = 6,
            }
        };

        contentRow.AddChild(viewportFrame);

        _cameraView = new ScalingViewport
        {
            Eye = _defaultEye,
            ViewportSize = new Vector2i(160, 160),
            HorizontalExpand = true,
            VerticalExpand = true,
            MouseFilter = MouseFilterMode.Stop, // DS14
        };

        // DS14-start: draggable preview framing
        _cameraView.OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            _previewPanActive = true;
            UpdatePreviewPan(args.RelativePosition);
            args.Handle();
        };

        _cameraView.OnKeyBindUp += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            _previewPanActive = false;
            args.Handle();
        };

        _cameraView.OnMouseExited += _ =>
        {
            _previewPanActive = false;
        };
        // DS14-end

        viewportFrame.AddChild(_cameraView);

        // DS14-start: preview controls help panel
        var helpPanel = new PanelContainer
        {
            MinSize = new Vector2(180f, 256f),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Robust.Shared.Maths.Color.FromHex("#20212c"),
                BorderColor = Robust.Shared.Maths.Color.FromHex("#4d5272"),
                BorderThickness = new Thickness(2),
                ContentMarginLeftOverride = 8,
                ContentMarginTopOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginBottomOverride = 8,
            }
        };

        contentRow.AddChild(helpPanel);

        var helpBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
        };

        helpPanel.AddChild(helpBox);

        helpBox.AddChild(new Label
        {
            Text = Loc.GetString("polaroid-camera-ui-help-title"),
        });

        var helpText = new RichTextLabel
        {
            VerticalExpand = true,
        };
        helpText.SetMessage(Loc.GetString("polaroid-camera-ui-help"));
        helpBox.AddChild(helpText);
        // DS14-end

        _chargesLabel = new Label();
        root.AddChild(_chargesLabel);

        var buttons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };

        root.AddChild(buttons);

        _captureButton = new Button
        {
            Text = Loc.GetString("polaroid-camera-ui-capture"),
            HorizontalExpand = true,
        };

        _captureButton.OnPressed += _ => CaptureScreenshot();
        buttons.AddChild(_captureButton);

        _printLastButton = new Button
        {
            Text = Loc.GetString("polaroid-camera-ui-print-last"),
            HorizontalExpand = true,
        };

        _printLastButton.OnPressed += _ => PrintLastPressed?.Invoke();
        buttons.AddChild(_printLastButton);

        UpdateButtons();
    }

    public void SetState(PolaroidCameraUiState state)
    {
        _nextState = state;
        _cameraView.ViewportSize = new Vector2i(state.ViewportPixelSize, state.ViewportPixelSize);
        _chargesLabel.Text = Loc.GetString("polaroid-camera-ui-cartridge",
            ("current", state.CurrentCharges),
            ("max", state.MaxCharges));
        UpdateButtons();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_nextState == null || _timing.LastRealTick < _nextState.Tick)
            return;

        if (_previewPanActive && _inputManager.MouseScreenPosition.IsValid) // DS14
            UpdatePreviewPan(_inputManager.MouseScreenPosition.Position - _cameraView.GlobalPixelPosition);

        var preview = _entManager.GetEntity(_nextState.PreviewCamera);
        if (preview == null)
        {
            if (_currentCamera != null)
            {
                _eyeLerping.RemoveEye(_currentCamera.Value);
                _currentCamera = null;
            }

            _cameraView.Eye = _defaultEye;
            ApplyPreviewView(); // DS14
            UpdateButtons();
            return;
        }

        if (_currentCamera == null)
        {
            _eyeLerping.AddEye(preview.Value);
            _currentCamera = preview;
        }
        else if (_currentCamera != preview)
        {
            _eyeLerping.RemoveEye(_currentCamera.Value);
            _eyeLerping.AddEye(preview.Value);
            _currentCamera = preview;
        }

        if (_entManager.TryGetComponent<EyeComponent>(_currentCamera, out var eye))
            _cameraView.Eye = eye.Eye ?? _defaultEye;

        ApplyPreviewView(); // DS14

        UpdateButtons();
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (_cameraView.Eye == null || args.Delta.Y == 0f)
            return;

        var previewRect = UIBox2.FromDimensions(_cameraView.GlobalPosition, _cameraView.Size);
        if (!previewRect.Contains(args.GlobalPosition))
            return;

        var oldZoom = _previewZoom;
        _previewZoom = Math.Clamp(_previewZoom * MathF.Pow(1f + PreviewZoomStep, -args.Delta.Y),
            MinPreviewZoom,
            MaxPreviewZoom);

        if (MathHelper.CloseToPercent(_previewZoom, oldZoom))
            return;

        ApplyPreviewView(); // DS14
        args.Handle();
    }

    private void CaptureScreenshot()
    {
        if (_disposed || _currentCamera == null)
            return;

        _cameraView.Screenshot(image =>
        {
            if (_disposed)
                return;

            using var stream = new MemoryStream();
            image.SaveAsPng(stream);
            CaptureReady?.Invoke(stream.ToArray());
        });
    }

    private void UpdateButtons()
    {
        var hasPreview = _currentCamera != null;
        var hasCharge = _nextState is { CurrentCharges: > 0 };
        var hasLastCapture = _nextState?.HasLastCapture == true;

        _captureButton.Disabled = !hasPreview || !hasCharge;
        _printLastButton.Disabled = !hasLastCapture || !hasCharge;
    }

    // DS14-start: zoom + pan preview view controls
    private void UpdatePreviewPan(Vector2 relativePosition)
    {
        if (_cameraView.Size.X <= 0f || _cameraView.Size.Y <= 0f)
            return;

        var normalized = relativePosition / _cameraView.Size;
        _previewPan = Vector2.Clamp(normalized * 2f - Vector2.One, -Vector2.One, Vector2.One);
        ApplyPreviewView();
    }
    // DS14-end

    private void ApplyPreviewView()
    {
        if (_cameraView.Eye == null)
            return;

        _cameraView.Eye.Zoom = new Vector2(_previewZoom, _previewZoom);

        var visibleArea = _cameraView.Size / EyeManager.PixelsPerMeter * _previewZoom;
        var maxOffset = visibleArea * PreviewPanStrength;
        _cameraView.Eye.Offset = new Vector2(
            _previewPan.X * maxOffset.X,
            -_previewPan.Y * maxOffset.Y);
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_currentCamera != null)
        {
            _eyeLerping.RemoveEye(_currentCamera.Value);
            _currentCamera = null;
        }

        base.Dispose(disposing);
    }
}
