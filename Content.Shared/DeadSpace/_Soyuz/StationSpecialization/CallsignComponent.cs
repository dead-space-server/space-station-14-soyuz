// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
namespace Content.Shared.DeadSpace._Soyuz.StationSpecialization;

/// <summary>
/// Позывной выдаётся при заходе в раунд, если у станции включены позывные
/// </summary>
[RegisterComponent]
public sealed partial class CallsignComponent : Component
{
    /// <summary>Основа позывного, например Щит</summary>
    [DataField]
    public string? Base;

    /// <summary>Готовый позывной без нумерации</summary>
    [DataField]
    public string? FixedValue;

    /// <summary>Глава отдела</summary>
    [DataField]
    public bool IsHead;

    [ViewVariables]
    public string? Assigned;
}
