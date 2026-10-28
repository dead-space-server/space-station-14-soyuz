// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.DeadSpace._Soyuz.Lockdown;

namespace Content.Server.DeadSpace._Soyuz.Lockdown;

/// <summary>
/// Состояние локдаунов станции. Общее для всех консолей станции. Создаётся на сущности станции
/// </summary>
[RegisterComponent]
public sealed partial class LockdownStationComponent : Component
{
    public readonly Dictionary<string, LockdownInstance> Departments = new();

    public LockdownInstance? Station;

    /// <summary>
    /// До какого времени консоли станции заблокированы для запуска новых локдаунов
    /// </summary>
    public TimeSpan ConsoleLockedUntil;

    /// <summary>
    /// Подтверждения по ID должности
    /// </summary>
    public readonly Dictionary<string, KeyConfirmation> Confirmations = new();
}

public sealed class LockdownInstance
{
    public LockdownPhase Phase;
    public string? DepartmentId;
    public EntityUid Console;
    public TimeSpan ActivateAt;
    public TimeSpan EndAt;
    public TimeSpan Duration;
    public TimeSpan SuccessCooldown;
    public readonly List<EntityUid> Doors = new();
    public readonly List<EntityUid> Bins = new();
}

public sealed class KeyConfirmation
{
    public EntityUid User;
    public TimeSpan ExpiresAt;
}

/// <summary>
/// Вешается на дверь на время локдауна
/// </summary>
[RegisterComponent]
public sealed partial class LockdownDoorComponent : Component
{
    public int Locks;
    public bool WasBolted;
    public EntityUid? Hologram;

    /// <summary>
    /// Пока задано, дверь временно снята с локдауна имплантом "свободный допуск"
    /// </summary>
    public TimeSpan? FreePassUntil;

    /// <summary>
    /// Время "свободного допуска" вышло, дверь ждёт закрытия, после чего снова болтируется
    /// </summary>
    public bool Relocking;

    /// <summary>
    /// Была ли дверь открыта в момент блокировки. Если да, то при снятии локдауна её нужно открыть обратно.
    /// </summary>
    public bool WasOpen;
}

/// <summary>
/// Вешается на мусорный бак на время локдауна. Считает число активных локдаунов
/// </summary>
[RegisterComponent]
public sealed partial class LockdownBinLockComponent : Component
{
    public int Locks;
}
