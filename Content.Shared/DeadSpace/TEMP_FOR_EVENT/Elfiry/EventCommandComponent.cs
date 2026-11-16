// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.TEMP_FOR_EVENT.Elfiry;

[RegisterComponent, NetworkedComponent]
public sealed partial class EventCommandTabletComponent : Component
{
    [DataField] public string Team = "HomewardRassvet";
    [DataField] public float CommandRange = 256f;
    public Guid Token;
    public TimeSpan NextRequest;
    public string Status = string.Empty;
    public EntityUid? User;
}

[RegisterComponent]
public sealed partial class EventCommanderComponent : Component
{
    [DataField] public string Team = "HomewardRassvet";
}

[RegisterComponent]
public sealed partial class EventCommandWaypointComponent : Component
{
    [DataField] public string Team = "HomewardRassvet";
}

[Serializable, NetSerializable]
public enum EventCommandUiKey : byte { Key }

[Serializable, NetSerializable]
public enum EventSquadOrder : byte { Move, Defend, Assault, Retreat, Follow }

public static class EventSquadLimits
{
    public const int Members = 5;
    public const int NameLength = 40;
}

[Serializable, NetSerializable]
public enum EventSquadManagement : byte { Create, Rename, Delete }

[Serializable, NetSerializable]
public sealed class EventSquadManageRequest(Guid token, EventSquadManagement action, int squad, string name) : BoundUserInterfaceMessage
{
    public readonly Guid Token = token;
    public readonly EventSquadManagement Action = action;
    public readonly int Squad = squad;
    public readonly string Name = name;
}

[Serializable, NetSerializable]
public sealed class EventCommandSquadState(int id, string name, int members)
{
    public readonly int Id = id;
    public readonly string Name = name;
    public readonly int Members = members;
}

[Serializable, NetSerializable]
public sealed class EventSquadRequest(Guid token, int squad, EventSquadOrder order, int mapId, Vector2 position) : BoundUserInterfaceMessage
{
    public readonly Guid Token = token;
    public readonly int Squad = squad;
    public readonly EventSquadOrder Order = order;
    public readonly int MapId = mapId;
    public readonly Vector2 Position = position;
}

[Serializable, NetSerializable]
public sealed class EventSquadAssignRequest(Guid token, NetEntity unit, int squad) : BoundUserInterfaceMessage
{
    public readonly Guid Token = token;
    public readonly NetEntity Unit = unit;
    public readonly int Squad = squad;
}

[Serializable, NetSerializable]
public sealed class EventCommandUnitState(NetEntity entity, string name, int squad, Vector2 position, string status, bool alive, bool medic)
{
    public readonly NetEntity Entity = entity;
    public readonly string Name = name;
    public readonly int Squad = squad;
    public readonly Vector2 Position = position;
    public readonly string Status = status;
    public readonly bool Alive = alive;
    public readonly bool Medic = medic;
}

[Serializable, NetSerializable]
public sealed class EventCommandPointState(string name, Vector2 position)
{
    public readonly string Name = name;
    public readonly Vector2 Position = position;
}

[Serializable, NetSerializable]
public enum EventMapKind : byte { Floor, Wall, Window, ClosedDoor, OpenDoor, Equipment }

[Serializable, NetSerializable]
public sealed class EventCommandMapRect(Box2 bounds, EventMapKind kind, float rotation = 0f, string name = "")
{
    public readonly Box2 Bounds = bounds;
    public readonly EventMapKind Kind = kind;
    public readonly float Rotation = rotation;
    public readonly string Name = name;
}

[Serializable, NetSerializable]
public sealed class EventCommandGridState(Vector2 position, float rotation, bool planet, EventCommandMapRect[] surfaces)
{
    public readonly Vector2 Position = position;
    public readonly float Rotation = rotation;
    public readonly bool Planet = planet;
    public readonly EventCommandMapRect[] Surfaces = surfaces;
}

[Serializable, NetSerializable]
public sealed class EventCommandUiState(Guid token, bool authorized, int mapId, Vector2 commander,
    EventCommandUnitState[] units, EventCommandPointState[] points, EventCommandGridState[] grids, string status,
    EventCommandSquadState[] squads) : BoundUserInterfaceState
{
    public readonly Guid Token = token;
    public readonly bool Authorized = authorized;
    public readonly int MapId = mapId;
    public readonly Vector2 Commander = commander;
    public readonly EventCommandUnitState[] Units = units;
    public readonly EventCommandPointState[] Points = points;
    public readonly EventCommandGridState[] Grids = grids;
    public readonly string Status = status;
    public readonly EventCommandSquadState[] Squads = squads;
}
