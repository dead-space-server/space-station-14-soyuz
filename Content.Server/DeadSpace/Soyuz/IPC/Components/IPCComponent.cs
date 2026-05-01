// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Shared.Alert;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace.IPC.Components;

[RegisterComponent]
public sealed partial class IPCComponent : Component
{
    public const short MaxBatteryAlertLevels = 10;

    /// <summary>
    /// Пассивный расход энергии
    /// </summary>
    [DataField, ViewVariables]
    public float IdleDrainRate = 3.5f;

    /// <summary>
    /// Порог низкого заряда батареи
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public float BatteryLowThreshold = 0.01f;

    /// <summary>
    /// Штраф к передвижению при низком заряде
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public float MovementPenalty = 0.2f;

    [DataField(readOnly: true)]
    public ProtoId<AlertPrototype> BatteryAlert = "IpcBattery"; // DS14

    [DataField(readOnly: true)]
    public ProtoId<AlertPrototype> NoBatteryAlert = "IpcBatteryNone"; // DS14

    [DataField(readOnly: true)]
    public EntProtoId DrainBatteryAction = "ActionDrainBattery";

    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? ActionEntity;

    [ViewVariables(VVAccess.ReadOnly)]
    public bool DrainActivated;

    [ViewVariables(VVAccess.ReadOnly)]
    public short LastBatteryLevel;

    public TimeSpan NextBatteryAlertUpdate;

    // DS14-start: merged from Soyuz IpcPower/IpcChargeTransfer.
    [DataField]
    public float PassiveDrainRate = 500f;

    [DataField]
    public float BackRechargerRate = 1000f;

    // DS14-start: charging (draining) tuning.
    /// <summary>
    /// How efficiently drained power is converted into IPC battery charge.
    /// </summary>
    [DataField]
    public float DrainEfficiency = 1f;

    /// <summary>
    /// Maximum rate (J/s) at which IPC can drain a power source while charging.
    /// </summary>
    [DataField]
    public float DrainRate = 20000f; // DS14-value: 2000 -> 20000

    /// <summary>
    /// DoAfter duration in seconds for each drain tick.
    /// </summary>
    [DataField]
    public float DrainTime = 1f;
    // DS14-end

    /// <summary>
    /// Runtime VV helper.
    /// Set 0..100 to force battery charge percent every tick.
    /// Set -1 to disable override.
    /// </summary>
    [DataField]
    public float SetChargePercent = -1f;

    [DataField]
    public float TransferPercentPerUse = 0.05f;

    [DataField]
    public float TransferPerUse = 2000f;

    [DataField]
    public float DonorReserve = 1000f;

    [DataField]
    public TimeSpan ReciprocalLockTime = TimeSpan.FromSeconds(5);

    [DataField]
    public EntityUid? LockedDrainTarget;

    [DataField]
    public TimeSpan LockedDrainUntil;
    // DS14-end

}
