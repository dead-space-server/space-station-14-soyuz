using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.DeadSpace.Sectants;
using Robust.Server.Player;
using Robust.Shared.Player;                  // ← ICommonSession
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace.Sectants;

public sealed class SectantHallucinationIllusionSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    private static readonly ProtoId<DamageTypePrototype> Cellular = "Cellular";

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<SectantHallucinationIllusionComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            comp.Elapsed += frameTime;

            // Получаем цель через pattern matching
            ICommonSession? targetSession = null;
            EntityUid targetUid = EntityUid.Invalid;

            if (TryGetEntity(comp.Target, out var targetNullable) && targetNullable is { } t)
            {
                targetUid = t;
                _players.TryGetSessionByEntity(targetUid, out targetSession);
            }

            // Цель исчезла или время истекло
            if (targetUid == EntityUid.Invalid
                || !HasComp<MobStateComponent>(targetUid)
                || comp.Elapsed >= comp.Lifetime)
            {
                if (targetSession != null)
                    RaiseNetworkEvent(new SectantHallucinationVisualEvent
                    {
                        Id = GetNetEntity(uid),
                        Remove = true,
                    }, targetSession.Channel);
                QueueDel(uid);
                continue;
            }

            var illusionPos = _xform.GetWorldPosition(uid);
            var targetPos = _xform.GetWorldPosition(targetUid);
            var dir = targetPos - illusionPos;
            var dist = dir.Length();

            // Дрейф к цели
            if (dist > 1.2f)
            {
                var step = Vector2.Normalize(dir) * comp.MoveSpeed * frameTime;
                _xform.SetWorldPosition(uid, illusionPos + step);
                illusionPos = _xform.GetWorldPosition(uid);
            }

            // Синхронизация визуала — 10 раз/сек
            comp.BroadcastAccum += frameTime;
            if (comp.BroadcastAccum >= 0.1f && targetSession != null)
            {
                comp.BroadcastAccum = 0f;
                RaiseNetworkEvent(new SectantHallucinationVisualEvent
                {
                    Id = GetNetEntity(uid),
                    Position = illusionPos,
                    MapId = (int)Transform(uid).MapID,
                    Lifetime = comp.Lifetime - comp.Elapsed,
                    Remove = false,
                }, targetSession.Channel);
            }

            // Атака — только цели
            comp.AttackAccum += frameTime;
            if (comp.AttackAccum >= comp.AttackInterval)
            {
                comp.AttackAccum -= comp.AttackInterval;
                if (dist < 1.5f)
                {
                    var dmg = new DamageSpecifier(_proto.Index(Cellular), comp.AttackDamage);
                    _damage.TryChangeDamage(targetUid, dmg, ignoreResistances: false);
                }
            }
        }
    }
}
