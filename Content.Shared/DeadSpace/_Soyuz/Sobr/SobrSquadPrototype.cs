// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.Sobr;

/// <summary>
/// Описание отряда патриотов. Пишешь прототип и дальше этот отряд используется
/// в пультах и капсулах. Все ясн понятн ставлю лосяру
/// </summary>
[Prototype]
public sealed partial class SobrSquadPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Как звать твой отряд
    /// Использовать либо ключ локализации, либо прям в название хардкодом бахай
    /// </summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>
    /// Цвет кнопощке в пульте вызова
    /// </summary>
    [DataField]
    public Color Color = Color.White;
}