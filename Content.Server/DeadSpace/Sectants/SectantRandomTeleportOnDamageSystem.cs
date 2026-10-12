// Скорее всего будет вырезано

using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.DeadSpace.Sectants;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.Sectants;

public sealed class SectantRandomTeleportOnDamageSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SectantSystem _sectant = default!;

    private readonly Dictionary<EntityUid, FixedPoint2> _lastDamage = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SectantRandomTeleportOnDamageComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(EntityUid uid, SectantRandomTeleportOnDamageComponent comp, ComponentShutdown args)
    {
        _lastDamage.Remove(uid);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<SectantRandomTeleportOnDamageComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var comp, out var damageable))
        {
            var current = damageable.Damage.GetTotal();

            if (!_lastDamage.TryGetValue(uid, out var last))
            {
                _lastDamage[uid] = current;
                continue;
            }

            if (current > last)
            {
                if (_sectant.CanTeleport(uid, comp))
                {
                    if (TryTeleport(uid, comp))
                        comp.LastTeleport = _timing.CurTime;
                }
            }

            _lastDamage[uid] = current;
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _lastDamage.Clear();
    }

    private bool TryTeleport(EntityUid uid, SectantRandomTeleportOnDamageComponent comp)
    {
        var xform = Transform(uid);
        if (xform.MapID == MapId.Nullspace) return false;
        if (xform.GridUid is not { } gridUid) return false;
        if (!TryComp<MapGridComponent>(gridUid, out var grid)) return false;

        var origin = _xform.GetWorldPosition(uid);

        for (var i = 0; i < 32; i++)
        {
            var angle = _random.NextAngle();
            var dist = _random.NextFloat(comp.MinDistance, comp.Radius);
            var target = origin + angle.ToVec() * dist;
            var tile = new Vector2i((int)MathF.Floor(target.X), (int)MathF.Floor(target.Y));

            if (!_mapSystem.TryGetTileRef(gridUid, grid, tile, out _))
                continue;

            _xform.SetWorldPosition(uid, target);

            if (comp.Sound != null)
                _audio.PlayPvs(comp.Sound, uid);
            if (comp.EffectPrototype is { } fx)
                Spawn(fx, _xform.GetMapCoordinates(uid));
            return true;
        }

        Logger.GetSawmill("sectant").Warning("TELEPORT failed: no valid tile");
        return false;
    }
}
