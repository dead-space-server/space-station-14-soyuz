// SPDX-FileCopyrightText: 2026 Ezio50
// SPDX-License-Identifier: LicenseRef-Ezio50

using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server.DeadSpace._Soyuz.Atmos.Reactions;

[UsedImplicitly]
public sealed partial class DiboraneOxidationReaction : IGasReactionEffect
{
    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        if (mixture.Immutable || mixture.GetMoles(Gas.Diborane) < 0.5f || mixture.GetMoles(Gas.Oxygen) < 1f)
            return ReactionResult.NoReaction;

        var burned = MathF.Min(mixture.GetMoles(Gas.Diborane), mixture.GetMoles(Gas.Oxygen) / 2f) * 0.5f;
        var temperature = mixture.Temperature;
        var capacity = atmosphereSystem.GetHeatCapacity(mixture, true);
        mixture.AdjustMoles(Gas.Diborane, -burned);
        mixture.AdjustMoles(Gas.Oxygen, -2f * burned);
        mixture.AdjustMoles(Gas.WaterVapor, 2f * burned);
        SoyuzGasReactionHelpers.ApplyEnergy(mixture, atmosphereSystem, heatScale, capacity, temperature,
            burned * Atmospherics.FireHydrogenEnergyReleased);
        mixture.ReactionResults[(byte) GasReaction.Fire] += burned;

        if (holder is TileAtmosphere tile)
            atmosphereSystem.HotspotExpose(tile, mixture.Temperature, mixture.Volume);

        if (burned >= 1f && atmosphereSystem.TryGetSoyuzGasReactionCoordinates(holder, out var coordinates))
            // HydrogenFireReaction uses 0.25/mol, slope 3 and maxTileIntensity 6.
            atmosphereSystem.Explosion.QueueExplosion(coordinates, "Default", MathF.Min(burned * MathF.Min(0.3f, 0.25f * 0.75f), 18f),
                3f, 5f, cause: null, addLog: false);

        return ReactionResult.Reacting;
    }
}
