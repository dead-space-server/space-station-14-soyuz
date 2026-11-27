// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Robust.Shared.Prototypes;

namespace Content.Shared.DeadSpace._Soyuz.StationSpecialization;

/// <summary>
/// Специализация станции. Задаётся админами в прототипе карты
/// </summary>
[RegisterComponent]
public sealed partial class StationSpecializationComponent : Component
{
    [DataField(required: true)]
    public ProtoId<StationSpecializationPrototype> Specialization;

    [ViewVariables]
    public TimeSpan NextPassive;

    [ViewVariables]
    public bool OneTimeApplied;

    /// <summary>Приписка к названию станции уже добавлена</summary>
    [ViewVariables]
    public bool NamePrefixApplied;

    /// <summary>Сколько позывных с данной основой уже выдано</summary>
    [ViewVariables]
    public Dictionary<string, int> CallsignCounters = new();
}
