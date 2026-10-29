// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Robust.Shared.Serialization;
using Robust.Shared.Utility;

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
    /// <summary>Объявлен, идёт обратный отсчёт до блокировки.</summary>
    Pending,
    /// <summary>Шлюзы заблокированы.</summary>
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
/// Один из «ключей» локдауна станции.
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

/// <summary>
/// Раздел бокового меню (запись, камеры и т.д.).
/// </summary>
[Serializable, NetSerializable]
public sealed class SecuritySectionEntry
{
    public readonly string Id;
    public readonly string Name;
    public readonly SpriteSpecifier? Icon;

    public SecuritySectionEntry(string id, string name, SpriteSpecifier? icon)
    {
        Id = id;
        Name = name;
        Icon = icon;
    }
}

[Serializable, NetSerializable]
public sealed class LockdownConsoleBoundUserInterfaceState : BoundUserInterfaceState
{
    /// <summary>
    /// Те из смотрящих консоль, у кого есть имплант специального допуска (разблокирует пункт LOCKDOWN).
    /// Клиент сам проверяет, есть ли в списке его сущность.
    /// </summary>
    public readonly List<NetEntity> AccessActors;
    public readonly List<SecuritySectionEntry> Sections;
    public readonly bool HasStation;
    public readonly TimeSpan CurTime;
    public readonly TimeSpan ConsoleLockedUntil;
    public readonly List<LockdownEntry> Departments;
    public readonly LockdownEntry Station;
    public readonly List<LockdownKeyEntry> Keys;

    public LockdownConsoleBoundUserInterfaceState(
        List<NetEntity> accessActors,
        List<SecuritySectionEntry> sections,
        bool hasStation,
        TimeSpan curTime,
        TimeSpan consoleLockedUntil,
        List<LockdownEntry> departments,
        LockdownEntry station,
        List<LockdownKeyEntry> keys)
    {
        AccessActors = accessActors;
        Sections = sections;
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

/// <summary>Подтверждение «ключа» для локдауна станции.</summary>
[Serializable, NetSerializable]
public sealed class LockdownConfirmStationMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class LockdownCancelStationMessage : BoundUserInterfaceMessage;

/// <summary>Открыть дополнительный раздел консоли (записи, камеры и т.д.).</summary>
[Serializable, NetSerializable]
public sealed class SecurityConsoleOpenSectionMessage : BoundUserInterfaceMessage
{
    public readonly string Section;

    public SecurityConsoleOpenSectionMessage(string section)
    {
        Section = section;
    }
}