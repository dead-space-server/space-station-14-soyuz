// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Content.Shared.UserInterface;

namespace Content.Shared.DeadSpace._Soyuz.Polaroid;

public static class PolaroidSharedConstants
{
    public const int MaxPhotoSignatureLength = 26;
}

[Serializable, NetSerializable]
public enum PolaroidCameraUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class PolaroidCameraUiState(
    NetEntity? previewCamera,
    GameTick tick,
    int currentCharges,
    int maxCharges,
    bool hasLastCapture,
    int viewportPixelSize) : BoundUserInterfaceState
{
    public readonly NetEntity? PreviewCamera = previewCamera;
    public readonly GameTick Tick = tick;
    public readonly int CurrentCharges = currentCharges;
    public readonly int MaxCharges = maxCharges;
    public readonly bool HasLastCapture = hasLastCapture;
    public readonly int ViewportPixelSize = viewportPixelSize;
}

[Serializable, NetSerializable]
public sealed class PolaroidCaptureMessage(byte[] png) : BoundUserInterfaceMessage
{
    public readonly byte[] Png = png;
}

[Serializable, NetSerializable]
public sealed class PolaroidPrintLastMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public enum PolaroidPhotoUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class PolaroidPhotoUiState(
    byte[] png,
    string? photographer,
    string? takenAt,
    string? signature) : BoundUserInterfaceState
{
    public readonly byte[] Png = png;
    public readonly string? Photographer = photographer;
    public readonly string? TakenAt = takenAt;
    public readonly string? Signature = signature;
}

[Serializable, NetSerializable]
public sealed class PolaroidPhotoSetSignatureMessage(string signature) : BoundUserInterfaceMessage
{
    public readonly string Signature = signature;
}
