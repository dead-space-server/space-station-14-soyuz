using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.DeadSpace.Soyuz.RitualAltar;

[RegisterComponent]
public sealed partial class RitualAltarInProgressComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan EndTime;

    [DataField]
    public bool HeartSacrificed;
}

