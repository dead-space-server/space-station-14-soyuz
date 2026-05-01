using System.Numerics;
using Content.Shared.DeadSpace.Soyuz.RitualAltar;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Random;
using Robust.Shared.Maths;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.DeadSpace.Soyuz.RitualAltar;

public sealed class RitualAltarClientSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;

    private readonly List<ActiveRitual> _active = new();

    private const string RuneProto = "RitualRuneEffect";
    private const float RingsScale = 1.875f; // 128px sprite scaled to roughly match the old 32px*7.5 ring.

    private RitualDarknessOverlay _darknessOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RitualEffectMessage>(OnRitualEffect);

        _darknessOverlay = new RitualDarknessOverlay();
        _overlays.AddOverlay(_darknessOverlay);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _timing.CurTime;

        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var ritual = _active[i];

            if (now >= ritual.EndTime || !TryComp(ritual.Altar, out TransformComponent? altarXform))
            {
                Cleanup(ritual);
                _active.RemoveAt(i);
                continue;
            }

            ritual.LastCoordinates = altarXform.Coordinates;
            UpdateRunes(ritual, frameTime);
        }
    }

    private void OnRitualEffect(RitualEffectMessage msg)
    {
        var altar = GetEntity(msg.Altar);
        if (!TryComp(altar, out TransformComponent? altarXform))
            return;

        var start = _timing.CurTime;
        var end = start + TimeSpan.FromSeconds(MathF.Max(0.1f, msg.DurationSeconds));

        var ritual = new ActiveRitual(altar, start, end, MathF.Max(0.1f, msg.Radius), Math.Clamp(msg.MaxDarkness, 0f, 1f))
        {
            LastCoordinates = altarXform.Coordinates
        };

        SpawnRunes(ritual);
        _active.Add(ritual);

        _darknessOverlay.Start(altar, ritual.Radius, ritual.MaxDarkness, end - start);
    }

    private void SpawnRunes(ActiveRitual ritual)
    {
        ritual.Rings.Clear();

        // One sprite that already contains three concentric circles, so it stays clean (no stacking mess).
        var ent = Spawn(RuneProto, ritual.LastCoordinates);
        ConfigureRingSprite(ent, RingsScale);

        var speed = 0.55f * (_random.Prob(0.5f) ? 1f : -1f);
        ritual.Rings.Add(new Ring(ent, speed));
    }

    private void ConfigureRingSprite(EntityUid uid, float scale)
    {
        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        _sprite.SetColor((uid, sprite), Color.FromHex("#dfe7ff").WithAlpha(0.90f));
        _sprite.SetScale((uid, sprite), new Vector2(scale, scale));
    }

    private void UpdateRunes(ActiveRitual ritual, float frameTime)
    {
        var dt = frameTime;
        if (dt <= 0f)
            return;

        var xformQuery = GetEntityQuery<TransformComponent>();

        for (var i = 0; i < ritual.Rings.Count; i++)
        {
            var ring = ritual.Rings[i];
            if (!xformQuery.TryGetComponent(ring.Uid, out var xform))
                continue;

            xform.Coordinates = ritual.LastCoordinates;
            xform.LocalRotation += new Angle(ring.SpinSpeed * dt);
        }
    }

    private void Cleanup(ActiveRitual ritual)
    {
        foreach (var ring in ritual.Rings)
        {
            if (Deleted(ring.Uid))
                continue;

            QueueDel(ring.Uid);
        }
    }

    private sealed class ActiveRitual
    {
        public readonly EntityUid Altar;
        public readonly TimeSpan StartTime;
        public readonly TimeSpan EndTime;
        public readonly float Radius;
        public readonly float MaxDarkness;

        public EntityCoordinates LastCoordinates;
        public readonly List<Ring> Rings = new();

        public ActiveRitual(EntityUid altar, TimeSpan startTime, TimeSpan endTime, float radius, float maxDarkness)
        {
            Altar = altar;
            StartTime = startTime;
            EndTime = endTime;
            Radius = radius;
            MaxDarkness = maxDarkness;
        }
    }

    private readonly record struct Ring(EntityUid Uid, float SpinSpeed);
}
