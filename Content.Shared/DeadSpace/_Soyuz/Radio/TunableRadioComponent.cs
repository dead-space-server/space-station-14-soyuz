// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.Radio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.Radio;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TunableRadioComponent : Component
{
    public const int MinimumFrequency = 1000;
    public const int MaximumFrequency = 9999;
    public static readonly ProtoId<RadioChannelPrototype> Channel = "Handheld";

    [DataField, AutoNetworkedField]
    public int Frequency = 1330;
}

[Serializable, NetSerializable]
public enum TunableRadioUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class TunableRadioUiState(int frequency, bool microphoneEnabled, bool speakerEnabled)
    : BoundUserInterfaceState
{
    public readonly int Frequency = frequency;
    public readonly bool MicrophoneEnabled = microphoneEnabled;
    public readonly bool SpeakerEnabled = speakerEnabled;
}

[Serializable, NetSerializable]
public sealed class TunableRadioSetFrequencyMessage(int frequency) : BoundUserInterfaceMessage
{
    public readonly int Frequency = frequency;
}

[Serializable, NetSerializable]
public sealed class TunableRadioToggleMessage(bool microphone, bool enabled) : BoundUserInterfaceMessage
{
    public readonly bool Microphone = microphone;
    public readonly bool Enabled = enabled;
}
