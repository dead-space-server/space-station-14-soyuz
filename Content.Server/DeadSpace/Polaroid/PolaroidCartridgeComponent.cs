// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT
namespace Content.Server.DeadSpace.Polaroid;

[RegisterComponent]
public sealed partial class PolaroidCartridgeComponent : Component
{
    [DataField("maxAmount")]
    public int MaxAmount = 8;

    [DataField("currentAmount")]
    public int CurrentAmount = 8;
}
