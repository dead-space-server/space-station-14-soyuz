using Content.Shared.Mobs.Components;
using Content.Shared.DeadSpace.Sectants;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace.Sectants;

public sealed class SectantSleepSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly StatusEffectsSystem _status = default!;
    [Dependency] private readonly SectantSystem _sectant = default!;

    private static readonly EntProtoId SleepEffect = "StatusEffectForcedSleeping";
    private const float SleepDuration = 3f;

    private readonly HashSet<EntityUid> _entitySet = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<SectantSleepActionEvent>(OnSleep);
    }

    private void OnSleep(SectantSleepActionEvent args)
    {
        var uid = args.Performer;
        var coords = Transform(uid).Coordinates;
        _entitySet.Clear();
        _lookup.GetEntitiesInRange(coords, 5f, _entitySet);

        foreach (var ent in _entitySet)
        {
            if (ent == uid) continue;
            if (!HasComp<MobStateComponent>(ent)) continue;
            if (!HasComp<StatusEffectsComponent>(ent)) continue;
            if (_sectant.IsAllyOf(uid, ent)) continue;

            _status.TryAddStatusEffect(ent, SleepEffect, TimeSpan.FromSeconds(SleepDuration), refresh: true);
        }

        args.Handled = true;
    }
}
