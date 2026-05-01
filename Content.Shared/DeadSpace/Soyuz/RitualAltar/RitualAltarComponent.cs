namespace Content.Shared.DeadSpace.Soyuz.RitualAltar;

[RegisterComponent]
public sealed partial class RitualAltarComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public float EffectRadius = 5f;

    /// <summary>
    /// Max darkness at center (0..1). 0.8 means 80% black overlay.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public float MaxDarkness = 0.8f;

    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan EffectDuration = TimeSpan.FromSeconds(10);
}
