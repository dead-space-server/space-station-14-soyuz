using System.Numerics;
using Content.Shared.Camera;
using Content.Shared.DeadSpace.Necromorphs.Sanity;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client.DeadSpace.Sanity;

public sealed class SanityEffectsSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _cameraRecoil = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private SanityOverlay? _damageOverlay = default!;
    private const float EyeNudge = 0.04f;
    private Vector2 _eyeNudge;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SanityComponent, GetEyeOffsetEvent>(OnEyeOffset);
        SubscribeLocalEvent<SanityOverlayComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SanityOverlayComponent, ComponentShutdown>(OnShutdown);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_timing.IsFirstTimePredicted) return;

        var local = _player.LocalEntity;
        if (local == null || !TryComp<SanityComponent>(local, out var c) || !HasComp<SanityOverlayComponent>(local))
        {
            _eyeNudge = Vector2.Zero;
            return;
        }
        _cameraRecoil.KickCamera(local.Value,
            new Vector2(_random.NextFloat(-1f, 1f), _random.NextFloat(-1f, 1f)) * ((c.MaxSanityLevel - c.SanityLevel) / 100));
        _damageOverlay?.Value = Math.Clamp((c.MaxSanityLevel - c.SanityLevel) / 10f, 0f, 10f);
        var t = new Vector2(_random.NextFloat(-1f, 1f), _random.NextFloat(-1f, 1f)) * EyeNudge;
        _eyeNudge = Vector2.Lerp(_eyeNudge, t, 0.35f);
    }

    private void OnEyeOffset(EntityUid uid, SanityComponent comp, ref GetEyeOffsetEvent args)
    {
        if (uid != _player.LocalEntity)
            return;
        args.Offset += _eyeNudge;
    }
    private void OnStartup(EntityUid uid, SanityOverlayComponent comp, ComponentStartup args)
    {
        if (_player.LocalEntity != uid || !TryComp<SanityComponent>(_player.LocalEntity, out var _)) return;
        _damageOverlay = new();
        _overlayManager.AddOverlay(_damageOverlay);
    }
    private void OnShutdown(EntityUid uid, SanityOverlayComponent comp, ComponentShutdown args)
    {
        if (_player.LocalEntity != uid || !TryComp<SanityComponent>(_player.LocalEntity, out var _)) return;
        _overlayManager.RemoveOverlay(_damageOverlay!);
        _damageOverlay = null;
    }
}
