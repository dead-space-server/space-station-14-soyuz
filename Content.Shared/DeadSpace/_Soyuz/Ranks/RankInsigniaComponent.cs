// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using Content.Shared._RMC14.Marines.Roles.Ranks;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.Ranks;

/// <summary>
/// Лычки (погоны со званием). Цепляется на комбинезон, а звание носителя определяется тем, какие лычки на нём сейчас видны
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RankInsigniaComponent : Component
{
    /// <summary>
    /// Звание, которое показывают эти лычки
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<RankPrototype>? Rank;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class RankInsigniaHolderComponent : Component;

/// <summary>
/// Игрок выбрал в меню комбинезона "Свернуть лычки" или "Развернуть лычки".
/// </summary>
public sealed class ToggleRankInsigniaEvent : EntityEventArgs
{
    public readonly EntityUid Holder;
    public readonly EntityUid Insignia;
    public readonly EntityUid User;

    public ToggleRankInsigniaEvent(EntityUid holder, EntityUid insignia, EntityUid user)
    {
        Holder = holder;
        Insignia = insignia;
        User = user;
    }
}