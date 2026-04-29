using Content.Shared.Actions;

namespace Content.Shared._DV.Harpy;

public abstract class SharedHarpyMidiSingerSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HarpyMidiSingerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<HarpyMidiSingerComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(EntityUid uid, HarpyMidiSingerComponent component, ComponentStartup args)
    {
        var actionId = component.MidiActionId;
        _actionsSystem.AddAction(uid, ref component.MidiAction, actionId);
    }

    private void OnShutdown(EntityUid uid, HarpyMidiSingerComponent component, ComponentShutdown args)
    {
        var action = component.MidiAction;
        _actionsSystem.RemoveAction(uid, action);
    }
}
