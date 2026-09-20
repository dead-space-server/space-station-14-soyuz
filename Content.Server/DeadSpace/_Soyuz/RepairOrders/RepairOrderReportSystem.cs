// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.DeadSpace._Soyuz.RepairOrders;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.DeadSpace._Soyuz.RepairOrders;

/// <summary>
/// Produces physical, server-authored reports for active and terminal repair orders.
/// </summary>
public sealed class RepairOrderReportSystem : EntitySystem
{
    private static readonly EntProtoId GeneralStaffStamp = "RubberStampCentcom";

    private static readonly SoundSpecifier PrintSound =
        new SoundPathSpecifier("/Audio/Machines/short_print_and_rip.ogg");

    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly ITileDefinitionManager _tiles = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RepairOrderValidationSystem _validation = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StationSystem _station = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("repair_orders");
        Subs.BuiEvents<RepairOrderConsoleComponent>(RepairOrderUiKey.Key, subs =>
        {
            subs.Event<RepairOrderPrintReportMessage>(OnPrintReport);
        });
    }

    private void OnPrintReport(Entity<RepairOrderConsoleComponent> console, ref RepairOrderPrintReportMessage args)
    {
        var stationUid = _station.GetOwningStation(console.Owner);
        if (stationUid == null || !TryComp<RepairOrderStationComponent>(stationUid.Value, out var state))
        {
            Fail(console.Owner, args.Actor, "repair-orders-error-no-station", "console has no owning station");
            return;
        }

        if (!_access.IsAllowed(args.Actor, console.Owner))
        {
            Fail(console.Owner, args.Actor, "repair-orders-error-access", "actor lacks engineering access");
            return;
        }

        ActiveRepairOrder? active = null;
        CompletedRepairOrder? completed = null;
        ProtoId<RepairOrderPrototype> orderId;
        if (state.Active is { } activeOrder && activeOrder.RuntimeId == args.RuntimeId)
        {
            active = activeOrder;
            orderId = activeOrder.Prototype;
        }
        else if (state.Completed is { } completedOrder && completedOrder.RuntimeId == args.RuntimeId)
        {
            completed = completedOrder;
            orderId = completedOrder.Prototype;
        }
        else
        {
            Fail(
                console.Owner,
                args.Actor,
                "repair-orders-error-report-unavailable",
                $"runtime order {args.RuntimeId} does not belong to the station's active or terminal result");
            return;
        }

        if (_timing.CurTime < console.Comp.NextReportPrint)
        {
            _popup.PopupEntity(
                Loc.GetString("repair-orders-error-printer-cooldown"),
                console.Owner,
                args.Actor,
                PopupType.Small);
            return;
        }

        if (!_prototype.TryIndex<RepairOrderPrototype>(orderId, out var order))
        {
            Fail(
                console.Owner,
                args.Actor,
                "repair-orders-error-report-unavailable",
                $"prototype {orderId} is missing");
            return;
        }

        string report;
        if (active != null)
        {
            if (active.ExpirationFrozen)
            {
                // This snapshot is immutable. Revalidating here would allow post-deadline repairs to leak into it.
                report = BuildFrozenExpirationReport(order, active);
            }
            else
            {
                if (_timing.CurTime >= active.ExpiresAt)
                {
                    Fail(
                        console.Owner,
                        args.Actor,
                        "repair-orders-error-report-terminal-pending",
                        $"active order {active.RuntimeId} reached its deadline before expiration was frozen");
                    return;
                }

                // A full authoritative pass makes the paper independent of deferred dirty-cell processing.
                if (!_validation.RevalidateAll(active.GridUid))
                {
                    Fail(
                        console.Owner,
                        args.Actor,
                        "repair-orders-error-blueprint",
                        $"grid {active.GridUid} has no ready validation runtime for report printing");
                    return;
                }

                report = BuildActiveReport(order, active);
            }
        }
        else
        {
            report = BuildTerminalReport(order, completed!);
        }

        var printed = Spawn("Paper", Transform(console.Owner).Coordinates);
        if (!TryComp<PaperComponent>(printed, out var paper))
        {
            QueueDel(printed);
            Fail(
                console.Owner,
                args.Actor,
                "repair-orders-error-report-unavailable",
                "the Paper entity prototype has no PaperComponent");
            return;
        }

        StampReport((printed, paper));
        // SetContent publishes the reading UI state, including the stamps already on the paper.
        _paper.SetContent((printed, paper), report);
        _metaData.SetEntityName(printed, Loc.GetString("repair-orders-report-paper-name"));
        _transform.DropNextTo(printed, console.Owner);
        _audio.PlayPvs(PrintSound, console.Owner);

        console.Comp.NextReportPrint = _timing.CurTime + console.Comp.ReportPrintCooldown;
        _sawmill.Info(
            $"Printed repair order report {args.RuntimeId} ({order.ID}) at {ToPrettyString(console.Owner)} " +
            $"for actor {ToPrettyString(args.Actor)}.");
    }

    private string BuildActiveReport(RepairOrderPrototype order, ActiveRepairOrder active)
    {
        var report = BeginReport(order);
        AddDamageSummary(report, active.DamageGeneration);
        AddExclusions(report, active.Exclusions, active.CurrentPoints, active.MaxPoints);
        AddLine(report, "repair-orders-report-progress", ("percent", RepairOrderProgress.CalculatePercent(
            active.CompletedTasks,
            active.TotalTasks)));
        AddLine(report, "repair-orders-report-tasks", ("completed", active.CompletedTasks), ("total", active.TotalTasks));
        AddLine(report, "repair-orders-report-points", ("current", active.CurrentPoints), ("max", active.MaxPoints));
        AddLine(report, "repair-orders-report-remaining", ("time", FormatRemaining(active.ExpiresAt - _timing.CurTime)));
        AddLine(report, "repair-orders-report-status", ("status", Loc.GetString("repair-orders-report-status-active")));
        return report.ToMarkup();
    }

    private string BuildFrozenExpirationReport(RepairOrderPrototype order, ActiveRepairOrder active)
    {
        var report = BeginReport(order);
        AddDamageSummary(report, active.DamageGeneration);
        AddExclusions(report, active.Exclusions, active.CurrentPoints, active.MaxPoints);
        AddLine(report, "repair-orders-report-final-progress", ("percent", RepairOrderProgress.CalculatePercent(
            active.CompletedTasks,
            active.TotalTasks)));
        AddLine(report, "repair-orders-report-tasks", ("completed", active.CompletedTasks), ("total", active.TotalTasks));
        AddLine(report, "repair-orders-report-final-points", ("current", active.FinalPoints), ("max", active.MaxPoints));
        AddLine(report, "repair-orders-report-reward-budget", ("budget", active.ExpiredRewardBudget));
        AddLine(
            report,
            "repair-orders-report-status",
            ("status", Loc.GetString("repair-orders-report-status-expired-pending")));
        AddLine(report, "repair-orders-report-timeout-reason");
        if (active.ExpiredRewardBudget > 0)
            AddLine(report, "repair-orders-report-partial-reward-pending-note");

        return report.ToMarkup();
    }

    private string BuildTerminalReport(RepairOrderPrototype order, CompletedRepairOrder completed)
    {
        var report = BeginReport(order);
        AddDamageSummary(report, completed.DamageGeneration);
        AddExclusions(report, completed.Exclusions, completed.FinalPoints, completed.MaxPoints);
        AddLine(report, "repair-orders-report-final-progress", ("percent", completed.RepairPercent));
        AddLine(
            report,
            "repair-orders-report-tasks",
            ("completed", completed.CompletedTasks),
            ("total", completed.TotalTasks));
        AddLine(
            report,
            "repair-orders-report-final-points",
            ("current", completed.FinalPoints),
            ("max", completed.MaxPoints));
        AddLine(report, "repair-orders-report-reward-budget", ("budget", completed.RewardBudget));

        if (completed.Result == RepairOrderResult.Completed)
        {
            AddLine(
                report,
                "repair-orders-report-status",
                ("status", Loc.GetString("repair-orders-report-status-completed")));
            return report.ToMarkup();
        }

        AddLine(
            report,
            "repair-orders-report-status",
            ("status", Loc.GetString("repair-orders-report-status-expired")));
        AddLine(report, "repair-orders-report-timeout-reason");
        if (completed.RewardBudget > 0)
            AddLine(report, "repair-orders-report-partial-reward-note");

        return report.ToMarkup();
    }

    public bool StampReport(Entity<PaperComponent> paper)
    {
        var prototype = _prototype.Index(GeneralStaffStamp);
        if (prototype.Components["Stamp"].Component is not StampComponent stamp) return false;
        return _paper.TryStamp(paper, new StampDisplayInfo
        {
            StampedName = stamp.StampedName, StampedColor = stamp.StampedColor,
            StampTexture = stamp.StampTexture, StampPatternTexture = stamp.StampPatternTexture,
            StampHeaderText = stamp.StampHeaderText, StampBackgroundText = stamp.StampBackgroundText,
            StampScale = stamp.StampScale, StampMainText = stamp.StampMainText,
            StampTextMaxScale = stamp.StampTextMaxScale, StampRotation = 0,
        }, stamp.StampState);
    }

    private void AddExclusions(FormattedMessage report, RepairTechnicalExclusionSnapshot? snapshot, int raw, int max)
    {
        var totals = snapshot?.Totals ?? new RepairExclusionTotals(0, 0, RepairTechnicalExclusion.MaxWaivedPoints(max), raw);
        AddLine(report, "repair-orders-waiver-report", ("count", totals.Count), ("used", totals.WaivedPoints),
            ("max", totals.MaxWaivedPoints), ("raw", totals.RawPoints), ("percent", totals.PenaltyPercent),
            ("penalty", totals.PenaltyPoints), ("final", totals.FinalPoints));
        if (snapshot == null) return;
        foreach (var requirement in snapshot.Requirements)
        {
            var name = Loc.GetString("repair-orders-waiver-unknown");
            if (requirement.Type != RepairTaskType.Tile && _prototype.TryIndex<EntityPrototype>(requirement.Prototype, out var entity))
                name = entity.Name;
            else if (requirement.Type == RepairTaskType.Tile && _tiles.TryGetDefinition(requirement.Prototype, out var tile))
                name = Loc.GetString(tile.Name);
            AddLine(report, "repair-orders-waiver-report-entry", ("name", FormattedMessage.EscapeText(name)),
                ("x", requirement.Cell.X), ("y", requirement.Cell.Y), ("points", requirement.Points));
        }
    }

    private void AddDamageSummary(FormattedMessage report, RepairDamageGenerationInfo? info)
    {
        AddLine(report, "repair-orders-damage-heading");
        report.AddText(RepairDamageSummary.Format(_prototype,
            info?.SelectedEvents.ToArray() ?? Array.Empty<string>(), detailed: true));
        report.PushNewline();
    }

    private FormattedMessage BeginReport(RepairOrderPrototype order)
    {
        var report = new FormattedMessage();
        AddLine(report, "repair-orders-report-title");
        report.PushNewline();
        AddLine(
            report,
            "repair-orders-report-object-name",
            ("value", FormattedMessage.EscapeText(Loc.GetString(order.ObjectName))));
        AddLine(
            report,
            "repair-orders-report-object-type",
            ("value", FormattedMessage.EscapeText(Loc.GetString(order.ObjectType))));
        AddLine(
            report,
            "repair-orders-report-order-name",
            ("value", FormattedMessage.EscapeText(Loc.GetString(order.Name))));
        AddLine(report, "repair-orders-report-difficulty",
            ("class", Loc.GetString(RepairOrderDifficulty.GetName(order.Difficulty))),
            ("difficulty", order.Difficulty), ("max", RepairOrderDifficulty.Maximum));
        return report;
    }

    private void AddLine(FormattedMessage report, string locId, params (string, object)[] args)
    {
        report.AddMarkupOrThrow(Loc.GetString(locId, args));
        report.PushNewline();
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        return $"{(int) remaining.TotalMinutes:00}:{remaining.Seconds:00}";
    }

    private void Fail(EntityUid console, EntityUid actor, string locKey, string reason)
    {
        _sawmill.Warning($"Repair order report at {ToPrettyString(console)} rejected: {reason}.");
        _popup.PopupEntity(Loc.GetString(locKey), console, actor, PopupType.Medium);
    }
}
