using System.IO;
using System.Numerics;
using Content.Shared.DeadSpace.Polaroid;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client.DeadSpace.Polaroid.UI;

public sealed class PolaroidPhotoWindow : DefaultWindow
{
    private readonly Label _metaLabel;
    private readonly Label _statusLabel;
    private readonly Label _signatureLabel;
    private readonly LineEdit _signatureEdit;
    private readonly Button _signatureSaveButton;
    private readonly TextureRect _photoTexture;

    public event Action<string>? SignatureChanged;

    public PolaroidPhotoWindow()
    {
        Title = Loc.GetString("polaroid-photo-ui-title");
        MinSize = new Vector2(360f, 560f);

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
                BackgroundColor = Color.FromHex("#f2ede3"),
                BorderColor = Color.FromHex("#d7d0c2"),
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
                BackgroundColor = Color.White,
            }
        };

        photoLayout.AddChild(imageFrame);

        _photoTexture = new TextureRect
        {
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        imageFrame.AddChild(_photoTexture);

        var signatureBand = new PanelContainer
        {
            MinSize = new Vector2(0f, 56f),
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#f2ede3"),
            }
        };

        photoLayout.AddChild(signatureBand);

        var signatureCenter = new CenterContainer();
        signatureBand.AddChild(signatureCenter);

        _signatureLabel = new Label
        {
            Align = Label.AlignMode.Center,
            HorizontalAlignment = HAlignment.Center,
        };

        signatureCenter.AddChild(_signatureLabel);

        var signatureControls = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };

        root.AddChild(signatureControls);

        _signatureEdit = new LineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = Loc.GetString("polaroid-photo-ui-signature-placeholder"),
        };

        _signatureEdit.OnTextEntered += e => SubmitSignature(e.Text);
        _signatureEdit.OnFocusExit += e => SubmitSignature(e.Text);
        signatureControls.AddChild(_signatureEdit);

        _signatureSaveButton = new Button
        {
            Text = Loc.GetString("polaroid-photo-ui-signature-save"),
        };

        _signatureSaveButton.OnPressed += _ => SubmitSignature(_signatureEdit.Text);
        signatureControls.AddChild(_signatureSaveButton);

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
        var photographer = string.IsNullOrWhiteSpace(state.Photographer)
            ? Loc.GetString("polaroid-photo-ui-unknown-photographer")
            : state.Photographer;

        var takenAt = string.IsNullOrWhiteSpace(state.TakenAt)
            ? Loc.GetString("polaroid-photo-ui-unknown-time")
            : state.TakenAt;

        _metaLabel.Text = Loc.GetString("polaroid-photo-ui-meta",
            ("photographer", photographer),
            ("takenAt", takenAt));

        if (!_signatureEdit.HasKeyboardFocus())
            _signatureEdit.Text = state.Signature ?? string.Empty;

        if (string.IsNullOrWhiteSpace(state.Signature))
        {
            _signatureLabel.Text = Loc.GetString("polaroid-photo-ui-signature-empty");
            _signatureLabel.ModulateSelfOverride = Color.FromHex("#a29b90");
        }
        else
        {
            _signatureLabel.Text = state.Signature;
            _signatureLabel.ModulateSelfOverride = Color.FromHex("#2d2923");
        }

        if (state.Png.Length == 0)
        {
            _photoTexture.Texture = null;
            _statusLabel.Text = Loc.GetString("polaroid-photo-ui-missing-image");
            return;
        }

        try
        {
            using var stream = new MemoryStream(state.Png, writable: false);
            _photoTexture.Texture = Texture.LoadFromPNGStream(stream, "polaroid-photo");
            _statusLabel.Text = string.Empty;
        }
        catch
        {
            _photoTexture.Texture = null;
            _statusLabel.Text = Loc.GetString("polaroid-photo-ui-missing-image");
        }
    }

    private void SubmitSignature(string signature)
    {
        SignatureChanged?.Invoke(signature);
    }
}
