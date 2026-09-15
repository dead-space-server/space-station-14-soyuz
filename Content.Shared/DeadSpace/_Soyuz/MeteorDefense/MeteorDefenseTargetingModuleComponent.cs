// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using Robust.Shared.GameStates;

namespace Content.Shared.DeadSpace._Soyuz.MeteorDefense;

/// <summary>Identifies the unique part required by the standard machine construction pipeline.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MeteorDefenseTargetingModuleComponent : Component;
