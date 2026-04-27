using Content.Shared.Damage;

namespace Content.Server.DeadSpace.Soyuz.Warhammer;

[RegisterComponent]
public sealed partial class WarhammerCritComponent : Component
{
    [DataField("chance")]
    public float Chance = 0.25f;

    [DataField("damageBonus")]
    public DamageSpecifier DamageBonus = new();

    [DataField("throwDistance")]
    public float ThrowDistance = 4f;

    [DataField("throwSpeed")]
    public float ThrowSpeed = 10f;
}
