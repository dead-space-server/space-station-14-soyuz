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
