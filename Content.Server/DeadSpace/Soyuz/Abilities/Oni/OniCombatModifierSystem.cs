using Content.Server.Tools;
using Content.Shared.Damage.Events;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable.Components;
using Content.Shared._NF.Weapons.Components;
using Robust.Shared.Containers;

namespace Content.Server.Abilities.Oni
{
    public sealed class OniCombatModifierSystem : EntitySystem
    {
        [Dependency] private readonly SharedGunSystem _gunSystem = default!;

        private const double GunInaccuracyFactor = 17.0; // DS14-Soyuz (20x<18x -> 10% buff)

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<OniComponent, EntInsertedIntoContainerMessage>(OnEntInserted);
            SubscribeLocalEvent<OniComponent, EntRemovedFromContainerMessage>(OnEntRemoved);
            SubscribeLocalEvent<OniComponent, MeleeHitEvent>(OnOniMelee);
            SubscribeLocalEvent<HeldByOniComponent, MeleeHitEvent>(OnHeldMeleeHit);
            SubscribeLocalEvent<HeldByOniComponent, StaminaMeleeHitEvent>(OnHeldStaminaMeleeHit);
            SubscribeLocalEvent<HeldByOniComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers);
        }

        private void OnEntInserted(EntityUid uid, OniComponent component, EntInsertedIntoContainerMessage args)
        {
            var heldComp = EnsureComp<HeldByOniComponent>(args.Entity);
            heldComp.Holder = uid;

            // DS14-Soyuz: Oni-friendly "guns" (crusher)
            var isGun = TryComp<GunComponent>(args.Entity, out _);
            var isOniFriendly = HasComp<OniFriendlyGunComponent>(args.Entity);
            if (!isGun || isOniFriendly)
                return;

            _gunSystem.RefreshModifiers(args.Entity); // DS14-Soyuz
        }

        private void OnEntRemoved(EntityUid uid, OniComponent component, EntRemovedFromContainerMessage args)
        {
            // DS14-Soyuz: Oni-friendly "guns" (crusher)
            var shouldRefresh = TryComp<GunComponent>(args.Entity, out _)
                && TryComp<HeldByOniComponent>(args.Entity, out _)
                && !HasComp<OniFriendlyGunComponent>(args.Entity);
            if (shouldRefresh)
                _gunSystem.RefreshModifiers(args.Entity); // DS14-Soyuz

            RemComp<HeldByOniComponent>(args.Entity);
        }

        private void OnGunRefreshModifiers(EntityUid uid, HeldByOniComponent component, ref GunRefreshModifiersEvent args)
        {
            if (HasComp<OniFriendlyGunComponent>(uid))
                return;

            // DS14-Soyuz: apply oni inaccuracy through refresh event instead of writing GunComponent fields.
            args.MinAngle += args.MinAngle * GunInaccuracyFactor;
            args.AngleIncrease += args.AngleIncrease * GunInaccuracyFactor;
            args.MaxAngle += args.MaxAngle * GunInaccuracyFactor;
        }

        private void OnOniMelee(EntityUid uid, OniComponent component, MeleeHitEvent args)
        {
            args.ModifiersList.Add(component.MeleeModifiers);
        }

        private void OnHeldMeleeHit(EntityUid uid, HeldByOniComponent component, MeleeHitEvent args)
        {
            if (!TryComp<OniComponent>(component.Holder, out var oni))
                return;

            args.ModifiersList.Add(oni.MeleeModifiers);
        }

        private void OnHeldStaminaMeleeHit(EntityUid uid, HeldByOniComponent component, StaminaMeleeHitEvent args)
        {
            if (!TryComp<OniComponent>(component.Holder, out var oni))
                return;

            args.Multiplier *= oni.StamDamageMultiplier;
        }
    }
}
