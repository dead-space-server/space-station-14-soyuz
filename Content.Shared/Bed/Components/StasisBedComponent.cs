using Robust.Shared.GameStates;

namespace Content.Shared.Bed.Components;

/// <summary>
/// A <see cref="StrapComponent"/> that modifies a strapped entity's metabolic rate by the given multiplier
/// </summary>
[RegisterComponent]
public sealed partial class StasisBedComponent : Component
{
    /// <summary>
    /// What the metabolic update rate will be multiplied by (higher = slower metabolism)
    /// </summary>
    [DataField]
    public float Multiplier = 0.5f;
}

