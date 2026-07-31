// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks

using Robust.Shared.GameStates;
namespace Content.Shared.DeadSpace._Soyuz.PoliticalLoudspeaker;

[RegisterComponent,NetworkedComponent,AutoGenerateComponentState(true)]
public sealed partial class PoliticalLoudspeakerSpeedBuffComponent : Component
{
    [DataField,AutoNetworkedField] public float SpeedMultiplier=1f;

    [DataField]  public TimeSpan EndTime;
}
