// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.Polaroid;

[RegisterComponent]
public sealed partial class PolaroidCameraComponent : Component
{
    [DataField(required: true)]
    public ItemSlot CartridgeSlot = new();

    [DataField]
    public EntProtoId PreviewCameraPrototype = "PolaroidPreviewCamera";

    [DataField]
    public EntProtoId PhotoPrototype = "PolaroidPhoto";

    [DataField]
    public int ViewportPixelSize = 160;

    [DataField]
    public int MaxPayloadBytes = 256 * 1024;

    [DataField]
    public int MaxCaptureDimension = 512;

    [DataField]
    public SoundSpecifier ShutterSound = new SoundPathSpecifier("/Audio/_Backmen/Machines/polaroid1.ogg");

    [DataField]
    public SoundSpecifier PrintSound = new SoundPathSpecifier("/Audio/Machines/printer.ogg");

    [ViewVariables]
    public EntityUid? CurrentViewer;

    [ViewVariables]
    public EntityUid? PreviewCamera;

    [ViewVariables]
    public byte[] LastCapture = Array.Empty<byte>();

    [ViewVariables]
    public string? LastCapturePhotographer;

    [ViewVariables]
    public TimeSpan? LastCaptureShiftTime;

    [ViewVariables]
    public DateTime? LastCaptureShiftDate;
}

[RegisterComponent]
public sealed partial class PolaroidCartridgeComponent : Component
{
    [DataField]
    public int MaxAmount = 8;

    [DataField]
    public int CurrentAmount = 8;
}

[RegisterComponent]
public sealed partial class PolaroidPhotoComponent : Component
{
    [ViewVariables]
    public byte[] PngData = Array.Empty<byte>();

    [ViewVariables]
    public string? Photographer;

    [ViewVariables]
    public TimeSpan? ShiftTime;

    [ViewVariables]
    public DateTime? ShiftDate;

    [ViewVariables]
    public string? Signature;
}
