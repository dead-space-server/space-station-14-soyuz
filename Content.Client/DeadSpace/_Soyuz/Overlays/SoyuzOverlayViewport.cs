// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks

using Robust.Client.Graphics;
using Robust.Client.Player;

namespace Content.Client.DeadSpace._Soyuz.Overlays;

/// <summary>
/// Keeps player HUD overlays out of secondary camera viewports.
/// </summary>
public static class SoyuzOverlayViewport
{
    public static bool IsPrimary(
        in OverlayDrawArgs args,
        IEntityManager entityManager,
        IPlayerManager playerManager)
    {
        return entityManager.TryGetComponent(
                   playerManager.LocalSession?.AttachedEntity,
                   out EyeComponent? eye) &&
               args.Viewport.Eye == eye.Eye;
    }
}
