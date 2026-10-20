// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.MeteorDefense;

[Serializable, NetSerializable]
public enum MeteorDefenseUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class MeteorDefenseSetEnabledMessage(bool enabled) : BoundUserInterfaceMessage
{
    public bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class MeteorDefenseSetMaxChargeMessage(float maxCharge) : BoundUserInterfaceMessage
{
    public float MaxCharge = maxCharge;
}

[Serializable, NetSerializable]
public sealed class MeteorDefenseBoundUserInterfaceState(
    bool enabled, float currentCharge, float maxCharge, float maxAllowedCharge,
    float energyPerIntercept, float maxChargeRate, int readyIntercepts) : BoundUserInterfaceState
{
    public bool Enabled = enabled;
    public float CurrentCharge = currentCharge;
    public float MaxCharge = maxCharge;
    public float MaxAllowedCharge = maxAllowedCharge;
    public float EnergyPerIntercept = energyPerIntercept;
    public float MaxChargeRate = maxChargeRate;
    public int ReadyIntercepts = readyIntercepts;
}
