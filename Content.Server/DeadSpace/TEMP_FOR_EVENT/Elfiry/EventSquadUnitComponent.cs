// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.DeadSpace.TEMP_FOR_EVENT.Elfiry;
using System.Numerics;
using Robust.Shared.Map;

namespace Content.Server.DeadSpace.TEMP_FOR_EVENT.Elfiry;

[RegisterComponent]
public sealed partial class EventSquadUnitComponent : Component
{
    [DataField] public string Team = "HomewardRassvet";
    [DataField] public int Squad;
    public EntityUid Commander;
    public EntityCoordinates Destination;
    public EventSquadOrder Order = EventSquadOrder.Defend;
    public uint Revision;
    public bool Ordered;
    public bool Arrived;
    public TimeSpan RetryAfter;
    public TimeSpan LastProgress;
    public float RemainingDistance = float.PositiveInfinity;
    public EntityUid? DefensiveGrid;
    public string Status = string.Empty;
    public int FormationSlot;
    public EventCommandPath? Path;
    public int RouteIndex;
    public EntityCoordinates ProgressPosition;
    public EntityCoordinates FollowPosition;
    public EventPassageYield? Passage;
    public EntityUid? WaitingFor;
    public TimeSpan WaitingSince;
    public TimeSpan? WaitingStarted;
    public Vector2 WaitingPosition;
    public EntityUid? Avoiding;
    public TimeSpan AvoidUntil;
    public TimeSpan NextPassageCheck;
    public bool TrafficStopped;
    public readonly List<EntityCoordinates> TravelHistory = new();
    public EntityUid? DamageSource;
    public MapCoordinates? DamagePosition;
    public TimeSpan DamageUntil;
    public EntityUid AssaultTarget;
    public EntityCoordinates AssaultPosition;
    public TimeSpan AssaultUntil;
    public EventWalkSearch? WalkSearch;
}

public sealed record EventCommandPath(uint Revision, EntityCoordinates Destination, EntityCoordinates[] Route, EntityUid[] Doors)
{
    public EventMovementTarget? Target { get; init; }
    public EntityUid? EngagementDoor { get; init; }
}

public enum EventMovementKind : byte { Command, Passage, Medicine, Combat }

public sealed record EventMovementTarget(EventMovementKind Kind, EntityCoordinates Destination, EntityUid? Subject = null,
    EventPassageYield? Passage = null)
{
    public bool Matches(EventMovementTarget other)
        => Kind == other.Kind && Subject == other.Subject && ReferenceEquals(Passage, other.Passage) &&
           Destination.EntityId == other.Destination.EntityId &&
           Vector2.DistanceSquared(Destination.Position, other.Destination.Position) <=
               (Kind == EventMovementKind.Combat ? 1f : Kind == EventMovementKind.Medicine ? 0.25f : 0.0001f);
}

public sealed record EventPassageYield(EntityUid Passer, uint Revision, uint PasserRevision,
    EntityCoordinates Destination, EntityCoordinates[] Route, EntityUid[] Doors,
    MapCoordinates Origin, Vector2 Direction, TimeSpan Until);

public static class EventTrafficPolicy
{
    public static bool Yields(int ownId, int otherId, bool moving, bool otherMoving)
        => moving != otherMoving ? !moving : ownId > otherId;

    public static bool Blocks(float along, float across, bool waiting)
        => along > -0.1f && along < (waiting ? 1.2f : 0.95f) && MathF.Abs(across) < 0.65f;

    public static bool OverlapPriority(int ownId, int otherId, float distanceSquared)
        => distanceSquared < 0.36f && ownId < otherId;
}
