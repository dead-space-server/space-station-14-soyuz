// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using System.Collections.Frozen;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.Telecommunications;

/// <summary>
/// Stores station-local radio channel blocks, keyed by mind entity.
/// </summary>
[RegisterComponent, Access(typeof(TelecommunicationChannelBlacklistSystem))]
public sealed partial class TelecommunicationChannelBlacklistComponent : Component
{
    public readonly Dictionary<EntityUid, FrozenSet<ProtoId<RadioChannelPrototype>>> BlockedChannels = new();
}
