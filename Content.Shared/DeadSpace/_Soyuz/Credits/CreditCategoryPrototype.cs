// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.DeadSpace._Soyuz.Credits;

/// <summary>Категория товаров магазина Советских кредитов. Добавляется прототипом, в коде не зашита.</summary>
[Prototype]
public sealed partial class CreditCategoryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public LocId? Description;

    /// <summary>
    /// Сколько товаров этой категории игрок может иметь одновременно (купленных и действующих).
    /// Не задано или 0 = без лимита. Продление уже действующего товара лимит не затрагивает.
    /// </summary>
    [DataField]
    public int? MaxOwned;

    /// <summary>Порядок в списке категорий: меньше = выше.</summary>
    [DataField]
    public int Priority;
}
