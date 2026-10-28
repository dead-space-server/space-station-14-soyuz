// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.Lockdown;

/// <summary>
/// Консоль локдауна. Позволяет объявить локдаун в отделе или на всей станции
/// Для работы требуется имплант с <see cref="LockdownAccessImplantComponent"/>
/// </summary>
[RegisterComponent]
public sealed partial class LockdownConsoleComponent : Component
{
    /// <summary>
    /// Отделы, доступные на этой консоли
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<LockdownDepartmentPrototype>> Departments = new();

    /// <summary>
    /// Должности, которые могут дать локдаун станции. Для локдауна станции каждая из них должна подтвердить запуск
    /// </summary>
    [DataField]
    public List<ProtoId<JobPrototype>> KeyJobs = new() { "Captain", "HeadOfSecurity" };

    /// <summary>
    /// Должность, которая может отменить локдаун станции
    /// </summary>
    [DataField]
    public ProtoId<JobPrototype> StationCancelJob = "Captain";

    /// <summary>
    /// Время между объявлением и блокировкой
    /// </summary>
    [DataField]
    public TimeSpan WarningDelay = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Сколько длится локдаун, после чего снимается автоматически
    /// </summary>
    [DataField]
    public TimeSpan LockdownDuration = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Окно, в течение которого два ключа должны быть подтверждены одновременно
    /// </summary>
    [DataField]
    public TimeSpan KeyConfirmWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Блокировка консоли, если локдаун отменили до блокировки шлюзов
    /// </summary>
    [DataField]
    public TimeSpan CancelledCooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Блокировка консоли после успешно сработавшего локдауна
    /// </summary>
    [DataField]
    public TimeSpan SuccessCooldown = TimeSpan.FromMinutes(3);
}
