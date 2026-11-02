// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

namespace Content.Server.DeadSpace._Soyuz.Ranks;

/// <summary>
/// Метка на мобе, чьё звание задаётся лычками на комбинезоне. Только у таких мобов звание сбрасывается при снятии лычек, чтобы не затрагивать звания, выданные другими способами.
/// </summary>
[RegisterComponent]
public sealed partial class RankInsigniaWearerComponent : Component;