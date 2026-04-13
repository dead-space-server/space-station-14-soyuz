using System;
using Content.Shared.Audio;

namespace Content.Server.DeadSpace.Soyuz.Sterility.Components;

[RegisterComponent]
public sealed partial class SyringeSterilizerComponent : Component
{
    [DataField]
    public string SlotId = "sterilizer_slot";

    [DataField]
    public float Duration = 4f;

    [DataField]
    public float SuccessDuration = 1f;

    [DataField]
    public SoundSpecifier? StartSound = new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");

    [DataField]
    public SoundSpecifier? FinishSound = new SoundPathSpecifier("/Audio/Machines/ding.ogg");

    [ViewVariables(VVAccess.ReadWrite)]
    public bool Running;

    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan SuccessEndsAt = TimeSpan.Zero;
}
