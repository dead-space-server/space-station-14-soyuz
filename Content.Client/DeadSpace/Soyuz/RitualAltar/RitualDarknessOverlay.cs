using System.Numerics;
using Content.Shared.DeadSpace.Soyuz.RitualAltar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.DeadSpace.Soyuz.RitualAltar;

public sealed class RitualDarknessOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> DarknessShader = "RitualDarknessCircle";

    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;

    private readonly ShaderInstance _shader;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly List<Entry> _entries = new();

    public RitualDarknessOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _protos.Index(DarknessShader).InstanceUnique();
        ZIndex = 250;
    }

    public void Start(EntityUid altar, float radius, float maxDarkness, TimeSpan duration)
    {
        var start = _timing.CurTime;
        var end = start + duration;

        _entries.Add(new Entry(altar, start, end, radius, maxDarkness));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var now = _timing.CurTime;

        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (now >= entry.EndTime || _entMan.Deleted(entry.Altar))
                _entries.RemoveAt(i);
        }
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_entries.Count == 0)
            return false;

        if (!_entMan.TryGetComponent(_players.LocalEntity, out EyeComponent? eyeComp))
            return false;

        return args.Viewport.Eye == eyeComp.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var now = _timing.CurTime;
        var invMatrix = args.Viewport.GetWorldToLocalMatrix();
        var renderScale = args.Viewport.RenderScale.X;
        var zoom = args.Viewport.Eye?.Zoom ?? Vector2.One;
        var length = zoom.X;
        var vertical = args.Viewport.Size.Y;

        var xformQuery = _entMan.GetEntityQuery<TransformComponent>();
        var xformSystem = _entMan.System<SharedTransformSystem>();

        foreach (var entry in _entries)
        {
            if (!xformQuery.TryGetComponent(entry.Altar, out var xform))
                continue;

            var fade = GetFade(now, entry.StartTime, entry.EndTime);
            var strength = Math.Clamp(entry.MaxDarkness * fade, 0f, 1f);
            if (strength <= 0.001f)
                continue;

            var worldPos = xformSystem.GetWorldPosition(xform);
            var pixelCenter = Vector2.Transform(worldPos, invMatrix);

            // Convert meters (tiles) -> pixels, taking zoom into account (same approach as StencilOverlay).
            var pixelMaxRange = entry.Radius * renderScale / length * EyeManager.PixelsPerMeter;

            _shader.SetParameter("position", new Vector2(pixelCenter.X, vertical - pixelCenter.Y));
            _shader.SetParameter("maxRange", pixelMaxRange);
            _shader.SetParameter("gradient", 1.0f);
            _shader.SetParameter("strength", strength);

            args.WorldHandle.UseShader(_shader);
            args.WorldHandle.DrawRect(args.WorldBounds, Color.White);
        }

        args.WorldHandle.UseShader(null);
    }

    private static float GetFade(TimeSpan now, TimeSpan start, TimeSpan end)
    {
        if (now <= start)
            return 0f;

        var fadeTime = TimeSpan.FromSeconds(0.8);
        var inT = (float) ((now - start) / fadeTime);
        var outT = (float) ((end - now) / fadeTime);
        var fade = MathF.Min(inT, outT);
        return Math.Clamp(fade, 0f, 1f);
    }

    private readonly record struct Entry(EntityUid Altar, TimeSpan StartTime, TimeSpan EndTime, float Radius, float MaxDarkness);
}
