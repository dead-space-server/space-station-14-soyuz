// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client.Resources;
using Content.Shared.DeadSpace.Polaroid;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.Client.DeadSpace.Polaroid.UI;

public sealed class PolaroidPhotoWindow : DefaultWindow
{
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IFileDialogManager _fileDialogManager = default!;

    private readonly Label _metaLabel;
    private readonly Label _statusLabel;
    private readonly Label _signatureLabel;
    private readonly BoxContainer _signatureControls;
    private readonly Button _saveButton;
    private readonly LineEdit _signatureEdit;
    private readonly Button _signatureSaveButton;
    private readonly PolaroidPhotoViewer _photoViewer;
    private byte[] _currentPng = Array.Empty<byte>();
    private bool _savingPhoto;

    public event Action<string>? SignatureChanged;

    public PolaroidPhotoWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("polaroid-photo-ui-title");
        MinSize = new Vector2(360f, 560f);

        var polaroidColor = Color.FromHex("#efe3c9");
        var polaroidBorderColor = Color.FromHex("#d7c7a8");

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
                BackgroundColor = polaroidColor,
                BorderColor = polaroidBorderColor,
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
                BackgroundColor = polaroidColor,
            }
        };

        photoLayout.AddChild(imageFrame);

        _photoViewer = new PolaroidPhotoViewer
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
                BackgroundColor = polaroidColor,
            }
        };

        photoLayout.AddChild(signatureBand);

        var signatureCenter = new CenterContainer();
        signatureBand.AddChild(signatureCenter);

        _signatureLabel = new Label
        {
            Align = Label.AlignMode.Center,
            HorizontalAlignment = HAlignment.Center,
            FontOverride = _resourceCache.GetFont("/Fonts/HandveticaNeue/Palaroid_shrifte.ttf", 18),
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

        _signatureSaveButton = new Button
        {
            Text = Loc.GetString("polaroid-photo-ui-signature-save"),
        };

        _signatureSaveButton.OnPressed += _ => SubmitSignature(_signatureEdit.Text);
        _signatureControls.AddChild(_signatureSaveButton);

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
            _signatureLabel.Text = Loc.GetString("polaroid-photo-ui-signature-empty");
            _signatureLabel.ModulateSelfOverride = Color.FromHex("#a29b90");
        }
        else
        {
            _signatureLabel.Text = state.Signature;
            _signatureLabel.ModulateSelfOverride = Color.FromHex("#2d2923");
        }

        _saveButton.Disabled = state.Png.Length == 0 || _savingPhoto;

        if (state.Png.Length == 0)
        {
            _photoViewer.SetTexture(null, resetView: true);
            _statusLabel.Text = Loc.GetString("polaroid-photo-ui-missing-image");
            return;
        }

        try
        {
            using var stream = new MemoryStream(state.Png, writable: false);
            _photoViewer.SetTexture(Texture.LoadFromPNGStream(stream, "polaroid-photo"), imageChanged);
            _statusLabel.Text = string.Empty;
        }
        catch
        {
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
        if (_currentPng.Length == 0 || _savingPhoto)
        {
            if (_currentPng.Length == 0)
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

            await using var stream = file.Value.fileStream;
            await stream.WriteAsync(_currentPng);
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
            _saveButton.Disabled = _currentPng.Length == 0;
        }
    }
}
