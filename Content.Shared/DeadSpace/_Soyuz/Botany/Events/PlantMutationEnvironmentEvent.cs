// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks
using Content.Shared.Atmos;

namespace Content.Shared.Botany.Events;

/// <summary>
/// Server systems supply the current environment for conditional species mutations.
/// Missing readings must fail requirements closed.
/// </summary>
[ByRefEvent]
public record struct PlantMutationEnvironmentEvent
{
    public GasMixture? Atmosphere;
    public float? Light;
}
