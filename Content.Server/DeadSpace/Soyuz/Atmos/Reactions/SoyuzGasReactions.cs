using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server.DeadSpace.Soyuz.Atmos.Reactions;

[UsedImplicitly]
public sealed partial class FixiriumProductionReaction : IGasReactionEffect
{
    private const float RatioTolerance = 0.06f;
    private const float ConversionDivisor = 12f;
    private const float EnergyPerMole = 60_000f;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var tritium = mixture.GetMoles(Gas.Tritium);
        var carbonDioxide = mixture.GetMoles(Gas.CarbonDioxide);
        var oxygen = mixture.GetMoles(Gas.Oxygen);

        if (!SoyuzGasReactionHelpers.HasApproximateRatios(
                RatioTolerance,
                (tritium, 1f),
                (carbonDioxide, 1f),
                (oxygen, 1f)))
        {
            return ReactionResult.NoReaction;
        }

        var reacted = MathF.Min(tritium, MathF.Min(carbonDioxide, oxygen)) / ConversionDivisor;
        if (reacted <= 0f)
            return ReactionResult.NoReaction;

        var oldTemperature = mixture.Temperature;
        var oldHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);

        mixture.AdjustMoles(Gas.Tritium, -reacted);
        mixture.AdjustMoles(Gas.CarbonDioxide, -reacted);
        mixture.AdjustMoles(Gas.Oxygen, -reacted);
        mixture.AdjustMoles(Gas.Fixirium, reacted);
        mixture.AdjustMoles(Gas.Nitrogen, reacted);
        mixture.AdjustMoles(Gas.Hydrogen, reacted);

        SoyuzGasReactionHelpers.ApplyEnergy(
            mixture,
            atmosphereSystem,
            heatScale,
            oldHeatCapacity,
            oldTemperature,
            reacted * EnergyPerMole);

        return ReactionResult.Reacting;
    }
}

[UsedImplicitly]
public sealed partial class BrizidiumProductionReaction : IGasReactionEffect
{
    private const float RatioTolerance = 0.05f;
    private const float ConversionDivisor = 10f;
    private const float EnergyPerMole = 35_000f;
    private const float MaxPressure = 40f;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        if (mixture.Pressure > MaxPressure)
            return ReactionResult.NoReaction;

        var plasma = mixture.GetMoles(Gas.Plasma);
        var nitryl = mixture.GetMoles(Gas.Nitryl);

        if (!SoyuzGasReactionHelpers.HasApproximateRatios(
                RatioTolerance,
                (plasma, 55f),
                (nitryl, 45f)))
        {
            return ReactionResult.NoReaction;
        }

        var totalReactants = MathF.Min(plasma / 0.55f, nitryl / 0.45f);
        var reacted = totalReactants / ConversionDivisor;
        if (reacted <= 0f)
            return ReactionResult.NoReaction;

        var oldTemperature = mixture.Temperature;
        var oldHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);

        mixture.AdjustMoles(Gas.Plasma, -(reacted * 0.55f));
        mixture.AdjustMoles(Gas.Nitryl, -(reacted * 0.45f));
        mixture.AdjustMoles(Gas.Brizidium, reacted);

        SoyuzGasReactionHelpers.ApplyEnergy(
            mixture,
            atmosphereSystem,
            heatScale,
            oldHeatCapacity,
            oldTemperature,
            reacted * EnergyPerMole);

        return ReactionResult.Reacting;
    }
}

[UsedImplicitly]
public sealed partial class NitriatiumProductionReaction : IGasReactionEffect
{
    private const float RatioTolerance = 0.06f;
    private const float ConversionDivisor = 6f;
    private const float EnergyPerMole = -50_000f;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var tritium = mixture.GetMoles(Gas.Tritium);
        var nitrogen = mixture.GetMoles(Gas.Nitrogen);
        var brizidium = mixture.GetMoles(Gas.Brizidium);

        if (!SoyuzGasReactionHelpers.HasApproximateRatios(
                RatioTolerance,
                (tritium, 4f),
                (nitrogen, 2f),
                (brizidium, 1f)))
        {
            return ReactionResult.NoReaction;
        }

        var units = MathF.Min(tritium / 4f, MathF.Min(nitrogen / 2f, brizidium)) / ConversionDivisor;
        if (units <= 0f)
            return ReactionResult.NoReaction;

        var oldTemperature = mixture.Temperature;
        var oldHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);

        mixture.AdjustMoles(Gas.Tritium, -(units * 4f));
        mixture.AdjustMoles(Gas.Nitrogen, -(units * 2f));
        mixture.AdjustMoles(Gas.Brizidium, -units);
        mixture.AdjustMoles(Gas.Nitriatium, units * 7f);

        SoyuzGasReactionHelpers.ApplyEnergy(
            mixture,
            atmosphereSystem,
            heatScale,
            oldHeatCapacity,
            oldTemperature,
            units * 7f * EnergyPerMole);

        return ReactionResult.Reacting;
    }
}

[UsedImplicitly]
public sealed partial class HiliumProductionReaction : IGasReactionEffect
{
    private const float RatioTolerance = 0.05f;
    private const float ConversionDivisor = 10f;
    private const float EnergyPerMole = 45_000f;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var frezon = mixture.GetMoles(Gas.Frezon);
        var brizidium = mixture.GetMoles(Gas.Brizidium);

        if (!SoyuzGasReactionHelpers.HasApproximateRatios(
                RatioTolerance,
                (frezon, 1f),
                (brizidium, 1f)))
        {
            return ReactionResult.NoReaction;
        }

        var reacted = MathF.Min(frezon, brizidium) / ConversionDivisor;
        if (reacted <= 0f)
            return ReactionResult.NoReaction;

        var oldTemperature = mixture.Temperature;
        var oldHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);

        mixture.AdjustMoles(Gas.Frezon, -reacted);
        mixture.AdjustMoles(Gas.Brizidium, -reacted);
        mixture.AdjustMoles(Gas.Hilium, reacted * 2f);

        SoyuzGasReactionHelpers.ApplyEnergy(
            mixture,
            atmosphereSystem,
            heatScale,
            oldHeatCapacity,
            oldTemperature,
            reacted * 2f * EnergyPerMole);

        return ReactionResult.Reacting;
    }
}

[UsedImplicitly]
public sealed partial class IpritProductionReaction : IGasReactionEffect
{
    private const float ConversionDivisor = 10f;
    private const float EnergyPerMole = 90_000f;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var tritium = mixture.GetMoles(Gas.Tritium);
        var fixirium = mixture.GetMoles(Gas.Fixirium);
        var reacted = MathF.Min(tritium, fixirium) / ConversionDivisor;

        if (reacted <= 0f)
            return ReactionResult.NoReaction;

        var oldTemperature = mixture.Temperature;
        var oldHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);

        mixture.AdjustMoles(Gas.Tritium, -reacted);
        mixture.AdjustMoles(Gas.Fixirium, -reacted);
        mixture.AdjustMoles(Gas.Iprit, reacted * 2f);

        SoyuzGasReactionHelpers.ApplyEnergy(
            mixture,
            atmosphereSystem,
            heatScale,
            oldHeatCapacity,
            oldTemperature,
            reacted * 2f * EnergyPerMole);

        return ReactionResult.Reacting;
    }
}

[UsedImplicitly]
public sealed partial class IpritNitrogenAmplificationReaction : IGasReactionEffect
{
    private const float ConversionDivisor = 8f;
    private const float EnergyPerMole = 70_000f;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var nitrogen = mixture.GetMoles(Gas.Nitrogen);
        var iprit = mixture.GetMoles(Gas.Iprit);

        if (nitrogen <= 0f || iprit <= 0f)
            return ReactionResult.NoReaction;

        var reacted = nitrogen / ConversionDivisor;
        if (reacted <= 0f)
            return ReactionResult.NoReaction;

        var oldTemperature = mixture.Temperature;
        var oldHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);

        mixture.AdjustMoles(Gas.Nitrogen, -reacted);
        mixture.AdjustMoles(Gas.Iprit, reacted * 2f);

        SoyuzGasReactionHelpers.ApplyEnergy(
            mixture,
            atmosphereSystem,
            heatScale,
            oldHeatCapacity,
            oldTemperature,
            reacted * 2f * EnergyPerMole);

        return ReactionResult.Reacting;
    }
}
