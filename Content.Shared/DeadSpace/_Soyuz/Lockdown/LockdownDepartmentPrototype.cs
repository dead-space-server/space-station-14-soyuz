// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.Access;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.Lockdown;

/// <summary>
/// Отдел, в котором можно объявить локдаун. Шлюзы отдела определяются по уровням доступа.
/// </summary>
[Prototype]
public sealed partial class LockdownDepartmentPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Ключ локализации названия отдела
    /// </summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>
    /// Шлюз относится к отделу, если его считыватель доступа содержит любой из этих уровней
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<AccessLevelPrototype>> Access = new();
}
