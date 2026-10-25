// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace.Actions;

[RegisterComponent]
public sealed partial class ActionGateComponent : Component
{
    public List<ActionGateGroup> Groups = new();
}

[DataDefinition]
public sealed partial class ActionGateGroup
{
    [DataField]
    public string Id = string.Empty;

    public List<EntProtoId> Prototypes = new();

    public List<EntityUid> Granted = new();
}
