using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.DeadSpace.Soyuz.RitualAltar;

public sealed class RitualVignetteOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> VignetteShader = "RitualVignette";

    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;

    private readonly ShaderInstance _shader;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly List<Entry> _entries = new();

    public RitualVignetteOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _protos.Index(VignetteShader).InstanceUnique();
        ZIndex = 251;
    }

    public void Start(TimeSpan duration, float strength)
    {
        var start = _timing.CurTime;
        var end = start + duration;
        _entries.Add(new Entry(start, end, strength));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        var now = _timing.CurTime;
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (now >= _entries[i].EndTime)
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
        var viewportSize = args.Viewport.Size;
        var time = (float) _timing.RealTime.TotalSeconds;

        foreach (var entry in _entries)
        {
            var fade = GetFade(now, entry.StartTime, entry.EndTime);
            var strength = Math.Clamp(entry.Strength * fade, 0f, 1f);
            if (strength <= 0.001f)
                continue;

            _shader.SetParameter("viewportSize", new Vector2(viewportSize.X, viewportSize.Y));
            _shader.SetParameter("time", time);
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

    private readonly record struct Entry(TimeSpan StartTime, TimeSpan EndTime, float Strength);
}

