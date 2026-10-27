// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.DeadSpace._Soyuz.EvacZones;

/// <summary>
/// Вешается на сущность карты. Хранит нарисованные зоны этой карты, их видят на сканере масс и как голограммы в мире
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EvacZoneMapComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<EvacZoneStroke> Strokes = new();
}
