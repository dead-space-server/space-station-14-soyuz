using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.PoliticalLoudspeaker; // DS14-PoliticalLoudspeaker
using Content.Shared.Speech;
using Content.Shared.Speech.Components;

namespace Content.Server.Speech.EntitySystems;

/// <summary>
///     This system redirects local chat messages to listening entities (e.g., radio microphones).
/// </summary>
public sealed class ListeningSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly SharedPoliticalLoudspeakerSystem _politicalLoudspeaker = default!; // DS14-PoliticalLoudspeaker

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpeak);
    }

    private void OnSpeak(EntitySpokeEvent ev)
    {
        PingListeners(ev.Source, ev.Message, ev.ObfuscatedMessage);
    }

    public void PingListeners(EntityUid source, string message, string? obfuscatedMessage)
    {
        // TODO whispering / audio volume? Microphone sensitivity?
        // for now, whispering just arbitrarily reduces the listener's max range.

        var xformQuery = GetEntityQuery<TransformComponent>();
        var sourceXform = xformQuery.GetComponent(source);
        var sourcePos = _xforms.GetWorldPosition(sourceXform, xformQuery);

        var attemptEv = new ListenAttemptEvent(source);
        var ev = new ListenEvent(message, source);
        var obfuscatedEv = obfuscatedMessage == null ? null : new ListenEvent(obfuscatedMessage, source);
        var query = EntityQueryEnumerator<ActiveListenerComponent, TransformComponent>();
        // DS14-PoliticalLoudspeaker-start: held loudspeakers extend listener pickup for normal speech
        var speechRangeMultiplier = obfuscatedMessage == null
            ? _politicalLoudspeaker.GetSpeechModifiers(source).SpeechRangeMultiplier
            : 1f;
        // DS14-PoliticalLoudspeaker-end

        while(query.MoveNext(out var listenerUid, out var listener, out var xform))
        {
            if (xform.MapID != sourceXform.MapID)
                continue;

            // range checks
            // TODO proper speech occlusion
            var distance = (sourcePos - _xforms.GetWorldPosition(xform, xformQuery)).LengthSquared();
            var listenRange = listener.Range * speechRangeMultiplier; // DS14-PoliticalLoudspeaker
            if (distance > listenRange * listenRange) // DS14-PoliticalLoudspeaker
                continue;

            RaiseLocalEvent(listenerUid, attemptEv);
            if (attemptEv.Cancelled)
            {
                attemptEv.Uncancel();
                continue;
            }

            if (obfuscatedEv != null && distance > ChatSystem.WhisperClearRange)
                RaiseLocalEvent(listenerUid, obfuscatedEv);
            else
                RaiseLocalEvent(listenerUid, ev);
        }
    }
}
