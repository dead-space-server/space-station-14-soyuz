// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.DeadSpace._Soyuz.MedicalOrders;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Storage.Components;

namespace Content.Server.DeadSpace._Soyuz.MedicalOrders;

public sealed partial class MedicalOrderSystem
{
    private bool CanReceivePatient(EntityUid receiver)
    {
        return Exists(receiver) && !Terminating(receiver) && !EntityManager.IsQueuedForDeletion(receiver) &&
            TryComp<EntityStorageComponent>(receiver, out var storage) && !storage.Open &&
            storage.Contents.ContainedEntities.Count == 0;
    }

    private MedicalOrderActive? PreparePatient(EntityUid station, EntityUid receiver, EntityUid actor,
        MedicalOrderConfigPrototype config, MedicalOrderOffer offer, TimeSpan deadline, int specialRuntimeId = 0)
    {
        if (!CanReceivePatient(receiver))
        {
            Reject(receiver, actor, "medical-orders-error-no-empty-capsule");
            return null;
        }

        EntityUid? body = null;
        EntityUid? gown = null;
        try
        {
            var storage = Comp<EntityStorageComponent>(receiver);
            body = _randomHumanoids.SpawnRandomHumanoid(config.PatientRandomHumanoidSettings.Id,
                Transform(receiver).Coordinates, string.Empty);
            gown = Spawn(config.PatientGown, Transform(receiver).Coordinates);
            if (!_inventory.TryEquip(body.Value, gown.Value, "outerClothing", silent: true, force: true) ||
                !HasComp<DamageableComponent>(body.Value) || !HasComp<MobStateComponent>(body.Value))
                return null;

            var damage = new DamageSpecifier();
            foreach (var line in offer.Lines)
                damage.DamageDict[line.ID] = FixedPoint2.New(line.Amount);
            if (!_damageable.TryChangeDamage(body.Value, damage, ignoreResistances: true))
                return null;

            _mobState.ChangeMobState(body.Value, MobState.Dead);
            if (!_storage.CanInsert(body.Value, receiver, storage) || !_storage.Insert(body.Value, receiver, storage))
                return null;

            var marker = EnsureComp<MedicalOrderPatientComponent>(body.Value);
            marker.Station = station;
            marker.RuntimeId = offer.RuntimeId;
            marker.SpecialRuntimeId = specialRuntimeId;
            if (!Exists(station) || Terminating(station) || EntityManager.IsQueuedForDeletion(station) ||
                !Exists(receiver) || Terminating(receiver) || EntityManager.IsQueuedForDeletion(receiver) ||
                !Exists(body.Value) || Terminating(body.Value) || EntityManager.IsQueuedForDeletion(body.Value) ||
                _stations.GetOwningStation(receiver) != station || !this.IsPowered(receiver, EntityManager) ||
                !_access.IsAllowed(actor, receiver) || _timing.CurTime >= deadline || storage.Open ||
                storage.Contents.ContainedEntities.Count != 1 || !storage.Contents.ContainedEntities.Contains(body.Value))
                return null;

            var prepared = new MedicalOrderActive
            {
                Offer = CopyOffer(offer),
                Terminal = receiver,
                AcceptedAt = _timing.CurTime,
                Deadline = deadline,
                Patient = body,
                Gown = gown,
                PatientName = Name(body.Value),
                InitialDamage = Comp<DamageableComponent>(body.Value).TotalDamage,
            };
            body = null;
            gown = null;
            return prepared;
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Preparing medical patient {offer.RuntimeId} failed: {exception}");
            return null;
        }
        finally
        {
            if (body != null || gown != null)
            {
                DiscardPreparedPatient(new MedicalOrderActive { Patient = body, Gown = gown });
                Reject(receiver, actor, "medical-orders-error-patient-create");
            }
        }
    }

    private void DiscardPreparedPatient(MedicalOrderActive? prepared)
    {
        if (prepared == null)
            return;
        try
        {
            if (prepared.Patient is { } body && Exists(body) && !Terminating(body))
                QueueDel(body);
        }
        finally
        {
            if (prepared.Gown is { } gown && Exists(gown) && !Terminating(gown))
                QueueDel(gown);
        }
    }

    private bool CanSubmitPatient(EntityUid station, EntityUid sender, MedicalOrderActive active,
        MedicalOrderConfigPrototype config, out string error, int specialRuntimeId = 0)
    {
        error = "medical-orders-error-no-patient";
        if (!TryComp<EntityStorageComponent>(sender, out var storage) || storage.Open ||
            storage.Contents.ContainedEntities.Count != 1)
            return false;

        error = "medical-orders-error-wrong-patient";
        if (active.Patient is not { } body || IsPatientMissing(active) ||
            !storage.Contents.ContainedEntities.Contains(body) ||
            !TryComp<MedicalOrderPatientComponent>(body, out var marker) || marker.Station != station ||
            marker.RuntimeId != active.Offer.RuntimeId || marker.SpecialRuntimeId != specialRuntimeId)
            return false;

        error = "medical-orders-error-not-alive";
        if (!_mobState.IsAlive(body) || _mobState.IsCritical(body))
            return false;

        error = "medical-orders-error-damage";
        return TryComp<DamageableComponent>(body, out var damageable) &&
            damageable.TotalDamage <= FixedPoint2.New(config.PatientCompletionDamageThreshold);
    }
}
