// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks-Polaroid
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client.Resources;
using Content.Shared.DeadSpace._Soyuz.Polaroid;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.IoC;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client.DeadSpace._Soyuz.Polaroid.UI;

public sealed class PolaroidPhotoWindow : DefaultWindow
{
    private const float ExportMarginRatio = 18f / 252f;
    private const float ExportSignatureHeightRatio = 56f / 252f;
    private const float ExportGapRatio = 18f / 252f;
    private const float ExportBorderRatio = 2f / 252f;
    private static readonly Color PolaroidColor = Color.FromHex("#efe3c9");
    private static readonly Color PolaroidBorderColor = Color.FromHex("#d7c7a8");
    private static readonly Color SignaturePlaceholderColor = Color.FromHex("#a29b90");
    private static readonly Color SignatureInkColor = Color.FromHex("#2d2923");

    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IFileDialogManager _fileDialogManager = default!;

    private readonly Label _metaLabel;
    private readonly Label _statusLabel;
    private readonly Label _signatureLabel;
    private readonly BoxContainer _signatureControls;
    private readonly Button _saveButton;
    private readonly LineEdit _signatureEdit;
    private readonly PhotoViewer _photoViewer;
    private readonly Font _signatureFont;
    private byte[] _currentPng = Array.Empty<byte>();
    private Texture? _currentTexture;
    private string _currentSignatureText = string.Empty;
    private Color _currentSignatureColor = SignaturePlaceholderColor;
    private bool _savingPhoto;
    private PendingPhotoExport? _pendingExport;

    public event Action<string>? SignatureChanged;

    public PolaroidPhotoWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("polaroid-photo-ui-title");
        MinSize = new Vector2(360f, 560f);

        _signatureFont = _resourceCache.GetFont("/Fonts/HandveticaNeue/Palaroid_shrifte.ttf", 18);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 10,
        };

        Contents.AddChild(root);

        var photoFrame = new PanelContainer
        {
            MinSize = new Vector2(288f, 404f),
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = PolaroidColor,
                BorderColor = PolaroidBorderColor,
                BorderThickness = new Thickness(2),
                ContentMarginLeftOverride = 18,
                ContentMarginTopOverride = 18,
                ContentMarginRightOverride = 18,
                ContentMarginBottomOverride = 18,
            }
        };

        root.AddChild(photoFrame);

        var photoLayout = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 18,
        };

        photoFrame.AddChild(photoLayout);

        var imageFrame = new PanelContainer
        {
            MinSize = new Vector2(0f, 260f),
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = PolaroidColor,
            }
        };

        photoLayout.AddChild(imageFrame);

        _photoViewer = new PhotoViewer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        imageFrame.AddChild(_photoViewer);

        var signatureBand = new PanelContainer
        {
            MinSize = new Vector2(0f, 56f),
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = PolaroidColor,
            }
        };

        photoLayout.AddChild(signatureBand);

        var signatureCenter = new CenterContainer();
        signatureBand.AddChild(signatureCenter);

        _signatureLabel = new Label
        {
            Align = Label.AlignMode.Center,
            HorizontalAlignment = HAlignment.Center,
            FontOverride = _signatureFont,
        };

        signatureCenter.AddChild(_signatureLabel);

        _signatureControls = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };

        root.AddChild(_signatureControls);

        _signatureEdit = new LineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = Loc.GetString("polaroid-photo-ui-signature-placeholder"),
            SelectAllOnFocus = true,
            IsValid = text => text.Length <= PolaroidSharedConstants.MaxPhotoSignatureLength,
        };

        _signatureEdit.OnTextEntered += e => SubmitSignature(e.Text);
        _signatureControls.AddChild(_signatureEdit);

        var signatureSaveButton = new Button
        {
            Text = Loc.GetString("polaroid-photo-ui-signature-save"),
        };

        signatureSaveButton.OnPressed += _ => SubmitSignature(_signatureEdit.Text);
        _signatureControls.AddChild(signatureSaveButton);

        _saveButton = new Button
        {
            Text = Loc.GetString("polaroid-photo-ui-save-local"),
        };

        _saveButton.OnPressed += args => { _ = SavePhotoWithDialogAsync(); };
        root.AddChild(_saveButton);

        _metaLabel = new Label();
        root.AddChild(_metaLabel);

        _statusLabel = new Label
        {
            ModulateSelfOverride = Color.FromHex("#6f6a61"),
        };

        root.AddChild(_statusLabel);
    }

    public void SetState(PolaroidPhotoUiState state)
    {
        var imageChanged = !_currentPng.AsSpan().SequenceEqual(state.Png);
        _currentPng = state.Png;

        var photographer = string.IsNullOrWhiteSpace(state.Photographer)
            ? Loc.GetString("polaroid-photo-ui-unknown-photographer")
            : state.Photographer;

        var takenAt = string.IsNullOrWhiteSpace(state.TakenAt)
            ? Loc.GetString("polaroid-photo-ui-unknown-time")
            : state.TakenAt;

        _metaLabel.Text = Loc.GetString("polaroid-photo-ui-meta",
            ("photographer", photographer),
            ("takenAt", takenAt));

        var signed = !string.IsNullOrWhiteSpace(state.Signature);
        _signatureControls.Visible = !signed;
        _signatureEdit.Editable = !signed;

        if (!signed && !_signatureEdit.HasKeyboardFocus())
            _signatureEdit.Text = state.Signature ?? string.Empty;
        else if (signed)
            _signatureEdit.Text = string.Empty;

        if (!signed)
        {
            _currentSignatureText = Loc.GetString("polaroid-photo-ui-signature-empty");
            _currentSignatureColor = SignaturePlaceholderColor;
        }
        else
        {
            _currentSignatureText = state.Signature!;
            _currentSignatureColor = SignatureInkColor;
        }

        _signatureLabel.Text = _currentSignatureText;
        _signatureLabel.ModulateSelfOverride = _currentSignatureColor;
        _saveButton.Disabled = state.Png.Length == 0 || _savingPhoto;

        if (state.Png.Length == 0)
        {
            _currentTexture = null;
            _photoViewer.SetTexture(null, resetView: true);
            _statusLabel.Text = Loc.GetString("polaroid-photo-ui-missing-image");
            return;
        }

        try
        {
            using var stream = new MemoryStream(state.Png, writable: false);
            _currentTexture = Texture.LoadFromPNGStream(stream, "polaroid-photo");
            _photoViewer.SetTexture(_currentTexture, imageChanged);
            _statusLabel.Text = string.Empty;
        }
        catch
        {
            _currentTexture = null;
            _photoViewer.SetTexture(null, resetView: true);
            _statusLabel.Text = Loc.GetString("polaroid-photo-ui-missing-image");
        }
    }

    private void SubmitSignature(string signature)
    {
        if (!_signatureControls.Visible)
            return;

        var trimmed = signature.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        if (trimmed.Length > PolaroidSharedConstants.MaxPhotoSignatureLength)
            trimmed = trimmed[..PolaroidSharedConstants.MaxPhotoSignatureLength];

        SignatureChanged?.Invoke(trimmed);
    }

    private async Task SavePhotoWithDialogAsync()
    {
        if (_currentTexture == null || _savingPhoto)
        {
            if (_currentTexture == null)
                _statusLabel.Text = Loc.GetString("polaroid-photo-ui-missing-image");

            return;
        }

        _savingPhoto = true;
        _saveButton.Disabled = true;

        try
        {
            var file = await _fileDialogManager.SaveFile(
                new FileDialogFilters(new FileDialogFilters.Group("png")),
                access: FileAccess.Write);

            if (file == null)
                return;

            var compositedPhoto = await ExportPhotoAsync();
            await using var stream = file.Value.fileStream;
            await stream.WriteAsync(compositedPhoto);
            await stream.FlushAsync();
            _statusLabel.Text = Loc.GetString("polaroid-photo-ui-save-success");
        }
        catch
        {
            _statusLabel.Text = Loc.GetString("polaroid-photo-ui-save-failed");
        }
        finally
        {
            _savingPhoto = false;
            _saveButton.Disabled = _currentTexture == null;
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_pendingExport == null)
            return;

        var export = _pendingExport;
        _pendingExport = null;

        try
        {
            var oldTransform = handle.GetTransform();
            var oldModulate = handle.Modulate;

            handle.RenderInRenderTarget(export.RenderTarget, () =>
            {
                handle.SetTransform(Matrix3x2.Identity);
                handle.Modulate = Color.White;
                DrawExport(handle, export);
            }, Color.Transparent);

            handle.SetTransform(oldTransform);
            handle.Modulate = oldModulate;
            handle.UseShader(null);

            export.RenderTarget.CopyPixelsToMemory<Rgba32>(image =>
            {
                try
                {
                    using var stream = new MemoryStream();
                    image.SaveAsPng(stream);
                    export.Completion.TrySetResult(stream.ToArray());
                }
                catch (Exception e)
                {
                    export.Completion.TrySetException(e);
                }
                finally
                {
                    export.RenderTarget.Dispose();
                }
            });
        }
        catch (Exception e)
        {
            export.RenderTarget.Dispose();
            export.Completion.TrySetException(e);
        }
    }

    protected override void ExitedTree()
    {
        if (_pendingExport != null)
        {
            _pendingExport.RenderTarget.Dispose();
            _pendingExport.Completion.TrySetCanceled();
            _pendingExport = null;
        }

        base.ExitedTree();
    }

    private async Task<byte[]> ExportPhotoAsync()
    {
        if (_currentTexture == null)
            throw new InvalidOperationException("There is no photo texture to export.");

        if (_pendingExport != null)
            throw new InvalidOperationException("A photo export is already in progress.");

        var layout = CreateExportLayout(_currentTexture.Size);
        var renderTarget = _clyde.CreateRenderTarget(
            layout.Size,
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            name: "polaroid-photo-export");

        var tcs = new TaskCompletionSource<byte[]>();
        _pendingExport = new PendingPhotoExport(
            renderTarget,
            _currentTexture,
            _currentSignatureText,
            _currentSignatureColor,
            layout,
            tcs);

        return await tcs.Task;
    }

    private void DrawExport(DrawingHandleScreen handle, PendingPhotoExport export)
    {
        handle.DrawRect(export.Layout.BorderRect, PolaroidBorderColor);
        handle.DrawRect(export.Layout.FillRect, PolaroidColor);
        handle.DrawTextureRect(export.PhotoTexture, export.Layout.PhotoRect);

        var textSize = handle.GetDimensions(_signatureFont, export.Signature.AsSpan(), 1f);
        var textPosition = new Vector2(
            export.Layout.SignatureRect.Left + MathF.Max(0f, (export.Layout.SignatureRect.Width - textSize.X) / 2f),
            export.Layout.SignatureRect.Top + MathF.Max(0f, (export.Layout.SignatureRect.Height - textSize.Y) / 2f));

        handle.DrawString(_signatureFont, textPosition, export.Signature, export.SignatureColor);
    }

    private static PolaroidExportLayout CreateExportLayout(Vector2i photoSize)
    {
        var referenceWidth = Math.Max(1, photoSize.X);
        var border = Math.Max(2, (int) MathF.Round(referenceWidth * ExportBorderRatio));
        var margin = Math.Max(border + 6, (int) MathF.Round(referenceWidth * ExportMarginRatio));
        var signatureGap = Math.Max(8, (int) MathF.Round(referenceWidth * ExportGapRatio));
        var signatureHeight = Math.Max(36, (int) MathF.Round(referenceWidth * ExportSignatureHeightRatio));

        var totalWidth = photoSize.X + margin * 2;
        var totalHeight = photoSize.Y + margin * 2 + signatureGap + signatureHeight;
        var size = new Vector2(totalWidth, totalHeight);
        var photoPosition = new Vector2(margin, margin);
        var signaturePosition = new Vector2(margin, margin + photoSize.Y + signatureGap);

        return new PolaroidExportLayout(
            new Vector2i(totalWidth, totalHeight),
            UIBox2.FromDimensions(Vector2.Zero, size),
            UIBox2.FromDimensions(new Vector2(border, border), size - new Vector2(border * 2f, border * 2f)),
            UIBox2.FromDimensions(photoPosition, new Vector2(photoSize.X, photoSize.Y)),
            UIBox2.FromDimensions(signaturePosition, new Vector2(photoSize.X, signatureHeight)));
    }

    private sealed record PendingPhotoExport(
        IRenderTexture RenderTarget,
        Texture PhotoTexture,
        string Signature,
        Color SignatureColor,
        PolaroidExportLayout Layout,
        TaskCompletionSource<byte[]> Completion);

    private readonly record struct PolaroidExportLayout(
        Vector2i Size,
        UIBox2 BorderRect,
        UIBox2 FillRect,
        UIBox2 PhotoRect,
        UIBox2 SignatureRect);

    private sealed class PhotoViewer : Control
    {
        private const float MinZoom = 1f;
        private const float MaxZoom = 8f;
        private const float ZoomStep = 0.15f;

        private Texture? _texture;
        private float _zoom = MinZoom;
        private Vector2 _panOffset;
        private bool _dragging;

        public PhotoViewer()
        {
            RectClipContent = true;
            MouseFilter = MouseFilterMode.Stop;
        }

        public void SetTexture(Texture? texture, bool resetView)
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
            if (_texture != null)
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

            var size = GetDisplaySize();
            _panOffset = anchor - imagePosition * size - (PixelSize - size) / 2f;
            ClampPan();
            args.Handle();
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);
            if (args.Function != EngineKeyFunctions.UIClick || !CanPan())
                return;

            _dragging = true;
            UpdateCursor();
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
            return UIBox2.FromDimensions((PixelSize - size) / 2f + _panOffset, size);
        }

        private Vector2 GetDisplaySize()
        {
            if (_texture == null || PixelSize.X <= 0f || PixelSize.Y <= 0f)
                return Vector2.Zero;

            var textureSize = (Vector2) _texture.Size;
            if (textureSize.X <= 0f || textureSize.Y <= 0f)
                return Vector2.Zero;

            return textureSize * Math.Min(PixelSize.X / textureSize.X, PixelSize.Y / textureSize.Y) * _zoom;
        }

        private bool CanPan()
        {
            var size = GetDisplaySize();
            return size.X > PixelSize.X || size.Y > PixelSize.Y;
        }

        private void ClampPan()
        {
            var overflow = GetDisplaySize() - PixelSize;
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
}

[UsedImplicitly]
public sealed class PolaroidPhotoBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private PolaroidPhotoWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<PolaroidPhotoWindow>();
        _window.SignatureChanged += signature => SendMessage(new PolaroidPhotoSetSignatureMessage(signature));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is PolaroidPhotoUiState photoState)
            _window?.SetState(photoState);
    }
}
