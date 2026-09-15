// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.MeteorDefense;

/// <summary>Raised before each station-bound threat spawns, independently of the originating rule.</summary>
[ByRefEvent]
public record struct MeteorInterceptAttemptEvent(EntityUid Target, EntProtoId Prototype, bool Cancelled = false);
