// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Reactions;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;

namespace Content.Server.DeadSpace._Soyuz.Atmos.Reactions;

public static class SoyuzProductionModifiers
{
    public static ReactionResult React(GasReactionPrototype prototype, GasMixture mixture,
        IGasMixtureHolder? holder, AtmosphereSystem atmosphere, float heatScale)
    {
        var production = prototype.ID is "FixiriumProduction" or "BrizidiumProduction" or
            "NitriatiumProduction" or "HiliumProduction" or "IpritProduction" or
            "NitrousOxideProduction" or "ProtoNitrateProduction" or "HalonProduction" or
            "ZaukerProduction" or "FrezonProduction";
        var rate = mixture.GetMoles(Gas.Forsazh) >= 5f ? 2f : 1f;
        var yield = mixture.GetMoles(Gas.Coronite) >= 5f ? 1.2f : 1f;
        if (!production || mixture.Immutable || (rate == 1f && yield == 1f))
            return prototype.React(mixture, holder, atmosphere, heatScale);

        Span<float> before = stackalloc float[Atmospherics.TotalNumberOfGases];
        for (var i = 0; i < before.Length; i++)
            before[i] = mixture.GetMoles(i);
        var energyBefore = mixture.Temperature * atmosphere.GetHeatCapacity(mixture, true);
        var result = prototype.React(mixture, holder, atmosphere, heatScale);
        if (result == ReactionResult.NoReaction || result.HasFlag(ReactionResult.StopReactions))
            return result;

        var energyAfter = mixture.Temperature * atmosphere.GetHeatCapacity(mixture, true);
        var ipritAfter = mixture.GetMoles(Gas.Iprit);
        for (var i = 0; i < before.Length; i++)
        {
            var consumed = before[i] - mixture.GetMoles(i);
            if (consumed > 0f)
                rate = MathF.Min(rate, before[i] / consumed);
        }
        for (var i = 0; i < before.Length; i++)
        {
            var delta = mixture.GetMoles(i) - before[i];
            mixture.SetMoles(i, MathF.Max(0f, before[i] + delta * rate * (delta > 0f ? yield : 1f)));
        }
        if (prototype.ID == "IpritProduction")
            mixture.BlendIpritDecayDeadline(ipritAfter, atmosphere.CurrentSimulationTime + IpritDecayReaction.Lifetime,
                mixture.GetMoles(Gas.Iprit) - ipritAfter);
        mixture.Temperature = Math.Clamp(
            (energyBefore + (energyAfter - energyBefore) * rate) / atmosphere.GetHeatCapacity(mixture, true),
            Atmospherics.TCMB, Atmospherics.Tmax);
        return result;
    }
}
