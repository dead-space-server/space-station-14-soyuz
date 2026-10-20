using Content.Shared.Chat;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;
using Content.Shared.DeadSpace.Languages.Prototypes;
using Robust.Shared.Audio; // DS14-Soyuz

namespace Content.Server.Radio;

// DS14-Soyuz start
[ByRefEvent]
public readonly record struct RadioReceiveEvent(string Message, EntityUid MessageSource, RadioChannelPrototype Channel, EntityUid RadioSource, MsgChatMessage ChatMsg, MsgChatMessage LexiconChatMsg, List<EntityUid> Receivers, ProtoId<LanguagePrototype>? LanguageId = null, ResolvedSoundSpecifier? Sound = null, AudioParams? SoundParams = null, int? FrequencyOverride = null)
{
    public int Frequency => FrequencyOverride ?? Channel.Frequency;
}
// DS14-Soyuz end

/// <summary>
/// Event raised on the parent entity of a headset radio when a radio message is received
/// </summary>
[ByRefEvent]
public readonly record struct HeadsetRadioReceiveRelayEvent(RadioReceiveEvent RelayedEvent);

/// <summary>
/// Use this event to cancel sending message per receiver
/// </summary>
[ByRefEvent]
public record struct RadioReceiveAttemptEvent(RadioChannelPrototype Channel, EntityUid RadioSource, EntityUid RadioReceiver, int? frequency = null) // DS14-Soyuz
{
    public readonly RadioChannelPrototype Channel = Channel;
    public readonly EntityUid RadioSource = RadioSource;
    public readonly EntityUid RadioReceiver = RadioReceiver;
    public readonly int Frequency = frequency ?? Channel.Frequency; // DS14-Soyuz
    public bool Cancelled = false;
}

/// <summary>
/// Use this event to cancel sending message to every receiver
/// </summary>
[ByRefEvent]
public record struct RadioSendAttemptEvent(EntityUid MessageSource, RadioChannelPrototype Channel, EntityUid RadioSource, int? frequency = null) // DS14-Soyuz
{
    public readonly EntityUid MessageSource = MessageSource; // DS14-Soyuz
    public readonly RadioChannelPrototype Channel = Channel;
    public readonly EntityUid RadioSource = RadioSource;
    public readonly int Frequency = frequency ?? Channel.Frequency; // DS14-Soyuz
    public bool Cancelled = false;
}
