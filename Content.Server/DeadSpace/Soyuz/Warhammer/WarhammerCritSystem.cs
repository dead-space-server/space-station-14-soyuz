using System.Numerics;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Random;

namespace Content.Server.DeadSpace.Soyuz.Warhammer;

public sealed class WarhammerCritSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WarhammerCritComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(Entity<WarhammerCritComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        if (!_random.Prob(ent.Comp.Chance))
            return;

        args.BonusDamage += ent.Comp.DamageBonus;

        var userPos = _transform.GetWorldPosition(args.User);

        foreach (var target in args.HitEntities)
        {
            var direction = args.Direction ?? _transform.GetWorldPosition(target) - userPos;

            if (direction == Vector2.Zero)
                continue;

            _throwing.TryThrow(target,
                direction.Normalized() * ent.Comp.ThrowDistance,
                ent.Comp.ThrowSpeed,
                args.User,
                pushbackRatio: 0f,
                recoil: false);
        }
    }
}
