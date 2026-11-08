// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared.DeadSpace._Soyuz.Respawn;

[CVarDefs]
public sealed class SoyuzRespawnCVars : CVars
{
    /// <summary>
    /// Через сколько секунд после превращения в призрака доступен респавн.
    /// </summary>
    public static readonly CVarDef<int> RespawnDelaySeconds =
        CVarDef.Create("soyuz.respawn.delay", 1200, CVar.SERVERONLY);
}