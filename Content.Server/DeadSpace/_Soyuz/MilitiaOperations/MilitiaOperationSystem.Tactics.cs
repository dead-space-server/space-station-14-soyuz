// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Shared.Throwing;
using Content.Shared.Trigger.Components;
using Content.Shared.Trigger.Systems;
using Content.Shared.Prying.Systems;
using Content.Shared.Prying.Components;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly ThrownItemSystem _thrownItems = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;
    [Dependency] private readonly PryingSystem _prying = default!;
    private TimeSpan _nextSquadGrenade;

    private bool OnDefensiveGrid(EntityUid uid, EntityCoordinates coordinates)
    {
        if (!coordinates.IsValid(EntityManager))
            return false;
        if (_turf.GetTileRef(coordinates) is not { } tile)
            return false;
        var home = Comp<MilitiaOperationActorComponent>(uid).DefensivePosition;
        if (!home.IsValid(EntityManager))
            return false;
        if (_turf.GetTileRef(home) is { } homeTile && tile.GridUid == homeTile.GridUid)
            return true;
        return TryComp<MilitiaOperationEnemyComponent>(uid, out var actor) && actor.Operation != Guid.Empty &&
            TryComp<MilitiaOperationGridComponent>(tile.GridUid, out var grid) &&
            grid.Operation == actor.Operation && grid.Station == actor.Station;
    }

    private EntityUid? DoorUser(EntityUid uid, EntityUid door)
        => !HasComp<FirelockComponent>(door) && TryComp<MilitiaOperationEnemyComponent>(uid, out var actor) && actor.Operation != Guid.Empty &&
           Transform(door).GridUid is { } gridUid && TryComp<MilitiaOperationGridComponent>(gridUid, out var grid) &&
           grid.Operation == actor.Operation && grid.Station == actor.Station ? null : uid;

    private bool CanPryRouteDoor(EntityUid uid, EntityUid door)
        => TryComp<MilitiaOperationActorComponent>(uid, out var actor) && OwnsItem(uid, actor.PryTool) &&
           TryComp<PryingComponent>(actor.PryTool, out var tool) && tool.Enabled && _prying.CanPry(door, uid, out _, tool);

    private HTNOperatorStatus PryRouteDoor(EntityUid uid, EntityUid door)
    {
        var actor = Comp<MilitiaOperationActorComponent>(uid);
        EndGuard(uid);
        if (!EquipOwnedItem(uid, actor, actor.PryTool, false))
            return HTNOperatorStatus.Failed;
        _steering.Unregister(uid);
        DoAfterId? id = null;
        var started = false;
        try
        {
            if (!_prying.TryPry(door, uid, out id, actor.PryTool) || id == null)
                return HTNOperatorStatus.Failed;
            actor.DoorPry = id;
            started = true;
            return HTNOperatorStatus.Continuing;
        }
        catch (Exception exception)
        {
            _log.Error($"Militia door action failed for {uid}: {exception}");
            return HTNOperatorStatus.Failed;
        }
        finally
        {
            if (!started)
            {
                if (id is { } failed && _doAfter.GetStatus(failed) == DoAfterStatus.Running)
                    _doAfter.Cancel(uid, failed.Index, null);
                ArmActor(uid);
            }
        }
    }

    private void EndRoutePry(EntityUid uid)
    {
        if (!TryComp<MilitiaOperationActorComponent>(uid, out var actor) || actor.DoorPry is not { } id)
            return;
        actor.DoorPry = null;
        if (_doAfter.GetStatus(id) == DoAfterStatus.Running)
            _doAfter.Cancel(uid, id.Index, null);
        if (actor.Patient.IsValid() && OwnsItem(uid, actor.Treatment))
            EquipOwnedItem(uid, actor, actor.Treatment, false);
        else
            ArmActor(uid);
    }

    public MilitiaSquadPlan? RunningSquadPlan(EntityUid uid, MilitiaSquadOrder order)
        => TryComp<MilitiaOperationActorComponent>(uid, out var actor) && ReferenceEquals(actor.RunningSquadOrder, order)
            ? new MilitiaSquadPlan(order, actor.SquadPosition, actor.SquadRoute.Skip(actor.SquadRouteIndex).ToArray(), actor.RetreatDoors)
            : null;

    private void TrySquadGrenade(EntityUid uid)
    {
        if (!SquadMember(uid) || !TryComp<MilitiaOperationActorComponent>(uid, out var actor) || actor.Medic ||
            actor.DoorPry != null || actor.Patient.IsValid() || ReceivingTreatment(uid) || actor.ReloadUntil != null || actor.Grenades.Length > 0 ||
            !DirectContact(uid, actor) || _timing.CurTime < actor.NextGrenade || _timing.CurTime < _nextSquadGrenade)
            return;
        actor.NextGrenade = _timing.CurTime + TimeSpan.FromSeconds(8 + _random.NextDouble() * 8);
        if (_random.NextDouble() > 0.35)
            return;
        var grenade = actor.PersonalGrenades.FirstOrDefault(item => OwnsItem(uid, item) && !HasComp<ActiveTimerTriggerComponent>(item));
        if (!grenade.IsValid())
            return;
        var target = _transform.GetMapCoordinates(actor.DirectTarget);
        var origin = _transform.GetWorldPosition(uid);
        var distance = Vector2.Distance(origin, target.Position);
        var axis = distance > 0 ? (target.Position - origin) / distance : Vector2.Zero;
        if (distance < 7f || distance > 11f || !_interaction.InRangeUnobstructed(uid, actor.DirectTarget, 11f, SquadSight) ||
            !OnDefensiveGrid(uid, Transform(actor.DirectTarget).Coordinates) ||
            _factions.GetNearbyFriendlies(uid, 24f).Any(ally => !_mobs.IsDead(ally) &&
                (Vector2.DistanceSquared(_transform.GetWorldPosition(ally), target.Position) < 36f ||
                 BlocksFireLane(_transform.GetWorldPosition(ally) - origin, axis) &&
                 Vector2.Dot(_transform.GetWorldPosition(ally) - origin, axis) < distance)))
            return;
        if (CaptureEquipment(uid, grenade) is not { } equipment)
            return;
        var dropped = false;
        var committed = false;
        EndGuard(uid);
        try
        {
            if (!EquipOwnedItem(uid, actor, grenade, false) || !_hands.TryDrop(uid, grenade))
                return;
            dropped = true;
            _throwing.TryThrow(grenade, Transform(actor.DirectTarget).Coordinates, baseThrowSpeed: 10f, user: uid,
                pushbackRatio: 0f, compensateFriction: true, recoil: false);
            if (!HasComp<ThrownItemComponent>(grenade))
                return;
            if (!_trigger.ActivateTimerTrigger(grenade, uid) && !HasComp<ActiveTimerTriggerComponent>(grenade))
                return;
            committed = true;
            actor.PersonalGrenades.Remove(grenade);
            actor.NextGrenade = _timing.CurTime + TimeSpan.FromSeconds(30 + _random.NextDouble() * 20);
            _nextSquadGrenade = _timing.CurTime + TimeSpan.FromSeconds(6);
        }
        catch (Exception exception) { _log.Error($"Militia grenade action failed for {uid}: {exception}"); }
        finally
        {
            try
            {
                if (!committed && !TerminatingOrDeleted(grenade) && !EntityManager.IsQueuedForDeletion(grenade) &&
                    (OwnsItem(uid, grenade) || dropped && !_equipmentContainers.TryGetContainingContainer(grenade, out _)))
                {
                    if (HasComp<ActiveTimerTriggerComponent>(grenade))
                        _trigger.StopTimerTrigger(grenade);
                    if (!HasComp<ActiveTimerTriggerComponent>(grenade))
                    {
                        if (TryComp<ThrownItemComponent>(grenade, out var thrown))
                            _thrownItems.StopThrow(grenade, thrown);
                        if (TryComp<PhysicsComponent>(grenade, out var physics))
                        {
                            _squadPhysics.SetLinearVelocity(grenade, Vector2.Zero, body: physics);
                            _squadPhysics.SetAngularVelocity(grenade, 0f, body: physics);
                        }
                        RestoreEquipment(uid, equipment.Items, equipment.ActiveHand, equipment.Wielded);
                    }
                }
            }
            catch (Exception exception) { _log.Error($"Militia grenade recovery failed for {uid}: {exception}"); }
            finally { ArmActor(uid); }
        }
    }
}
