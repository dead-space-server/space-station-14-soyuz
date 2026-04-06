// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Shared.Damage.Prototypes; using Robust.Shared.Prototypes;

namespace Content.Server.PoliticalLoudspeaker;

[RegisterComponent] public sealed partial class PoliticalLoudspeakerFortifyBuffComponent : Component
{
    [DataField] public float DamageCoefficient = 1f;
    [DataField] public HashSet<ProtoId<DamageTypePrototype>> ExcludedDamageTypes = new();
    [DataField] public TimeSpan EndTime;
}
