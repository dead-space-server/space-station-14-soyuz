// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using System.Numerics;
using System.Runtime.InteropServices;
using Content.Shared.DeadSpace._Soyuz.EvacZones;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Client.DeadSpace._Soyuz.EvacZones;

public static class EvacZoneGeometry
{
    public static void AppendStroke(List<Vector2> tris, IReadOnlyList<Vector2> points, float halfWidth, bool caps)
    {
        if (points.Count == 0)
            return;

        if (points.Count == 1)
        {
            AppendDisc(tris, points[0], halfWidth);
            return;
        }

        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var d = b - a;
            var len = d.Length();
            if (len < 1e-4f)
                continue;

            var n = new Vector2(-d.Y, d.X) / len * halfWidth;
            var v0 = a + n;
            var v1 = a - n;
            var v2 = b + n;
            var v3 = b - n;
            tris.Add(v0);
            tris.Add(v1);
            tris.Add(v2);
            tris.Add(v1);
            tris.Add(v3);
            tris.Add(v2);
        }

        if (!caps)
            return;

        foreach (var p in points)
            AppendDisc(tris, p, halfWidth);
    }

    public static void AppendDisc(List<Vector2> tris, Vector2 center, float radius)
    {
        const int segments = 8;
        for (var i = 0; i < segments; i++)
        {
            var a0 = MathF.Tau * i / segments;
            var a1 = MathF.Tau * (i + 1) / segments;
            tris.Add(center);
            tris.Add(center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius);
            tris.Add(center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius);
        }
    }

    public static void Draw(DrawingHandleBase handle, List<Vector2> tris, Color color)
    {
        if (tris.Count == 0)
            return;

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, CollectionsMarshal.AsSpan(tris), color);
    }
}

/// <summary>
/// Рисование зон на сканере масс.
/// </summary>
public static class EvacZoneRadarDrawer
{
    public static void Draw(DrawingHandleScreen handle, MapId mapId, Func<Vector2, Vector2> worldToView, float widthPx = 3f)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        var mapUid = entMan.System<SharedMapSystem>().GetMapOrInvalid(mapId);
        if (!entMan.TryGetComponent(mapUid, out EvacZoneMapComponent? zones))
            return;

        var tris = new List<Vector2>();
        var view = new List<Vector2>();
        foreach (var stroke in zones.Strokes)
        {
            view.Clear();
            tris.Clear();
            foreach (var p in stroke.Points)
                view.Add(worldToView(p));

            EvacZoneGeometry.AppendStroke(tris, view, widthPx / 2f, true);
            EvacZoneGeometry.Draw(handle, tris, stroke.Color.WithAlpha(0.85f));
        }
    }
}
