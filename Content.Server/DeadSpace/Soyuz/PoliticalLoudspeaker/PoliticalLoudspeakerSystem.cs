// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using System.Linq; using Content.Server.Chat.Systems;
using Content.Shared.Actions;
using Content.Shared.Actions.Components; using Content.Shared.Actions.Events;
using Content.Shared.Chat;
using Content.Shared.Damage; using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes; using Content.Shared.Damage.Systems;
using Content.Shared.Examine; using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components; using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Components; using Content.Shared.Movement.Systems; using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems; using Content.Shared.PoliticalLoudspeaker;
using Content.Shared.StatusEffectNew; using Robust.Shared.GameObjects;
using Robust.Shared.Map; using Robust.Shared.Prototypes; using Robust.Shared.Random; using Robust.Shared.Timing;

namespace Content.Server.PoliticalLoudspeaker;

public sealed class PoliticalLoudspeakerSystem : EntitySystem
{
    private const int SpeechVariantCount = 20;
    private const string HealSpeechKeyPrefix = "political-loudspeaker-slogan-heal";
    private const string SpeedSpeechKeyPrefix = "political-loudspeaker-slogan-speed";
    private const string FortifySpeechKeyPrefix = "political-loudspeaker-slogan-fortify";

    [Dependency] private readonly SharedHandsSystem _hands = default!; [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;   [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!; [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!; [Dependency] private readonly MovementModStatusSystem _moveMod = default!;
    [Dependency] private readonly NpcFactionSystem _factionSystem = default!; [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] private readonly ChatSystem _chat = default!; [Dependency] private readonly IRobustRandom _random = default!;

    private EntityQuery<NpcFactionMemberComponent> _factionQuery; private EntityQuery<DamageableComponent> _damageableQuery;

    private readonly HashSet<EntityUid> _rangeSet = new(); private readonly List<EntityUid> _validTargets = new();
    private readonly List<EntityUid> _expiredSpeed = new(); private readonly List<EntityUid> _expiredFortify = new();
    private readonly List<EntityUid> _expiredHeal = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PoliticalLoudspeakerComponent, PoliticalLoudspeakerHealActionEvent>(OnHealAction);
        SubscribeLocalEvent<PoliticalLoudspeakerComponent, PoliticalLoudspeakerSpeedActionEvent>(OnSpeedAction);
        SubscribeLocalEvent<PoliticalLoudspeakerComponent, PoliticalLoudspeakerFortifyActionEvent>(OnFortifyAction);
        SubscribeLocalEvent<ActionComponent, ActionPerformedEvent>(OnActionPerformed);
        SubscribeLocalEvent<PoliticalLoudspeakerFortifyBuffComponent, DamageModifyEvent>(OnFortifyDamageModify);

        _factionQuery = GetEntityQuery<NpcFactionMemberComponent>();
        _damageableQuery = GetEntityQuery<DamageableComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime); var now = _timing.CurTime;

        _expiredSpeed.Clear(); var speedQuery = EntityQueryEnumerator<PoliticalLoudspeakerSpeedBuffComponent>();
        while (speedQuery.MoveNext(out var uid, out var speed))
        {
            if (speed.EndTime > now)  continue;  _expiredSpeed.Add(uid);
        }
        foreach (var uid in _expiredSpeed) { RemComp<PoliticalLoudspeakerSpeedBuffComponent>(uid); }

        _expiredFortify.Clear(); var fortifyQuery = EntityQueryEnumerator<PoliticalLoudspeakerFortifyBuffComponent>();
        while (fortifyQuery.MoveNext(out var uid, out var fortify))
        {
            if (fortify.EndTime > now) continue;  _expiredFortify.Add(uid);
        }
        foreach (var uid in _expiredFortify) { RemComp<PoliticalLoudspeakerFortifyBuffComponent>(uid); }

        _expiredHeal.Clear(); var healQuery = EntityQueryEnumerator<PoliticalLoudspeakerHealOverTimeComponent>();
        while (healQuery.MoveNext(out var uid, out var heal))
        {
            if (heal.EndTime <= now) { _expiredHeal.Add(uid);  continue; }
            if (heal.NextTick > now) continue;

            if(!_damageableQuery.TryGetComponent(uid, out var damageable)) continue;
            _damageable.TryChangeDamage(uid, new DamageSpecifier(heal.HealPerTick));
            heal.NextTick = now + heal.TickInterval;
        }
        foreach (var uid in _expiredHeal) { RemComp<PoliticalLoudspeakerHealOverTimeComponent>(uid); }
    }

    private void OnActionPerformed(Entity<ActionComponent> ent, ref ActionPerformedEvent args)
    {
        if (ent.Comp.Container is not { } container) return;
        if (!TryComp<PoliticalLoudspeakerComponent>(container, out var loudspeaker)) return;

        if (ent.Owner != loudspeaker.HealActionEntity && ent.Owner != loudspeaker.SpeedActionEntity && ent.Owner != loudspeaker.FortifyActionEntity)
            return;

        var start = _timing.CurTime; var end = start + loudspeaker.Cooldown;
        _actions.SetCooldown(loudspeaker.HealActionEntity, start, end);
        _actions.SetCooldown(loudspeaker.SpeedActionEntity, start, end);
        _actions.SetCooldown(loudspeaker.FortifyActionEntity, start, end);
    }

    private void OnHealAction(Entity<PoliticalLoudspeakerComponent> ent, ref PoliticalLoudspeakerHealActionEvent args)
    {
        if (!CanUse(ent, args.Performer)) return;  args.Handled = true;  SpawnFlash(ent, args.Performer);
        SpeakSlogan(args.Performer, HealSpeechKeyPrefix);

        var interval = ent.Comp.HealTickInterval; if (interval <= TimeSpan.Zero) interval = TimeSpan.FromSeconds(1);
        var duration = ent.Comp.HealDuration; if (duration <= TimeSpan.Zero) duration = interval;

        var ticks = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds / interval.TotalSeconds));
        var healPerTickAmount = ent.Comp.HealAmount / (float)ticks;

        var healSpec = BuildHealSpecifier(ent.Comp, healPerTickAmount);
        if (healSpec.DamageDict.Count == 0) return;

        var endTime = _timing.CurTime + duration;

        GetValidTargets(ent.Comp, args.Performer, _validTargets);
        foreach (var target in _validTargets)
        {
            if (!_damageableQuery.TryGetComponent(target, out _)) continue;

            var heal = EnsureComp<PoliticalLoudspeakerHealOverTimeComponent>(target);
            heal.HealPerTick = new DamageSpecifier(healSpec);  heal.EndTime = endTime;
            heal.TickInterval = interval;  heal.NextTick = _timing.CurTime;
        }
    }

    private void OnSpeedAction(Entity<PoliticalLoudspeakerComponent> ent, ref PoliticalLoudspeakerSpeedActionEvent args)
    {
        if (!CanUse(ent, args.Performer)) return;  args.Handled = true; SpawnFlash(ent, args.Performer);
        SpeakSlogan(args.Performer, SpeedSpeechKeyPrefix);

        GetValidTargets(ent.Comp, args.Performer, _validTargets);
        foreach (var target in _validTargets)
        {   if(_statusEffects.TrySetStatusEffectDuration(target, ent.Comp.SpeedStatusEffect, out var effect, ent.Comp.SpeedDuration))
            {
                var mod = EnsureComp<MovementModStatusEffectComponent>(effect.Value); _moveMod.TryUpdateMovementStatus(target, (effect.Value, mod), ent.Comp.SpeedMultiplier, ent.Comp.SpeedMultiplier);
            }
            RemComp<PoliticalLoudspeakerSpeedBuffComponent>(target);
        }
    }

    private void OnFortifyAction(Entity<PoliticalLoudspeakerComponent> ent, ref PoliticalLoudspeakerFortifyActionEvent args)
    {
        if (!CanUse(ent, args.Performer)) return; args.Handled = true; SpawnFlash(ent, args.Performer);
        SpeakSlogan(args.Performer, FortifySpeechKeyPrefix);

        GetValidTargets(ent.Comp, args.Performer, _validTargets); var endTime = _timing.CurTime + ent.Comp.FortifyDuration;
        foreach (var target in _validTargets)
        {
            var buff = EnsureComp<PoliticalLoudspeakerFortifyBuffComponent>(target);
            buff.DamageCoefficient = ent.Comp.FortifyCoefficient; buff.EndTime = endTime;
            buff.ExcludedDamageTypes = new HashSet<ProtoId<DamageTypePrototype>>(ent.Comp.FortifyExcludedDamageTypes);
            _statusEffects.TrySetStatusEffectDuration(target, ent.Comp.FortifyStatusEffect, ent.Comp.FortifyDuration);
        }
    }

    private void OnFortifyDamageModify(EntityUid uid , PoliticalLoudspeakerFortifyBuffComponent comp , DamageModifyEvent args)
    {
        if (comp.DamageCoefficient >= 1f) return;

        var newDamage = new DamageSpecifier(args.Damage);
        foreach (var (type, value) in newDamage.DamageDict.ToArray())
        {
            if (value <= FixedPoint2.Zero) continue;
            if (comp.ExcludedDamageTypes.Contains(type)) continue;
            var scaled = value * comp.DamageCoefficient;
            if (scaled <= FixedPoint2.Zero) { newDamage.DamageDict.Remove(type); continue; }
            newDamage.DamageDict[type] = scaled;
        }
        args.Damage = newDamage;
    }

    private bool CanUse(Entity<PoliticalLoudspeakerComponent> ent, EntityUid performer)
    {
        if (!TryComp<HandsComponent>(performer, out var hands)) return false;
        return _hands.IsHolding((performer, hands), ent.Owner);
    }

    private void SpeakSlogan(EntityUid performer, string keyPrefix)
    {
        var slogan = Loc.GetString($"{keyPrefix}-{_random.Next(1, SpeechVariantCount + 1)}");
        _chat.TrySendInGameICMessage(
            performer,
            slogan,
            InGameICChatType.Speak,
            ChatTransmitRange.Normal,
            checkRadioPrefix: false,
            ignoreActionBlocker: true);
    }

    private void SpawnFlash(Entity<PoliticalLoudspeakerComponent> ent, EntityUid performer)
    { Spawn(ent.Comp.FlashEffectPrototype, Transform(performer).Coordinates); }

    private DamageSpecifier BuildHealSpecifier(PoliticalLoudspeakerComponent comp, FixedPoint2 amount)
    {
        var spec = new DamageSpecifier(); var healAmount = -amount;
        foreach (var type in comp.HealDamageTypes)
        {
            if (comp.HealExcludedDamageTypes.Contains(type)) continue; spec.DamageDict[type] = healAmount;
        }
        return spec;
    }

    private void GetValidTargets(PoliticalLoudspeakerComponent comp, EntityUid performer, List<EntityUid> targets)
    {
        targets.Clear(); var sourceCoords = _transform.GetMapCoordinates(performer);
        if (!_factionQuery.TryGetComponent(performer, out var performerFaction)) return;

        _rangeSet.Clear(); _lookup.GetEntitiesInRange(sourceCoords.MapId, sourceCoords.Position, comp.Range, _rangeSet);
        foreach (var entity in _rangeSet)
        {
            if (!_factionQuery.TryGetComponent(entity, out var targetFaction)) continue;
            if (!_factionSystem.IsMemberOfAny((entity, targetFaction), performerFaction.Factions)) continue;
            if (!_examine.InRangeUnOccluded(entity, sourceCoords, comp.Range)) continue;
            targets.Add(entity);
        }
    }
}
