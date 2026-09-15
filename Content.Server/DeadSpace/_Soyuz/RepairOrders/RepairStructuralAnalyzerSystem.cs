using System.Numerics;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Authorizes structural-analyzer data on the server and sends only in-range unfinished tasks
/// to the specific player who currently owns an enabled analyzer.
/// </summary>
public sealed class RepairStructuralAnalyzerSystem : EntitySystem
{
    private const float SnapshotInterval = 0.25f;

    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<NetUserId, Dictionary<EntityUid, RepairAnalyzerTaskData[]>> _sentSnapshots = new();
    private float _snapshotAccumulator;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RepairStructuralAnalyzerComponent, ItemToggledEvent>(OnAnalyzerToggled);
        SubscribeLocalEvent<RepairStructuralAnalyzerComponent, GotEquippedEvent>(OnAnalyzerEquipped);
        SubscribeLocalEvent<RepairStructuralAnalyzerComponent, GotUnequippedEvent>(OnAnalyzerUnequipped);
        SubscribeLocalEvent<RepairStructuralAnalyzerComponent, GotEquippedHandEvent>(OnAnalyzerEquippedHand);
        SubscribeLocalEvent<RepairStructuralAnalyzerComponent, GotUnequippedHandEvent>(OnAnalyzerUnequippedHand);
        SubscribeLocalEvent<RepairStructuralAnalyzerComponent, ComponentShutdown>(OnAnalyzerShutdown);

        _player.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        _player.PlayerStatusChanged -= OnPlayerStatusChanged;
        _sentSnapshots.Clear();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _snapshotAccumulator += frameTime;
        if (_snapshotAccumulator < SnapshotInterval)
            return;

        _snapshotAccumulator %= SnapshotInterval;
        foreach (var session in _player.Sessions)
        {
            if (session.Status == SessionStatus.InGame)
                RefreshSession(session);
        }
    }

    private void OnAnalyzerToggled(
        Entity<RepairStructuralAnalyzerComponent> analyzer,
        ref ItemToggledEvent args)
    {
        if (args.User is { } user)
            RefreshUser(user);
        else
            RequestImmediateRefresh();
    }

    private void OnAnalyzerEquipped(
        Entity<RepairStructuralAnalyzerComponent> analyzer,
        ref GotEquippedEvent args)
    {
        RefreshUser(args.Equipee);
    }

    private void OnAnalyzerUnequipped(
        Entity<RepairStructuralAnalyzerComponent> analyzer,
        ref GotUnequippedEvent args)
    {
        RefreshUser(args.Equipee);
    }

    private void OnAnalyzerEquippedHand(
        Entity<RepairStructuralAnalyzerComponent> analyzer,
        ref GotEquippedHandEvent args)
    {
        RefreshUser(args.User);
    }

    private void OnAnalyzerUnequippedHand(
        Entity<RepairStructuralAnalyzerComponent> analyzer,
        ref GotUnequippedHandEvent args)
    {
        RefreshUser(args.User);
    }

    private void OnAnalyzerShutdown(
        Entity<RepairStructuralAnalyzerComponent> analyzer,
        ref ComponentShutdown args)
    {
        // A terminating analyzer may no longer have a reliable owner. Recheck every authorized session next update.
        RequestImmediateRefresh();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.InGame)
        {
            RefreshSession(args.Session);
            return;
        }

        if (_sentSnapshots.Remove(args.Session.UserId, out var snapshots) && snapshots.Count > 0)
            RaiseNetworkEvent(new RepairAnalyzerSnapshotEvent(Array.Empty<RepairAnalyzerGridSnapshot>()), args.Session);
    }

    private void RequestImmediateRefresh()
    {
        _snapshotAccumulator = SnapshotInterval;
    }

    private void RefreshUser(EntityUid user)
    {
        if (_player.TryGetSessionByEntity(user, out var session) && session.Status == SessionStatus.InGame)
            RefreshSession(session);
    }

    private void RefreshSession(ICommonSession session)
    {
        var snapshots = new Dictionary<EntityUid, RepairAnalyzerTaskData[]>();
        if (session.AttachedEntity is { Valid: true } user &&
            Exists(user) &&
            TryGetEnabledAnalyzerRange(user, out var range))
        {
            BuildAuthorizedSnapshots(user, range, snapshots);
        }

        if (_sentSnapshots.TryGetValue(session.UserId, out var previous) &&
            SnapshotsEqual(previous, snapshots))
        {
            return;
        }

        SendSnapshot(session, snapshots);
        _sentSnapshots[session.UserId] = snapshots;
    }

    private void SendSnapshot(
        ICommonSession session,
        IReadOnlyDictionary<EntityUid, RepairAnalyzerTaskData[]> snapshots)
    {
        var networkSnapshots = new RepairAnalyzerGridSnapshot[snapshots.Count];
        var index = 0;
        foreach (var (gridUid, tasks) in snapshots)
        {
            networkSnapshots[index++] = new RepairAnalyzerGridSnapshot(GetNetEntity(gridUid), tasks);
        }

        RaiseNetworkEvent(new RepairAnalyzerSnapshotEvent(networkSnapshots), session);
    }

    /// <summary>
    /// Server-side access check. Only a directly held or directly equipped enabled analyzer grants a range.
    /// </summary>
    private bool TryGetEnabledAnalyzerRange(EntityUid user, out float range)
    {
        range = 0f;

        if (_inventory.TryGetContainerSlotEnumerator(user, out var enumerator))
        {
            while (enumerator.MoveNext(out var slot))
            {
                foreach (var item in slot.ContainedEntities)
                    range = MathF.Max(range, GetEnabledRange(item));
            }
        }

        foreach (var hand in _hands.EnumerateHands(user))
        {
            if (_hands.TryGetHeldItem(user, hand, out var held))
                range = MathF.Max(range, GetEnabledRange(held.Value));
        }

        return range > 0f;
    }

    private float GetEnabledRange(EntityUid analyzer)
    {
        if (!TryComp<RepairStructuralAnalyzerComponent>(analyzer, out var component) ||
            !TryComp<ItemToggleComponent>(analyzer, out var toggle) ||
            !toggle.Activated)
        {
            return 0f;
        }

        return MathF.Max(0f, component.Range);
    }

    private void BuildAuthorizedSnapshots(
        EntityUid user,
        float range,
        Dictionary<EntityUid, RepairAnalyzerTaskData[]> snapshots)
    {
        var viewerCoordinates = _transform.GetMapCoordinates(user);
        var rangeSquared = range * range;
        var query = EntityQueryEnumerator<RepairBlueprintComponent, MapGridComponent, TransformComponent>();

        while (query.MoveNext(out var gridUid, out var blueprint, out var grid, out var gridTransform))
        {
            if (!blueprint.Ready ||
                gridTransform.MapID != viewerCoordinates.MapId ||
                !TryComp<RepairOrderStationComponent>(blueprint.Station, out var station) ||
                station.Active is not { } active ||
                active.GridUid != gridUid ||
                !active.BlueprintReady ||
                active.Prototype != blueprint.OrderPrototype)
            {
                continue;
            }

            var worldMatrix = _transform.GetWorldMatrix(gridUid);
            var tasks = new List<RepairAnalyzerTaskData>();
            foreach (var cellTasks in blueprint.TasksByCell.Values)
            {
                foreach (var task in cellTasks)
                {
                    if (task.State == RepairTaskState.Correct)
                        continue;

                    var localPosition = task.Type == RepairTaskType.Tile
                        ? new Vector2(
                            (task.Cell.X + 0.5f) * grid.TileSize,
                            (task.Cell.Y + 0.5f) * grid.TileSize)
                        : task.ExpectedLocalPosition;
                    var worldPosition = Vector2.Transform(localPosition, worldMatrix);
                    if (Vector2.DistanceSquared(viewerCoordinates.Position, worldPosition) > rangeSquared)
                        continue;

                    var expectedPrototype = task.Type == RepairTaskType.Tile
                        ? task.ExpectedTilePrototype ?? string.Empty
                        : task.ExpectedEntityPrototype ?? string.Empty;

                    tasks.Add(new RepairAnalyzerTaskData(
                        task.Type,
                        localPosition,
                        task.DisplayLocalRotation,
                        expectedPrototype,
                        task.State));
                }
            }

            if (tasks.Count == 0)
                continue;

            tasks.Sort(CompareTasks);
            snapshots.Add(gridUid, tasks.ToArray());
        }
    }

    private static int CompareTasks(RepairAnalyzerTaskData left, RepairAnalyzerTaskData right)
    {
        var comparison = left.LocalPosition.X.CompareTo(right.LocalPosition.X);
        if (comparison != 0)
            return comparison;

        comparison = left.LocalPosition.Y.CompareTo(right.LocalPosition.Y);
        if (comparison != 0)
            return comparison;

        comparison = left.Type.CompareTo(right.Type);
        if (comparison != 0)
            return comparison;

        comparison = string.Compare(left.ExpectedPrototype, right.ExpectedPrototype, StringComparison.Ordinal);
        if (comparison != 0)
            return comparison;

        comparison = left.LocalRotation.Theta.CompareTo(right.LocalRotation.Theta);
        return comparison != 0 ? comparison : left.State.CompareTo(right.State);
    }

    private static bool SnapshotsEqual(
        IReadOnlyDictionary<EntityUid, RepairAnalyzerTaskData[]> left,
        IReadOnlyDictionary<EntityUid, RepairAnalyzerTaskData[]> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (gridUid, leftTasks) in left)
        {
            if (!right.TryGetValue(gridUid, out var rightTasks) || leftTasks.Length != rightTasks.Length)
                return false;

            for (var i = 0; i < leftTasks.Length; i++)
            {
                var leftTask = leftTasks[i];
                var rightTask = rightTasks[i];
                if (leftTask.Type != rightTask.Type ||
                    leftTask.LocalPosition != rightTask.LocalPosition ||
                    leftTask.LocalRotation != rightTask.LocalRotation ||
                    leftTask.ExpectedPrototype != rightTask.ExpectedPrototype ||
                    leftTask.State != rightTask.State)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
