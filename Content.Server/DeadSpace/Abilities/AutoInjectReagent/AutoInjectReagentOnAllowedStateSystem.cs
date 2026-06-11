using Content.Shared.DeadSpace.Abilities.AutoInjectReagent.Components;
using Content.Server.Popups;
using Robust.Shared.Audio.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.DeadSpace.Abilities.AutoInjectReagent;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.Abilities.AutoInjectReagentOnAllowedState;

public sealed partial class AutoInjectReagentOnAllowedStateSystem : SharedReagentSystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AutoInjectReagentOnAllowedStateComponent, MobStateChangedEvent>(OnState);
        SubscribeLocalEvent<AutoInjectReagentOnAllowedStateComponent, ComponentInit>(OnMapInit);
        SubscribeLocalEvent<AutoInjectReagentOnAllowedStateComponent, EntityUnpausedEvent>(OnRegenUnpause);
        SubscribeLocalEvent<AutoInjectReagentOnAllowedStateComponent, ComponentShutdown>(OnShutdown); //DS14-Soyuz
    }

    private void OnMapInit(EntityUid uid, AutoInjectReagentOnAllowedStateComponent component, ComponentInit args)
    {
        component.TimeUntilRegen = TimeSpan.FromSeconds(component.DurationRegenReagents) + _timing.CurTime;
        component.NextInjectTime = _timing.CurTime; //DS14-Soyuz
    }

    private void OnRegenUnpause(EntityUid uid, AutoInjectReagentOnAllowedStateComponent component, ref EntityUnpausedEvent args)
    {
        component.TimeUntilRegen += args.PausedTime;
        component.NextInjectTime += args.PausedTime; //DS14-Soyuz
        Dirty(uid, component);
    }
//DS14-Soyuz-start
    private void OnShutdown(EntityUid uid, AutoInjectReagentOnAllowedStateComponent component, ComponentShutdown args)
    {
    }
//DS14-Soyuz-end

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var curTime = _timing.CurTime;

        var autoInjectReagentOnAllowedStateQuery = EntityQueryEnumerator<AutoInjectReagentOnAllowedStateComponent>();
        while (autoInjectReagentOnAllowedStateQuery.MoveNext(out var uid, out var comp)) //DS14-Soyuz
        {
            if (!comp.IsReady && curTime > comp.TimeUntilRegen) //DS14-Soyuz
            {
                comp.IsReady = true;
//DS14-Soyuz-start
                Dirty(uid, comp);
            }
            if (comp.TimeUntilNextInject > 0 && comp.IsReady)
            {
                if (curTime >= comp.NextInjectTime)
                {
                    if (IsInAllowedState(uid, comp))
                    {
                        PerformInject(uid, comp);
                        
                        comp.NextInjectTime = curTime + TimeSpan.FromSeconds(comp.TimeUntilNextInject);
                        Dirty(uid, comp);
                    }
                    else
                    {
                        comp.NextInjectTime = curTime;
                        Dirty(uid, comp);
                    }
                }
//DS14-Soyuz-end
            }
        }
    }

    private void OnState(EntityUid uid, AutoInjectReagentOnAllowedStateComponent component, MobStateChangedEvent args)
    {
        foreach (var allowedState in component.AllowedStates)
        {
            if (allowedState == args.NewMobState)
            {
// DS14-Soyuz-start
                if (component.TimeUntilNextInject == 0 && component.IsReady)
                {
                    PerformInject(uid, component);
                    component.IsReady = false;
                    component.TimeUntilRegen = TimeSpan.FromSeconds(component.DurationRegenReagents) + _timing.CurTime;
                    Dirty(uid, component);
                }
                else if (component.TimeUntilNextInject > 0 && !component.IsReady)
                {
                    component.IsReady = true;
                    component.NextInjectTime = _timing.CurTime;
                    Dirty(uid, component);
                }
                break;
            }
        }
    }

    private bool IsInAllowedState(EntityUid uid, AutoInjectReagentOnAllowedStateComponent component)
    {
        if (!TryComp<MobStateComponent>(uid, out var mobState))
            return false;

        foreach (var allowedState in component.AllowedStates)
        {
            if (mobState.CurrentState == allowedState)
                return true;
        }
        
        return false;
    }

    private void PerformInject(EntityUid uid, AutoInjectReagentOnAllowedStateComponent component)
    {
        Inject(component.Reagents, uid);
        _popup.PopupEntity(Loc.GetString("hypospray-component-feel-prick-message"), uid, uid);
        _audio.PlayPvs(component.InjectSound, uid);
// DS14-Soyuz-end
    }
}
