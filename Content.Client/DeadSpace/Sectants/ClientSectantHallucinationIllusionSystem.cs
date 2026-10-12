using System.Numerics;
using Content.Shared.DeadSpace.Sectants;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.DeadSpace.Sectants;

public sealed class ClientSectantHallucinationIllusionSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IResourceCache _cache = default!;

    private ClientSectantHallucinationIllusionOverlay? _ill;

    public override void Initialize()
    {
        base.Initialize();
        _ill = new ClientSectantHallucinationIllusionOverlay(_cache);
        _overlay.AddOverlay(_ill);

        SubscribeNetworkEvent<SectantHallucinationVisualEvent>(OnVisual);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _ill?.Tick(frameTime);
    }

    public override void Shutdown()
    {
        if (_ill != null)
            _overlay.RemoveOverlay(_ill);
        _ill = null;
        base.Shutdown();
    }

    private void OnVisual(SectantHallucinationVisualEvent ev)
    {
        if (_ill == null) return;

        if (ev.Remove)
        {
            _ill.RemoveIllusion(ev.Id);
            return;
        }

        _ill.AddOrUpdate(ev.Id, ev.Position, new MapId(ev.MapId), ev.Lifetime);
    }
}

/// <summary>
/// Клиентский оверлей иллюзий: рисует «призраков» только у целевого игрока.
/// </summary>
public sealed class ClientSectantHallucinationIllusionOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private sealed class Illusion
    {
        public Vector2 Position;
        public MapId Map;
        public float TimeLeft;
        public float PulsePhase;
    }

    private readonly Dictionary<NetEntity, Illusion> _illusions = new();
    private readonly RSI? _rsi;

    public ClientSectantHallucinationIllusionOverlay(IResourceCache cache)
    {
        if (cache.TryGetResource<RSIResource>(
            new ResPath("/_DeadSpace/TEMP_FOR_EVENT/little_gabry/actions.rsi"), out var res))
            _rsi = res.RSI;
    }

    public void AddOrUpdate(NetEntity id, Vector2 pos, MapId map, float lifetime)
    {
        if (!_illusions.TryGetValue(id, out var ill))
        {
            ill = new Illusion();
            _illusions[id] = ill;
        }
        ill.Position = pos;
        ill.Map = map;
        ill.TimeLeft = lifetime;
    }

    public void RemoveIllusion(NetEntity id) => _illusions.Remove(id);

    // ВАЖНО: public override (в fobos Overlay.FrameUpdate — protected internal,
    // но Celestial использует public override — значит, base тоже public)
    public void Tick(float dt)
    {
        var toRemove = new List<NetEntity>();
        foreach (var (id, ill) in _illusions)
        {
            ill.TimeLeft -= dt;
            ill.PulsePhase += dt * 3f;
            if (ill.TimeLeft <= 0)
                toRemove.Add(id);
        }
        foreach (var id in toRemove)
            _illusions.Remove(id);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_rsi == null) return;
        if (!_rsi.TryGetState("execute", out var state)) return;

        var handle = args.WorldHandle;
        var mapId = args.MapId;

        foreach (var (_, ill) in _illusions)
        {
            if (ill.Map != mapId) continue;

            var pulse = 1f + 0.08f * MathF.Sin(ill.PulsePhase * 1.7f);
            var size = new Vector2(pulse, pulse);
            var alpha = 0.55f + 0.25f * MathF.Sin(ill.PulsePhase);
            var color = new Color(0.55f, 0.6f, 1f, alpha);

            handle.DrawTextureRect(state.Frame0,
                Box2.FromDimensions(ill.Position - size / 2f, size),
                color);
        }
    }
}
