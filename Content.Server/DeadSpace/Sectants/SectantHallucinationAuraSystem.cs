using Content.Shared.DeadSpace.Sectants;
using Content.Shared.StatusEffect;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.Sectants;

public sealed class SectantHallucinationAuraSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly StatusEffectsSystem _status = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SectantSystem _sectant = default!;

    private readonly HashSet<EntityUid> _entitySet = new();

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<SectantHallucinationAuraComponent>();
        while (query.MoveNext(out var uid, out var aura))
        {
            if (_timing.CurTime < aura.NextTick) continue;
            aura.NextTick = _timing.CurTime + TimeSpan.FromSeconds(aura.TickInterval);

            var coords = Transform(uid).Coordinates;
            _entitySet.Clear();
            _lookup.GetEntitiesInRange(coords, aura.Radius, _entitySet);

            foreach (var target in _entitySet)
            {
                if (target == uid) continue;
                if (!HasComp<StatusEffectsComponent>(target)) continue;
                if (_sectant.IsAllyOf(uid, target)) continue;

                _status.TryAddStatusEffect(target, aura.SleepEffect,  TimeSpan.FromSeconds(3), refresh: true);
                _status.TryAddStatusEffect(target, aura.PacifyEffect, TimeSpan.FromSeconds(5), refresh: true);
                _status.TryAddStatusEffect(target, aura.BlindEffect,  TimeSpan.FromSeconds(4), refresh: true);
            }
        }
    }
}
