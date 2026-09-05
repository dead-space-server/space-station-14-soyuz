// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

namespace Content.Server.Shuttles.Events;

/// <summary>
/// Raised on a shuttle grid before a player or server system is allowed to control its movement.
/// Feature systems may cancel this without changing the shuttle's components or physics state.
/// </summary>
[ByRefEvent]
public record struct ShuttleControlAttemptEvent(
    EntityUid GridUid,
    ShuttleControlType ControlType,
    EntityUid? User,
    bool Cancelled = false,
    string? Reason = null);

public enum ShuttleControlType : byte
{
    Pilot,
    Movement,
    Docking,
    Ftl,
}
