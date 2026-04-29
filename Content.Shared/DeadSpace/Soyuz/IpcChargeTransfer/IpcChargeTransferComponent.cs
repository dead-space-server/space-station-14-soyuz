using Robust.Shared.GameStates;

namespace Content.Shared.DeadSpace.Soyuz.IpcChargeTransfer;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class IpcChargeTransferComponent : Component
{
    /// <summary>
    /// How much charge in joules can be transferred per hand interaction.
    /// </summary>
    [DataField]
    public float TransferPerUse = 2000f;

    /// <summary>
    /// Minimal charge donor keeps for itself.
    /// </summary>
    [DataField]
    public float DonorReserve = 1000f;

    /// <summary>
    /// Reverse transfer lock duration after draining.
    /// Prevents A->B then B->A ping-pong loops.
    /// </summary>
    [DataField]
    public TimeSpan ReciprocalLockTime = TimeSpan.FromSeconds(5);

    /// <summary>
    /// If set, this entity cannot drain charge from this target while lock is active.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? LockedDrainTarget;

    [DataField, AutoNetworkedField]
    public TimeSpan LockedDrainUntil;
}

