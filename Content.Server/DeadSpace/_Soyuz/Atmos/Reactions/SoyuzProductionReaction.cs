// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server.DeadSpace._Soyuz.Atmos.Reactions;

/// <summary>Stoichiometric production, with separate rate and product yield modifiers.</summary>
[UsedImplicitly]
public sealed partial class SoyuzProductionReaction : IGasReactionEffect
{
    [DataField(required: true)] public Gas Product;
    [DataField(required: true)] public Dictionary<Gas, float> Reactants = new();
    [DataField] public float ProductMoles = 2f;
    [DataField] public float Fraction = 0.25f;
    [DataField] public float EnergyPerBlock;
    [DataField] public float MinimumPressure;
    [DataField] public float MaximumPressure = float.MaxValue;
    [DataField] public float MinimumTemperature = Atmospherics.TCMB;
    [DataField] public float MaximumTemperature = float.MaxValue;

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        if (mixture.Immutable || mixture.Pressure < MinimumPressure || mixture.Pressure > MaximumPressure ||
            mixture.Temperature < MinimumTemperature || mixture.Temperature > MaximumTemperature ||
            (Product == Gas.Isoflux && mixture.GetMoles(Gas.Tritium) < 0.01f) ||
            (Product == Gas.Tlec && mixture.GetMoles(Gas.Coronite) < 1f) ||
            (Product == Gas.Diborane && mixture.GetMoles(Gas.Boracite) < 1f))
            return ReactionResult.NoReaction;

        var available = float.MaxValue;
        foreach (var (gas, ratio) in Reactants)
        {
            if (ratio <= 0f)
                return ReactionResult.NoReaction;
            available = MathF.Min(available, mixture.GetMoles(gas) / ratio);
        }

        if (Reactants.Count == 0 || available <= 0f || !float.IsFinite(available))
            return ReactionResult.NoReaction;

        var rate = mixture.GetMoles(Gas.Forsazh) >= 5f ? 2f : 1f;
        var yield = Product != Gas.Tlec && mixture.GetMoles(Gas.Coronite) >= 5f ? 1.2f : 1f;
        var blocks = available * Math.Clamp(Fraction * rate, 0f, 1f);
        var temperature = mixture.Temperature;
        var capacity = atmosphereSystem.GetHeatCapacity(mixture, true);
        foreach (var (gas, ratio) in Reactants)
            mixture.AdjustMoles(gas, -blocks * ratio);
        mixture.AdjustMoles(Product, blocks * ProductMoles * yield);

        var newCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);
        mixture.Temperature = Math.Clamp(
            (temperature * capacity + blocks * EnergyPerBlock / heatScale) / newCapacity,
            Atmospherics.TCMB, Atmospherics.Tmax);
        return ReactionResult.Reacting;
    }
}
