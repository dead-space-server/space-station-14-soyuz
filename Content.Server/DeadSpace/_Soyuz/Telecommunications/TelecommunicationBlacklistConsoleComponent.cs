// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using Content.Shared.StationRecords;

namespace Content.Server.DeadSpace._Soyuz.Telecommunications;

/// <summary>
/// Stores the currently selected mind for a single-user telecommunications blacklist console.
/// </summary>
[RegisterComponent, Access(typeof(TelecommunicationBlacklistConsoleSystem))]
public sealed partial class TelecommunicationBlacklistConsoleComponent : Component
{
    public EntityUid? ActiveMind;
}

/// <summary>
/// Keeps the station record assigned to a mind when its character spawned.
/// This association survives the character dropping or losing their physical ID card.
/// </summary>
[RegisterComponent, Access(typeof(TelecommunicationBlacklistConsoleSystem))]
public sealed partial class TelecommunicationBlacklistStationRecordComponent : Component
{
    public StationRecordKey RecordKey = StationRecordKey.Invalid;
}
