using Content.Shared.DeadSpace.Sectants;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.DeadSpace.Sectants;

public sealed class SectantMaleRandomBlinkSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private const float MinRadius = 3f;
    private const float MaxRadius = 12f;

    public override void Initialize()
    {
        SubscribeLocalEvent<SectantMaleRandomBlinkActionEvent>(OnBlink);
    }

    private void OnBlink(SectantMaleRandomBlinkActionEvent args)
    {
        var uid = args.Performer;
        var xform = Transform(uid);

        if (xform.MapID == MapId.Nullspace)
            return;

        var origin = _xform.GetWorldPosition(uid);
        var angle = _random.NextAngle();
        var dist = _random.NextFloat(MinRadius, MaxRadius);
        var target = origin + angle.ToVec() * dist;

        _xform.SetWorldPosition(uid, target);
        Spawn("SectantBlinkEffect", _xform.GetMapCoordinates(uid));

        args.Handled = true;
    }
}
