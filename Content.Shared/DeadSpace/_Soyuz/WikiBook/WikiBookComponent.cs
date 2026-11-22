// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
namespace Content.Shared.DeadSpace._Soyuz.WikiBook;

/// <summary>
/// Книжка с руководством, содержимое которой берётся с вики
/// </summary>
[RegisterComponent]
public sealed partial class WikiBookComponent : Component
{
    /// <summary>Название страницы на вики, например «Союз:Правила_Внутреннего_Распорядка»</summary>
    [DataField(required: true)]
    public string Page = string.Empty;

    /// <summary>Подставлять вариант страницы для специализации станции, если он есть</summary>
    [DataField]
    public bool UseSpecialization = true;
}
