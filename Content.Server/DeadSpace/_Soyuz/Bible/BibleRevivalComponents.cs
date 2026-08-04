// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks

namespace Content.Server.Bible.Components;

public sealed partial class BibleComponent
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float ReviveDeadChance;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float ReviveDeadDamageFraction = 0.99f;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool RestoreBloodOnRevive;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool ReviveDeadOncePerBody = true;
}

[RegisterComponent]
public sealed partial class BibleReviveAttemptedComponent : Component;
