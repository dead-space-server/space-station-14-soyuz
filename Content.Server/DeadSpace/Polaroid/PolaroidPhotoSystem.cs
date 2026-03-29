using Content.Shared.DeadSpace.Polaroid;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;

namespace Content.Server.DeadSpace.Polaroid;

public sealed class PolaroidPhotoSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PolaroidPhotoComponent, AfterActivatableUIOpenEvent>(OnUiOpened);
        SubscribeLocalEvent<PolaroidPhotoComponent, PolaroidPhotoSetSignatureMessage>(OnSignatureChanged);
    }

    private void OnUiOpened(EntityUid uid, PolaroidPhotoComponent component, AfterActivatableUIOpenEvent args)
    {
        UpdateUi(uid, component);
    }

    private void OnSignatureChanged(EntityUid uid, PolaroidPhotoComponent component, PolaroidPhotoSetSignatureMessage args)
    {
        var signature = args.Signature.Trim();
        if (signature.Length > PolaroidPhotoComponent.MaxSignatureLength)
            signature = signature[..PolaroidPhotoComponent.MaxSignatureLength];

        component.Signature = string.IsNullOrWhiteSpace(signature)
            ? null
            : signature;

        UpdateUi(uid, component);
    }

    private void UpdateUi(EntityUid uid, PolaroidPhotoComponent component)
    {
        var state = new PolaroidPhotoUiState(
            component.PngData,
            component.Photographer,
            component.TakenAt?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
            component.Signature);

        _ui.SetUiState(uid, PolaroidPhotoUiKey.Key, state);
    }
}
