using Content.Shared.Audio;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared.DeadSpace.Soyuz.BlueInsulatingTape.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WindowRepairTapeComponent : Component
{
    [DataField, AutoNetworkedField]
    public float RepairFraction = 0.25f;

    [DataField, AutoNetworkedField]
    public float Delay = 2f;

    [DataField, AutoNetworkedField]
    public SoundSpecifier RepairBeginSound = new SoundPathSpecifier("/Audio/Items/Medical/ointment_begin.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier RepairEndSound = new SoundPathSpecifier("/Audio/Items/Medical/ointment_end.ogg");
}
