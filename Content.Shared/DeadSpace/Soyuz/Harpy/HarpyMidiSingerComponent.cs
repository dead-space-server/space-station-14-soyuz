using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._DV.Harpy;

[RegisterComponent, ComponentProtoName("HarpySinger"), NetworkedComponent] // DS14: keep old prototype/component id
public sealed partial class HarpyMidiSingerComponent : Component
{
    [DataField("midiActionId", serverOnly: true,
        customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string? MidiActionId = "ActionHarpyPlayMidi";

    [DataField("midiAction", serverOnly: true)] // server only, as it uses a server-BUI event !type
    public EntityUid? MidiAction;
}
