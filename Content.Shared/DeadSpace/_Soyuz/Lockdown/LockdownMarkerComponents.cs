// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

namespace Content.Shared.DeadSpace._Soyuz.Lockdown;

/// <summary>
/// Маркер для шлюзов, которые не блокируются при локдауне (общие шлюзы в коридорах)
/// </summary>
[RegisterComponent]
public sealed partial class LockdownImmuneComponent : Component;

/// <summary>
/// Маркер для импланта специального допуска. Владелец импланта может пользоваться консолью локдауна
/// </summary>
[RegisterComponent]
public sealed partial class LockdownAccessImplantComponent : Component;

/// <summary>
/// Маркер мусорного бака. При локдауне станции такие баки отключаются на всей станции, при локдауне отдела — только в помещениях этого отдела
/// </summary>
[RegisterComponent]
public sealed partial class LockdownTrashBinComponent : Component
{
    /// <summary>
    /// Максимальный размер помещения (в тайлах), которое проверяется при определении отдела
    /// Большие открытые зоны (коридоры, холлы) ни к какому отделу не относятся
    /// </summary>
    [DataField]
    public int RoomSearchLimit = 150;
}
