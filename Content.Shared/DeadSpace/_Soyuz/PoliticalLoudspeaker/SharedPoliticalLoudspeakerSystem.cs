// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks

using Content.Shared.Actions;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared.DeadSpace._Soyuz.PoliticalLoudspeaker;

public sealed class SharedPoliticalLoudspeakerSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {  base.Initialize();

        SubscribeLocalEvent<PoliticalLoudspeakerComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<PoliticalLoudspeakerSpeedBuffComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<PoliticalLoudspeakerSpeedBuffComponent, ComponentStartup>(OnSpeedBuffStartup);
        SubscribeLocalEvent<PoliticalLoudspeakerSpeedBuffComponent, ComponentShutdown>(OnSpeedBuffShutdown);
        SubscribeLocalEvent<PoliticalLoudspeakerSpeedBuffComponent, AfterAutoHandleStateEvent>(OnSpeedBuffAfterAutoHandleState);
    }

    private void OnGetActions(Entity<PoliticalLoudspeakerComponent> ent, ref GetItemActionsEvent args)
    {   
        if(!args.InHands) return;

        args.AddAction(ref ent.Comp.HealActionEntity,   ent.Comp.HealAction);
        args.AddAction(ref ent.Comp.SpeedActionEntity,  ent.Comp.SpeedAction);
        args.AddAction(ref ent.Comp.FortifyActionEntity,ent.Comp.FortifyAction);
    }

    private void OnRefreshMovementSpeed(Entity<PoliticalLoudspeakerSpeedBuffComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {   args.ModifySpeed(ent.Comp.SpeedMultiplier, ent.Comp.SpeedMultiplier); }

    private void OnSpeedBuffStartup(Entity<PoliticalLoudspeakerSpeedBuffComponent> ent, ref ComponentStartup args)
    {  _movementSpeed.RefreshMovementSpeedModifiers(ent.Owner); }

    private void OnSpeedBuffShutdown(Entity<PoliticalLoudspeakerSpeedBuffComponent> ent, ref ComponentShutdown args)
    {   _movementSpeed.RefreshMovementSpeedModifiers(ent.Owner);  }

    private void OnSpeedBuffAfterAutoHandleState(Entity<PoliticalLoudspeakerSpeedBuffComponent> ent, ref AfterAutoHandleStateEvent args)
    { _movementSpeed.RefreshMovementSpeedModifiers(ent.Owner); }

    public (float SpeechRangeMultiplier, float TtsVolumeMultiplier) GetSpeechModifiers(
        EntityUid speaker,
        HandsComponent? hands = null)
    {
        var speechRangeMultiplier = 1f;
        var ttsVolumeMultiplier = 1f;

        if (!Resolve(speaker, ref hands, false))
            return (speechRangeMultiplier, ttsVolumeMultiplier);

        foreach (var held in _hands.EnumerateHeld((speaker, hands)))
        {
            if (!TryComp<PoliticalLoudspeakerComponent>(held, out var loudspeaker))
                continue;

            speechRangeMultiplier = MathF.Max(speechRangeMultiplier, loudspeaker.SpeechRangeMultiplier);
            ttsVolumeMultiplier = MathF.Max(ttsVolumeMultiplier, loudspeaker.TtsVolumeMultiplier);
        }

        return (speechRangeMultiplier, ttsVolumeMultiplier);
    }
}
