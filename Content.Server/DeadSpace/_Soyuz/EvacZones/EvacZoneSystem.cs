// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Shared.DeadSpace._Soyuz.EvacZones;
using Content.Shared.Popups;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.EvacZones;

public sealed class EvacZoneSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EvacZoneTabletComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<EvacZoneTabletComponent, EvacTabletSendMessage>(OnSend);
    }

    private void OnUiOpened(Entity<EvacZoneTabletComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not EvacTabletUiKey)
            return;

        UpdateUi(ent);
    }

    private void OnSend(Entity<EvacZoneTabletComponent> ent, ref EvacTabletSendMessage args)
    {
        if (_timing.CurTime < ent.Comp.NextUse)
        {
            _popup.PopupEntity(Loc.GetString("evac-cooldown"), ent.Owner, args.Actor);
            return;
        }

        var targetMap = _transform.GetMapCoordinates(ent.Owner).MapId;
        if (targetMap == MapId.Nullspace)
            return;

        var mapUid = _map.GetMapOrInvalid(targetMap);
        if (!Exists(mapUid))
            return;

        ent.Comp.NextUse = _timing.CurTime + ent.Comp.Cooldown;

        var strokes = Sanitize(args.Strokes, ent.Comp);
        var zones = EnsureComp<EvacZoneMapComponent>(mapUid);

        // Считаем, какие зоны появились (для текста объявления).
        var added = new int[ent.Comp.Palette.Count];
        var addedTotal = 0;
        foreach (var stroke in strokes)
        {
            if (zones.Strokes.Exists(old => SameStroke(old, stroke)))
                continue;

            for (var i = 0; i < ent.Comp.Palette.Count; i++)
            {
                if (ent.Comp.Palette[i].Color != stroke.Color)
                    continue;

                added[i]++;
                addedTotal++;
                break;
            }
        }

        zones.Strokes = strokes;
        Dirty(mapUid, zones);

        if (args.Announce && ent.Comp.Announcement is { } text && addedTotal > 0)
        {
            var parts = new List<string>();
            for (var i = 0; i < added.Length; i++)
            {
                if (added[i] > 0)
                {
                    parts.Add(Loc.GetString("evac-zones-summary-entry",
                        ("name", Loc.GetString(ent.Comp.Palette[i].Name)), ("count", added[i])));
                }
            }

            var message = Loc.GetString(text) + "\n" +
                          Loc.GetString("evac-zones-summary", ("list", string.Join(", ", parts)));

            _chat.DispatchGlobalAnnouncement(
                message,
                Loc.GetString(ent.Comp.AnnouncementSender),
                true,
                ent.Comp.AnnouncementSound,
                ent.Comp.AnnouncementColor);
        }

        _popup.PopupEntity(Loc.GetString("evac-sent"), ent.Owner, args.Actor);
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<EvacZoneTabletComponent> ent)
    {
        var own = _transform.GetMapCoordinates(ent.Owner);
        var targetMap = own.MapId;
        var strokes = new List<EvacZoneStroke>();

        var mapUid = _map.GetMapOrInvalid(targetMap);
        if (TryComp<EvacZoneMapComponent>(mapUid, out var zones))
        {
            foreach (var stroke in zones.Strokes)
                strokes.Add(stroke.Clone());
        }

        var state = new EvacTabletBuiState(
            targetMap,
            own.Position,
            strokes,
            ent.Comp.Palette,
            ent.Comp.Announcement != null);

        _ui.SetUiState(ent.Owner, EvacTabletUiKey.Key, state);
    }

    private static List<EvacZoneStroke> Sanitize(List<EvacZoneStroke> input, EvacZoneTabletComponent tablet)
    {
        var result = new List<EvacZoneStroke>();
        foreach (var stroke in input)
        {
            if (result.Count >= EvacZoneConstants.MaxStrokes)
                break;

            Color? color = null;
            foreach (var entry in tablet.Palette)
            {
                if (Close(entry.Color, stroke.Color))
                {
                    color = entry.Color;
                    break;
                }
            }

            if (color == null)
                continue;

            var points = new List<Vector2>();
            foreach (var point in stroke.Points)
            {
                if (points.Count >= EvacZoneConstants.MaxPointsPerStroke)
                    break;

                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
                    continue;

                if (MathF.Abs(point.X) > EvacZoneConstants.MaxCoordinate ||
                    MathF.Abs(point.Y) > EvacZoneConstants.MaxCoordinate)
                    continue;

                points.Add(point);
            }

            if (points.Count == 0)
                continue;

            result.Add(new EvacZoneStroke(color.Value, points));
        }

        return result;
    }

    private static bool Close(Color a, Color b)
    {
        const float eps = 0.02f;
        return MathF.Abs(a.R - b.R) < eps && MathF.Abs(a.G - b.G) < eps && MathF.Abs(a.B - b.B) < eps;
    }

    private static bool SameStroke(EvacZoneStroke a, EvacZoneStroke b)
    {
        if (a.Color != b.Color || a.Points.Count != b.Points.Count)
            return false;

        for (var i = 0; i < a.Points.Count; i++)
        {
            if (a.Points[i] != b.Points[i])
                return false;
        }

        return true;
    }
}