// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.DeadSpace._Soyuz.EvacZones;

/// <summary>
/// Планшет зон. Что объявлять при отправке и какие цвета доступны - настраивается в прототипе.
/// </summary>
[RegisterComponent]
public sealed partial class EvacZoneTabletComponent : Component
{

    /// <summary>
    /// Ключ локализации текста объявления, а если пусто, то объявления нет
    /// </summary>
    [DataField]
    public string? Announcement;

    /// <summary>
    /// Отправитель объявление, по умолчанию - командование. Допускается использование ключа локализации
    /// </summary>
    [DataField]
    public string AnnouncementSender = "evac-sender-default";

    /// <summary>
    /// Цвет объявления
    /// </summary>
    [DataField]
    public Color? AnnouncementColor;

    [DataField]
    public SoundSpecifier? AnnouncementSound;

    /// <summary>
    /// Цвета планшета
    /// </summary>
    [DataField]
    public List<EvacZoneColor> Palette = new()
    {
        new EvacZoneColor("evac-color-green", new Color(0.24f, 0.86f, 0.37f)),
        new EvacZoneColor("evac-color-red", new Color(0.92f, 0.22f, 0.2f)),
        new EvacZoneColor("evac-color-blue", new Color(0.25f, 0.55f, 1f)),
    };

    /// <summary>
    /// Пауза между отправками.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(3);

    [ViewVariables]
    public TimeSpan NextUse;
}
