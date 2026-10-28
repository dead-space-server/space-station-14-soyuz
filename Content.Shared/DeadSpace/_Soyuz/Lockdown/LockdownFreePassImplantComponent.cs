// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.Actions;

namespace Content.Shared.DeadSpace._Soyuz.Lockdown;

/// <summary>
/// Имплант "свободный допуск". Даёт действие, которое на короткое время снимает выбранную дверь с локдауна
/// </summary>
[RegisterComponent]
public sealed partial class LockdownFreePassImplantComponent : Component
{
    /// <summary>
    /// На сколько дверь снимается с локдауна
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Уровни тревоги станции (ID), при которых имплант не работает
    /// </summary>
    [DataField]
    public List<string> DisabledAlertLevels = new() { "green", "blue" };

    /// <summary>
    /// Максимальная дистанция до двери
    /// </summary>
    [DataField]
    public float Range = 2f;
}

/// <summary>
/// Действие импланта
/// </summary>
public sealed partial class LockdownFreePassEvent : EntityTargetActionEvent;
