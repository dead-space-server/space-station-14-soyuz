// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Shared.Damage;
namespace Content.Server.PoliticalLoudspeaker;

[RegisterComponent]
public sealed partial class PoliticalLoudspeakerHealOverTimeComponent : Component
{
    [DataField] public TimeSpan EndTime;
    [DataField] public TimeSpan NextTick;

    [DataField] public TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    [DataField] public DamageSpecifier HealPerTick = new();
}
