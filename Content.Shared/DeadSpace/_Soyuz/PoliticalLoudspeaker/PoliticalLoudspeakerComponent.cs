// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks

using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint; 
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.PoliticalLoudspeaker;

[RegisterComponent , NetworkedComponent]
public sealed partial class PoliticalLoudspeakerComponent : Component
{
    [DataField] public float Range = 5f;
    [DataField] public TimeSpan Cooldown = TimeSpan.FromSeconds(30);
    [DataField] public float SpeechRangeMultiplier = 2f;
    [DataField] public float TtsVolumeMultiplier = 2f;

    [DataField] public TimeSpan SpeedDuration = TimeSpan.FromSeconds(10);
    [DataField] public float SpeedMultiplier = 1.15f;

    [DataField] public TimeSpan FortifyDuration = TimeSpan.FromSeconds(10);
    [DataField] public float FortifyCoefficient = 0.8f;

    [DataField] public FixedPoint2 HealAmount = FixedPoint2.New(10);
    [DataField] public HashSet<ProtoId<DamageTypePrototype>> HealDamageTypes = new();
    [DataField] public HashSet<ProtoId<DamageTypePrototype>> HealExcludedDamageTypes = new();

    [DataField] public TimeSpan HealDuration = TimeSpan.FromSeconds(3);
    [DataField] public TimeSpan HealTickInterval = TimeSpan.FromSeconds(0.5);

    [DataField] public HashSet<ProtoId<DamageTypePrototype>> FortifyExcludedDamageTypes = new();

    [DataField] public EntProtoId HealAction = "ActionPoliticalLoudspeakerHeal";
    [DataField] public EntProtoId SpeedAction = "ActionPoliticalLoudspeakerSpeed";  
    [DataField] public EntProtoId FortifyAction = "ActionPoliticalLoudspeakerFortify";

    [DataField] public EntProtoId SpeedStatusEffect = "StatusEffectPoliticalLoudspeakerSpeed";
    [DataField] public EntProtoId FortifyStatusEffect = "StatusEffectPoliticalLoudspeakerFortify";

    [DataField] public EntityUid? HealActionEntity;
    [DataField] public EntityUid? SpeedActionEntity;
    [DataField] public EntityUid? FortifyActionEntity;

    [DataField] public EntProtoId FlashEffectPrototype = "GrenadeFlashEffect";
}
