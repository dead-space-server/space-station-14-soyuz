// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks

using Content.Shared.Audio;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.BlueInsulatingTape;

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

[Serializable, NetSerializable]
public sealed partial class WindowRepairTapeDoAfterEvent : SimpleDoAfterEvent;
