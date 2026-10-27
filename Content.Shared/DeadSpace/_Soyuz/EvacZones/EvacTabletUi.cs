// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace._Soyuz.EvacZones;

[Serializable, NetSerializable]
public enum EvacTabletUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class EvacTabletBuiState : BoundUserInterfaceState
{
    public MapId Map;
    public Vector2 Origin;
    public List<EvacZoneStroke> Strokes;
    public List<EvacZoneColor> Palette;
    public bool CanAnnounce;

    public bool UsingDisk;

    public EvacTabletBuiState(MapId map, Vector2 origin, List<EvacZoneStroke> strokes, List<EvacZoneColor> palette, bool canAnnounce, bool usingDisk)
    {
        Map = map;
        Origin = origin;
        Strokes = strokes;
        Palette = palette;
        CanAnnounce = canAnnounce;
        UsingDisk = usingDisk;
    }
}

[Serializable, NetSerializable]
public sealed class EvacTabletSendMessage : BoundUserInterfaceMessage
{
    public List<EvacZoneStroke> Strokes;
    public bool Announce;

    public EvacTabletSendMessage(List<EvacZoneStroke> strokes, bool announce)
    {
        Strokes = strokes;
        Announce = announce;
    }
}
