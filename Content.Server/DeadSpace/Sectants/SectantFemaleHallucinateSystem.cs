using Content.Shared.Mobs.Components;
using Content.Shared.DeadSpace.Sectants;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.DeadSpace.Sectants;

public sealed class SectantFemaleHallucinateSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SectantFemaleHallucinateActionEvent>(OnHallucinate);
    }

    private void OnHallucinate(SectantFemaleHallucinateActionEvent args)
    {
        var uid = args.Performer;
        var xform = Transform(uid);
        var origin = _xform.GetWorldPosition(uid);

        // Ближайшая живая цель
        EntityUid targetUid = EntityUid.Invalid;
        var bestDist = float.MaxValue;
        foreach (var s in _players.Sessions)
        {
            if (s.AttachedEntity is not { } player) continue;
            if (!HasComp<MobStateComponent>(player)) continue;
            if (Transform(player).MapID != xform.MapID) continue;
            var d = (_xform.GetWorldPosition(player) - origin).LengthSquared();
            if (d < bestDist) { bestDist = d; targetUid = player; }
        }

        if (targetUid == EntityUid.Invalid) { args.Handled = true; return; }

        if (!_players.TryGetSessionByEntity(targetUid, out var targetSession))
        {
            args.Handled = true;
            return;
        }

        var targetMap = Transform(targetUid).MapID;
        var targetPos = _xform.GetWorldPosition(targetUid);

        for (var i = 0; i < 3; i++)
        {
            var angle = _random.NextAngle();
            var dist = _random.NextFloat(2f, 5f);
            var pos = targetPos + angle.ToVec() * dist;

            var illusion = Spawn("MobSectantHallucinationIllusion", new MapCoordinates(pos, targetMap));

            var comp = EnsureComp<SectantHallucinationIllusionComponent>(illusion);
            comp.Target = GetNetEntity(targetUid);
            comp.Lifetime = 20f;

            RaiseNetworkEvent(new SectantHallucinationVisualEvent
            {
                Id = GetNetEntity(illusion),
                Position = pos,
                MapId = (int)targetMap,
                Lifetime = comp.Lifetime,
                Remove = false,
            }, targetSession.Channel);
        }

        args.Handled = true;
    }
}
