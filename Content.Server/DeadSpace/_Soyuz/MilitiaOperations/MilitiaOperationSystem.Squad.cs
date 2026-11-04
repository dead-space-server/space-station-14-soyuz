// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server.Weapons.Melee;
using Content.Shared.Damage.Components;
using Content.Shared.Body.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Interaction;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Content.Shared.Trigger.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Physics.Systems;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly EntityLookupSystem _squadLookup = default!;
    [Dependency] private readonly SharedPhysicsSystem _squadPhysics = default!;
    [Dependency] private readonly SharedDoorSystem _squadDoors = default!;
    [Dependency] private readonly MeleeWeaponSystem _squadMelee = default!;
    [Dependency] private readonly TagSystem _squadTags = default!;
    private static readonly ProtoId<TagPrototype> GrenadeTag = "Grenade";
    private static readonly ProtoId<TagPrototype> HandGrenadeTag = "HandGrenade";
    private static readonly ProtoId<TagPrototype> FlashGrenadeTag = "GrenadeFlashBang";
    private TimeSpan _nextSquadUpdate;
    private TimeSpan _nextSquadSafety;
    private const CollisionGroup SquadSight = CollisionGroup.Impassable | CollisionGroup.InteractImpassable;

    private bool SquadMember(EntityUid uid)
        => AvailableActor(uid, out _) && HasComp<ActiveNPCComponent>(uid) && !MetaData(uid).EntityPaused &&
           TryComp<HTNComponent>(uid, out var htn) && htn.Enabled;

    private bool SquadThreat(EntityUid uid, EntityUid target)
    {
        if (!Exists(target) || TerminatingOrDeleted(target) || EntityManager.IsQueuedForDeletion(target) ||
            _mobs.IsIncapacitated(target) || Transform(uid).MapID != Transform(target).MapID)
            return false;
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        if (!actor.Hostility.TryGetValue(target, out var hostile))
            actor.Hostility[target] = hostile = _factions.IsEntityHostile(uid, target);
        return hostile;
    }

    private bool SquadVisible(EntityUid uid, EntityUid target, float range)
        => SquadThreat(uid, target) && _interaction.InRangeUnobstructed(uid, target, range, SquadSight);

    private bool CoverExposed(MapCoordinates origin, MapCoordinates target, float range = 0f)
        => _interaction.InRangeUnobstructed(origin, target, range, SquadSight, predicate: entity => HasComp<DoorComponent>(entity));

    private void UpdateSquads()
    {
        if (_timing.CurTime < _nextSquadUpdate)
            return;
        _nextSquadUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        var members = new List<EntityUid>();
        var query = EntityQueryEnumerator<MilitiaOperationActorComponent>();
        while (query.MoveNext(out var uid, out var actor))
        {
            if (!SquadMember(uid))
                continue;
            members.Add(uid);
            foreach (var target in actor.Contacts.Keys.ToArray())
            {
                if (actor.Contacts[target].Expires <= _timing.CurTime ||
                    !actor.Contacts[target].Position.IsValid(EntityManager) || !SquadThreat(uid, target))
                    actor.Contacts.Remove(target);
            }
            foreach (var target in _factions.GetNearbyHostiles(uid, actor.SquadVision)
                         .Where(target => SquadVisible(uid, target, actor.SquadVision))
                         .OrderBy(target => Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(target)))
                         .Take(16))
            {
                var transform = Transform(target);
                var anchor = transform.GridUid ?? transform.MapUid;
                if (anchor == null)
                    continue;
                actor.Contacts[target] = new MilitiaSquadContact(target,
                    _transform.ToCoordinates(anchor.Value, _transform.GetMapCoordinates(target)),
                    _timing.CurTime, _timing.CurTime + actor.ContactMemory);
            }
        }
        var remaining = members.OrderBy(uid => uid.GetHashCode()).ToHashSet();
        while (remaining.Count > 0)
        {
            var group = new List<EntityUid>();
            var pending = new Queue<EntityUid>();
            var leader = remaining.MinBy(uid => uid.GetHashCode());
            remaining.Remove(leader);
            pending.Enqueue(leader);
            while (pending.TryDequeue(out var member))
            {
                group.Add(member);
                foreach (var other in remaining.ToArray())
                {
                    var range = MathF.Min(Comp<MilitiaOperationActorComponent>(member).SquadRange,
                        Comp<MilitiaOperationActorComponent>(other).SquadRange);
                    if (!SameOperation(member, other) || Transform(member).MapID != Transform(other).MapID ||
                        Vector2.DistanceSquared(_transform.GetWorldPosition(member), _transform.GetWorldPosition(other)) > range * range &&
                        (!TryComp<MilitiaOperationEnemyComponent>(member, out var radio) || radio.Operation == Guid.Empty))
                        continue;
                    remaining.Remove(other);
                    pending.Enqueue(other);
                }
            }
            CoordinateSquad(group.ToArray());
        }
    }

    private bool DirectContact(EntityUid uid, MilitiaOperationActorComponent actor)
        => actor.ThreatScanValidUntil > _timing.CurTime && actor.DirectTarget.IsValid() && SquadThreat(uid, actor.DirectTarget);

    private bool RecentContact(MilitiaOperationActorComponent actor, EntityUid target)
        => target.IsValid() && actor.LastDirectTarget == target && _timing.CurTime < actor.LastDirectAt + TimeSpan.FromSeconds(1.5);

    private void ClearSquadFacing(Entity<MilitiaOperationActorComponent> actor)
    {
        if (!actor.Comp.ControlsFacing)
            return;
        actor.Comp.ControlsFacing = false;
        if (!TerminatingOrDeleted(actor.Owner))
            RemComp<NoRotateOnMoveComponent>(actor.Owner);
    }

    private void FaceSquadThreat(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (!SquadMember(uid))
        {
            ClearSquadFacing((uid, actor));
            return;
        }
        var known = actor.SquadContacts.Where(c => c.Expires > _timing.CurTime && c.Position.IsValid(EntityManager))
            .MinBy(c => Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.ToMapCoordinates(c.Position).Position));
        var target = DirectContact(uid, actor) ? _transform.GetMapCoordinates(actor.DirectTarget)
            : known != null ? _transform.ToMapCoordinates(known.Position) : (MapCoordinates?) null;
        if (target == null)
        {
            ClearSquadFacing((uid, actor));
            return;
        }
        if (!HasComp<NoRotateOnMoveComponent>(uid))
        {
            EnsureComp<NoRotateOnMoveComponent>(uid);
            actor.ControlsFacing = true;
        }
        var delta = target.Value.Position - _transform.GetWorldPosition(uid);
        if (delta.LengthSquared() > 0.01f)
            _transform.SetWorldRotation(uid, delta.ToWorldAngle());
    }

    private bool NeedsRecovery(EntityUid uid, MilitiaOperationActorComponent actor)
        => actor.TriageRequired || NeedsReload(uid) ||
           TryComp<DamageableComponent>(uid, out var damage) && damage.TotalDamage >= actor.TreatmentThreshold;

    private bool HasTriageWork(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (!actor.Medic)
            return false;
        var patients = EntityQueryEnumerator<MilitiaOperationActorComponent, DamageableComponent>();
        while (patients.MoveNext(out var other, out _, out var damage))
        {
            if (!_mobs.IsDead(other) && !HasPlayerMind(other) && SameOperation(uid, other) && InTreatmentArea(uid, other, actor) &&
                (damage.TotalDamage >= actor.TreatmentThreshold || TryComp<BloodstreamComponent>(other, out var blood) && blood.BleedAmount > 0) &&
                HasTreatment(uid, actor, other, damage))
                return true;
        }
        return false;
    }

    private bool MedicSupportPriority(EntityUid uid, MilitiaOperationActorComponent actor)
        => actor.Medic && (actor.MedicalRefill != null || actor.Patient.IsValid() || actor.TriageRequired ||
            HasTriageWork(uid, actor) || NeedsMedicalRefill(uid));

    private bool EnemyPushing(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (!DirectContact(uid, actor))
            return false;
        if (!actor.PeekHome.IsValid(EntityManager) || !actor.PeekEnemyPosition.IsValid(EntityManager))
            return true;
        if (CoverExposed(_transform.ToMapCoordinates(actor.PeekHome),
                _transform.GetMapCoordinates(actor.DirectTarget), actor.SquadVision))
            return true;
        var origin = _transform.GetWorldPosition(uid);
        return Vector2.Distance(origin, _transform.GetWorldPosition(actor.DirectTarget)) + 1f <
               Vector2.Distance(origin, _transform.ToMapCoordinates(actor.PeekEnemyPosition).Position);
    }

    private void CoordinateSquad(EntityUid[] group)
    {
        var contacts = group.SelectMany(uid => Comp<MilitiaOperationActorComponent>(uid).Contacts.Values)
            .GroupBy(contact => contact.Target).Select(known => known.MaxBy(contact => contact.SeenAt)!)
            .OrderByDescending(contact => contact.SeenAt).ToArray();
        var orders = new List<(EntityUid Uid, MilitiaSquadOrder? Order)>();
        var occupied = new List<MapCoordinates>();
        var peeking = group.Any(uid => Comp<MilitiaOperationActorComponent>(uid).SquadOrder is { Task: MilitiaSquadTask.Peek } order &&
            order.Until > _timing.CurTime);
        foreach (var uid in group.OrderBy(uid => Comp<MilitiaOperationActorComponent>(uid).Medic ? 0 : 1))
        {
            var actor = Comp<MilitiaOperationActorComponent>(uid);
            actor.SquadContacts = contacts;
            actor.AlertUntil = group.Select(other => Comp<MilitiaOperationActorComponent>(other).AlertUntil).Max();
            actor.TriageRequired = HasTriageWork(uid, actor);
            var transform = Transform(uid);
            if (!actor.DefensivePosition.IsValid(EntityManager))
                actor.DefensivePosition = transform.Coordinates;
            foreach (var position in actor.RejectedSquadPositions.Keys.ToArray())
            {
                if (actor.RejectedSquadPositions[position] <= _timing.CurTime)
                    actor.RejectedSquadPositions.Remove(position);
            }
            if (actor.MedicalRefill != null || actor.Patient.IsValid() || ReceivingTreatment(uid) || actor.ReloadUntil != null)
                continue;
            var direct = DirectContact(uid, actor);
            var danger = actor.Grenades.Length > 0;
            var medic = NeedsMedic(uid, actor) ? FindMedic(uid) : EntityUid.Invalid;
            var seekingMedic = medic.IsValid();
            if (!danger && !direct && actor.Medic && actor.TriageRequired && SafeForTreatment(uid))
            {
                EndGuard(uid);
                orders.Add((uid, null));
                continue;
            }
            if (actor.ReturningFromPeek && (EnemyPushing(uid, actor) || !actor.PeekHome.IsValid(EntityManager)))
                actor.ReturningFromPeek = false;
            if (actor.SquadOrder is { } current && current.Until > _timing.CurTime &&
                current.Positions.All(position => position.IsValid(EntityManager)) &&
                ValidSquadTarget(uid, current) &&
                (danger ? current.Task == MilitiaSquadTask.Evade : seekingMedic ? current.Task == MilitiaSquadTask.SeekMedic && current.Target == medic &&
                    Vector2.DistanceSquared(_transform.ToMapCoordinates(current.Positions[0]).Position, _transform.GetWorldPosition(medic)) < 6.25f
                    : current.Task != MilitiaSquadTask.SeekMedic) &&
                (danger ? current.Task == MilitiaSquadTask.Evade : current.Task != MilitiaSquadTask.Evade) &&
                (!direct || current.Task is MilitiaSquadTask.Advance or MilitiaSquadTask.Cover or MilitiaSquadTask.Peek or MilitiaSquadTask.SeekMedic ||
                    current.Task == MilitiaSquadTask.Retreat && (actor.ReturningFromPeek || actor.TriageRequired)) &&
                (direct || current.Task is MilitiaSquadTask.Advance or MilitiaSquadTask.Cover && RecentContact(actor, current.Target) ||
                    current.Task is MilitiaSquadTask.Rally or MilitiaSquadTask.Retreat or MilitiaSquadTask.Peek or MilitiaSquadTask.Evade or MilitiaSquadTask.Reinforce or MilitiaSquadTask.SeekMedic) &&
                (danger || !NeedsSquadRetreat(uid, actor) || RetreatTask(current.Task)))
            {
                orders.Add((uid, current));
                occupied.Add(_transform.ToMapCoordinates(actor.RunningSquadOrder != null ? actor.SquadPosition : current.Positions[0]));
                continue;
            }
            var contact = direct ? contacts.FirstOrDefault(c => c.Target == actor.DirectTarget) : contacts.MinBy(c =>
                Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.ToMapCoordinates(c.Position).Position));
            if (contact == null)
                actor.ReturningFromPeek = false;
            var task = danger ? MilitiaSquadTask.Evade : seekingMedic ? MilitiaSquadTask.SeekMedic : actor.ReturningFromPeek || contact != null && NeedsSquadRetreat(uid, actor)
                ? MilitiaSquadTask.Retreat : direct ? MilitiaSquadTask.Advance : MilitiaSquadTask.Rally;
            var reference = task == MilitiaSquadTask.SeekMedic ? Transform(medic).Coordinates : contact?.Position ?? actor.DefensivePosition;
            var supporting = group.Where(other => other != uid && DirectContact(other, Comp<MilitiaOperationActorComponent>(other)))
                .OrderBy(other => Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(other))).FirstOrDefault();
            if (!danger && !seekingMedic && !actor.Medic && !direct && !actor.ReturningFromPeek && !NeedsSquadRetreat(uid, actor) && supporting.IsValid() && contact != null)
            {
                task = MilitiaSquadTask.Reinforce;
                reference = Transform(supporting).Coordinates;
            }
            EntityCoordinates? home = null;
            if (task != MilitiaSquadTask.Reinforce && !danger && !direct && !actor.ReturningFromPeek && !NeedsRecovery(uid, actor) && !actor.Medic &&
                !peeking && actor.NextPeek <= _timing.CurTime && contact != null &&
                !CoverExposed(_transform.GetMapCoordinates(uid), _transform.ToMapCoordinates(contact.Position)) &&
                Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.ToMapCoordinates(contact.Position).Position) < 64f)
            {
                task = MilitiaSquadTask.Peek;
                home = transform.Coordinates;
            }
            if (task == MilitiaSquadTask.Rally)
                reference = actor.DefensivePosition;
            if (task == MilitiaSquadTask.Rally &&
                Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.ToMapCoordinates(reference).Position) < 0.56f)
            {
                orders.Add((uid, null));
                continue;
            }
            var positions = actor.ReturningFromPeek && task == MilitiaSquadTask.Retreat
                ? new[] { actor.PeekHome } : SquadPositions(uid, actor, task, reference, occupied, group);
            if (positions.Length == 0 && task != MilitiaSquadTask.Peek && task != MilitiaSquadTask.Evade)
                positions = new[] { transform.Coordinates };
            var duration = task is MilitiaSquadTask.Reinforce or MilitiaSquadTask.SeekMedic ? 12f : task is MilitiaSquadTask.Peek or MilitiaSquadTask.Retreat or MilitiaSquadTask.Rally
                ? 8f : task == MilitiaSquadTask.Evade ? 4f : 2f + (float) _random.NextDouble();
            var order = positions.Length == 0 ? null : new MilitiaSquadOrder(task == MilitiaSquadTask.SeekMedic ? medic : contact?.Target ?? EntityUid.Invalid,
                positions, task, _timing.CurTime + TimeSpan.FromSeconds(duration), contacts, home);
            orders.Add((uid, order));
            if (order == null)
                continue;
            if (task == MilitiaSquadTask.Peek)
                actor.NextPeek = order.Until + TimeSpan.FromSeconds(2);
            peeking |= task == MilitiaSquadTask.Peek;
            actor.StrafeSide *= -1f;
            occupied.Add(_transform.ToMapCoordinates(positions[0]));
        }
        foreach (var (uid, order) in orders)
            Comp<MilitiaOperationActorComponent>(uid).SquadOrder = order;
    }

    private bool NeedsSquadRetreat(EntityUid uid, MilitiaOperationActorComponent actor)
        => (!DirectContact(uid, actor) || actor.TriageRequired) && !SafeForTreatment(uid) && NeedsRecovery(uid, actor);

    private static bool RetreatTask(MilitiaSquadTask task)
        => task is MilitiaSquadTask.Retreat or MilitiaSquadTask.SeekMedic;

    private bool ValidSquadTarget(EntityUid uid, MilitiaSquadOrder order)
        => order.Task == MilitiaSquadTask.SeekMedic
            ? NeedsMedic(uid, Comp<MilitiaOperationActorComponent>(uid)) && FindMedic(uid) == order.Target
            : order.Task is MilitiaSquadTask.Rally or MilitiaSquadTask.Evade || SquadThreat(uid, order.Target);

    private MapCoordinates[] GrenadePositions(MilitiaOperationActorComponent actor)
        => actor.Grenades.Where(uid => Exists(uid) && !TerminatingOrDeleted(uid) && HasComp<ActiveTimerTriggerComponent>(uid))
            .Select(uid => _transform.GetMapCoordinates(uid)).ToArray();

    private EntityCoordinates[] SquadPositions(EntityUid uid, MilitiaOperationActorComponent actor, MilitiaSquadTask task,
        EntityCoordinates reference, List<MapCoordinates> occupied, EntityUid[] group)
    {
        var origin = _transform.GetWorldPosition(uid);
        var target = _transform.ToMapCoordinates(reference);
        var home = _transform.ToMapCoordinates(actor.DefensivePosition);
        var candidates = new List<(EntityCoordinates Position, float Score)>();
        var transform = Transform(uid);
        if (transform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return Array.Empty<EntityCoordinates>();
        var center = _maps.CoordinatesToTile(grid, gridComp, task is MilitiaSquadTask.Reinforce or MilitiaSquadTask.SeekMedic ? reference : transform.Coordinates);
        var friends = group.Where(other => other != uid).Select(other => _transform.GetWorldPosition(other)).ToArray();
        var grenades = GrenadePositions(actor);
        var threats = actor.SquadContacts.Where(c => c.Position.IsValid(EntityManager)).Select(c => _transform.ToMapCoordinates(c.Position)).ToArray();
        for (var x = -4; x <= 4; x++)
        for (var y = -4; y <= 4; y++)
        {
            var tile = _maps.GetTileRef(grid, gridComp, center + new Vector2i(x, y));
            if (tile.Tile.IsEmpty || _turf.IsTileBlocked(tile, CollisionGroup.MobMask) || !OnDefensiveGrid(uid, coordinates: _maps.GridTileToLocal(grid, gridComp, tile.GridIndices)))
                continue;
            var coordinates = _maps.GridTileToLocal(grid, gridComp, tile.GridIndices);
            var position = _transform.ToMapCoordinates(coordinates);
            var travel = Vector2.Distance(origin, position.Position);
            if (Vector2.Distance(position.Position, home.Position) > actor.DefensiveRange + (task == MilitiaSquadTask.Evade ? 4f : 0f) ||
                !SquadPositionAvailable(uid, coordinates) ||
                occupied.Any(other => other.MapId == position.MapId && Vector2.DistanceSquared(other.Position, position.Position) < 0.81f))
                continue;
            var visible = _interaction.InRangeUnobstructed(position, target, 0, SquadSight,
                predicate: entity => group.Contains(entity));
            var score = travel * 0.3f;
            if (task == MilitiaSquadTask.Rally)
                score += Vector2.Distance(position.Position, home.Position) * 5f;
            else if (task == MilitiaSquadTask.Reinforce)
                score += Vector2.Distance(position.Position, target.Position) * 4f;
            else if (task == MilitiaSquadTask.SeekMedic)
            {
                if (Vector2.Distance(position.Position, target.Position) < 0.75f || !visible)
                    continue;
                score += Vector2.Distance(position.Position, target.Position) * 5f;
                if (threats.Any(threat => CoverExposed(position, threat)))
                    score += 12f;
            }
            else if (task == MilitiaSquadTask.Retreat)
            {
                if (CoverExposed(position, target))
                    continue;
                score += Vector2.Distance(position.Position, home.Position) * 0.3f;
            }
            else if (task == MilitiaSquadTask.Evade)
            {
                if (grenades.Length == 0 || grenades.Any(g => Vector2.Distance(position.Position, g.Position) <
                        Vector2.Distance(origin, g.Position) + 0.75f) ||
                    threats.Any(t => Vector2.Distance(position.Position, t.Position) + 0.25f < Vector2.Distance(origin, t.Position)))
                    continue;
                score -= MathF.Min(grenades.Min(g => Vector2.Distance(position.Position, g.Position)), 12f) * 5f;
            }
            else
            {
                if (!visible || travel > (task == MilitiaSquadTask.Peek ? 2.75f : 2.25f) ||
                    Vector2.Distance(position.Position, target.Position) + 0.4f < Vector2.Distance(origin, target.Position))
                    continue;
                var axis = target.Position - origin;
                if (axis.LengthSquared() < 0.01f)
                    continue;
                axis = Vector2.Normalize(axis);
                var offset = position.Position - origin;
                var lateral = offset.X * axis.Y - offset.Y * axis.X;
                var strafe = actor.Medic ? 0.75f : actor.SquadRole == MilitiaSquadRole.Support ? 0.9f
                    : actor.SquadRole == MilitiaSquadRole.Flanker ? 1.75f : 1.25f;
                score += MathF.Abs(Vector2.Dot(offset, axis)) * 5f + MathF.Abs(MathF.Abs(lateral) - strafe) * 3f;
                score -= lateral * actor.StrafeSide;
                var firingAxis = target.Position - position.Position;
                if (firingAxis.LengthSquared() > 0.01f && friends.Concat(occupied.Select(p => p.Position)).Any(friend =>
                        BlocksFireLane(friend - position.Position, Vector2.Normalize(firingAxis))))
                    continue;
                if (task != MilitiaSquadTask.Peek && !_interaction.InRangeUnobstructed(_transform.GetMapCoordinates(uid), position,
                        0, CollisionGroup.MobMask, predicate: entity => entity == uid))
                    continue;
            }
            candidates.Add((coordinates, score));
        }
        return candidates.OrderBy(entry => entry.Score).Take(6).Select(entry => entry.Position).ToArray();
    }

    public MilitiaSquadOrder? GetSquadOrder(EntityUid uid, bool retreat, bool evade = false)
    {
        if (!SquadMember(uid) || !TryComp<MilitiaOperationActorComponent>(uid, out var actor) || actor.MedicalRefill != null || actor.Patient.IsValid() ||
            actor.ReloadUntil != null || actor.SquadOrder is not { } order || order.Until <= _timing.CurTime ||
            RetreatTask(order.Task) != retreat || (order.Task == MilitiaSquadTask.Evade) != evade ||
            !evade && actor.Grenades.Length > 0 ||
            order.Task == MilitiaSquadTask.Retreat && DirectContact(uid, actor) && !actor.ReturningFromPeek && !actor.TriageRequired ||
            !ValidSquadTarget(uid, order) ||
            !order.Positions.All(position => position.IsValid(EntityManager)) ||
            !retreat && !evade && actor.DoorPry == null && (NeedsWeapon(uid) || NeedsReload(uid) || order.Task is not (MilitiaSquadTask.Rally or MilitiaSquadTask.Reinforce) &&
                (_hands.GetActiveItem(uid) != actor.Weapon || !OwnsItem(uid, actor.Weapon) || !WeaponReady(actor.Weapon))))
            return null;
        return order;
    }

    public bool SquadPositionAvailable(EntityUid uid, EntityCoordinates position)
    {
        if (!SquadMember(uid) || !position.IsValid(EntityManager) ||
            Comp<MilitiaOperationActorComponent>(uid).RejectedSquadPositions.GetValueOrDefault(position) > _timing.CurTime)
            return false;
        var positionMap = _transform.ToMapCoordinates(position);
        var query = EntityQueryEnumerator<MilitiaOperationActorComponent>();
        while (query.MoveNext(out var other, out var ally))
        {
            if (other != uid && (ally.RunningSquadOrder != null || ally.ReloadUntil != null) && ally.SquadPosition.IsValid(EntityManager) &&
                SquadMember(other) && SameOperation(uid, other) && _transform.ToMapCoordinates(ally.SquadPosition) is var reserved &&
                reserved.MapId == positionMap.MapId && Vector2.DistanceSquared(reserved.Position, positionMap.Position) < 0.81f)
                return false;
        }
        return true;
    }

    private MapCoordinates[] RetreatThreats(EntityUid uid, MilitiaSquadOrder order)
    {
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        var threats = order.Contacts.Concat(actor.SquadContacts)
            .Where(contact => contact.Expires > _timing.CurTime && contact.Position.IsValid(EntityManager) && SquadThreat(uid, contact.Target))
            .GroupBy(contact => contact.Target).Select(known => known.MaxBy(contact => contact.SeenAt)!)
            .Take(16).Select(contact => _transform.ToMapCoordinates(contact.Position));
        return DirectContact(uid, actor) ? threats.Append(_transform.GetMapCoordinates(actor.DirectTarget)).ToArray() : threats.ToArray();
    }

    private bool RetreatDoor(EntityUid uid, EntityUid obstacle)
        => !TerminatingOrDeleted(obstacle) && TryComp<DoorComponent>(obstacle, out var door) &&
           (_squadDoors.CanOpen(obstacle, door, DoorUser(uid, obstacle)) || CanPryRouteDoor(uid, obstacle));

    private bool SquadSegment(EntityUid uid, MapCoordinates from, MapCoordinates to, MapCoordinates origin,
        MapCoordinates[] threats, EntityUid[] doors, ref bool covered, MilitiaSquadTask task, MapCoordinates[] grenades)
    {
        if (from.MapId != to.MapId || !_interaction.InRangeUnobstructed(from, to, 0, CollisionGroup.MobMask,
                predicate: entity => entity == uid || doors.Contains(entity) && RetreatDoor(uid, entity)))
            return false;
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        var home = _transform.ToMapCoordinates(actor.DefensivePosition);
        var limit = MathF.Max(actor.DefensiveRange + (task == MilitiaSquadTask.Evade ? 4f : 0f),
            Vector2.Distance(origin.Position, home.Position)) + 0.25f;
        var steps = Math.Max(1, (int) MathF.Ceiling(Vector2.Distance(from.Position, to.Position) / 0.4f));
        for (var step = 0; step <= steps; step++)
        {
            var point = new MapCoordinates(Vector2.Lerp(from.Position, to.Position, (float) step / steps), from.MapId);
            if (_turf.GetTileRef(_transform.ToCoordinates(point)) is not { } tile || tile.Tile.IsEmpty ||
                !OnDefensiveGrid(uid, _transform.ToCoordinates(point)) ||
                Vector2.Distance(point.Position, home.Position) > limit ||
                threats.Any(t => Vector2.Distance(point.Position, t.Position) + 0.1f <
                    MathF.Min(1.5f, Vector2.Distance(origin.Position, t.Position))))
                return false;
            if (task == MilitiaSquadTask.Treat && threats.Any(threat =>
                    CoverExposed(point, threat, actor.ThreatRange)))
                return false;
            if (task == MilitiaSquadTask.Evade)
            {
                if (grenades.Any(g => Vector2.Distance(point.Position, g.Position) + 0.25f < Vector2.Distance(origin.Position, g.Position)) ||
                    threats.Any(t => Vector2.Distance(point.Position, t.Position) + 0.25f < Vector2.Distance(origin.Position, t.Position)))
                    return false;
                continue;
            }
            if (task != MilitiaSquadTask.Retreat)
                continue;
            var exposed = threats.Any(threat => CoverExposed(point, threat));
            if (covered && exposed)
                return false;
            covered |= !exposed;
            if (exposed && threats.Any(threat => Vector2.Distance(point.Position, threat.Position) + 0.5f <
                    Vector2.Distance(origin.Position, threat.Position)))
                return false;
        }
        return true;
    }

    public MilitiaSquadPlan? PrepareSquadPath(EntityUid uid, MilitiaSquadOrder order, EntityCoordinates position, List<PathPoly> path)
    {
        if (!ReferenceEquals(GetSquadOrder(uid, RetreatTask(order.Task), order.Task == MilitiaSquadTask.Evade), order) || !SquadPositionAvailable(uid, position))
            return null;
        if (!PrepareRoute(uid, position, path, out var route, out var doors))
            return null;
        var plan = new MilitiaSquadPlan(order, position, route, doors);
        return ValidateSquadRoute(uid, plan) ? plan : null;
    }

    private bool PrepareRoute(EntityUid uid, EntityCoordinates position, List<PathPoly> path,
        out EntityCoordinates[] points, out EntityUid[] routeDoors, float stopRange = 0f)
    {
        points = Array.Empty<EntityCoordinates>();
        routeDoors = Array.Empty<EntityUid>();
        if (!position.IsValid(EntityManager))
            return false;
        var route = new List<EntityCoordinates>();
        var from = _transform.GetMapCoordinates(uid);
        var target = _transform.ToMapCoordinates(position);
        var reached = stopRange > 0f && Vector2.Distance(from.Position, target.Position) <= stopRange &&
            _interaction.InRangeUnobstructed(from, target, 0, SquadSight);
        foreach (var coordinates in path.Select(poly => poly.Coordinates).Append(position))
        {
            if (reached)
                break;
            if (!coordinates.IsValid(EntityManager))
                return false;
            var to = _transform.ToMapCoordinates(coordinates);
            if (Vector2.DistanceSquared(from.Position, to.Position) < 0.01f)
                continue;
            var steps = Math.Max(1, (int) MathF.Ceiling(Vector2.Distance(from.Position, to.Position) / 0.5f));
            if (steps > 64 || route.Count + steps > 128 || from.MapId != to.MapId)
                return false;
            for (var step = 1; step <= steps; step++)
            {
                var point = new MapCoordinates(Vector2.Lerp(from.Position, to.Position, (float) step / steps), from.MapId);
                route.Add(_transform.ToCoordinates(coordinates.EntityId, point));
                if (stopRange > 0f && Vector2.Distance(point.Position, target.Position) <= stopRange &&
                    _interaction.InRangeUnobstructed(point, target, 0, SquadSight))
                {
                    reached = true;
                    break;
                }
            }
            from = to;
        }
        var doors = new HashSet<EntityUid>();
        from = _transform.GetMapCoordinates(uid);
        foreach (var point in route)
        {
            var to = _transform.ToMapCoordinates(point);
            if (!_interaction.InRangeUnobstructed(from, to, 0, CollisionGroup.MobMask, predicate: entity =>
                {
                    if (entity == uid)
                        return true;
                    if (!RetreatDoor(uid, entity))
                        return false;
                    doors.Add(entity);
                    return true;
                }))
                return false;
            from = to;
        }
        points = route.ToArray();
        routeDoors = doors.ToArray();
        return stopRange <= 0f || reached;
    }

    private bool ValidateSquadRoute(EntityUid uid, MilitiaSquadPlan plan)
    {
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        var from = _transform.GetMapCoordinates(uid);
        var origin = from;
        var threats = RetreatThreats(uid, plan.Order);
        var grenades = GrenadePositions(actor);
        var task = plan.Order.Task;
        if (task == MilitiaSquadTask.Evade && grenades.Length == 0)
            return false;
        var covered = !threats.Any(threat => CoverExposed(from, threat));
        var exposed = 0f;
        foreach (var point in plan.Route)
        {
            if (!point.IsValid(EntityManager))
                return false;
            var to = _transform.ToMapCoordinates(point);
            if (!covered)
                exposed += Vector2.Distance(from.Position, to.Position);
            if (task == MilitiaSquadTask.Retreat && exposed > 6f ||
                !SquadSegment(uid, from, to, origin, threats, plan.Doors, ref covered, task, grenades))
                return false;
            from = to;
        }
        return task != MilitiaSquadTask.Retreat || covered;
    }

    public MilitiaTreatmentPath? PrepareTreatmentPath(EntityUid uid, EntityUid patient, List<PathPoly> path)
    {
        if (!AvailableActor(uid, out var actor) || !ValidPatient(uid, patient, actor) ||
            !PrepareRoute(uid, Transform(patient).Coordinates, path, out var route, out var doors,
                SharedInteractionSystem.InteractionRange - 1f))
            return null;
        var plan = new MilitiaTreatmentPath(route, doors);
        return TreatmentPathSafe(uid, patient, plan) ? plan : null;
    }

    private bool TreatmentPathSafe(EntityUid uid, EntityUid patient, MilitiaTreatmentPath path, int index = 0)
    {
        if (!AvailableActor(uid, out var actor) || !ValidPatient(uid, patient, actor) ||
            !actor.DefensivePosition.IsValid(EntityManager) || index < 0 || index > path.Route.Length ||
            path.Route.Any(point => !point.IsValid(EntityManager)))
            return false;
        var from = _transform.GetMapCoordinates(uid);
        var end = path.Route.Length > 0 ? _transform.ToMapCoordinates(path.Route[^1]) : from;
        var target = _transform.GetMapCoordinates(patient);
        if (end.MapId != target.MapId || Vector2.Distance(end.Position, target.Position) > SharedInteractionSystem.InteractionRange - 0.25f)
            return false;
        var points = path.Route.Skip(index).Select(point => _transform.ToMapCoordinates(point)).Prepend(from).ToArray();
        if (points.Any(point => point.MapId != from.MapId))
            return false;
        var range = actor.ThreatRange + points.Max(point => Vector2.Distance(from.Position, point.Position)) + 0.5f;
        var threats = _factions.GetNearbyHostiles(uid, range).Where(targetUid => SquadThreat(uid, targetUid))
            .Select(targetUid => _transform.GetMapCoordinates(targetUid)).ToArray();
        var origin = from;
        var covered = false;
        foreach (var point in points)
        {
            if (!SquadSegment(uid, from, point, origin, threats, path.Doors, ref covered, MilitiaSquadTask.Treat,
                    Array.Empty<MapCoordinates>()))
                return false;
            from = point;
        }
        return true;
    }

    public void BeginTreatmentApproach(EntityUid uid)
    {
        if (!TryComp<MilitiaOperationActorComponent>(uid, out var actor))
            return;
        if (actor.TreatmentPath is not { } path || !TreatmentPathSafe(uid, actor.Patient, path))
        {
            EndTreatment(uid);
            return;
        }
        actor.SquadRouteIndex = 0;
        if (path.Route.Length == 0)
            return;
        var steering = _steering.Register(uid, path.Route[0]);
        steering.Range = 0.2f;
        steering.ArriveOnLineOfSight = false;
        steering.Status = SteeringStatus.Moving;
    }

    public HTNOperatorStatus ApproachTreatment(EntityUid uid)
    {
        if (!AvailableActor(uid, out var actor) || actor.TreatmentPath is not { } path || !ValidPatient(uid, actor.Patient, actor))
            return HTNOperatorStatus.Failed;
        return AdvanceRoute(uid, path.Route, path.Doors, ref actor.SquadRouteIndex);
    }

    public void EndTreatmentApproach(EntityUid uid)
    {
        EndRoutePry(uid);
        _steering.Unregister(uid);
    }

    public void BeginSquadOrder(EntityUid uid, MilitiaSquadPlan plan)
    {
        var order = plan.Order;
        if (!ReferenceEquals(GetSquadOrder(uid, RetreatTask(order.Task), order.Task == MilitiaSquadTask.Evade), order) || !order.Positions.Contains(plan.Position))
            return;
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        if (!SquadPositionAvailable(uid, plan.Position))
        {
            actor.RejectedSquadPositions[plan.Position] = order.Until;
            return;
        }
        if (plan.Route.Any(point => !point.IsValid(EntityManager)))
            return;
        if (!ValidateSquadRoute(uid, plan))
        {
            actor.RejectedSquadPositions[plan.Position] = order.Until;
            return;
        }
        var committed = false;
        try
        {
            var steering = _steering.Register(uid, plan.Route.Length > 0 ? plan.Route[0] : plan.Position);
            steering.Range = plan.Route.Length > 0 ? 0.2f : 0.6f;
            steering.ArriveOnLineOfSight = false;
            steering.Status = SteeringStatus.Moving;
            if (!ReferenceEquals(GetSquadOrder(uid, RetreatTask(order.Task), order.Task == MilitiaSquadTask.Evade), order) ||
                !SquadPositionAvailable(uid, plan.Position))
                return;
            actor.SquadRoute = plan.Route;
            actor.RetreatDoors = plan.Doors;
            actor.SquadRouteIndex = 0;
            actor.RetreatCovered = false;
            actor.RetreatOrigin = _transform.ToCoordinates(plan.Position.EntityId, _transform.GetMapCoordinates(uid));
            actor.RunningSquadOrder = order;
            actor.SquadPosition = plan.Position;
            if (order.Task == MilitiaSquadTask.Peek && order.ReturnPosition is { } home)
            {
                actor.PeekHome = home;
                actor.PeekEnemyPosition = order.Contacts.FirstOrDefault(c => c.Target == order.Target)?.Position ?? default;
                actor.PeekFireUntil = TimeSpan.Zero;
            }
            committed = true;
        }
        finally
        {
            if (!committed)
                _steering.Unregister(uid);
        }
    }

    private void FireAtTarget(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid target)
    {
        if (!SquadVisible(uid, target, actor.SquadVision) || MedicSupportPriority(uid, actor) ||
            actor.Medic && actor.AlertUntil <= _timing.CurTime)
        {
            EndGuard(uid);
            return;
        }
        _combat.SetInCombatMode(uid, true);
        if (_guns.TryGetGun(uid, out var gun) && WeaponReady(gun.Owner))
        {
            var ranged = EnsureComp<NPCRangedCombatComponent>(uid);
            ranged.Target = target;
            ranged.Status = CombatStatus.Normal;
            return;
        }
        RemComp<NPCRangedCombatComponent>(uid);
        if (_squadMelee.TryGetWeapon(uid, out var weaponUid, out var weapon) && weapon.NextAttack <= _timing.CurTime &&
            Transform(uid).Coordinates.TryDistance(EntityManager, Transform(target).Coordinates, out var distance) &&
            distance <= weapon.Range && _interaction.InRangeUnobstructed(uid, target))
        {
            _squadMelee.AttemptLightAttack(uid, weaponUid, weapon, target);
            actor.AlertUntil = _timing.CurTime + TimeSpan.FromSeconds(12);
        }
    }

    private void StartReloadStep(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (!DirectContact(uid, actor) || !actor.DefensivePosition.IsValid(EntityManager))
            return;
        var origin = _transform.GetMapCoordinates(uid);
        var group = _squadLookup.GetEntitiesInRange<MilitiaOperationActorComponent>(origin, actor.SquadRange)
            .Where(other => SquadMember(other.Owner) && SameOperation(uid, other.Owner)).Select(other => other.Owner).ToArray();
        var target = _transform.ToCoordinates(actor.DefensivePosition.EntityId, _transform.GetMapCoordinates(actor.DirectTarget));
        var threats = new[] { _transform.GetMapCoordinates(actor.DirectTarget) };
        foreach (var point in SquadPositions(uid, actor, MilitiaSquadTask.Advance, target, new(), group))
        {
            var covered = false;
            if (!SquadSegment(uid, origin, _transform.ToMapCoordinates(point), origin, threats, Array.Empty<EntityUid>(),
                    ref covered, MilitiaSquadTask.Advance, Array.Empty<MapCoordinates>()))
                continue;
            var steering = _steering.Register(uid, point);
            steering.Range = 0.3f;
            steering.ArriveOnLineOfSight = false;
            steering.Status = SteeringStatus.Moving;
            actor.SquadPosition = point;
            actor.RetreatOrigin = Transform(uid).Coordinates;
            break;
        }
    }

    public HTNOperatorStatus GuardPosition(EntityUid uid)
    {
        if (!SquadMember(uid) || ReceivingTreatment(uid))
            return HTNOperatorStatus.Failed;
        _steering.Unregister(uid);
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        if (DirectContact(uid, actor))
            FireAtTarget(uid, actor, actor.DirectTarget);
        else
            EndGuard(uid);
        return HTNOperatorStatus.Continuing;
    }

    public void EndGuard(EntityUid uid)
    {
        RemComp<NPCRangedCombatComponent>(uid);
        RemComp<NPCMeleeCombatComponent>(uid);
        if (TryComp<MilitiaOperationActorComponent>(uid, out var actor) && TryComp<GunComponent>(actor.Weapon, out var gun))
            _guns.StopExecutionShooting((actor.Weapon, gun));
        _combat.SetInCombatMode(uid, false);
    }

    public HTNOperatorStatus RunSquadOrder(EntityUid uid)
    {
        if (!TryComp<MilitiaOperationActorComponent>(uid, out var actor) || actor.RunningSquadOrder is not { } order ||
            !SquadMember(uid) || order.Until <= _timing.CurTime ||
            !ReferenceEquals(actor.SquadOrder, order) || actor.MedicalRefill != null || actor.Patient.IsValid() || ReceivingTreatment(uid) ||
            !ValidSquadTarget(uid, order))
            return HTNOperatorStatus.Failed;
        var movement = AdvanceRoute(uid, actor.SquadRoute, actor.RetreatDoors, ref actor.SquadRouteIndex);
        if (movement == HTNOperatorStatus.Failed)
            return movement;
        var arrived = movement == HTNOperatorStatus.Finished;
        if (actor.DoorPry != null)
            return HTNOperatorStatus.Continuing;
        if (order.Task is MilitiaSquadTask.Retreat or MilitiaSquadTask.Rally or MilitiaSquadTask.Reinforce or MilitiaSquadTask.SeekMedic)
        {
            if (DirectContact(uid, actor))
                FireAtTarget(uid, actor, actor.DirectTarget);
            else
                EndGuard(uid);
            return arrived && order.Task != MilitiaSquadTask.SeekMedic ? HTNOperatorStatus.Finished : HTNOperatorStatus.Continuing;
        }
        if (order.Task == MilitiaSquadTask.Evade && actor.Grenades.Length == 0)
            return HTNOperatorStatus.Finished;
        if (order.Task == MilitiaSquadTask.Peek && arrived)
        {
            if (actor.PeekFireUntil == TimeSpan.Zero)
                actor.PeekFireUntil = _timing.CurTime + TimeSpan.FromSeconds(0.8f + _random.NextDouble());
            if (EnemyPushing(uid, actor))
            {
                actor.PeekHome = default;
                return HTNOperatorStatus.Finished;
            }
            if (_timing.CurTime >= actor.PeekFireUntil)
            {
                actor.ReturningFromPeek = actor.PeekHome.IsValid(EntityManager);
                return HTNOperatorStatus.Finished;
            }
        }
        if (DirectContact(uid, actor))
            FireAtTarget(uid, actor, actor.DirectTarget);
        else
            EndGuard(uid);
        return HTNOperatorStatus.Continuing;
    }

    private HTNOperatorStatus AdvanceRoute(EntityUid uid, EntityCoordinates[] route, EntityUid[] doors, ref int index)
    {
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        if (actor.DoorPry is { } id)
        {
            var status = _doAfter.GetStatus(id);
            if (status == Content.Shared.DoAfter.DoAfterStatus.Running)
                return HTNOperatorStatus.Continuing;
            EndRoutePry(uid);
            if (!AvailableActor(uid, out _) || status != Content.Shared.DoAfter.DoAfterStatus.Finished || index >= route.Length || !route[index].IsValid(EntityManager))
                return HTNOperatorStatus.Failed;
            var resume = _steering.Register(uid, route[index]);
            resume.Range = 0.2f;
            resume.ArriveOnLineOfSight = false;
            resume.Status = SteeringStatus.Moving;
        }
        if (index >= route.Length)
            return HTNOperatorStatus.Finished;
        if (!TryComp<NPCSteeringComponent>(uid, out var steering) || steering.Status == SteeringStatus.NoPath)
            return HTNOperatorStatus.Failed;
        foreach (var doorUid in doors)
        {
            if (TryComp<DoorComponent>(doorUid, out var door) && door.State is DoorState.Closed or DoorState.Closing &&
                _interaction.InRangeUnobstructed(uid, doorUid))
            {
                if (!_squadDoors.TryOpen(doorUid, door, DoorUser(uid, doorUid), quiet: true) && CanPryRouteDoor(uid, doorUid))
                    return PryRouteDoor(uid, doorUid);
            }
        }
        if (steering.Status == SteeringStatus.InRange && ++index < route.Length)
        {
            if (!route[index].IsValid(EntityManager))
                return HTNOperatorStatus.Failed;
            var next = _steering.Register(uid, route[index]);
            next.Range = 0.2f;
            next.ArriveOnLineOfSight = false;
            next.Status = SteeringStatus.Moving;
        }
        return index >= route.Length ? HTNOperatorStatus.Finished : HTNOperatorStatus.Continuing;
    }

    public void EndSquadOrder(EntityUid uid)
    {
        if (!TryComp<MilitiaOperationActorComponent>(uid, out var actor) || actor.RunningSquadOrder is not { } order)
            return;
        actor.RunningSquadOrder = null;
        EndRoutePry(uid);
        if (ReferenceEquals(actor.SquadOrder, order))
            actor.SquadOrder = null;
        actor.SquadPosition = default;
        actor.SquadRoute = Array.Empty<EntityCoordinates>();
        actor.RetreatDoors = Array.Empty<EntityUid>();
        actor.SquadRouteIndex = 0;
        if (order.Task == MilitiaSquadTask.Peek)
            actor.NextPeek = _timing.CurTime + TimeSpan.FromSeconds(3 + _random.NextDouble() * 3);
        if (order.Task == MilitiaSquadTask.Retreat)
            actor.ReturningFromPeek = false;
        _steering.Unregister(uid);
        EndGuard(uid);
    }

    private bool UpdateSquadSafety()
    {
        var scan = _timing.CurTime >= _nextSquadSafety;
        if (scan)
            _nextSquadSafety = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        var stop = new HashSet<EntityUid>();
        var interrupt = new HashSet<EntityUid>();
        var throwers = new List<EntityUid>();
        var query = EntityQueryEnumerator<MilitiaOperationActorComponent>();
        while (query.MoveNext(out var uid, out var actor))
        {
            if (actor.ControlsFacing && !SquadMember(uid))
                ClearSquadFacing((uid, actor));
            if (scan && actor.EquipmentReady && !MetaData(uid).EntityPaused && !TerminatingOrDeleted(uid) &&
                !HasPlayerMind(uid) && !_mobs.IsDead(uid) && (HasComp<ActiveNPCComponent>(uid) || _mobs.IsIncapacitated(uid)))
            {
                actor.Hostility.Clear();
                var nearby = _factions.GetNearbyHostiles(uid, actor.ThreatRange).Where(target => SquadThreat(uid, target)).ToArray();
                var visible = nearby.Where(target => SquadVisible(uid, target, actor.ThreatRange))
                    .OrderBy(target => Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(target))).ToArray();
                actor.ExposedToThreat = nearby.Any(target =>
                    CoverExposed(_transform.GetMapCoordinates(uid), _transform.GetMapCoordinates(target), actor.ThreatRange));
                var previous = actor.DirectTarget;
                actor.DirectTarget = visible.FirstOrDefault(target =>
                    Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(target)) <= actor.SquadVision * actor.SquadVision);
                if (actor.DirectTarget.IsValid())
                {
                    actor.LastDirectTarget = actor.DirectTarget;
                    actor.LastDirectAt = _timing.CurTime;
                }
                actor.ThreatScanValidUntil = _timing.CurTime + TimeSpan.FromSeconds(0.5);
                var grenades = _squadLookup.GetEntitiesInRange<ActiveTimerTriggerComponent>(_transform.GetMapCoordinates(uid), 6f)
                    .Where(grenade => !TerminatingOrDeleted(grenade.Owner) && !EntityManager.IsQueuedForDeletion(grenade.Owner) &&
                        (_squadTags.HasTag(grenade.Owner, GrenadeTag) || _squadTags.HasTag(grenade.Owner, HandGrenadeTag) ||
                         _squadTags.HasTag(grenade.Owner, FlashGrenadeTag)) && _interaction.IsAccessible(uid, grenade.Owner) &&
                        _interaction.InRangeUnobstructed(uid, grenade.Owner, 6f, SquadSight))
                    .Select(grenade => grenade.Owner).Concat(actor.Grenades.Where(grenade =>
                        Exists(grenade) && !TerminatingOrDeleted(grenade) && !EntityManager.IsQueuedForDeletion(grenade) &&
                        HasComp<ActiveTimerTriggerComponent>(grenade) && Transform(grenade).MapID == Transform(uid).MapID))
                    .Distinct().ToArray();
                if (grenades.Any(grenade => !actor.Grenades.Contains(grenade)))
                {
                    actor.AlertUntil = _timing.CurTime + TimeSpan.FromSeconds(12);
                    actor.ReturningFromPeek = false;
                    actor.SquadOrder = null;
                    interrupt.Add(uid);
                    _nextSquadUpdate = _timing.CurTime;
                }
                if (previous != actor.DirectTarget || grenades.Length != actor.Grenades.Length)
                    _nextSquadUpdate = _timing.CurTime;
                actor.Grenades = grenades;
                FaceSquadThreat(uid, actor);
                if (actor.DoorPry != null && DirectContact(uid, actor))
                    interrupt.Add(uid);
                if (SquadMember(uid) && actor.MedicalRefill == null && actor.DoorPry == null && actor.ReloadUntil == null && !actor.Patient.IsValid() && !ReceivingTreatment(uid))
                {
                    if (DirectContact(uid, actor))
                    {
                        FireAtTarget(uid, actor, actor.DirectTarget);
                        if (!actor.Medic && _timing.CurTime >= actor.NextGrenade)
                            throwers.Add(uid);
                    }
                    else
                        EndGuard(uid);
                }
            }
            if (scan && actor.ReloadUntil != null && actor.SquadPosition.IsValid(EntityManager))
            {
                var covered = false;
                var threats = DirectContact(uid, actor) ? new[] { _transform.GetMapCoordinates(actor.DirectTarget) } : Array.Empty<MapCoordinates>();
                if (threats.Length == 0 || !SquadSegment(uid, _transform.GetMapCoordinates(uid), _transform.ToMapCoordinates(actor.SquadPosition),
                        _transform.ToMapCoordinates(actor.RetreatOrigin), threats, Array.Empty<EntityUid>(), ref covered,
                        MilitiaSquadTask.Advance, Array.Empty<MapCoordinates>()))
                {
                    _steering.Unregister(uid);
                    actor.SquadPosition = default;
                }
            }
            if (actor.RunningSquadOrder is not { } order)
                continue;
            var direct = DirectContact(uid, actor);
            var cancel = !SquadMember(uid) || order.Until <= _timing.CurTime || !ReferenceEquals(actor.SquadOrder, order) ||
                !actor.SquadPosition.IsValid(EntityManager) ||
                !ValidSquadTarget(uid, order) ||
                actor.Grenades.Length > 0 && order.Task != MilitiaSquadTask.Evade ||
                actor.Grenades.Length == 0 && order.Task == MilitiaSquadTask.Evade ||
                direct && (order.Task is MilitiaSquadTask.Retreat or MilitiaSquadTask.Rally) && !actor.ReturningFromPeek && !actor.TriageRequired ||
                !direct && !RecentContact(actor, order.Target) && (order.Task is MilitiaSquadTask.Advance or MilitiaSquadTask.Cover);
            cancel |= direct && order.Task == MilitiaSquadTask.Reinforce;
            if (direct && (actor.ReturningFromPeek || order.Task == MilitiaSquadTask.Peek) &&
                (actor.DirectTarget != order.Target || EnemyPushing(uid, actor)))
            {
                actor.ReturningFromPeek = false;
                actor.PeekHome = default;
                cancel = true;
            }
            if (!cancel && scan && actor.SquadRouteIndex < actor.SquadRoute.Length)
            {
                cancel = !actor.SquadRoute[actor.SquadRouteIndex].IsValid(EntityManager) ||
                    !SquadSegment(uid, _transform.GetMapCoordinates(uid), _transform.ToMapCoordinates(actor.SquadRoute[actor.SquadRouteIndex]),
                        _transform.ToMapCoordinates(actor.RetreatOrigin), RetreatThreats(uid, order), actor.RetreatDoors,
                        ref actor.RetreatCovered, order.Task, GrenadePositions(actor));
                if (cancel)
                    actor.RejectedSquadPositions[actor.SquadPosition] = order.Until;
            }
            if (cancel)
            {
                stop.Add(uid);
                _nextSquadUpdate = _timing.CurTime;
            }
        }
        foreach (var uid in interrupt)
            InterruptActor(uid);
        foreach (var uid in stop)
            EndSquadOrder(uid);
        foreach (var uid in throwers)
            TrySquadGrenade(uid);
        return scan;
    }

    private void OnSquadShot(Entity<MilitiaOperationActorComponent> actor, ref ShotAttemptedEvent args)
    {
        if (HasPlayerMind(actor.Owner))
            return;
        if (!SquadMember(actor.Owner) || actor.Comp.MedicalRefill != null || actor.Comp.DoorPry != null || actor.Comp.ReloadUntil != null || actor.Comp.Patient.IsValid() || ReceivingTreatment(actor.Owner) ||
            MedicSupportPriority(actor.Owner, actor.Comp) || actor.Comp.Medic && actor.Comp.AlertUntil <= _timing.CurTime ||
            !TryComp<NPCRangedCombatComponent>(actor.Owner, out var ranged) ||
            !SquadVisible(actor.Owner, ranged.Target, actor.Comp.SquadVision) ||
            args.Used.Comp.ShootCoordinates is not { } aim || !aim.IsValid(EntityManager))
        {
            args.Cancel();
            _guns.StopExecutionShooting(args.Used);
            return;
        }
        var origin = _transform.GetWorldPosition(actor.Owner);
        var delta = _transform.ToMapCoordinates(aim).Position - origin;
        if (delta.LengthSquared() < 0.01f)
            return;
        var direction = Vector2.Normalize(delta);
        const float distance = 24f;
        var shooterVelocity = _squadPhysics.GetMapLinearVelocity(new EntityCoordinates(actor.Owner, Vector2.Zero));
        foreach (var ally in _squadLookup.GetEntitiesInRange<NpcFactionMemberComponent>(
                     _transform.GetMapCoordinates(actor.Owner), distance))
        {
            if (ally.Owner == actor.Owner || ally.Owner == ranged.Target || TerminatingOrDeleted(ally.Owner) || _mobs.IsDead(ally.Owner) ||
                !_factions.IsEntityFriendly(actor.Owner, (ally.Owner, ally.Comp)) ||
                !_interaction.InRangeUnobstructed(actor.Owner, ally.Owner, distance, SquadSight))
                continue;
            var offset = _transform.GetWorldPosition(ally.Owner) - origin;
            var relativeVelocity = _squadPhysics.GetMapLinearVelocity(new EntityCoordinates(ally.Owner, Vector2.Zero)) - shooterVelocity;
            var movement = relativeVelocity * 0.25f;
            var side = offset.X * direction.Y - offset.Y * direction.X;
            var sideMovement = movement.X * direction.Y - movement.Y * direction.X;
            var time = MathF.Abs(sideMovement) > 0.001f ? Math.Clamp(-side / sideMovement, 0f, 1f) : 0f;
            var predicted = offset + movement * time;
            if (BlocksFireLane(predicted, direction))
            {
                args.Cancel();
                _guns.StopExecutionShooting(args.Used);
                if (_timing.CurTime >= actor.Comp.NextFireReposition &&
                    actor.Comp.RunningSquadOrder?.Task is not (MilitiaSquadTask.Retreat or MilitiaSquadTask.Evade or MilitiaSquadTask.Rally or MilitiaSquadTask.SeekMedic))
                {
                    actor.Comp.NextFireReposition = _timing.CurTime + TimeSpan.FromSeconds(1);
                    if (actor.Comp.SquadPosition.IsValid(EntityManager))
                        actor.Comp.RejectedSquadPositions[actor.Comp.SquadPosition] = _timing.CurTime + TimeSpan.FromSeconds(2);
                    actor.Comp.SquadOrder = null;
                    _nextSquadUpdate = _timing.CurTime;
                }
                return;
            }
        }
        actor.Comp.AlertUntil = _timing.CurTime + TimeSpan.FromSeconds(12);
    }

    private static bool BlocksFireLane(Vector2 offset, Vector2 direction)
    {
        var along = Vector2.Dot(offset, direction);
        return along > 0f && along < 24f && MathF.Abs(offset.X * direction.Y - offset.Y * direction.X) < 0.65f;
    }

    public bool NeedsSquadHold(EntityUid uid)
        => GetSquadOrder(uid, true) != null && !NeedsReload(uid);

    public void BeginSquadHold(EntityUid uid)
    {
        if (!NeedsSquadHold(uid))
            return;
        _steering.Unregister(uid);
        Comp<MilitiaOperationActorComponent>(uid).SquadHoldUntil = _timing.CurTime + TimeSpan.FromSeconds(0.75);
    }

    public HTNOperatorStatus HoldSquadPosition(EntityUid uid)
    {
        if (!NeedsSquadHold(uid) || !TryComp<MilitiaOperationActorComponent>(uid, out var actor) || actor.SquadHoldUntil <= _timing.CurTime)
            return HTNOperatorStatus.Finished;
        if (DirectContact(uid, actor))
            FireAtTarget(uid, actor, actor.DirectTarget);
        else
            EndGuard(uid);
        return HTNOperatorStatus.Continuing;
    }

    public void EndSquadHold(EntityUid uid)
    {
        if (!TryComp<MilitiaOperationActorComponent>(uid, out var actor) || actor.SquadHoldUntil == TimeSpan.Zero)
            return;
        actor.SquadHoldUntil = TimeSpan.Zero;
        if (TryComp<GunComponent>(actor.Weapon, out var gun))
            _guns.StopExecutionShooting((actor.Weapon, gun));
        RemComp<NPCRangedCombatComponent>(uid);
        _combat.SetInCombatMode(uid, false);
    }
}

public enum MilitiaSquadRole : byte { Line, Flanker, Support }
public enum MilitiaSquadTask : byte { Advance, Cover, Retreat, Rally, Peek, Evade, Treat, Reinforce, SeekMedic }
public sealed record MilitiaSquadContact(EntityUid Target, EntityCoordinates Position, TimeSpan SeenAt, TimeSpan Expires);
public sealed record MilitiaSquadOrder(EntityUid Target, EntityCoordinates[] Positions, MilitiaSquadTask Task, TimeSpan Until, MilitiaSquadContact[] Contacts, EntityCoordinates? ReturnPosition = null);
public sealed record MilitiaSquadPlan(MilitiaSquadOrder Order, EntityCoordinates Position, EntityCoordinates[] Route, EntityUid[] Doors);
public sealed record MilitiaTreatmentPath(EntityCoordinates[] Route, EntityUid[] Doors);
