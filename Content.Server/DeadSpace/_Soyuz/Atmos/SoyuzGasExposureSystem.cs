// SPDX-FileCopyrightText: 2026 Ezio50
// SPDX-License-Identifier: LicenseRef-Ezio50

using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;

namespace Content.Server.DeadSpace._Soyuz.Atmos;

public sealed class SoyuzGasExposureSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AtmosExposedComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);
        SubscribeLocalEvent<MovementSpeedModifierComponent, AtmosExposedUpdateEvent>(OnExposure);
    }

    private void OnSpeed(EntityUid uid, AtmosExposedComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        var xform = Transform(uid);
        if (xform.GridUid == null || xform.ParentUid != xform.GridUid)
            return;
        var air = _atmos.GetTileMixture((uid, xform));
        if (air != null)
            args.ModifySpeed(1f - Math.Clamp(air.GetMoles(Gas.Gravion) * 0.01f, 0f, 0.55f));
    }

    private void OnExposure(EntityUid uid, MovementSpeedModifierComponent component, ref AtmosExposedUpdateEvent args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }
}
