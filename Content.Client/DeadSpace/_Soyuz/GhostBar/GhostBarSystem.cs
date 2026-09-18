// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE.TXT

using Content.Shared.DeadSpace._Soyuz.GhostBar;
using JetBrains.Annotations;

namespace Content.Client.DeadSpace._Soyuz.GhostBar;

[UsedImplicitly]
public sealed class GhostBarSystem : EntitySystem
{
    public void RequestJoin()
    {
        RaiseNetworkEvent(new JoinGhostBarEvent());
    }
}