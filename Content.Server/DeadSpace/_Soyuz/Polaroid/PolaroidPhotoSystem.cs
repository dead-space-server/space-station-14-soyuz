// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks
using Content.Shared.DeadSpace._Soyuz.Polaroid;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server.DeadSpace._Soyuz.Polaroid;

public sealed class PolaroidPhotoSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private static readonly SoundSpecifier SignatureSound =
        new SoundCollectionSpecifier("PaperScribbles", AudioParams.Default.WithVariation(0.1f));

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
        if (!string.IsNullOrWhiteSpace(component.Signature))
        {
            UpdateUi(uid, component);
            return;
        }

        var signature = args.Signature.Trim();
        if (signature.Length > PolaroidSharedConstants.MaxPhotoSignatureLength)
            signature = signature[..PolaroidSharedConstants.MaxPhotoSignatureLength];

        if (string.IsNullOrWhiteSpace(signature))
        {
            UpdateUi(uid, component);
            return;
        }

        component.Signature = signature;
        _audio.PlayPvs(SignatureSound, uid);

        UpdateUi(uid, component);
    }

    private void UpdateUi(EntityUid uid, PolaroidPhotoComponent component)
    {
        string? takenAt = null;

        if (component.ShiftTime is { } time && component.ShiftDate is { } date)
            takenAt = $"{time:hh\\:mm\\:ss}, {date:dd.MM.yyyy}";

        var state = new PolaroidPhotoUiState(
            component.PngData,
            component.Photographer,
            takenAt,
            component.Signature);

        _ui.SetUiState(uid, PolaroidPhotoUiKey.Key, state);
    }
}
