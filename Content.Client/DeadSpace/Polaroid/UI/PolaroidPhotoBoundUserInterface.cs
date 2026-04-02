// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT
using Content.Shared.DeadSpace.Polaroid;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.DeadSpace.Polaroid.UI;

[UsedImplicitly]
public sealed class PolaroidPhotoBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private PolaroidPhotoWindow? _window;

    public PolaroidPhotoBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<PolaroidPhotoWindow>();
        _window.SignatureChanged += OnSignatureChanged;
    }

    private void OnSignatureChanged(string signature)
    {
        SendMessage(new PolaroidPhotoSetSignatureMessage(signature));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not PolaroidPhotoUiState cast)
            return;

        _window?.SetState(cast);
    }
}
