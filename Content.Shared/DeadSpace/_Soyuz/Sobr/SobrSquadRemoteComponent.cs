// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.Sobr;

/// <summary>
/// Пульт для пробуждения отрядов, сами отряды настраиваются в прототипе
/// </summary>
[RegisterComponent]
public sealed partial class SobrSquadRemoteComponent : Component
{
    /// <summary>
    /// Отряды, которые можно пробудить
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<SobrSquadPrototype>> Squads = new();

    /// <summary>
    /// Максимальный радиус, на который пульт действует. Если ничего не писать, то действует на всю карту
    /// </summary>
    [DataField]
    public float? MaxRange;

    /// <summary>
    /// Кулдаун между активациями пробуждений отрядов
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(2);

    [ViewVariables]
    public TimeSpan NextUse;
}