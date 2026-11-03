// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Content.Shared.DeadSpace._Soyuz.EvacZones;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Client.DeadSpace._Soyuz.EvacZones;

/// <summary>
/// Голограммы зон в мире
/// </summary>
public sealed class EvacZoneOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly SharedMapSystem _map;
    private readonly List<Vector2> _tris = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public EvacZoneOverlay()
    {
        IoCManager.InjectDependencies(this);
        _map = _entMan.System<SharedMapSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var mapUid = _map.GetMapOrInvalid(args.MapId);
        if (!_entMan.TryGetComponent(mapUid, out EvacZoneMapComponent? zones) || zones.Strokes.Count == 0)
            return;

        var handle = args.WorldHandle;
        var pulse = 0.30f + 0.06f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 2f);

        foreach (var stroke in zones.Strokes)
        {
            _tris.Clear();
            EvacZoneGeometry.AppendStroke(_tris, stroke.Points, EvacZoneConstants.Thickness / 2f, false);
            EvacZoneGeometry.Draw(handle, _tris, stroke.Color.WithAlpha(pulse));
        }
    }
}

public sealed class EvacZoneOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new EvacZoneOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<EvacZoneOverlay>();
    }
}
