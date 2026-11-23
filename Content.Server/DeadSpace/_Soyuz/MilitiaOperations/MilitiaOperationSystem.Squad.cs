// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.Destructible;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Server.Weapons.Melee;
using Content.Shared.Damage.Components;
using Content.Shared.Body.Components;
using Content.Shared.Construction;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Interaction;
using Content.Shared.Weapons.Melee.EnergySword;
using Content.Shared.Explosion.Components;
using Content.Shared.NPC;
using Content.Shared.NPC.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Tag;
using Content.Shared.Sticky.Components;
using Content.Shared.Storage.Components;
using Content.Shared.Trigger.Components;
using Content.Shared.Turrets;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    [Dependency] private readonly NPCSteeringSystem _steering = default!;
    [Dependency] private readonly EntityLookupSystem _squadLookup = default!;
    [Dependency] private readonly SharedPhysicsSystem _squadPhysics = default!;
    [Dependency] private readonly RayCastSystem _squadRayCast = default!;
    [Dependency] private readonly SharedDoorSystem _squadDoors = default!;
    [Dependency] private readonly MeleeWeaponSystem _squadMelee = default!;
    [Dependency] private readonly TagSystem _squadTags = default!;
    [Dependency] private readonly IComponentFactory _squadComponents = default!;
    private static readonly ProtoId<TagPrototype> GrenadeTag = "Grenade";
    private static readonly ProtoId<TagPrototype> HandGrenadeTag = "HandGrenade";
    private static readonly ProtoId<TagPrototype> FlashGrenadeTag = "GrenadeFlashBang";
    private static readonly ProtoId<TagPrototype> DoubleSwordTag = "EnergySwordDouble";
    private static readonly EntProtoId EnergyShieldPrototype = "EnergyShield";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";
    private static readonly Vector2i[] SquadAdjacent = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
    private TimeSpan _nextSquadUpdate;
    private TimeSpan _nextSquadSafety;
    private readonly HashSet<EntityUid> _urgentSquadOrders = new();
    private readonly HashSet<EntityUid> _urgentThreatScans = new();
    private readonly HashSet<EntityUid> _scannedThreats = new();
    private readonly HashSet<EntityUid> _squadStops = new();
    private readonly HashSet<EntityUid> _squadInterrupts = new();
    private readonly List<EntityUid> _squadThrowers = new();
    private readonly HashSet<Entity<ActiveTimerTriggerComponent>> _squadGrenadeCandidates = new();
    private readonly Dictionary<(EntityUid Actor, float Range), EntityUid[]> _squadHostileQueries = new();
    private bool _reuseSquadQueries;
    private const CollisionGroup SquadSight = CollisionGroup.Impassable | CollisionGroup.InteractImpassable;
    private static readonly TimeSpan RecentTargetCheckInterval = TimeSpan.FromSeconds(0.1);
    private static readonly TimeSpan RecentContactDuration = TimeSpan.FromSeconds(1.5);

    private bool SquadMember(EntityUid uid)
        => AvailableActor(uid, out _) && HasComp<ActiveNPCComponent>(uid) && !MetaData(uid).EntityPaused &&
           !_equipmentContainers.IsEntityInContainer(uid) && !IsRestrained(uid) &&
           TryComp<HTNComponent>(uid, out var htn) && htn.Enabled;

    private float CombatVision(MilitiaOperationActorComponent actor)
        => actor.AlertUntil > _timing.CurTime ? MathF.Max(actor.SquadVision, actor.ThreatRange) : actor.SquadVision;

    private float CombatVision(MilitiaOperationActorComponent actor, EntityUid target)
        => actor.RangedAttacker == target && actor.RangedAttackerUntil > _timing.CurTime
            ? MathF.Max(CombatVision(actor), RangedResponseRange) : CombatVision(actor);

    private IEnumerable<EntityUid> SquadCombatHostiles(EntityUid uid, MilitiaOperationActorComponent actor, float range)
    {
        var nearby = NearbySquadHostiles(uid, range);
        if (actor.RangedAttackerUntil <= _timing.CurTime || !SquadThreat(uid, actor.RangedAttacker))
            return nearby;
        return nearby.Append(actor.RangedAttacker).Distinct();
    }

    private IEnumerable<EntityUid> NearbySquadHostiles(EntityUid uid, float range)
    {
        if (!_reuseSquadQueries)
            return _factions.GetNearbyHostiles(uid, range);
        var key = (uid, range);
        if (!_squadHostileQueries.TryGetValue(key, out var hostiles))
            _squadHostileQueries[key] = hostiles = _factions.GetNearbyHostiles(uid, range).ToArray();
        return hostiles;
    }

    private bool SquadThreat(EntityUid uid, EntityUid target)
    {
        if (!Exists(target) || TerminatingOrDeleted(target) || EntityManager.IsQueuedForDeletion(target) ||
            _mobs.IsIncapacitated(target) || Transform(uid).MapID != Transform(target).MapID || InactiveSquadTurret(target))
            return false;
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        if (!actor.Hostility.TryGetValue(target, out var hostile))
            actor.Hostility[target] = hostile = _factions.IsEntityHostile(uid, target);
        return hostile;
    }

    private bool InactiveSquadTurret(EntityUid target)
    {
        if (TryComp<DeployableTurretComponent>(target, out var turret))
            return !turret.Enabled || turret.CurrentState is not (DeployableTurretState.Deployed or DeployableTurretState.Firing) ||
                   CompOrNull<DestructibleComponent>(target)?.IsBroken == true;
        return HasComp<TurretTargetSettingsComponent>(target) &&
               (!HasComp<GunComponent>(target) || CompOrNull<HTNComponent>(target)?.Enabled != true ||
                CompOrNull<DestructibleComponent>(target)?.IsBroken == true);
    }

    private bool SquadVisible(EntityUid uid, EntityUid target, float range)
        => SquadThreat(uid, target) && SquadSightClear(uid, target, range);

    private bool SquadSightClear(EntityUid uid, EntityUid target, float range)
        => !TryGetClosedLocker(target, out _) && SquadLineClear(uid, target, range, CollisionGroup.Opaque,
            ignored: entity => HasComp<SharedCanBuildWindowOnTopComponent>(entity) ||
                                 HasComp<NpcFactionMemberComponent>(entity) ||
                                 CompOrNull<RequireProjectileTargetComponent>(entity)?.Active == true ||
                                 TryComp<EntityStorageComponent>(entity, out var storage) && storage.Open);

    private bool CoverExposed(MapCoordinates origin, MapCoordinates target, float range = 0f)
        => SquadLineClear(origin, target, range, CollisionGroup.Opaque,
            ignored: entity => HasComp<DoorComponent>(entity) || HasComp<SharedCanBuildWindowOnTopComponent>(entity) ||
                                 HasComp<NpcFactionMemberComponent>(entity) ||
                                 CompOrNull<RequireProjectileTargetComponent>(entity)?.Active == true ||
                                 !TryComp<PhysicsComponent>(entity, out var body) || body.BodyType != BodyType.Static);

    private bool ThroughWindowOrGrille(MapCoordinates origin, MapCoordinates target)
    {
        var delta = target.Position - origin.Position;
        if (origin.MapId != target.MapId || delta.LengthSquared() < 0.01f)
            return false;
        return !SquadLineClear(origin, target, mask: SquadSight,
            ignored: entity => !_squadTags.HasTag(entity, WindowTag) &&
                !HasComp<SharedCanBuildWindowOnTopComponent>(entity) &&
                !(TryComp<DoorComponent>(entity, out var door) && door.State is DoorState.Closed or DoorState.Closing));
    }

    private IEnumerable<MapCoordinates> RememberedWindowThreats(MilitiaOperationActorComponent actor)
        => actor.WindowThreats.Where(threat => threat.Value > _timing.CurTime && threat.Key.IsValid(EntityManager))
            .Select(threat => _transform.ToMapCoordinates(threat.Key));

    private bool WindowThreatExposed(MilitiaOperationActorComponent actor, MapCoordinates position)
    {
        if (RecentlyHitAt(actor, position))
            return true;
        if (actor.WindowHoldUntil <= _timing.CurTime)
            return false;
        foreach (var (threat, expires) in actor.WindowThreats)
        {
            if (expires > _timing.CurTime && threat.IsValid(EntityManager) &&
                SquadCoverExposed(actor.Owner, position, _transform.ToMapCoordinates(threat)))
                return true;
        }
        return false;
    }

    private bool RecentlyHitAt(MilitiaOperationActorComponent actor, MapCoordinates position)
        => actor.LastRangedImpactUntil > _timing.CurTime && actor.LastRangedImpact.IsValid(EntityManager) &&
           _transform.ToMapCoordinates(actor.LastRangedImpact) is var impact && impact.MapId == position.MapId &&
           Vector2.DistanceSquared(impact.Position, position.Position) < 0.1225f;

    private void RememberWindowThreat(EntityUid uid, MilitiaOperationActorComponent actor, MapCoordinates threat)
    {
        var transform = Transform(uid);
        if (transform.MapID != threat.MapId || (transform.GridUid ?? transform.MapUid) is not { } anchor)
            return;
        EntityCoordinates? previous = null;
        foreach (var position in actor.WindowThreats.Keys)
        {
            if (position.IsValid(EntityManager) && _transform.ToMapCoordinates(position) is var known &&
                known.MapId == threat.MapId && Vector2.DistanceSquared(known.Position, threat.Position) <= 4f)
            {
                previous = position;
                break;
            }
        }
        if (previous is { } old)
            actor.WindowThreats.Remove(old);
        else if (actor.WindowThreats.Count >= 4)
            actor.WindowThreats.Remove(actor.WindowThreats.MinBy(entry => entry.Value).Key);
        actor.WindowHoldUntil = _timing.CurTime + TimeSpan.FromMinutes(1);
        actor.WindowThreats[_transform.ToCoordinates(anchor, threat)] = actor.WindowHoldUntil;
    }

    private void RefreshWindowThreats(EntityUid uid, MilitiaOperationActorComponent actor, MilitiaSquadContact[] contacts)
    {
        foreach (var (position, expires) in actor.WindowThreats.ToArray())
        {
            if (expires <= _timing.CurTime || !position.IsValid(EntityManager) ||
                _transform.ToMapCoordinates(position).MapId != Transform(uid).MapID)
            {
                actor.WindowThreats.Remove(position);
                continue;
            }
            var threat = _transform.ToMapCoordinates(position);
            if (contacts.Any(contact => contact.SeenAt >= _timing.CurTime - TimeSpan.FromSeconds(1) &&
                contact.Position.IsValid(EntityManager) && _transform.ToMapCoordinates(contact.Position) is var seen &&
                seen.MapId == threat.MapId && Vector2.DistanceSquared(seen.Position, threat.Position) <= 4f))
                actor.WindowThreats[position] = _timing.CurTime + TimeSpan.FromMinutes(1);
        }
        actor.WindowHoldUntil = actor.WindowThreats.Values.DefaultIfEmpty().Max();
    }

    private void UpdateSquads()
    {
        var periodic = _timing.CurTime >= _nextSquadUpdate;
        if (!periodic && _urgentSquadOrders.Count == 0)
            return;
        if (periodic)
            _nextSquadUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.5);
        _urgentSquadOrders.RemoveWhere(uid => !SquadMember(uid));
        var members = new List<(EntityUid Uid, SquadScope Scope)>();
        var medicalActors = new Dictionary<SquadScope, List<EntityUid>>();
        var query = EntityQueryEnumerator<MilitiaOperationActorComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            var scope = GetSquadScope(uid);
            if (!medicalActors.TryGetValue(scope, out var candidates))
                medicalActors[scope] = candidates = new List<EntityUid>();
            candidates.Add(uid);
            if (SquadMember(uid))
                members.Add((uid, scope));
        }
        members.Sort((left, right) => left.Uid.GetHashCode().CompareTo(right.Uid.GetHashCode()));
        var buckets = new Dictionary<SquadScope, HashSet<EntityUid>>();
        foreach (var (uid, scope) in members)
        {
            if (!buckets.TryGetValue(scope, out var bucket))
                buckets[scope] = bucket = new HashSet<EntityUid>();
            bucket.Add(uid);
        }
        var neighbors = new List<EntityUid>();
        foreach (var (leader, scope) in members)
        {
            if (!periodic && _urgentSquadOrders.Count == 0)
                break;
            var remaining = buckets[scope];
            if (!remaining.Remove(leader))
                continue;
            var group = new List<EntityUid>();
            if (scope.Operation != Guid.Empty)
            {
                group.Add(leader);
                group.AddRange(remaining);
                remaining.Clear();
            }
            else
            {
                var pending = new Queue<EntityUid>();
                pending.Enqueue(leader);
                while (pending.TryDequeue(out var member))
                {
                    group.Add(member);
                    neighbors.Clear();
                    var position = _transform.GetWorldPosition(member);
                    var memberRange = Comp<MilitiaOperationActorComponent>(member).SquadRange;
                    foreach (var other in remaining)
                    {
                        var range = MathF.Min(memberRange, Comp<MilitiaOperationActorComponent>(other).SquadRange);
                        if (Vector2.DistanceSquared(position, _transform.GetWorldPosition(other)) <= range * range)
                            neighbors.Add(other);
                    }
                    foreach (var other in neighbors)
                    {
                        remaining.Remove(other);
                        pending.Enqueue(other);
                    }
                }
            }
            var urgent = false;
            foreach (var member in group)
                urgent |= _urgentSquadOrders.Remove(member);
            if (!periodic && !urgent)
                continue;
            foreach (var member in group)
                RefreshSquadContacts(member);
            CoordinateSquad(group.ToArray(), medicalActors[scope]);
        }
    }

    private SquadScope GetSquadScope(EntityUid uid)
    {
        var map = Transform(uid).MapID;
        return TryComp<MilitiaOperationEnemyComponent>(uid, out var owner) && owner.Operation != Guid.Empty
            ? new SquadScope(map, owner.Controller, owner.Operation)
            : new SquadScope(map, EntityUid.Invalid, Guid.Empty);
    }

    private readonly record struct SquadScope(MapId Map, EntityUid Controller, Guid Operation);

    private void RefreshSquadContacts(EntityUid uid)
    {
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        foreach (var (target, contact) in actor.Contacts.ToArray())
        {
            if (contact.Expires <= _timing.CurTime ||
                !contact.Position.IsValid(EntityManager) || !SquadThreat(uid, target))
                actor.Contacts.Remove(target);
        }
        var vision = CombatVision(actor);
        var position = _transform.GetWorldPosition(uid);
        foreach (var target in SquadCombatHostiles(uid, actor, vision)
                     .Where(target => SquadThreat(uid, target))
                     .OrderBy(target => Vector2.DistanceSquared(position, _transform.GetWorldPosition(target)))
                     .Where(target => SquadSightClear(uid, target, CombatVision(actor, target)))
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

    private bool DirectContact(EntityUid uid, MilitiaOperationActorComponent actor)
        => actor.ThreatScanValidUntil > _timing.CurTime && actor.DirectTarget.IsValid() && SquadThreat(uid, actor.DirectTarget);

    private bool RecentContact(MilitiaOperationActorComponent actor, EntityUid target)
        => target.IsValid() && actor.LastDirectTarget == target && _timing.CurTime < actor.LastDirectAt + RecentContactDuration;

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

    private bool HasTriageWork(EntityUid uid, MilitiaOperationActorComponent actor, List<EntityUid>? candidates = null)
    {
        if (!actor.Medic)
            return false;
        if (candidates != null)
        {
            foreach (var other in candidates)
            {
                if (TryComp<DamageableComponent>(other, out var injury) && CanTriage(other, injury))
                    return true;
            }
            return false;
        }

        var patients = EntityQueryEnumerator<MilitiaOperationActorComponent, DamageableComponent>();
        while (patients.MoveNext(out var other, out _, out var damage))
        {
            if (CanTriage(other, damage))
                return true;
        }

        return false;

        bool CanTriage(EntityUid patient, DamageableComponent damage)
            => (!actor.UnreachablePatients.TryGetValue(patient, out var retry) || retry <= _timing.CurTime) &&
               ValidPatient(uid, patient, actor) && NeedsTreatment(uid, actor, patient, damage) &&
               HasTreatment(uid, actor, patient, damage);
    }

    private bool MedicSupportPriority(MilitiaOperationActorComponent actor)
        => actor.Medic && actor.MedicWithdrawUntil <= _timing.CurTime &&
           (actor.MedicalRefill != null || actor.Patient.IsValid());

    private bool EnemyPushing(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (!DirectContact(uid, actor))
            return false;
        if (!actor.PeekHome.IsValid(EntityManager) || !actor.PeekEnemyPosition.IsValid(EntityManager))
            return true;
        if (CoverExposed(_transform.ToMapCoordinates(actor.PeekHome),
                _transform.GetMapCoordinates(actor.DirectTarget), CombatVision(actor)))
            return true;
        var origin = _transform.GetWorldPosition(uid);
        return Vector2.Distance(origin, _transform.GetWorldPosition(actor.DirectTarget)) + 1f <
               Vector2.Distance(origin, _transform.ToMapCoordinates(actor.PeekEnemyPosition).Position);
    }

    private void CoordinateSquad(EntityUid[] group, List<EntityUid> medicalCandidates)
    {
        var contactIndices = new Dictionary<EntityUid, int>();
        var latestContacts = new List<MilitiaSquadContact>();
        foreach (var uid in group)
        {
            foreach (var contact in Comp<MilitiaOperationActorComponent>(uid).Contacts.Values)
            {
                if (contactIndices.TryAdd(contact.Target, latestContacts.Count))
                    latestContacts.Add(contact);
                else
                {
                    var index = contactIndices[contact.Target];
                    if (contact.SeenAt > latestContacts[index].SeenAt)
                        latestContacts[index] = contact;
                }
            }
        }
        var contacts = latestContacts.OrderByDescending(contact => contact.SeenAt).ToArray();
        var orders = new List<(EntityUid Uid, MilitiaSquadOrder? Order)>();
        var occupied = new List<MapCoordinates>();
        HashSet<EntityUid>? assignedLockers = null;
        List<(EntityUid Owner, MapCoordinates Position)>? reservations = null;
        var alertUntil = group.Select(uid => Comp<MilitiaOperationActorComponent>(uid).AlertUntil).Max();
        if (contacts.Any(contact => contact.SeenAt >= _timing.CurTime - TimeSpan.FromSeconds(1)))
            alertUntil = TimeSpan.FromTicks(Math.Max(alertUntil.Ticks, (_timing.CurTime + TimeSpan.FromSeconds(12)).Ticks));
        var peeking = group.Any(uid => Comp<MilitiaOperationActorComponent>(uid).SquadOrder is { Task: MilitiaSquadTask.Peek } order &&
            order.Until > _timing.CurTime);
        foreach (var uid in group.OrderBy(uid => Comp<MilitiaOperationActorComponent>(uid).Medic ? 0 : 1))
        {
            var actor = Comp<MilitiaOperationActorComponent>(uid);
            actor.SquadContacts = contacts;
            RefreshWindowThreats(uid, actor, contacts);
            actor.AlertUntil = alertUntil;
            var hadTriageWork = actor.TriageRequired;
            actor.TriageRequired = HasTriageWork(uid, actor, medicalCandidates);
            if (actor.TriageRequired && !hadTriageWork && TryComp<HTNComponent>(uid, out var htn))
                _htn.Replan(htn);
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
            var medicWithdrawing = actor.MedicWithdrawUntil > _timing.CurTime;
            if (medicWithdrawing && !danger && PlanMedicWithdrawal(uid, actor, group, contacts, occupied) is { } withdrawal)
            {
                orders.Add((uid, withdrawal));
                occupied.Add(_transform.ToMapCoordinates(withdrawal.Positions[0]));
                continue;
            }
            var crowded = group.Any(other => other.GetHashCode() < uid.GetHashCode() &&
                Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(other)) < 0.81f);
            var lockerDetour = actor.LockerDetourUntil > _timing.CurTime;
            var needsSpace = crowded || actor.FireBlockedUntil > _timing.CurTime || lockerDetour;
            var incoming = RecentDamageThreat(uid, actor) ?? RememberedWindowThreats(actor).Select(point => (MapCoordinates?) point).FirstOrDefault();
            var underFire = actor.DamageThreatUntil > _timing.CurTime;
            var melee = MeleePursuit(uid, actor);
            var blockedContact = ProactiveBreakout(uid, actor);
            var exposedWithoutReturnFire = actor.WindowExposed && !CanHoldFiringPosition(uid, actor);
            var escaping = exposedWithoutReturnFire && actor.RangedDangerUntil > _timing.CurTime;
            var needsRetreat = medicWithdrawing || exposedWithoutReturnFire ||
                !direct && (underFire || !SafeForTreatment(uid) && NeedsRecovery(uid, actor));
            var holdingWindowCover = actor.WindowHoldUntil > _timing.CurTime && actor.WindowSafePosition.IsValid(EntityManager) &&
                                     OnDefensiveGrid(uid, actor.WindowSafePosition) &&
                                     !WindowThreatExposed(actor, _transform.ToMapCoordinates(actor.WindowSafePosition));
            var selfTreatment = TryComp<DamageableComponent>(uid, out var injury) && HasTreatment(uid, actor, uid, injury);
            var medic = !selfTreatment && NeedsMedic(uid, actor) ? FindMedic(uid, medicalCandidates) : EntityUid.Invalid;
            var seekingMedic = medic.IsValid();
            if (actor.ReturningFromPeek && (EnemyPushing(uid, actor) || !actor.PeekHome.IsValid(EntityManager)))
                actor.ReturningFromPeek = false;
            if (!direct && !danger && !seekingMedic && !actor.WindowExposed && !actor.ReturningFromPeek && contacts.Length > 0 &&
                (!needsRetreat || !NeedsRecovery(uid, actor)) &&
                PlanLockerSweep(uid, actor, contacts, assignedLockers ??= new HashSet<EntityUid>(), occupied) is { } sweep)
            {
                assignedLockers.Add(sweep.Locker);
                orders.Add((uid, sweep));
                var approach = ReferenceEquals(actor.RunningSquadOrder, sweep) ? actor.SquadPosition : sweep.Positions[0];
                occupied.Add(_transform.ToMapCoordinates(approach));
                continue;
            }
            if (actor.SquadOrder is { } current && current.Until > _timing.CurTime && current.Positions.Length > 0 &&
                current.Positions.All(position => position.IsValid(EntityManager)) &&
                !(current.Task == MilitiaSquadTask.Separate && escaping && actor.NextBreakoutAttempt <= _timing.CurTime) &&
                (!melee || current.Task is MilitiaSquadTask.Melee or MilitiaSquadTask.Evade or MilitiaSquadTask.SeekMedic) &&
                (!blockedContact || lockerDetour && current.Task == MilitiaSquadTask.Separate ||
                    current.Task is MilitiaSquadTask.Breakout or MilitiaSquadTask.Retreat or MilitiaSquadTask.Evade or MilitiaSquadTask.SeekMedic or MilitiaSquadTask.Melee) &&
                (!escaping || current.Task is MilitiaSquadTask.Breakout or MilitiaSquadTask.Evade or MilitiaSquadTask.Melee ||
                    !WindowThreatExposed(actor, _transform.ToMapCoordinates(ReferenceEquals(actor.RunningSquadOrder, current) ? actor.SquadPosition : current.Positions[0]))) &&
                (!needsSpace || current.Task is MilitiaSquadTask.Separate or MilitiaSquadTask.Evade or MilitiaSquadTask.SeekMedic or MilitiaSquadTask.Retreat or MilitiaSquadTask.Breakout or MilitiaSquadTask.Melee) &&
                SquadPositionAvailable(uid, ReferenceEquals(actor.RunningSquadOrder, current) ? actor.SquadPosition : current.Positions[0]) &&
                !occupied.Any(position => position.MapId == transform.MapID && Vector2.DistanceSquared(position.Position,
                    _transform.ToMapCoordinates(ReferenceEquals(actor.RunningSquadOrder, current) ? actor.SquadPosition : current.Positions[0]).Position) < 0.81f) &&
                (current.Task switch
                {
                    MilitiaSquadTask.Melee => melee && MeleeDestinationInReach(actor, current),
                    MilitiaSquadTask.SeekMedic => seekingMedic && current.Target == medic,
                    _ => ValidSquadTarget(uid, current),
                }) &&
                (!direct || current.Task is not (MilitiaSquadTask.Advance or MilitiaSquadTask.Cover or MilitiaSquadTask.Peek) ||
                    current.Target == actor.DirectTarget) &&
                (danger ? current.Task == MilitiaSquadTask.Evade : seekingMedic ? current.Task == MilitiaSquadTask.SeekMedic && current.Target == medic &&
                    Vector2.DistanceSquared(_transform.ToMapCoordinates(current.Positions[0]).Position, _transform.GetWorldPosition(medic)) < 6.25f
                    : current.Task != MilitiaSquadTask.SeekMedic) &&
                (danger ? current.Task == MilitiaSquadTask.Evade : current.Task != MilitiaSquadTask.Evade) &&
                (!direct || current.Task is MilitiaSquadTask.Advance or MilitiaSquadTask.Cover or MilitiaSquadTask.Peek or MilitiaSquadTask.SeekMedic or MilitiaSquadTask.Separate or MilitiaSquadTask.Breakout or MilitiaSquadTask.Melee ||
                    current.Task == MilitiaSquadTask.Retreat && (medicWithdrawing || actor.ReturningFromPeek || actor.TriageRequired ||
                        exposedWithoutReturnFire)) &&
                (direct || current.Task is MilitiaSquadTask.Advance or MilitiaSquadTask.Cover && RecentContact(actor, current.Target) ||
                    current.Task is MilitiaSquadTask.Rally or MilitiaSquadTask.Retreat or MilitiaSquadTask.Peek or MilitiaSquadTask.Evade or MilitiaSquadTask.Reinforce or MilitiaSquadTask.SeekMedic or MilitiaSquadTask.Separate or MilitiaSquadTask.Breakout or MilitiaSquadTask.Melee) &&
                (danger || !needsRetreat || RetreatTask(current.Task) ||
                    current.Task is MilitiaSquadTask.Breakout or MilitiaSquadTask.Melee || underFire && current.Task == MilitiaSquadTask.Separate))
            {
                orders.Add((uid, current));
                occupied.Add(_transform.ToMapCoordinates(ReferenceEquals(actor.RunningSquadOrder, current) ? actor.SquadPosition : current.Positions[0]));
                continue;
            }
            var contact = direct ? contacts.FirstOrDefault(c => c.Target == actor.DirectTarget) : contacts.MinBy(c =>
                Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.ToMapCoordinates(c.Position).Position));
            if (contact == null)
                actor.ReturningFromPeek = false;
            var task = danger ? MilitiaSquadTask.Evade : medicWithdrawing ? MilitiaSquadTask.Retreat : seekingMedic ? MilitiaSquadTask.SeekMedic : melee ? MilitiaSquadTask.Melee : needsSpace && !actor.WindowExposed ? MilitiaSquadTask.Separate : actor.ReturningFromPeek || (contact != null || underFire || escaping) && needsRetreat
                ? MilitiaSquadTask.Retreat : direct ? MilitiaSquadTask.Advance : MilitiaSquadTask.Rally;
            var reference = task == MilitiaSquadTask.SeekMedic ? Transform(medic).Coordinates
                : contact?.Position ?? (incoming is { } point ? _transform.ToCoordinates(point) : actor.DefensivePosition);
            if (task == MilitiaSquadTask.Retreat && !actor.ReturningFromPeek &&
                (contact == null && incoming == null || (underFire || actor.WindowExposed) && actor.RetreatRetryAfter > _timing.CurTime))
                task = MilitiaSquadTask.Separate;
            var supporting = group.Where(other => other != uid && DirectContact(other, Comp<MilitiaOperationActorComponent>(other)))
                .OrderBy(other => Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(other))).FirstOrDefault();
            if (task != MilitiaSquadTask.Separate && !danger && !seekingMedic && !actor.Medic && !direct && !actor.ReturningFromPeek && !holdingWindowCover && !needsRetreat && supporting.IsValid() && contact != null)
            {
                task = MilitiaSquadTask.Reinforce;
                reference = Transform(supporting).Coordinates;
            }
            EntityCoordinates? home = null;
            if (task is not (MilitiaSquadTask.Reinforce or MilitiaSquadTask.Separate) && !danger && !direct && !actor.ReturningFromPeek && actor.WindowHoldUntil <= _timing.CurTime && !NeedsRecovery(uid, actor) && !actor.Medic &&
                !peeking && actor.NextPeek <= _timing.CurTime && contact != null &&
                !CoverExposed(_transform.GetMapCoordinates(uid), _transform.ToMapCoordinates(contact.Position)) &&
                Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.ToMapCoordinates(contact.Position).Position) < 64f)
            {
                task = MilitiaSquadTask.Peek;
                home = transform.Coordinates;
            }
            if (task == MilitiaSquadTask.Rally)
                reference = holdingWindowCover ? actor.WindowSafePosition : actor.DefensivePosition;
            if (task == MilitiaSquadTask.Rally &&
                Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.ToMapCoordinates(reference).Position) < 0.56f)
            {
                orders.Add((uid, null));
                continue;
            }
            // If withdrawing towards allies found no route, a medic must still be able to escape crossfire.
            var canBreakOut = !danger && (!medicWithdrawing || escaping) && !seekingMedic &&
                !actor.ReturningFromPeek && (escaping || blockedContact);
            var proactiveBreakout = canBreakOut && blockedContact && !escaping && !(lockerDetour && task == MilitiaSquadTask.Separate);
            var breakout = proactiveBreakout ? FindBreakoutOrder(uid, actor, occupied, allowCover: false, contacts,
                reservations ??= SquadReservations(uid)) : null;
            var positions = breakout?.Positions ?? Array.Empty<EntityCoordinates>();
            var searchingRetreat = !escaping || task is not (MilitiaSquadTask.Retreat or MilitiaSquadTask.Separate) ||
                actor.NextRangedRetreatSearch <= _timing.CurTime;
            if (breakout != null)
                task = MilitiaSquadTask.Breakout;
            else
            {
                if (searchingRetreat)
                {
                    if (escaping && task is (MilitiaSquadTask.Retreat or MilitiaSquadTask.Separate))
                        actor.NextRangedRetreatSearch = _timing.CurTime + TimeSpan.FromSeconds(0.5);
                    positions = task == MilitiaSquadTask.Melee ? MeleePositions(uid, actor, occupied)
                        : actor.ReturningFromPeek && task == MilitiaSquadTask.Retreat
                            ? new[] { actor.PeekHome } : SquadPositions(uid, actor, task, reference, occupied, group,
                                reservations: reservations ??= SquadReservations(uid));
                    if (positions.Length == 0 && task == MilitiaSquadTask.Retreat && actor.WindowExposed && !actor.ReturningFromPeek)
                        positions = SquadPositions(uid, actor, task, reference, occupied, group, extraRadius: 4,
                            reservations: reservations ??= SquadReservations(uid));
                }
                if (canBreakOut && !proactiveBreakout &&
                    (positions.Length == 0 || task == MilitiaSquadTask.Separate && actor.RetreatRetryAfter > _timing.CurTime))
                {
                    breakout = FindBreakoutOrder(uid, actor, occupied, escaping, contacts, reservations ??= SquadReservations(uid));
                    if (breakout != null)
                    {
                        task = MilitiaSquadTask.Breakout;
                        positions = breakout.Positions;
                    }
                }
            }
            if (searchingRetreat && positions.Length == 0 && task == MilitiaSquadTask.Retreat &&
                !actor.ReturningFromPeek && (underFire || actor.WindowExposed))
            {
                task = MilitiaSquadTask.Separate;
                positions = SquadPositions(uid, actor, task, reference, occupied, group, reservations: reservations ??= SquadReservations(uid));
            }
            if (positions.Length == 0 && task is not (MilitiaSquadTask.Peek or MilitiaSquadTask.Evade or MilitiaSquadTask.Separate or MilitiaSquadTask.Melee) && !escaping && !blockedContact &&
                SquadPositionAvailable(uid, transform.Coordinates))
                positions = new[] { transform.Coordinates };
            var order = breakout;
            if (order != null)
                RememberWindowThreat(uid, actor, _transform.ToMapCoordinates(order.BreakoutThreat));
            else if (positions.Length > 0)
            {
                var duration = task == MilitiaSquadTask.Melee ? 2f :
                    task is MilitiaSquadTask.Reinforce or MilitiaSquadTask.SeekMedic or MilitiaSquadTask.Separate ? 12f :
                    task is MilitiaSquadTask.Peek or MilitiaSquadTask.Retreat or MilitiaSquadTask.Rally ? 8f :
                    task == MilitiaSquadTask.Evade ? 4f : 2f + (float) _random.NextDouble();
                order = new MilitiaSquadOrder(task == MilitiaSquadTask.SeekMedic ? medic : contact?.Target ?? EntityUid.Invalid,
                    positions, task, _timing.CurTime + TimeSpan.FromSeconds(duration), contacts, home);
            }
            orders.Add((uid, order));
            if (order == null)
                continue;
            if (task == MilitiaSquadTask.Peek)
                actor.NextPeek = order.Until + TimeSpan.FromSeconds(2);
            peeking |= task == MilitiaSquadTask.Peek;
            if (task != MilitiaSquadTask.Melee)
                actor.StrafeSide *= -1f;
            occupied.Add(_transform.ToMapCoordinates(positions[0]));
        }
        foreach (var (uid, order) in orders)
            Comp<MilitiaOperationActorComponent>(uid).SquadOrder = order;
    }

    public void RejectUnreachableRetreat(EntityUid uid, MilitiaSquadOrder order)
    {
        if (order.Task != MilitiaSquadTask.Retreat || !AvailableActor(uid, out var actor) ||
            (!actor.WindowExposed && actor.DamageThreatUntil <= _timing.CurTime) || !ReferenceEquals(actor.SquadOrder, order))
            return;
        actor.RetreatRetryAfter = _timing.CurTime + TimeSpan.FromSeconds(2);
        actor.SquadOrder = null;
        _urgentSquadOrders.Add(uid);
    }

    private static bool RetreatTask(MilitiaSquadTask task)
        => task is MilitiaSquadTask.Retreat or MilitiaSquadTask.SeekMedic;

    private bool ValidSquadTarget(EntityUid uid, MilitiaSquadOrder order)
        => order.Task == MilitiaSquadTask.MedicWithdraw ? ValidMedicWithdrawal(uid, order)
            : order.Task == MilitiaSquadTask.SweepLocker ? ValidLockerSweep(uid, order)
            : order.Task == MilitiaSquadTask.Breakout
            ? ValidBreakoutOrder(uid, Comp<MilitiaOperationActorComponent>(uid), order)
            : order.Task == MilitiaSquadTask.Melee
            ? ValidMeleeOrder(uid, Comp<MilitiaOperationActorComponent>(uid), order)
            : order.Task == MilitiaSquadTask.SeekMedic
            ? NeedsMedic(uid, Comp<MilitiaOperationActorComponent>(uid)) && FindMedic(uid) == order.Target
            : order.Task is MilitiaSquadTask.Rally or MilitiaSquadTask.Evade or MilitiaSquadTask.Separate ||
              order.Task == MilitiaSquadTask.Retreat &&
                  (RecentDamageThreat(uid, Comp<MilitiaOperationActorComponent>(uid)) != null ||
                   Comp<MilitiaOperationActorComponent>(uid).WindowHoldUntil > _timing.CurTime) ||
              SquadThreat(uid, order.Target);

    private MapCoordinates[] GrenadePositions(MilitiaOperationActorComponent actor)
        => actor.Grenades.Where(uid => Exists(uid) && !TerminatingOrDeleted(uid) && HasComp<ActiveTimerTriggerComponent>(uid))
            .Select(uid => _transform.GetMapCoordinates(uid)).ToArray();

    private List<(EntityUid Owner, MapCoordinates Position)> SquadReservations(EntityUid uid)
    {
        var reservations = new List<(EntityUid, MapCoordinates)>();
        var allies = EntityQueryEnumerator<MilitiaOperationActorComponent>();
        while (allies.MoveNext(out var other, out var ally))
        {
            if (!SquadMember(other) || !SameOperation(uid, other))
                continue;
            reservations.Add((other, _transform.GetMapCoordinates(other)));
            if ((ally.RunningSquadOrder != null || ally.ReloadUntil != null) && ally.SquadPosition.IsValid(EntityManager))
                reservations.Add((other, _transform.ToMapCoordinates(ally.SquadPosition)));
        }
        return reservations;
    }

    private static bool SquadPositionReserved(EntityUid uid, MapCoordinates position,
        List<(EntityUid Owner, MapCoordinates Position)> reservations, List<MapCoordinates> occupied)
    {
        foreach (var (owner, other) in reservations)
        {
            if (owner != uid && other.MapId == position.MapId && Vector2.DistanceSquared(other.Position, position.Position) < 0.81f)
                return true;
        }
        return occupied.Any(other => other.MapId == position.MapId && Vector2.DistanceSquared(other.Position, position.Position) < 0.81f);
    }

    private EntityCoordinates[] SquadPositions(EntityUid uid, MilitiaOperationActorComponent actor, MilitiaSquadTask task,
        EntityCoordinates reference, List<MapCoordinates> occupied, EntityUid[] group, int extraRadius = 0,
        List<(EntityUid Owner, MapCoordinates Position)>? reservations = null)
    {
        var origin = _transform.GetWorldPosition(uid);
        var target = _transform.ToMapCoordinates(reference);
        var home = _transform.ToMapCoordinates(actor.DefensivePosition);
        var maxPositions = task == MilitiaSquadTask.Separate ? 5 : 6;
        var candidates = new List<(EntityCoordinates Position, float Score)>(maxPositions + 1);
        var transform = Transform(uid);
        if (!SquadMember(uid) || transform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return Array.Empty<EntityCoordinates>();
        var originMap = _transform.GetMapCoordinates(uid);
        reservations ??= SquadReservations(uid);
        var tilePositions = task == MilitiaSquadTask.Separate ? new Dictionary<Vector2i, EntityCoordinates?>() : null;
        var center = _maps.CoordinatesToTile(grid, gridComp, task is MilitiaSquadTask.Reinforce or MilitiaSquadTask.SeekMedic or MilitiaSquadTask.MedicWithdraw ? reference : transform.Coordinates);
        var friends = group.Where(other => other != uid).Select(other => _transform.GetWorldPosition(other)).ToArray();
        var fireLanePositions = friends.Concat(occupied.Select(position => position.Position)).ToArray();
        var grenades = GrenadePositions(actor);
        var threats = actor.SquadContacts.Where(c => c.Position.IsValid(EntityManager)).Select(c => _transform.ToMapCoordinates(c.Position))
            .Concat(RememberedWindowThreats(actor)).ToArray();
        if (RecentDamageThreat(uid, actor) is { } incoming)
            threats = threats.Append(incoming).ToArray();
        var needsFirePosition = task is not (MilitiaSquadTask.Retreat or MilitiaSquadTask.Rally or MilitiaSquadTask.Evade or
            MilitiaSquadTask.Reinforce or MilitiaSquadTask.SeekMedic or MilitiaSquadTask.MedicWithdraw);
        var shotSpread = needsFirePosition ? HolographicFireSpread(actor) : 0f;
        var shotCollision = needsFirePosition ? GetShotCollision(actor.Weapon) : default;
        var radius = (actor.WindowExposed ? 8 : task is MilitiaSquadTask.Evade or MilitiaSquadTask.Separate ? 6 : 4) + extraRadius;
        var nearestGrenade = grenades.Length == 0 ? float.PositiveInfinity : grenades.Min(g => Vector2.Distance(origin, g.Position));
        var reflectingFields = needsFirePosition && shotSpread > 0f
            ? ReflectingFieldBounds(originMap, 24f + (radius + 1) * gridComp.TileSize * MathF.Sqrt(2f))
            : Array.Empty<Box2>();
        var blockedFire = task == MilitiaSquadTask.Separate && actor.DirectTarget.IsValid() &&
            SquadShotBlocked(uid, actor.DirectTarget, originMap, target, shotCollision, shotSpread, reflectingFields);
        var lockerDetour = task == MilitiaSquadTask.Separate && actor.LockerDetourUntil > _timing.CurTime;
        var rangedEscape = actor.RangedDangerUntil > _timing.CurTime && actor.WindowExposed;
        EntityCoordinates? fallback = null;
        var fallbackTravel = float.PositiveInfinity;
        var keepWindowCover = !WindowThreatExposed(actor, originMap);
        var targetDistance = Vector2.Distance(origin, target.Position);
        var axis = target.Position - origin;
        var hasAxis = axis.LengthSquared() >= 0.01f;
        if (hasAxis)
            axis = Vector2.Normalize(axis);
        var escapeAxis = target.Position - origin;
        var hasEscapeAxis = rangedEscape && escapeAxis.LengthSquared() > 0.01f;
        if (hasEscapeAxis)
            escapeAxis = Vector2.Normalize(escapeAxis);
        var strafe = actor.Medic ? 0.75f : actor.SquadRole == MilitiaSquadRole.Support ? 0.9f
            : actor.SquadRole == MilitiaSquadRole.Flanker ? 1.75f : 1.25f;
        for (var x = -radius; x <= radius; x++)
        for (var y = -radius; y <= radius; y++)
        {
            var tile = _maps.GetTileRef(grid, gridComp, center + new Vector2i(x, y));
            if (tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile))
                continue;
            if (needsFirePosition && task != MilitiaSquadTask.Separate)
            {
                var tileCenter = _transform.ToMapCoordinates(_maps.GridTileToLocal(grid, gridComp, tile.GridIndices));
                var maximumTravel = (task == MilitiaSquadTask.Peek ? 2.75f : 2.25f) + gridComp.TileSize * 0.5f;
                if (Vector2.DistanceSquared(origin, tileCenter.Position) > maximumTravel * maximumTravel)
                    continue;
            }
            if (!TryPosition(tile.GridIndices, out var coordinates))
                continue;
            var position = _transform.ToMapCoordinates(coordinates);
            var travel = Vector2.Distance(origin, position.Position);
            if (Vector2.Distance(position.Position, home.Position) > actor.DefensiveRange + (task == MilitiaSquadTask.Evade ? 4f : 0f) ||
                actor.RejectedSquadPositions.GetValueOrDefault(coordinates) > _timing.CurTime ||
                SquadPositionReserved(uid, position, reservations, occupied))
                continue;
            if (task == MilitiaSquadTask.Separate &&
                (travel < 0.9f || travel > 6f || friends.Any(friend => Vector2.DistanceSquared(friend, position.Position) < 0.81f) ||
                 !lockerDetour && threats.Any(threat => Vector2.Distance(position.Position, threat.Position) +
                     (rangedEscape ? 1.5f : 0.1f) < Vector2.Distance(origin, threat.Position))))
                continue;
            if (needsFirePosition && task != MilitiaSquadTask.Separate &&
                (travel > (task == MilitiaSquadTask.Peek ? 2.75f : 2.25f) ||
                 Vector2.Distance(position.Position, target.Position) + 0.4f < targetDistance))
                continue;
            if (!OnDefensiveGrid(uid, coordinates))
                continue;
            var windowExposed = (task == MilitiaSquadTask.Retreat || task != MilitiaSquadTask.Evade && keepWindowCover) &&
                WindowThreatExposed(actor, position);
            if (task != MilitiaSquadTask.Evade && keepWindowCover && windowExposed)
                continue;
            var clearFire = needsFirePosition && !SquadShotBlocked(uid, actor.DirectTarget, position, target, shotCollision, shotSpread, reflectingFields);
            var score = travel * 0.3f;
            bool? exposedToThreats = null;
            bool ThreatsExposed() => exposedToThreats ??= threats.Any(threat => SquadCoverExposed(uid, position, threat));
            if (actor.WindowExposed && ThreatsExposed())
                score += 100f;
            if (task == MilitiaSquadTask.Separate)
            {
                if (actor.WindowExposed && (WindowThreatExposed(actor, position) || ThreatsExposed()))
                    continue;
                var openings = 0;
                foreach (var direction in SquadAdjacent)
                {
                    var adjacent = _maps.GetTileRef(grid, gridComp, tile.GridIndices + direction);
                    if (!adjacent.Tile.IsEmpty && TryPosition(adjacent.GridIndices, out _))
                        openings++;
                }
                if (lockerDetour && !SquadRouteClear(uid, originMap, position, entity => CanCrossHolographicBarrier(uid, entity)))
                    continue;
                score += lockerDetour ? travel * 5f - openings : travel - openings * 8f;
                if (hasEscapeAxis)
                {
                    var step = position.Position - origin;
                    score -= MathF.Min(MathF.Abs(step.X * escapeAxis.Y - step.Y * escapeAxis.X), 3f) * 4f;
                }
                if (blockedFire && !clearFire)
                    score += 20f;
                var firingAxis = target.Position - position.Position;
                if (clearFire && firingAxis.LengthSquared() > 0.01f &&
                    !friends.Any(friend => BlocksFireLane(friend - position.Position, Vector2.Normalize(firingAxis))))
                    score -= 4f;
                if (travel < fallbackTravel && SquadRouteClear(uid, originMap, position,
                        entity => CanCrossHolographicBarrier(uid, entity)))
                {
                    fallback = coordinates;
                    fallbackTravel = travel;
                }
            }
            else if (task == MilitiaSquadTask.Rally)
                score += Vector2.Distance(position.Position, target.Position) * 5f;
            else if (task is MilitiaSquadTask.Reinforce or MilitiaSquadTask.MedicWithdraw)
                score += Vector2.Distance(position.Position, target.Position) * 4f;
            else if (task == MilitiaSquadTask.SeekMedic)
            {
                if (Vector2.Distance(position.Position, target.Position) < 0.75f ||
                    !SquadLineClear(position, target, 0, SquadSight,
                        ignored: entity => group.Contains(entity) || HasComp<SharedCanBuildWindowOnTopComponent>(entity)))
                    continue;
                score += Vector2.Distance(position.Position, target.Position) * 5f;
                if (ThreatsExposed())
                    score += 12f;
            }
            else if (task == MilitiaSquadTask.Retreat)
            {
                if (travel < 0.9f || windowExposed || SquadCoverExposed(uid, position, target) || ThreatsExposed())
                    continue;
                score += Vector2.Distance(position.Position, home.Position) * 0.3f;
            }
            else if (task == MilitiaSquadTask.Evade)
            {
                if (grenades.Length == 0)
                    continue;
                var clearance = grenades.Min(g => Vector2.Distance(position.Position, g.Position));
                if (clearance < nearestGrenade + 0.75f)
                    continue;
                score -= MathF.Min(clearance, 8f) * 5f;
            }
            else
            {
                if (!clearFire)
                    continue;
                if (!hasAxis)
                    continue;
                var offset = position.Position - origin;
                var lateral = offset.X * axis.Y - offset.Y * axis.X;
                score += MathF.Abs(Vector2.Dot(offset, axis)) * 5f + MathF.Abs(MathF.Abs(lateral) - strafe) * 3f;
                score -= lateral * actor.StrafeSide;
                var firingAxis = target.Position - position.Position;
                if (firingAxis.LengthSquared() > 0.01f && fireLanePositions.Any(friend =>
                        BlocksFireLane(friend - position.Position, Vector2.Normalize(firingAxis))))
                    continue;
                if (task != MilitiaSquadTask.Peek && !SquadRouteClear(uid, originMap, position,
                        entity => CanCrossHolographicBarrier(uid, entity)))
                    continue;
            }
            var index = candidates.Count;
            while (index > 0 && score.CompareTo(candidates[index - 1].Score) < 0)
                index--;
            if (index < maxPositions)
            {
                candidates.Insert(index, (coordinates, score));
                if (candidates.Count > maxPositions)
                    candidates.RemoveAt(maxPositions);
            }
        }
        var selected = candidates.Select(entry => entry.Position);
        return task == MilitiaSquadTask.Separate && fallback is { } nearby
            ? selected.Append(nearby).Distinct().ToArray() : selected.ToArray();

        bool TryPosition(Vector2i tile, out EntityCoordinates position)
        {
            if (tilePositions != null && tilePositions.TryGetValue(tile, out var cached))
            {
                position = cached.GetValueOrDefault();
                return cached.HasValue;
            }
            var clear = TrySquadTilePosition(uid, grid, gridComp, tile, out position);
            if (tilePositions != null)
                tilePositions.Add(tile, clear ? position : null);
            return clear;
        }
    }

    public MilitiaSquadOrder? GetSquadOrder(EntityUid uid, bool retreat, bool evade = false)
    {
        EntityUid? magazine = null;
        if (!SquadMember(uid) || !TryComp<MilitiaOperationActorComponent>(uid, out var actor) || actor.MedicalRefill != null || actor.Patient.IsValid() ||
            actor.ReloadUntil != null || actor.SquadOrder is not { } order || order.Until <= _timing.CurTime ||
            RetreatTask(order.Task) != retreat || (order.Task == MilitiaSquadTask.Evade) != evade ||
            !evade && actor.Grenades.Length > 0 ||
            order.Task == MilitiaSquadTask.Retreat && DirectContact(uid, actor) && actor.MedicWithdrawUntil <= _timing.CurTime && !actor.ReturningFromPeek && !actor.TriageRequired &&
                (!actor.WindowExposed || CanHoldFiringPosition(uid, actor)) ||
            !ValidSquadTarget(uid, order) ||
            !order.Positions.All(position => position.IsValid(EntityManager)) ||
            !retreat && !evade && actor.DoorPry == null && order.Task is not (MilitiaSquadTask.Breakout or MilitiaSquadTask.Melee or MilitiaSquadTask.SweepLocker or MilitiaSquadTask.MedicWithdraw) &&
            (NeedsWeapon(uid, actor, ref magazine) || NeedsReload(uid, actor, ref magazine) || order.Task is not (MilitiaSquadTask.Rally or MilitiaSquadTask.Reinforce or MilitiaSquadTask.Separate) &&
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
            if (other == uid || !SquadMember(other) || !SameOperation(uid, other))
                continue;
            var actual = _transform.GetMapCoordinates(other);
            if (actual.MapId == positionMap.MapId && Vector2.DistanceSquared(actual.Position, positionMap.Position) < 0.81f)
                return false;
            if ((ally.RunningSquadOrder != null || ally.ReloadUntil != null) && ally.SquadPosition.IsValid(EntityManager) &&
                _transform.ToMapCoordinates(ally.SquadPosition) is var reserved &&
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
            .Take(16).Select(contact => _transform.ToMapCoordinates(contact.Position)).Concat(RememberedWindowThreats(actor));
        if (RecentDamageThreat(uid, actor) is { } incoming)
            threats = threats.Append(incoming);
        return DirectContact(uid, actor) ? threats.Append(_transform.GetMapCoordinates(actor.DirectTarget)).ToArray() : threats.ToArray();
    }

    private bool RetreatDoor(EntityUid uid, EntityUid obstacle)
        => !TerminatingOrDeleted(obstacle) && TryComp<DoorComponent>(obstacle, out var door) &&
           (_squadDoors.CanOpen(obstacle, door, DoorUser(uid, obstacle)) || CanPryRouteDoor(uid, obstacle));

    private bool SquadRouteClear(EntityUid uid, MapCoordinates from, MapCoordinates to, Func<EntityUid, bool> ignored)
    {
        if (from.MapId != to.MapId || !TryComp<FixturesComponent>(uid, out var fixtures))
            return false;
        var origin = _squadPhysics.GetPhysicsTransform(uid);
        origin.Position = from.Position;
        var translation = to.Position - from.Position;
        if (translation.LengthSquared() < 0.0001f)
            return true;
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;
            var filter = new QueryFilter
            {
                LayerBits = fixture.CollisionLayer,
                MaskBits = fixture.CollisionMask,
                // Sensors and item-only collision layers must not obstruct walking routes.
                Flags = QueryFlags.Dynamic | QueryFlags.Static,
                IsIgnored = entity => entity == uid || ignored(entity) ||
                    !TryComp<PhysicsComponent>(entity, out var body) || !body.CanCollide,
            };
            if (_squadRayCast.CastShape(from.MapId, fixture.Shape, origin, translation, filter,
                    RayCastSystem.RayCastClosestCallback).Hit)
                return false;
        }
        return true;
    }

    private bool RouteMakesProgress(EntityUid uid, MilitiaOperationActorComponent actor, EntityCoordinates target)
    {
        if (!target.IsValid(EntityManager))
            return false;
        if (actor.RunningSquadOrder?.Task is MilitiaSquadTask.Retreat or MilitiaSquadTask.Evade or MilitiaSquadTask.Breakout)
        {
            // Keep this observation across replans: changing a waypoint is not actual movement.
            var position = Transform(uid).Coordinates;
            if (!actor.RouteMotionOrigin.IsValid(EntityManager) ||
                _timing.CurTime > actor.RouteMotionObservedAt + TimeSpan.FromSeconds(1) ||
                !position.TryDistance(EntityManager, actor.RouteMotionOrigin, out var moved) || moved >= 0.6f)
            {
                actor.RouteMotionOrigin = position;
                actor.RouteMotionAt = _timing.CurTime;
            }
            actor.RouteMotionObservedAt = _timing.CurTime;
            var timeout = actor.DamageThreatUntil > _timing.CurTime || actor.WindowExposed ? 1.5 : 3;
            if (_timing.CurTime >= actor.RouteMotionAt + TimeSpan.FromSeconds(timeout))
                return false;
        }
        var distance = Vector2.Distance(_transform.GetWorldPosition(uid), _transform.ToMapCoordinates(target).Position);
        if (actor.RouteProgressTarget != target || distance < actor.RouteProgressDistance - 0.15f)
        {
            actor.RouteProgressTarget = target;
            actor.RouteProgressDistance = distance;
            actor.RouteProgressAt = _timing.CurTime;
        }
        return _timing.CurTime < actor.RouteProgressAt + TimeSpan.FromSeconds(3);
    }

    private bool SquadSegment(EntityUid uid, MapCoordinates from, MapCoordinates to, MapCoordinates origin,
        MapCoordinates[] threats, EntityUid[] doors, ref bool covered, MilitiaSquadTask task, MapCoordinates[] grenades,
        bool geometryValidated = false)
    {
        if (!geometryValidated && !SquadRouteClear(uid, from, to,
                entity => CanCrossHolographicBarrier(uid, entity) ||
                    doors.Contains(entity) && RetreatDoor(uid, entity)))
            return false;
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        var home = _transform.ToMapCoordinates(actor.DefensivePosition);
        var limit = MathF.Max(actor.DefensiveRange + (task == MilitiaSquadTask.Evade ? 4f : 0f),
            Vector2.Distance(origin.Position, home.Position)) + 0.25f;
        var steps = Math.Max(1, (int) MathF.Ceiling(Vector2.Distance(from.Position, to.Position) / 0.4f));
        var rangedEscape = actor.RangedDangerUntil > _timing.CurTime && actor.WindowExposed;
        var windowCovered = false;
        for (var step = 0; step <= steps; step++)
        {
            var point = new MapCoordinates(Vector2.Lerp(from.Position, to.Position, (float) step / steps), from.MapId);
            var exposedToWindow = false;
            if (task is not (MilitiaSquadTask.Evade or MilitiaSquadTask.Breakout or MilitiaSquadTask.Melee or MilitiaSquadTask.Treat or MilitiaSquadTask.MedicWithdraw) && actor.WindowHoldUntil > _timing.CurTime)
            {
                exposedToWindow = WindowThreatExposed(actor, point);
                if (windowCovered && exposedToWindow)
                    return false;
                windowCovered |= !exposedToWindow;
            }
            var coordinates = _transform.ToCoordinates(point);
            if (_turf.GetTileRef(coordinates) is not { } tile || tile.Tile.IsEmpty ||
                task is (MilitiaSquadTask.Breakout or MilitiaSquadTask.Treat) && _turf.IsSpace(tile.Tile) ||
                !OnDefensiveGrid(uid, coordinates) ||
                Vector2.Distance(point.Position, home.Position) > limit ||
                task is not (MilitiaSquadTask.Breakout or MilitiaSquadTask.Melee or MilitiaSquadTask.Evade) && TooCloseToThreat(point.Position))
                return false;
            if (task == MilitiaSquadTask.Separate && actor.LockerDetourUntil <= _timing.CurTime && threats.Any(threat =>
                    Vector2.Distance(point.Position, threat.Position) + (rangedEscape ? 1.5f : 0.25f) < Vector2.Distance(origin.Position, threat.Position)))
                return false;
            if (task == MilitiaSquadTask.Evade)
            {
                if (grenades.Any(g => Vector2.Distance(point.Position, g.Position) + 0.1f <
                    MathF.Min(1.5f, Vector2.Distance(origin.Position, g.Position))))
                    return false;
                continue;
            }
            if (task != MilitiaSquadTask.Retreat)
                continue;
            var exposed = exposedToWindow || threats.Any(threat => SquadCoverExposed(uid, point, threat));
            if (covered && exposed)
                return false;
            covered |= !exposed;
            if (exposed && threats.Any(threat => Vector2.Distance(point.Position, threat.Position) + (rangedEscape ? 1.5f : 0.5f) <
                    Vector2.Distance(origin.Position, threat.Position)))
                return false;
        }
        return true;

        bool TooCloseToThreat(Vector2 point)
        {
            for (var i = 0; i < threats.Length; i++)
            {
                var threat = threats[i].Position;
                var initialDistance = Vector2.Distance(origin.Position, threat);
                if (Vector2.Distance(point, threat) + 0.1f < MathF.Min(1.5f, initialDistance))
                    return true;
            }
            return false;
        }
    }

    public void RejectSquadPath(EntityUid uid, MilitiaSquadOrder order, EntityCoordinates position)
    {
        if (AvailableActor(uid, out var actor) && ReferenceEquals(actor.SquadOrder, order))
            actor.RejectedSquadPositions[position] = _timing.CurTime + TimeSpan.FromSeconds(5);
    }

    public MilitiaSquadPlan? PrepareDirectSquadPath(EntityUid uid, MilitiaSquadOrder order, EntityCoordinates position)
    {
        if (order.Task is not (MilitiaSquadTask.Retreat or MilitiaSquadTask.Evade or MilitiaSquadTask.Breakout) ||
            !position.IsValid(EntityManager))
            return null;
        var destination = _transform.ToMapCoordinates(position);
        var origin = _transform.GetMapCoordinates(uid);
        if (origin.MapId != destination.MapId || Vector2.DistanceSquared(origin.Position, destination.Position) > 36f)
            return null;
        // One sweep first; blocked routes still use the normal pathfinder around walls and furniture.
        if (!SquadPositionClear(uid, destination) ||
            !SquadRouteClear(uid, origin, destination,
                entity => CanCrossHolographicBarrier(uid, entity) || RetreatDoor(uid, entity)))
            return null;
        return PrepareSquadPath(uid, order, position, Array.Empty<PathPoly>());
    }

    public MilitiaSquadPlan? PrepareSquadPath(EntityUid uid, MilitiaSquadOrder order, EntityCoordinates position, IEnumerable<PathPoly> path)
    {
        if (!ReferenceEquals(GetSquadOrder(uid, RetreatTask(order.Task), order.Task == MilitiaSquadTask.Evade), order) || !SquadPositionAvailable(uid, position))
            return null;
        var clearSegments = new HashSet<(MapCoordinates From, MapCoordinates To)>();
        if (!PrepareRoute(uid, position, path, out var route, out var doors, clearSegments: clearSegments))
            return null;
        var plan = new MilitiaSquadPlan(order, position, route, doors);
        return ValidateSquadRoute(uid, plan, clearSegments) ? plan : null;
    }

    private bool PrepareRoute(EntityUid uid, EntityCoordinates position, IEnumerable<PathPoly> path,
        out EntityCoordinates[] points, out EntityUid[] routeDoors, float stopRange = 0f,
        HashSet<(MapCoordinates From, MapCoordinates To)>? clearSegments = null)
    {
        points = Array.Empty<EntityCoordinates>();
        routeDoors = Array.Empty<EntityUid>();
        if (!position.IsValid(EntityManager))
            return false;
        var route = new List<EntityCoordinates>();
        var doors = new HashSet<EntityUid>();
        var from = _transform.GetMapCoordinates(uid);
        var origin = from;
        var checkedFrom = from;
        var target = _transform.ToMapCoordinates(position);
        var reached = stopRange > 0f && Vector2.Distance(from.Position, target.Position) <= stopRange &&
            SquadLineClear(from, target, 0, SquadSight);
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
                var anchored = _transform.ToCoordinates(coordinates.EntityId, point);
                var checkedTo = _transform.ToMapCoordinates(anchored);
                if (!SquadRouteClear(uid, checkedFrom, checkedTo, IgnoreRouteObstacle))
                {
                    if (Vector2.DistanceSquared(origin.Position, checkedFrom.Position) <= 4f &&
                        SquadLockerOnRoute(uid, checkedFrom, checkedTo))
                        RequestLockerDetour(uid, Comp<MilitiaOperationActorComponent>(uid));
                    return false;
                }
                clearSegments?.Add((checkedFrom, checkedTo));
                checkedFrom = checkedTo;
                route.Add(anchored);
                if (stopRange > 0f && Vector2.Distance(point.Position, target.Position) <= stopRange &&
                    SquadLineClear(point, target, 0, SquadSight))
                {
                    reached = true;
                    break;
                }
            }
            from = to;
        }
        points = route.ToArray();
        routeDoors = doors.ToArray();
        return stopRange <= 0f || reached;

        bool IgnoreRouteObstacle(EntityUid entity)
        {
            if (entity == uid || CanCrossHolographicBarrier(uid, entity))
                return true;
            if (!RetreatDoor(uid, entity))
                return false;
            doors.Add(entity);
            return true;
        }
    }

    private bool ValidateSquadRoute(EntityUid uid, MilitiaSquadPlan plan,
        IReadOnlySet<(MapCoordinates From, MapCoordinates To)>? clearSegments = null)
    {
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        var task = plan.Order.Task;
        if (task == MilitiaSquadTask.Breakout &&
            !BreakoutDestinationValid(uid, plan.Order, plan.Position, Transform(uid).Coordinates))
            return false;
        var from = _transform.GetMapCoordinates(uid);
        var origin = from;
        var threats = RetreatThreats(uid, plan.Order);
        var grenades = GrenadePositions(actor);
        if (task == MilitiaSquadTask.Evade && grenades.Length == 0)
            return false;
        var covered = !WindowThreatExposed(actor, from) && !threats.Any(threat => SquadCoverExposed(uid, from, threat));
        var exposed = 0f;
        foreach (var point in plan.Route)
        {
            if (!point.IsValid(EntityManager))
                return false;
            var to = _transform.ToMapCoordinates(point);
            if (!covered)
                exposed += Vector2.Distance(from.Position, to.Position);
            if (task == MilitiaSquadTask.Retreat && exposed > (actor.WindowExposed ? 12f : 6f) ||
                !SquadSegment(uid, from, to, origin, threats, plan.Doors, ref covered, task, grenades,
                    geometryValidated: clearSegments?.Contains((from, to)) == true))
                return false;
            from = to;
        }
        return task != MilitiaSquadTask.Retreat || covered;
    }

    public MilitiaTreatmentPath? PrepareTreatmentPath(EntityUid uid, EntityUid patient, List<PathPoly> path)
    {
        if (!AvailableActor(uid, out var actor) || !ValidPatient(uid, patient, actor))
            return null;
        if (uid == patient)
            return new MilitiaTreatmentPath(Array.Empty<EntityCoordinates>(), Array.Empty<EntityUid>());
        var clearSegments = new HashSet<(MapCoordinates From, MapCoordinates To)>();
        if (!PrepareRoute(uid, Transform(patient).Coordinates, path, out var route, out var doors,
                SharedInteractionSystem.InteractionRange - 1f, clearSegments))
            return null;
        var plan = new MilitiaTreatmentPath(route, doors);
        return TreatmentPathSafe(uid, patient, plan, clearSegments: clearSegments) ? plan : null;
    }

    public EntityCoordinates[] TreatmentDetours(EntityUid uid, EntityUid patient)
    {
        if (!AvailableActor(uid, out var actor) || !ValidPatient(uid, patient, actor) ||
            Transform(uid).GridUid is not { } grid)
            return Array.Empty<EntityCoordinates>();
        var from = _transform.GetMapCoordinates(uid);
        var target = _transform.GetMapCoordinates(patient);
        var delta = target.Position - from.Position;
        if (delta.LengthSquared() < 0.01f)
            return Array.Empty<EntityCoordinates>();
        var side = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * 4f;
        var center = (from.Position + target.Position) / 2f;
        var points = new List<EntityCoordinates>(2);
        foreach (var offset in new[] { side, -side })
        {
            var position = new MapCoordinates(center + offset, from.MapId);
            var coordinates = _transform.ToCoordinates(grid, position);
            if (_turf.GetTileRef(coordinates) is not { } tile || tile.Tile.IsEmpty || _turf.IsSpace(tile.Tile) ||
                !OnDefensiveGrid(uid, coordinates) || !SquadPositionClear(uid, position))
                continue;
            points.Add(coordinates);
        }
        return points.ToArray();
    }

    private bool TreatmentPathSafe(EntityUid uid, EntityUid patient, MilitiaTreatmentPath path, int index = 0,
        IReadOnlySet<(MapCoordinates From, MapCoordinates To)>? clearSegments = null)
    {
        if (!AvailableActor(uid, out var actor) || !ValidPatient(uid, patient, actor) || index < 0 || index > path.Route.Length)
            return false;
        if (uid == patient)
            return path.Route.Length == 0;
        if (!actor.DefensivePosition.IsValid(EntityManager) || path.Route.Length > 0 && !path.Route[^1].IsValid(EntityManager))
            return false;
        var from = _transform.GetMapCoordinates(uid);
        var end = path.Route.Length > 0 ? _transform.ToMapCoordinates(path.Route[^1]) : from;
        var target = _transform.GetMapCoordinates(patient);
        if (end.MapId != target.MapId || Vector2.Distance(end.Position, target.Position) > SharedInteractionSystem.InteractionRange - 0.25f)
            return false;
        var origin = from;
        var covered = false;
        // Check the whole route during planning and only the next step while it is being used.
        var limit = clearSegments != null ? path.Route.Length : Math.Min(index + 1, path.Route.Length);
        for (var i = index; i < limit; i++)
        {
            if (!path.Route[i].IsValid(EntityManager))
                return false;
            var point = _transform.ToMapCoordinates(path.Route[i]);
            if (point.MapId != from.MapId || !SquadSegment(uid, from, point, origin, Array.Empty<MapCoordinates>(), path.Doors,
                    ref covered, MilitiaSquadTask.Treat, Array.Empty<MapCoordinates>(),
                    geometryValidated: clearSegments?.Contains((from, point)) == true))
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
        actor.RouteProgressTarget = default;
        _steering.Unregister(uid);
        if (path.Route.Length == 0)
            return;
        var steering = _steering.Register(uid, path.Route[0]);
        steering.Range = 0.2f;
        steering.ArriveOnLineOfSight = false;
        steering.Status = SteeringStatus.Moving;
    }

    public HTNOperatorStatus ApproachTreatment(EntityUid uid)
    {
        if (!AvailableActor(uid, out var actor) || actor.TreatmentPath is not { } path ||
            !ValidPatient(uid, actor.Patient, actor))
            return HTNOperatorStatus.Failed;
        var status = AdvanceRoute(uid, path.Route, path.Doors, ref actor.SquadRouteIndex);
        if (status == HTNOperatorStatus.Failed && actor.Patient != uid)
            actor.UnreachablePatients[actor.Patient] = _timing.CurTime + TimeSpan.FromSeconds(5);
        return status;
    }

    public void EndTreatmentApproach(EntityUid uid)
    {
        EndRoutePry(uid);
        CancelBarrierClimb(uid);
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
            steering.Range = 0.2f;
            steering.ArriveOnLineOfSight = false;
            steering.Status = SteeringStatus.Moving;
            if (!ReferenceEquals(GetSquadOrder(uid, RetreatTask(order.Task), order.Task == MilitiaSquadTask.Evade), order) ||
                !SquadPositionAvailable(uid, plan.Position))
                return;
            actor.SquadRoute = plan.Route;
            actor.RetreatDoors = plan.Doors;
            actor.SquadRouteIndex = 0;
            actor.RouteProgressTarget = default;
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

    private void FireAtTarget(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid target, bool pursuingMelee = false)
    {
        RefreshGirderBreach(uid, actor, target);
        if (BarrierClimbBusy(uid) || actor.InLockerFootprint || !SquadVisible(uid, target, CombatVision(actor, target)) || MedicSupportPriority(actor))
        {
            EndGuard(uid);
            return;
        }
        var desired = pursuingMelee ? actor.Backup : DesiredWeapon(uid, actor);
        if (!desired.IsValid())
        {
            EndGuard(uid);
            return;
        }
        var meleeMode = desired == actor.Backup;
        if (!meleeMode && UnsafeRangedTarget(actor, target))
        {
            EndGuard(uid);
            return;
        }
        var origin = _transform.GetMapCoordinates(uid);
        var targetPosition = _transform.GetMapCoordinates(target);
        if (!meleeMode && SquadShotBlocked(uid, target, origin, targetPosition, GetShotCollision(actor.Weapon), HolographicFireSpread(actor)))
        {
            if (!TryGirderFireTarget(uid, actor, target, out var girder))
            {
                RepositionBlockedFire(uid, actor);
                EndGuard(uid);
                return;
            }
            target = girder;
        }
        if (_hands.GetActiveItem(uid) != desired || HasComp<EnergySwordComponent>(desired) && !IsActiveEnergySword(desired))
        {
            EndGuard(uid);
            if (!EquipOwnedItem(uid, actor, desired, true))
                return;
        }
        _combat.SetInCombatMode(uid, true);
        if (!meleeMode && _guns.TryGetGun(uid, out var gun) && WeaponReady(gun.Owner))
        {
            AimRangedGuard(uid, actor, target, gun);
            return;
        }
        RemComp<NPCRangedCombatComponent>(uid);
        if (desired != actor.Backup || _hands.GetActiveItem(uid) != actor.Backup)
            return;
        if (_squadMelee.TryGetWeapon(uid, out var weaponUid, out var weapon) && weapon.NextAttack <= _timing.CurTime &&
            Transform(uid).Coordinates.TryDistance(EntityManager, Transform(target).Coordinates, out var distance) &&
            distance <= weapon.Range && SquadLineClear(uid, target, weapon.Range))
        {
            _squadHostileQueries.Clear();
            _squadMelee.AttemptLightAttack(uid, weaponUid, weapon, target);
            actor.AlertUntil = _timing.CurTime + TimeSpan.FromSeconds(12);
        }
    }

    private void AimRangedGuard(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid target, Entity<GunComponent> gun)
    {
        if ((gun.Comp.AvailableModes & SelectiveFire.FullAuto) != 0)
            _guns.TrySelectFireMode(gun, SelectiveFire.FullAuto);

        var ranged = EnsureComp<NPCRangedCombatComponent>(uid);
        ranged.HoldAutomaticTrigger = gun.Comp.SelectedMode == SelectiveFire.FullAuto;
        ranged.ShootDelay = ranged.HoldAutomaticTrigger ? 0f : 0.1f;
        if (HasRecentPrimaryAim(actor, target, gun.Owner))
            ranged.ShootAccumulator = MathF.Max(ranged.ShootAccumulator, ranged.ShootDelay);
        ranged.Target = target;
        ranged.TargetInLOS = true;
        ranged.LOSAccumulator = 0.5f;
        ranged.Status = CombatStatus.Normal;
    }

    private bool HasRecentPrimaryAim(MilitiaOperationActorComponent actor, EntityUid target, EntityUid weapon)
        => weapon == actor.PrimaryWeapon && actor.RecentPrimaryAim.Target == target &&
           actor.RecentPrimaryAim.Weapon == weapon && actor.RecentPrimaryAim.Until > _timing.CurTime;

    private void TryResumePrimaryFire(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        var now = _timing.CurTime;
        var target = actor.LastDirectTarget;
        if (now < actor.NextRecentTargetCheck || now >= actor.ThreatScanValidUntil ||
            !RecentContact(actor, target) || !HasRecentPrimaryAim(actor, target, actor.Weapon) ||
            actor.DirectTarget.IsValid() && actor.DirectTarget != target ||
            TryComp<NPCRangedCombatComponent>(uid, out var ranged) && (ranged.Target != target || ranged.TargetInLOS))
            return;

        actor.NextRecentTargetCheck = now + RecentTargetCheckInterval;
        if (actor.Grenades.Length != 0 || actor.WindowExposed || actor.InLockerFootprint || actor.MedicalRefill != null ||
            actor.DoorPry != null || actor.ReloadUntil != null || actor.Patient.IsValid() ||
            actor.RunningSquadOrder?.Task is MilitiaSquadTask.Melee or MilitiaSquadTask.SweepLocker ||
            !SquadMember(uid) || BarrierClimbBusy(uid) || ReceivingTreatment(uid) ||
            !_guns.TryGetGun(uid, out var gun) || gun.Owner != actor.PrimaryWeapon || !_guns.CanShoot(gun) ||
            !WeaponReady(gun.Owner) || !SquadVisible(uid, target, CombatVision(actor, target)) ||
            UnsafeRangedTarget(actor, target) || NeedsMeleeCounter(uid, actor, target))
            return;

        var origin = _transform.GetMapCoordinates(uid);
        var destination = _transform.GetMapCoordinates(target);
        if (SquadLockerAt(origin) || SquadShotBlocked(uid, target, origin, destination, GetShotCollision(gun.Owner), HolographicFireSpread(actor)))
            return;

        // Refresh only this contact. Retreat, grenade and target selection still use the full scan.
        actor.DirectTarget = target;
        actor.LastDirectAt = now;
        actor.ExposedToThreat = true;
        _combat.SetInCombatMode(uid, true);
        AimRangedGuard(uid, actor, target, gun);
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
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        if (actor.SquadOrder is { } order && order.Until > _timing.CurTime &&
            (order.Task is MilitiaSquadTask.Retreat or MilitiaSquadTask.Evade or MilitiaSquadTask.SeekMedic or
                MilitiaSquadTask.Breakout or MilitiaSquadTask.Melee or MilitiaSquadTask.SweepLocker or MilitiaSquadTask.MedicWithdraw ||
             order.Task == MilitiaSquadTask.Separate && (actor.WindowExposed || actor.LockerDetourUntil > _timing.CurTime)))
        {
            // Yield to an assigned escape immediately instead of holding still until the planning cooldown expires.
            if (TryComp<HTNComponent>(uid, out var htn) && htn.PlanningJob == null)
                _htn.Replan(htn);
            return HTNOperatorStatus.Finished;
        }
        _steering.Unregister(uid);
        if (MedicSupportPriority(actor))
            return HTNOperatorStatus.Finished;
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
        if (order.Task == MilitiaSquadTask.SweepLocker && TrySweepLocker(uid, actor, order) is { } sweep)
            return sweep;
        var movement = AdvanceRoute(uid, actor.SquadRoute, actor.RetreatDoors, ref actor.SquadRouteIndex);
        if (movement == HTNOperatorStatus.Failed)
        {
            if (actor.SquadPosition.IsValid(EntityManager))
            {
                var retry = _timing.CurTime + TimeSpan.FromSeconds(5);
                if (actor.RejectedSquadPositions.GetValueOrDefault(actor.SquadPosition) < retry)
                    actor.RejectedSquadPositions[actor.SquadPosition] = retry;
            }
            _urgentSquadOrders.Add(uid);
            RejectUnreachableMedic(uid, order);
            RejectUnreachableRetreat(uid, order);
            RejectUnreachableBreakout(uid, order);
            return movement;
        }
        if (order.Task == MilitiaSquadTask.SweepLocker)
            return movement;
        var arrived = movement == HTNOperatorStatus.Finished;
        if (arrived && actor.WindowHoldUntil > _timing.CurTime && !actor.ExposedToThreat)
            actor.WindowSafePosition = Transform(uid).Coordinates;
        if (actor.DoorPry != null)
            return HTNOperatorStatus.Continuing;
        if (order.Task is MilitiaSquadTask.Retreat or MilitiaSquadTask.Rally or MilitiaSquadTask.Reinforce or MilitiaSquadTask.SeekMedic or MilitiaSquadTask.Separate or MilitiaSquadTask.Breakout or MilitiaSquadTask.Melee or MilitiaSquadTask.MedicWithdraw)
        {
            if (arrived && order.Task == MilitiaSquadTask.Separate)
            {
                actor.FireBlockedUntil = TimeSpan.Zero;
                actor.LockerDetourUntil = TimeSpan.Zero;
            }
            if (DirectContact(uid, actor))
                FireAtTarget(uid, actor, actor.DirectTarget, pursuingMelee: order.Task == MilitiaSquadTask.Melee);
            else
                EndGuard(uid);
            if (arrived && order.Task == MilitiaSquadTask.Melee)
            {
                actor.SquadOrder = null;
                _urgentSquadOrders.Add(uid);
            }
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
        if (BarrierClimbBusy(uid))
        {
            actor.RouteProgressTarget = default;
            actor.RouteMotionOrigin = default;
            EndGuard(uid);
            return HTNOperatorStatus.Continuing;
        }
        if (StartBarrierClimb(uid, route, ref index, out var blocked))
        {
            actor.RouteProgressTarget = default;
            actor.RouteMotionOrigin = default;
            EndGuard(uid);
            return HTNOperatorStatus.Continuing;
        }
        if (blocked)
            return HTNOperatorStatus.Failed;
        actor.BarrierClimbAction = null;
        if (actor.DoorPry is { } id)
        {
            var status = _doAfter.GetStatus(id);
            if (status == Content.Shared.DoAfter.DoAfterStatus.Running)
            {
                actor.RouteMotionOrigin = default;
                return HTNOperatorStatus.Continuing;
            }
            EndRoutePry(uid);
            if (!AvailableActor(uid, out _) || status != Content.Shared.DoAfter.DoAfterStatus.Finished || index >= route.Length || !route[index].IsValid(EntityManager))
                return HTNOperatorStatus.Failed;
            var resume = _steering.Register(uid, route[index]);
            resume.Range = 0.2f;
            resume.ArriveOnLineOfSight = false;
            resume.Status = SteeringStatus.Moving;
            actor.RouteProgressTarget = default;
        }
        if (index >= route.Length)
            return HTNOperatorStatus.Finished;
        if (actor.OpeningRouteDoor is { } opening)
        {
            if (!TryComp<DoorComponent>(opening, out var openingDoor) || _timing.CurTime >= actor.OpeningRouteDoorUntil)
            {
                actor.OpeningRouteDoor = null;
                return HTNOperatorStatus.Failed;
            }
            if (openingDoor.State != DoorState.Open)
            {
                actor.RouteMotionOrigin = default;
                return HTNOperatorStatus.Continuing;
            }
            actor.OpeningRouteDoor = null;
            var resume = _steering.Register(uid, route[index]);
            resume.Range = 0.2f;
            resume.ArriveOnLineOfSight = false;
            resume.Status = SteeringStatus.Moving;
            actor.RouteProgressTarget = default;
        }
        foreach (var doorUid in doors)
        {
            if (!TryComp<DoorComponent>(doorUid, out var door) || door.State == DoorState.Open ||
                !SquadLineClear(uid, doorUid, SharedInteractionSystem.InteractionRange))
                continue;
            if (door.State == DoorState.Opening || _squadDoors.TryOpen(doorUid, door, DoorUser(uid, doorUid), quiet: true))
            {
                actor.OpeningRouteDoor = doorUid;
                actor.OpeningRouteDoorUntil = _timing.CurTime + TimeSpan.FromSeconds(3);
                actor.RouteProgressTarget = default;
                _steering.Unregister(uid);
                return HTNOperatorStatus.Continuing;
            }
            if (CanPryRouteDoor(uid, doorUid))
                return PryRouteDoor(uid, doorUid);
        }
        if (!TryComp<NPCSteeringComponent>(uid, out var steering) || steering.Status == SteeringStatus.NoPath)
        {
            if (!TryComp<Content.Shared.Climbing.Components.ClimbingComponent>(uid, out var climbing) || !climbing.IsClimbing ||
                !route[index].IsValid(EntityManager))
                return RouteFailedAt(uid, actor, route[index]);
            steering = _steering.Register(uid, route[index]);
            steering.Range = 0.2f;
            steering.ArriveOnLineOfSight = false;
            steering.Status = SteeringStatus.Moving;
            actor.RouteProgressTarget = default;
        }
        // Move on before the steering tick comes to a stop at each intermediate point.
        var reached = steering.Status == SteeringStatus.InRange || index + 1 < route.Length &&
            Transform(uid).Coordinates.TryDistance(EntityManager, route[index], out var distance) && distance <= steering.Range;
        if (!reached)
            return RouteMakesProgress(uid, actor, route[index]) ? HTNOperatorStatus.Continuing : RouteFailedAt(uid, actor, route[index]);
        if (++index < route.Length)
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

    private HTNOperatorStatus RouteFailedAt(EntityUid uid, MilitiaOperationActorComponent actor, EntityCoordinates point)
    {
        if (point.IsValid(EntityManager) && SquadLockerOnRoute(uid, _transform.GetMapCoordinates(uid), _transform.ToMapCoordinates(point)))
            RequestLockerDetour(uid, actor);
        return HTNOperatorStatus.Failed;
    }

    public void EndSquadOrder(EntityUid uid)
    {
        if (!TryComp<MilitiaOperationActorComponent>(uid, out var actor) || actor.RunningSquadOrder is not { } order)
            return;
        actor.RunningSquadOrder = null;
        actor.OpeningRouteDoor = null;
        EndRoutePry(uid);
        CancelBarrierClimb(uid);
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
        var stop = _squadStops;
        var interrupt = _squadInterrupts;
        var throwers = _squadThrowers;
        stop.Clear();
        interrupt.Clear();
        throwers.Clear();
        var query = EntityQueryEnumerator<MilitiaOperationActorComponent>();
        while (query.MoveNext(out var uid, out var actor))
        {
            var scanActor = _urgentThreatScans.Remove(uid) || scan;
            if (actor.ControlsFacing && !SquadMember(uid))
                ClearSquadFacing((uid, actor));
            var contained = _equipmentContainers.IsEntityInContainer(uid);
            if (scanActor && contained && TryLeaveLocker(uid, actor))
            {
                contained = false;
                interrupt.Add(uid);
            }
            if (!HasPlayerMind(uid) && (contained || IsRestrained(uid)))
            {
                actor.DirectTarget = EntityUid.Invalid;
                actor.SquadOrder = null;
                RemComp<NPCRangedCombatComponent>(uid);
                if (actor.RunningSquadOrder != null || actor.Patient.IsValid() || actor.MedicalRefill != null || actor.ReloadUntil != null)
                    interrupt.Add(uid);
                continue;
            }
            if (scanActor && actor.EquipmentReady && !MetaData(uid).EntityPaused && !TerminatingOrDeleted(uid) &&
                !HasPlayerMind(uid) && !_mobs.IsDead(uid) && (HasComp<ActiveNPCComponent>(uid) || _mobs.IsIncapacitated(uid)))
            {
                actor.NextRecentTargetCheck = _timing.CurTime + RecentTargetCheckInterval;
                actor.Hostility.Clear();
                foreach (var target in actor.UnsafeRangedTargets.Keys.ToArray())
                {
                    if (actor.UnsafeRangedTargets[target] <= _timing.CurTime || !Exists(target))
                        actor.UnsafeRangedTargets.Remove(target);
                }
                var origin = _transform.GetMapCoordinates(uid);
                var nearby = SquadCombatHostiles(uid, actor, actor.ThreatRange).Where(target => SquadThreat(uid, target)).ToArray();
                var visible = nearby.Where(target => SquadVisible(uid, target, MathF.Max(actor.ThreatRange, CombatVision(actor, target))))
                    .OrderBy(target => Vector2.DistanceSquared(origin.Position, _transform.GetWorldPosition(target))).ToArray();
                actor.InLockerFootprint = visible.Length > 0 && SquadLockerAt(origin);
                if (actor.InLockerFootprint)
                    RequestLockerDetour(uid, actor);
                if (visible.Length > 0)
                    actor.AlertUntil = _timing.CurTime + TimeSpan.FromSeconds(12);
                actor.ExposedToThreat = nearby.Any(target =>
                    SquadCoverExposed(uid, origin, _transform.GetMapCoordinates(target), MathF.Max(actor.ThreatRange, CombatVision(actor, target)))) ||
                    RecentDamageThreat(uid, actor) is { } incoming && SquadCoverExposed(uid, origin, incoming);
                var shotSpread = HolographicFireSpread(actor);
                var shotCollision = GetShotCollision(actor.Weapon);
                var previous = actor.DirectTarget;
                actor.DirectTarget = actor.RunningSquadOrder is { Task: MilitiaSquadTask.Melee } pursuit && visible.Contains(pursuit.Target)
                    ? pursuit.Target
                    : visible.FirstOrDefault(target => target == actor.MeleeCounterTarget && NeedsMeleeCounter(uid, actor, target));
                if (!actor.DirectTarget.IsValid())
                {
                    var selectedBlocked = true;
                    foreach (var target in visible)
                    {
                        var vision = CombatVision(actor, target);
                        var position = _transform.GetMapCoordinates(target);
                        if (Vector2.DistanceSquared(origin.Position, position.Position) > vision * vision)
                            continue;
                        // Even a lower-ranked target can refresh the double-sword warning.
                        var unsafeTarget = UnsafeRangedTarget(actor, target);
                        if (!selectedBlocked && (target != previous || actor.DirectTarget == previous))
                            continue;
                        var blocked = unsafeTarget && !NeedsMeleeCounter(uid, actor, target) ||
                            SquadShotBlocked(uid, target, origin, position, shotCollision, shotSpread);
                        if (!actor.DirectTarget.IsValid() || selectedBlocked && !blocked || selectedBlocked == blocked && target == previous)
                        {
                            actor.DirectTarget = target;
                            selectedBlocked = blocked;
                        }
                    }
                }
                var targetBehindBarrier = actor.DirectTarget.IsValid() &&
                    ThroughWindowOrGrille(origin, _transform.GetMapCoordinates(actor.DirectTarget));
                if (targetBehindBarrier)
                {
                    var startWithdrawal = actor.RangedDangerUntil <= _timing.CurTime;
                    RememberWindowThreat(uid, actor, _transform.GetMapCoordinates(actor.DirectTarget));
                    // A visible target behind glass or a grille already requires covering fire and withdrawal.
                    // Drawing a laser must not leave the escape disabled until a damage event arrives.
                    actor.RangedDangerUntil = actor.WindowHoldUntil;
                    if (startWithdrawal)
                    {
                        actor.NextRangedRetreatSearch = TimeSpan.Zero;
                        actor.RetreatRetryAfter = TimeSpan.Zero;
                        actor.NextBreakoutAttempt = TimeSpan.Zero;
                        actor.SquadOrder = null;
                        interrupt.Add(uid);
                        _urgentSquadOrders.Add(uid);
                    }
                }
                var counter = NeedsMeleeCounter(uid, actor, actor.DirectTarget, out var armedThreat);
                UpdateSecondaryWeapon(uid, actor, targetBehindBarrier, counter);
                actor.WindowExposed = WindowThreatExposed(actor, origin);
                actor.ExposedToThreat |= actor.WindowExposed;
                if (actor.DirectTarget.IsValid())
                {
                    actor.LastDirectTarget = actor.DirectTarget;
                    actor.LastDirectAt = _timing.CurTime;
                }
                actor.ThreatScanValidUntil = _timing.CurTime + TimeSpan.FromSeconds(0.5);
                if (!scan)
                    _scannedThreats.Add(uid);
                if (armedThreat)
                {
                    actor.MeleeCounterTarget = actor.DirectTarget;
                    actor.MeleeCounterUntil = _timing.CurTime + TimeSpan.FromSeconds(2);
                }
                if (actor.ReloadUntil != null && counter)
                    EndReload(uid);
                var grenades = ScanSquadGrenades(uid, actor, _transform.GetMapCoordinates(uid));
                if (grenades.Any(grenade => !actor.Grenades.Contains(grenade)))
                {
                    actor.AlertUntil = _timing.CurTime + TimeSpan.FromSeconds(12);
                    actor.ReturningFromPeek = false;
                    actor.SquadOrder = null;
                    interrupt.Add(uid);
                    _urgentSquadOrders.Add(uid);
                }
                if (previous != actor.DirectTarget || grenades.Length != actor.Grenades.Length)
                    _urgentSquadOrders.Add(uid);
                actor.Grenades = grenades;
                FaceSquadThreat(uid, actor);
                if (actor.DoorPry != null && DirectContact(uid, actor) && !actor.WindowExposed &&
                    actor.RunningSquadOrder?.Task is not (MilitiaSquadTask.Retreat or MilitiaSquadTask.Evade or MilitiaSquadTask.SeekMedic or MilitiaSquadTask.Breakout))
                    interrupt.Add(uid);
                if (SquadMember(uid) && actor.MedicalRefill == null && actor.DoorPry == null && actor.ReloadUntil == null && !actor.Patient.IsValid() && !ReceivingTreatment(uid))
                {
                    if (DirectContact(uid, actor))
                    {
                        FireAtTarget(uid, actor, actor.DirectTarget);
                        if ((!actor.Medic || actor.DamageThreatUntil > _timing.CurTime && !actor.TriageRequired) &&
                            _timing.CurTime >= actor.NextGrenade)
                            throwers.Add(uid);
                    }
                    else
                        EndGuard(uid);
                }
            }
            else if (!scanActor)
                TryResumePrimaryFire(uid, actor);

            if (scanActor && actor.ReloadUntil != null && actor.SquadPosition.IsValid(EntityManager))
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
                (order.Task != MilitiaSquadTask.Melee || scanActor) && !ValidSquadTarget(uid, order) ||
                direct && (order.Task is MilitiaSquadTask.Advance or MilitiaSquadTask.Cover or MilitiaSquadTask.Peek) &&
                    order.Target != actor.DirectTarget ||
                actor.Grenades.Length > 0 && order.Task != MilitiaSquadTask.Evade ||
                actor.Grenades.Length == 0 && order.Task == MilitiaSquadTask.Evade ||
                direct && (order.Task is MilitiaSquadTask.Retreat or MilitiaSquadTask.Rally) && actor.MedicWithdrawUntil <= _timing.CurTime && !actor.ReturningFromPeek && !actor.TriageRequired &&
                    (!actor.WindowExposed || CanHoldFiringPosition(uid, actor)) ||
                !direct && !RecentContact(actor, order.Target) && (order.Task is MilitiaSquadTask.Advance or MilitiaSquadTask.Cover);
            cancel |= direct && order.Task is (MilitiaSquadTask.Reinforce or MilitiaSquadTask.SweepLocker);
            if (!cancel && scanActor && order.Task == MilitiaSquadTask.Breakout)
                cancel = !GirderAssault(actor, order.Target) && CanHoldFiringPosition(uid, actor) ||
                         !BreakoutDestinationValid(uid, order, actor.SquadPosition, actor.RetreatOrigin);
            if (direct && (actor.ReturningFromPeek || order.Task == MilitiaSquadTask.Peek) &&
                (actor.DirectTarget != order.Target || EnemyPushing(uid, actor)))
            {
                actor.ReturningFromPeek = false;
                actor.PeekHome = default;
                cancel = true;
            }
            if (!cancel && scanActor && actor.SquadRouteIndex < actor.SquadRoute.Length)
            {
                var next = actor.SquadRoute[actor.SquadRouteIndex];
                cancel = !next.IsValid(EntityManager) ||
                    !SquadSegment(uid, _transform.GetMapCoordinates(uid), _transform.ToMapCoordinates(next),
                        _transform.ToMapCoordinates(actor.RetreatOrigin), RetreatThreats(uid, order), actor.RetreatDoors,
                        ref actor.RetreatCovered, order.Task, GrenadePositions(actor));
                if (cancel)
                {
                    RouteFailedAt(uid, actor, next);
                    actor.RejectedSquadPositions[actor.SquadPosition] = order.Until;
                }
            }
            if (cancel)
            {
                stop.Add(uid);
                _urgentSquadOrders.Add(uid);
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

    private EntityUid[] ScanSquadGrenades(EntityUid uid, MilitiaOperationActorComponent actor, MapCoordinates origin)
    {
        if (!AllEntityQuery<ActiveTimerTriggerComponent>().MoveNext(out _))
            return Array.Empty<EntityUid>();

        var candidates = _squadGrenadeCandidates;
        try
        {
            _squadLookup.GetEntitiesInRange(origin, 8f, candidates);
            return candidates.Where(grenade => DetectArmedGrenade(uid, grenade.Owner))
                .Select(grenade => grenade.Owner).Concat(actor.Grenades.Where(grenade =>
                    Exists(grenade) && !TerminatingOrDeleted(grenade) && !EntityManager.IsQueuedForDeletion(grenade) &&
                    HasComp<ActiveTimerTriggerComponent>(grenade) && Transform(grenade).MapID == origin.MapId &&
                    Vector2.DistanceSquared(origin.Position, _transform.GetWorldPosition(grenade)) <= 64f))
                .Distinct().ToArray();
        }
        finally
        {
            candidates.Clear();
        }
    }

    private bool DetectArmedGrenade(EntityUid uid, EntityUid grenade)
    {
        if (TerminatingOrDeleted(grenade) || EntityManager.IsQueuedForDeletion(grenade))
            return false;
        var surface = CompOrNull<StickyComponent>(grenade)?.StuckTo;
        if (HasComp<ExplosiveComponent>(grenade))
            return SquadLineClear(uid, grenade, 8f, CollisionGroup.Opaque,
                ignored: entity => entity == surface || HasComp<NpcFactionMemberComponent>(entity));
        return (_squadTags.HasTag(grenade, GrenadeTag) || _squadTags.HasTag(grenade, HandGrenadeTag) ||
                _squadTags.HasTag(grenade, FlashGrenadeTag)) &&
               (_interaction.IsAccessible(uid, grenade) || surface is { } attached && _interaction.IsAccessible(uid, attached)) &&
               SquadLineClear(uid, grenade, 6f, SquadSight, ignored: entity => entity == surface);
    }

    private void OnSquadShot(Entity<MilitiaOperationActorComponent> actor, ref ShotAttemptedEvent args)
    {
        if (HasPlayerMind(actor.Owner))
            return;
        if (!SquadMember(actor.Owner) || BarrierClimbBusy(actor.Owner) || actor.Comp.InLockerFootprint || actor.Comp.MedicalRefill != null || actor.Comp.DoorPry != null || actor.Comp.ReloadUntil != null || actor.Comp.Patient.IsValid() || ReceivingTreatment(actor.Owner) ||
            MedicSupportPriority(actor.Comp) ||
            !TryComp<NPCRangedCombatComponent>(actor.Owner, out var ranged) ||
            args.Used.Comp.Target != ranged.Target || UnsafeRangedTarget(actor.Comp, ranged.Target) ||
            NeedsMeleeCounter(actor.Owner, actor.Comp, ranged.Target) ||
            !(ranged.Target == actor.Comp.GirderTarget ? ValidGirderShot(actor.Owner, actor.Comp, ranged.Target)
                : SquadVisible(actor.Owner, ranged.Target, CombatVision(actor.Comp, ranged.Target))) ||
            args.Used.Comp.ShootCoordinates is not { } aim || !aim.IsValid(EntityManager))
        {
            args.Cancel();
            _guns.StopExecutionShooting(args.Used);
            RemComp<NPCRangedCombatComponent>(actor.Owner);
            return;
        }
        var shotOrigin = _transform.GetMapCoordinates(actor.Owner);
        var shotTarget = _transform.ToMapCoordinates(aim);
        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(args.Used, ref ammo);
        var spread = ReflectionShotSpread(args.Used, in ammo);
        var collision = GetShotCollision(in ammo);
        var targetPosition = _transform.GetMapCoordinates(ranged.Target);
        // Leading a moving target can put the aim point in front of an obstruction while the
        // target itself is already behind it. Recheck the target at shot time, not just on squad ticks.
        if (args.Used.Owner == actor.Comp.LaserWeapon &&
                (actor.Comp.SecondaryDepleted || !SecondaryInRange(actor.Comp, shotOrigin, targetPosition)) ||
            SquadShotBlocked(actor.Owner, ranged.Target, shotOrigin, shotTarget, collision, spread) ||
            Vector2.DistanceSquared(shotTarget.Position, targetPosition.Position) > 0.01f &&
            ShotLineBlocked(actor.Owner, ranged.Target, shotOrigin, targetPosition, collision))
        {
            RejectGirderShot(actor.Comp, ranged.Target);
            RepositionBlockedFire(actor.Owner, actor.Comp);
            args.Cancel();
            _guns.StopExecutionShooting(args.Used);
            RemComp<NPCRangedCombatComponent>(actor.Owner);
            return;
        }
        if (SquadFireLaneBlocked(actor.Owner, ranged.Target, shotOrigin, shotTarget, spread, out var reflector))
        {
            RejectGirderShot(actor.Comp, ranged.Target);
            if (reflector)
                actor.Comp.UnsafeRangedTargets[ranged.Target] = _timing.CurTime + TimeSpan.FromSeconds(0.75);
            RepositionBlockedFire(actor.Owner, actor.Comp);
            args.Cancel();
            _guns.StopExecutionShooting(args.Used);
            RemComp<NPCRangedCombatComponent>(actor.Owner);
            return;
        }
        actor.Comp.FireBlockedUntil = TimeSpan.Zero;
        actor.Comp.AlertUntil = _timing.CurTime + TimeSpan.FromSeconds(12);
        if (args.Used.Owner == actor.Comp.PrimaryWeapon && ranged.Target != actor.Comp.GirderTarget)
            actor.Comp.RecentPrimaryAim = (ranged.Target, args.Used.Owner, _timing.CurTime + RecentContactDuration);
    }

    private bool SquadFireLaneBlocked(EntityUid uid, EntityUid target, MapCoordinates origin, MapCoordinates destination,
        float reflectionSpread, out bool blockedByReflector)
    {
        blockedByReflector = false;
        var delta = destination.Position - origin.Position;
        if (origin.MapId != destination.MapId || delta.LengthSquared() < 0.01f)
            return true;
        var direction = Vector2.Normalize(delta);
        var distance = MathF.Max(24f, delta.Length() + 0.65f);
        var shooterVelocity = _squadPhysics.GetMapLinearVelocity(new EntityCoordinates(uid, Vector2.Zero));
        var energyShot = TryComp<MilitiaOperationActorComponent>(uid, out var actor) && UsingSecondary(actor);
        foreach (var other in _squadLookup.GetEntitiesInRange<NpcFactionMemberComponent>(origin, distance))
        {
            if (other.Owner == uid || other.Owner == target || TerminatingOrDeleted(other.Owner) ||
                CompOrNull<RequireProjectileTargetComponent>(other.Owner)?.Active == true)
                continue;
            var friendly = !_mobs.IsDead(other.Owner) && _factions.IsEntityFriendly(uid, (other.Owner, other.Comp));
            var hasReflector = HasRangedReflector(other.Owner, energyShot);
            if (!friendly && !hasReflector)
                continue;
            var offset = _transform.GetWorldPosition(other.Owner) - origin.Position;
            var relativeVelocity = _squadPhysics.GetMapLinearVelocity(new EntityCoordinates(other.Owner, Vector2.Zero)) - shooterVelocity;
            var movement = relativeVelocity * 0.25f;
            var side = offset.X * direction.Y - offset.Y * direction.X;
            var sideMovement = movement.X * direction.Y - movement.Y * direction.X;
            var time = MathF.Abs(sideMovement) > 0.001f ? Math.Clamp(-side / sideMovement, 0f, 1f) : 0f;
            var predicted = offset + movement * time;
            var threatensAlly = friendly &&
                (BlocksFireLane(offset, direction, distance) || BlocksFireLane(predicted, direction, distance));
            var threatensReflection = hasReflector &&
                (BlocksReflectionLane(offset, direction, reflectionSpread, distance) ||
                  BlocksReflectionLane(predicted, direction, reflectionSpread, distance) ||
                  BlocksReflectionLane(offset + movement, direction, reflectionSpread, distance));
            if (!threatensAlly && !threatensReflection)
                continue;
            if (hasReflector && SquadSightClear(uid, other.Owner, distance))
            {
                if (threatensReflection)
                {
                    blockedByReflector = true;
                    return true;
                }
                continue;
            }
            if (threatensAlly && (offset.LengthSquared() <= 0.4225f || SquadLineClear(uid, other.Owner, distance, SquadSight)))
                return true;
        }
        return false;
    }

    private static bool BlocksFireLane(Vector2 offset, Vector2 direction, float distance = 24f)
    {
        var along = Vector2.Dot(offset, direction);
        return offset.LengthSquared() < 0.4225f ||
               along > 0f && along < distance && MathF.Abs(offset.X * direction.Y - offset.Y * direction.X) < 0.65f;
    }

    private float ReflectionShotSpread(Entity<GunComponent> gun)
    {
        var ammo = new GetAmmoCountEvent();
        RaiseLocalEvent(gun, ref ammo);
        return ReflectionShotSpread(gun, in ammo);
    }

    private float ReflectionShotSpread(Entity<GunComponent> gun, in GetAmmoCountEvent ammo)
    {
        EntityPrototype? prototype = null;
        ProjectileSpreadComponent? pellets = null;
        if (ammo.NextAmmoEntity is { } entity)
        {
            if (TryComp<CartridgeAmmoComponent>(entity, out var cartridge))
                _prototypes.TryIndex(cartridge.Prototype, out prototype);
            else
                TryComp(entity, out pellets);
        }
        else if (ammo.NextAmmoPrototype is { } id)
            _prototypes.TryIndex(id, out prototype);
        for (var depth = 0; prototype != null && depth < 8; depth++)
        {
            if (prototype.TryGetComponent<ProjectileSpreadComponent>(out pellets, _squadComponents))
                break;
            if (!prototype.TryGetComponent<CartridgeAmmoComponent>(out var cartridge, _squadComponents) ||
                !_prototypes.TryIndex(cartridge.Prototype, out prototype))
                break;
        }
        var angle = Math.Max(Math.Abs(gun.Comp.CurrentAngle.Theta), Math.Abs(gun.Comp.MaxAngleModified.Theta)) / 2;
        if (pellets is { Count: > 1 })
        {
            var spread = new GunGetAmmoSpreadEvent(pellets.Spread);
            RaiseLocalEvent(gun, ref spread);
            angle += Math.Abs(spread.Spread.Theta) / 2;
        }
        return (float) Math.Tan(Math.Clamp(angle, 0, Math.PI / 2 - 0.01));
    }

    private static bool BlocksReflectionLane(Vector2 offset, Vector2 direction, float spread, float distance)
    {
        var along = Vector2.Dot(offset, direction);
        return offset.LengthSquared() < 0.4225f ||
               along > 0f && along < distance && MathF.Abs(offset.X * direction.Y - offset.Y * direction.X) < 0.65f + along * spread;
    }

    public bool NeedsSquadHold(EntityUid uid)
        => TryComp<MilitiaOperationActorComponent>(uid, out var actor) &&
           !(actor.WindowExposed && actor.RangedDangerUntil > _timing.CurTime) &&
           GetSquadOrder(uid, true) != null && !NeedsReload(uid);

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
public enum MilitiaSquadTask : byte { Advance, Cover, Retreat, Rally, Peek, Evade, Treat, Reinforce, SeekMedic, Separate, Breakout, Melee, SweepLocker, MedicWithdraw }
public enum MilitiaBreakoutGoal : byte { Cover, Fight, Withdraw, Escape }
public sealed record MilitiaSquadContact(EntityUid Target, EntityCoordinates Position, TimeSpan SeenAt, TimeSpan Expires);
public sealed record MilitiaSquadOrder(EntityUid Target, EntityCoordinates[] Positions, MilitiaSquadTask Task, TimeSpan Until, MilitiaSquadContact[] Contacts, EntityCoordinates? ReturnPosition = null)
{
    public EntityCoordinates BreakoutThreat { get; init; }
    public IReadOnlyDictionary<EntityCoordinates, MilitiaBreakoutGoal>? BreakoutGoals { get; init; }
    public EntityUid Locker { get; init; }
}
public sealed record MilitiaSquadPlan(MilitiaSquadOrder Order, EntityCoordinates Position, EntityCoordinates[] Route, EntityUid[] Doors);
public sealed record MilitiaTreatmentPath(EntityCoordinates[] Route, EntityUid[] Doors);
