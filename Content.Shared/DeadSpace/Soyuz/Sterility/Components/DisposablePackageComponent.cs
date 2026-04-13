using Content.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.DeadSpace.Soyuz.Sterility.Components;

[RegisterComponent]
public sealed partial class DisposablePackageComponent : Component
{
    [DataField(required: true, customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string SpawnInjectorProto = default!;

    [DataField(required: true, customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string SpawnTrashProto = default!;

    [DataField]
    public SoundSpecifier? UnwrapSound = new SoundPathSpecifier("/Audio/Effects/unwrap.ogg");
}
