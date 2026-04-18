using System;
using Content.Shared.DeadSpace.Virus.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.DeadSpace.Soyuz.Sterility.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SterilityComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Contamination;

    [DataField, AutoNetworkedField]
    public bool IsOpened;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextExposureTick = TimeSpan.Zero;

    [DataField]
    public TimeSpan ExposureInterval = TimeSpan.Zero;

    [DataField]
    public float ExposurePerTick;

    [DataField]
    public float PerUseIncrease = 13.5f;

    [ViewVariables(VVAccess.ReadWrite)]
    public VirusData? StoredVirusData;
}
