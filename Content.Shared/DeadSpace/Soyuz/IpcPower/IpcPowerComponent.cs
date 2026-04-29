using Robust.Shared.GameStates;

namespace Content.Shared.DeadSpace.Soyuz.IpcPower;

[RegisterComponent]
public sealed partial class IpcPowerComponent : Component
{
    /// <summary>
    /// Passive IPC discharge rate in watts.
    /// 500 W with 450000 J gives 15 minutes from full to zero.
    /// </summary>
    [DataField]
    public float PassiveDrainRate = 500f;

    /// <summary>
    /// Charge rate applied while a portable recharger is equipped in back slot.
    /// </summary>
    [DataField]
    public float BackRechargerRate = 1000f;
}
