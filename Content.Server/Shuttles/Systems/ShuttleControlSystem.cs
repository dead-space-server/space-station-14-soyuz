using System.Diagnostics.CodeAnalysis;
using Content.Server.Shuttles.Events;
using Content.Shared.Popups;

namespace Content.Server.Shuttles.Systems;

/// <summary>
/// Provides one server-authoritative extension point for features that temporarily forbid shuttle control.
/// </summary>
public sealed class ShuttleControlSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public bool CanControl(
        EntityUid gridUid,
        ShuttleControlType controlType,
        EntityUid? user = null)
    {
        return CanControl(gridUid, controlType, out _, user);
    }

    public bool CanControl(
        EntityUid gridUid,
        ShuttleControlType controlType,
        [NotNullWhen(false)] out string? reason,
        EntityUid? user = null)
    {
        var attempt = new ShuttleControlAttemptEvent(gridUid, controlType, user);
        RaiseLocalEvent(gridUid, ref attempt, true);

        reason = attempt.Reason;
        if (!attempt.Cancelled)
            return true;

        reason ??= string.Empty;
        if (user is { Valid: true } recipient && !string.IsNullOrWhiteSpace(reason))
            _popup.PopupEntity(reason, recipient, recipient, PopupType.Medium);

        return false;
    }
}
