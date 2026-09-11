// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks

using Content.Shared.Damage.Prototypes; 
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.PoliticalLoudspeaker;

[RegisterComponent] public sealed partial class PoliticalLoudspeakerFortifyBuffComponent : Component
{
    [DataField] public float DamageCoefficient = 1f;
    [DataField] public HashSet<ProtoId<DamageTypePrototype>> ExcludedDamageTypes = new();
    [DataField] public TimeSpan EndTime;
}
