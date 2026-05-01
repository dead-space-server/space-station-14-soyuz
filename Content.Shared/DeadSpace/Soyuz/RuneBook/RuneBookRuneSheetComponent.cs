using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace.Soyuz.RuneBook;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RuneBookRuneSheetComponent : Component
{
    [DataField("runePrototype"), AutoNetworkedField]
    public ProtoId<RuneBookRunePrototype>? RunePrototype;

    [DataField("runeIndex"), AutoNetworkedField]
    public int RuneIndex = -1;
}
