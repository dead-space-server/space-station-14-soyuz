// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.Lockdown;

[Serializable, NetSerializable]
public enum LockdownConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum LockdownPhase : byte
{
    None,
    /// <summary>
    /// Объявлен, идёт обратный отсчёт до блокировки.
    /// </summary>
    Pending,
    /// <summary>
    /// Шлюзы заблокированы.
    /// </summary>
    Active,
}

/// <summary>
/// Состояние локдауна отдела (или станции). <see cref="PhaseEnd"/> — игровое время конца текущей фазы.
/// </summary>
[Serializable, NetSerializable]
public sealed class LockdownEntry
{
    public readonly string Id;
    public readonly string Name;
    public readonly LockdownPhase Phase;
    public readonly TimeSpan PhaseEnd;

    public LockdownEntry(string id, string name, LockdownPhase phase, TimeSpan phaseEnd)
    {
        Id = id;
        Name = name;
        Phase = phase;
        PhaseEnd = phaseEnd;
    }
}

/// <summary>
/// Один из ключей локдауна станции
/// </summary>
[Serializable, NetSerializable]
public sealed class LockdownKeyEntry
{
    public readonly string Job;
    public readonly string JobName;
    public readonly bool Confirmed;
    public readonly TimeSpan ExpiresAt;

    public LockdownKeyEntry(string job, string jobName, bool confirmed, TimeSpan expiresAt)
    {
        Job = job;
        JobName = jobName;
        Confirmed = confirmed;
        ExpiresAt = expiresAt;
    }
}

[Serializable, NetSerializable]
public sealed class LockdownConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly bool HasStation;
    public readonly TimeSpan CurTime;
    public readonly TimeSpan ConsoleLockedUntil;
    public readonly List<LockdownEntry> Departments;
    public readonly LockdownEntry Station;
    public readonly List<LockdownKeyEntry> Keys;

    public LockdownConsoleBoundUserInterfaceState(
        bool hasStation,
        TimeSpan curTime,
        TimeSpan consoleLockedUntil,
        List<LockdownEntry> departments,
        LockdownEntry station,
        List<LockdownKeyEntry> keys)
    {
        HasStation = hasStation;
        CurTime = curTime;
        ConsoleLockedUntil = consoleLockedUntil;
        Departments = departments;
        Station = station;
        Keys = keys;
    }
}

[Serializable, NetSerializable]
public sealed class LockdownStartDepartmentMessage : BoundUserInterfaceMessage
{
    public readonly string Department;

    public LockdownStartDepartmentMessage(string department)
    {
        Department = department;
    }
}

[Serializable, NetSerializable]
public sealed class LockdownCancelDepartmentMessage : BoundUserInterfaceMessage
{
    public readonly string Department;

    public LockdownCancelDepartmentMessage(string department)
    {
        Department = department;
    }
}

/// <summary>
/// Подтверждение ключа для локдауна станции
/// </summary>
[Serializable, NetSerializable]
public sealed class LockdownConfirmStationMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class LockdownCancelStationMessage : BoundUserInterfaceMessage;
