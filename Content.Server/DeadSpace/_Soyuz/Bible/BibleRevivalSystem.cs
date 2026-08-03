// SPDX-FileCopyrightText: 2026 Kofeecheks
// SPDX-License-Identifier: LicenseRef-Kofeecheks

using Content.Server.Bible.Components;
using Content.Server.Body.Systems;
using Content.Server.Mind;
using Content.Server.Popups;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server.DeadSpace._Soyuz.Bible;

public sealed class BibleRevivalSystem : EntitySystem
{
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedRottingSystem _rotting = default!;
    [Dependency] private readonly UseDelaySystem _delay = default!;

    public void TryRevive(
        Entity<BibleComponent> bible,
        EntityUid user,
        EntityUid target,
        UseDelayComponent useDelay)
    {
        var (uid, component) = bible;
        if (component.ReviveDeadChance <= 0f)
            return;

        if (!HasComp<BibleUserComponent>(user))
        {
            _popup.PopupEntity(Loc.GetString("bible-sizzle"), user, user);
            _audio.PlayPvs(component.SizzleSoundPath, user);
            _damageable.TryChangeDamage(user, component.DamageOnUntrainedUse, true, origin: uid);
            _delay.TryResetDelay((uid, useDelay));
            return;
        }

        _delay.TryResetDelay((uid, useDelay));

        if (component.ReviveDeadOncePerBody && HasComp<BibleReviveAttemptedComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("bible-revive-already-tried"), user, user,
                PopupType.MediumCaution);
            return;
        }

        if (component.ReviveDeadOncePerBody)
            EnsureComp<BibleReviveAttemptedComponent>(target);

        if (_rotting.IsRotten(target))
        {
            _popup.PopupEntity(Loc.GetString("defibrillator-rotten"), user, user,
                PopupType.MediumCaution);
            _audio.PlayPvs(component.BibleHitSound, user);
            return;
        }

        if (TryComp<UnrevivableComponent>(target, out var unrevivable))
        {
            _popup.PopupEntity(Loc.GetString(unrevivable.ReasonMessage), user, user,
                PopupType.MediumCaution);
            _audio.PlayPvs(component.BibleHitSound, user);
            return;
        }

        var userEnt = Identity.Entity(user, EntityManager);
        var targetEnt = Identity.Entity(target, EntityManager);

        if (!_random.Prob(component.ReviveDeadChance))
        {
            var othersFailMessage = Loc.GetString("bible-revive-fail-others",
                ("user", userEnt), ("target", targetEnt), ("bible", uid));
            var selfFailMessage = Loc.GetString("bible-revive-fail-self",
                ("target", targetEnt), ("bible", uid));

            _popup.PopupEntity(othersFailMessage, user, Filter.PvsExcept(user), true,
                PopupType.SmallCaution);
            _popup.PopupEntity(selfFailMessage, user, user, PopupType.MediumCaution);
            _audio.PlayPvs(component.BibleHitSound, user);
            return;
        }

        if (!TryComp<DamageableComponent>(target, out var damageable) ||
            !TryComp<MobThresholdsComponent>(target, out var thresholds) ||
            !_mobThreshold.TryGetThresholdForState(target, MobState.Dead, out var deadThreshold, thresholds))
        {
            _popup.PopupEntity(Loc.GetString("bible-revive-fail-self",
                ("target", targetEnt), ("bible", uid)), user, user, PopupType.MediumCaution);
            _audio.PlayPvs(component.BibleHitSound, user);
            return;
        }

        var lethalThreshold = deadThreshold.Value;
        var desiredDamage = lethalThreshold * component.ReviveDeadDamageFraction;
        if (desiredDamage >= lethalThreshold)
            desiredDamage = lethalThreshold - FixedPoint2.Epsilon;

        var healAmount = damageable.TotalDamage - desiredDamage;
        if (healAmount > FixedPoint2.Zero)
            _damageable.HealDistributed((target, damageable), -healAmount, origin: uid);

        if (component.RestoreBloodOnRevive && TryComp<BloodstreamComponent>(target, out var bloodstream))
            _bloodstream.TryRegulateBloodLevel((target, bloodstream), bloodstream.BloodReferenceSolution.Volume);

        var revivedState = _mobState.HasState(target, MobState.Critical)
            ? MobState.Critical
            : MobState.Alive;
        _mobState.ChangeMobState(target, revivedState, origin: uid);
        ReturnSoulToBody(target);

        var othersMessage = Loc.GetString("bible-revive-success-others",
            ("user", userEnt), ("target", targetEnt), ("bible", uid));
        var selfMessage = Loc.GetString("bible-revive-success-self",
            ("target", targetEnt), ("bible", uid));

        _popup.PopupEntity(othersMessage, user, Filter.PvsExcept(user), true, PopupType.Medium);
        _popup.PopupEntity(selfMessage, user, user, PopupType.Large);
        _audio.PlayPvs(component.HealSoundPath, user);
    }

    private void ReturnSoulToBody(EntityUid target)
    {
        if (!_mind.TryGetMind(target, out var mindId, out var mind) || mind.CurrentEntity == target)
            return;

        if (mind.VisitingEntity != null)
            _mind.UnVisit(mindId, mind);
        else
            _mind.TransferTo(mindId, target, ghostCheckOverride: true, mind: mind);
    }
}
