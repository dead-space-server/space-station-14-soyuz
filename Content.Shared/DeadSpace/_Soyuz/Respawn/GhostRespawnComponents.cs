// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.Respawn;

/// <summary>
/// Когда призраку можно респавниться 
/// </summary>
[RegisterComponent]
public sealed partial class GhostRespawnComponent : Component
{
    /// <summary>
    /// Игровое время, начиная с которого доступен респавн
    /// </summary>
    [ViewVariables]
    public TimeSpan RespawnAt;

    [ViewVariables]
    public EntityUid? Action;
}

/// <summary>
/// Пока флаг активен, респавн в режиме запрещен
/// </summary>
[RegisterComponent]
public sealed partial class RespawnDisabledComponent : Component;

/// <summary>
/// Событие респавна
/// </summary>
public sealed partial class GhostRespawnActionEvent : InstantActionEvent;

/// <summary>
/// Сервер просит клиент показать окно подтверждения респавна
/// </summary>
[Serializable, NetSerializable]
public sealed class RespawnConfirmRequestEvent : EntityEventArgs;

/// <summary>
/// Клиент подтвердил респавн в окне
/// </summary>
[Serializable, NetSerializable]
public sealed class RespawnConfirmedEvent : EntityEventArgs;