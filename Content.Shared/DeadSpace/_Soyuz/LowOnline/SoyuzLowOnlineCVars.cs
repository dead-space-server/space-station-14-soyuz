// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared.DeadSpace._Soyuz.LowOnline;

[CVarDefs]
public sealed class SoyuzLowOnlineCVars : CVars
{
    /// <summary>
    /// Сколько секунд от начала смены (или от момента исчезновения инженеров) должно пройти без инженеров, чтобы АэС «Марта» включила бесконечный заряд СМЭСов
    /// </summary>
    public static readonly CVarDef<int> EngineeringActivateDelay =
        CVarDef.Create("soyuz.low_online.engineering_activate_delay", 600, CVar.SERVERONLY);

    /// <summary>
    /// Через сколько секунд после появления инженера бесконечный заряд отключается
    /// </summary>
    public static readonly CVarDef<int> EngineeringDisableDelay =
        CVarDef.Create("soyuz.low_online.engineering_disable_delay", 300, CVar.SERVERONLY);
}