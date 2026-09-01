using Content.Server.Shuttles.Events;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Prevents an active or terminal-pending repair grid from being piloted or moved through shuttle controls.
/// The lock is derived from station-owned lifecycle state on every attempt and has no separate boolean flag.
/// </summary>
public sealed class RepairOrderShuttleControlSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShuttleControlAttemptEvent>(OnShuttleControlAttempt);
    }

    private void OnShuttleControlAttempt(ref ShuttleControlAttemptEvent args)
    {
        var query = EntityQueryEnumerator<RepairOrderStationComponent>();
        while (query.MoveNext(out _, out var station))
        {
            if (station.Active?.GridUid != args.GridUid &&
                !station.PendingCleanupGrids.Contains(args.GridUid))
                continue;

            args.Cancelled = true;
            args.Reason = Loc.GetString("repair-orders-shuttle-controls-locked");
            return;
        }
    }
}
