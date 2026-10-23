// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.CartridgeLoader;

[Serializable, NetSerializable]
public enum PdaTimerStatus : byte
{
    Idle,
    Running,
    Paused,
    Finished,
}

[Serializable, NetSerializable]
public enum PdaTimerAction : byte
{
    Start,
    Pause,
    Resume,
    Reset,
}

[Serializable, NetSerializable]
public sealed class TimerCartridgeState : BoundUserInterfaceState
{
    public const int MaxDurationSeconds = 24 * 60 * 60;
    public const int MaxLabelLength = 64;

    public readonly int DurationSeconds;
    public readonly string Label;
    public readonly PdaTimerStatus Status;
    public readonly TimeSpan EndsAt;
    public readonly TimeSpan Remaining;
    public readonly int Revision;

    public TimerCartridgeState(int durationSeconds, string label, PdaTimerStatus status,
        TimeSpan endsAt, TimeSpan remaining, int revision)
    {
        DurationSeconds = durationSeconds;
        Label = label;
        Status = status;
        EndsAt = endsAt;
        Remaining = remaining;
        Revision = revision;
    }
}

[Serializable, NetSerializable]
public sealed class TimerCartridgeMessage : CartridgeMessageEvent
{
    public readonly PdaTimerAction Action;
    public readonly int DurationSeconds;
    public readonly string Label;
    public readonly int Revision;

    public TimerCartridgeMessage(PdaTimerAction action, int durationSeconds, string label, int revision)
    {
        Action = action;
        DurationSeconds = durationSeconds;
        Label = label;
        Revision = revision;
    }
}
