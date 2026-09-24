// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks
using Content.Shared.DoAfter;

namespace Content.Server.DeadSpace._Soyuz.Botany;

[RegisterComponent]
public sealed partial class SoyuzPlantAnalyzerComponent : Component
{
    [DataField]
    public bool AdvancedScan;

    [DataField]
    public float ScanDelay = 0.5f;

    [DataField]
    public float AdvancedScanDelay = 1f;

    [ViewVariables]
    public DoAfterId? CurrentScan;
}
