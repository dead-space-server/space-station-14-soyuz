// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT
using Robust.Shared.Prototypes;

namespace Content.Shared._Soyuz.Implant;

/// <summary>
/// Компонент для имплантации отдельных компонентов в сущность
/// </summary>
[RegisterComponent]
public sealed partial class ComponentInjectorImplantComponent : Component
{
    /// <summary>
    /// Какие компоненты надо сущности имплантировать
    /// </summary>
    [DataField(required: true)]
    public ComponentRegistry TargetComponents = new();

    /// <summary>
    /// Регистр установленных компонентов
    /// </summary>
    [DataField]
    public ComponentRegistry InstalledComponents = new();
}