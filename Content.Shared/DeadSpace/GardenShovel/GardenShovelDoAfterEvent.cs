using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.GardenShovel;

[Serializable, NetSerializable]
public sealed partial class GardenShovelDoAfterEvent : SimpleDoAfterEvent
{
}
