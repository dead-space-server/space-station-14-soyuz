// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Server.NPC.HTN;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Events;
using Content.Shared.Chemistry.Prototypes;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Components;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Medical.Healing;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stacks;
using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _medicalSolutions = default!;
    [Dependency] private readonly MobThresholdSystem _medicalThresholds = default!;
    [Dependency] private readonly InjectorSystem _medicalInjectors = default!;
    [Dependency] private readonly BloodstreamSystem _medicalBloodstream = default!;
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _treatmentReservations = new();
    private const float MedicalDashLimit = 11f;
    private static readonly ProtoId<ReagentPrototype> MilitiaMedicine = "Omnizine";
    private static readonly ProtoId<ReagentPrototype> MilitiaCombatMedicine = "Derytracine";
    private static readonly ProtoId<ReagentPrototype> MilitiaClottingMedicine = "TranexamicAcid";
    private static readonly ProtoId<ReagentPrototype> MilitiaBloodMedicine = "Saline";
    private static readonly ProtoId<ReagentPrototype>[] MilitiaMedicines =
        { MilitiaMedicine, MilitiaCombatMedicine, MilitiaClottingMedicine, MilitiaBloodMedicine };
    private static readonly ProtoId<InjectorModePrototype> MilitiaInjectionMode = "HyposprayInjectMode";
    private static readonly ProtoId<InjectorModePrototype> MilitiaRefillMode = "HyposprayDynamicMode";

    private static bool IsMedicineSolution(Solution solution)
    {
        var recognized = FixedPoint2.Zero;
        foreach (var reagent in MilitiaMedicines)
            recognized += solution.GetTotalPrototypeQuantity(reagent);
        return solution.Volume > 0 && recognized == solution.Volume;
    }

    private bool BloodDeficit(EntityUid patient)
        => TryComp<BloodstreamComponent>(patient, out var blood) &&
           _medicalBloodstream.GetBloodLevel((patient, blood)) < 1f;

    private bool NeedsBloodMedicine(EntityUid patient)
        => BloodDeficit(patient) || TryComp<BloodstreamComponent>(patient, out var blood) && blood.BleedAmount > 0;

    private bool MedicineUseful(ProtoId<ReagentPrototype> reagent, EntityUid patient, DamageableComponent damage)
    {
        if (reagent == MilitiaClottingMedicine)
            return TryComp<BloodstreamComponent>(patient, out var blood) && blood.BleedAmount > 0;
        if (reagent == MilitiaBloodMedicine)
            return BloodDeficit(patient);
        return damage.TotalDamage > 0 && (reagent == MilitiaMedicine ||
            new[] { "Piercing", "Heat", "Cold", "Shock" }.Any(type => damage.Damage.DamageDict.TryGetValue(type, out var amount) && amount > 0));
    }

    private static bool InjectorAcceptsMedicine(InjectorComponent injector, Solution solution)
    {
        if (injector.ReagentWhitelist == null)
            return true;
        var allowed = FixedPoint2.Zero;
        foreach (var reagent in injector.ReagentWhitelist)
            allowed += solution.GetTotalPrototypeQuantity(reagent);
        return allowed == solution.Volume;
    }

    private static bool SupportsMedicalRefill(InjectorComponent injector)
    {
        foreach (var mode in injector.AllowedModes)
        {
            if (mode == MilitiaRefillMode)
                return true;
        }
        return false;
    }

    private bool FindMedicalRefill(EntityUid uid, MilitiaOperationActorComponent actor, out EntityUid injector, out EntityUid bottle)
    {
        injector = bottle = EntityUid.Invalid;
        var pending = actor.Medic ? HasTriageWork(uid, actor) : NeedsBloodMedicine(uid);
        foreach (var item in actor.Treatments)
        {
            if (!OwnsItem(uid, item) || !TryComp<InjectorComponent>(item, out var component) ||
                !SupportsMedicalRefill(component) ||
                !_medicalSolutions.TryGetSolution(item, component.SolutionName, out _, out var solution) || solution.AvailableVolume < 5 ||
                solution.Volume > 0 && !IsMedicineSolution(solution) || pending && solution.Volume >= 5)
                continue;
            foreach (var supply in actor.MedicineBottles)
            {
                if (!OwnsItem(uid, supply) || !_medicalSolutions.TryGetDrawableSolution(supply, out _, out var stock) || stock.Volume < 5 ||
                    !IsMedicineSolution(stock) || !InjectorAcceptsMedicine(component, stock))
                    continue;
                injector = item;
                bottle = supply;
                return true;
            }
        }
        return false;
    }

    public bool NeedsMedicalRefill(EntityUid uid)
        => AvailableActor(uid, out var actor) && actor.Medic && actor.MedicalRefill == null &&
           actor.DoorPry == null && !actor.Patient.IsValid() && !ReceivingTreatment(uid) &&
           _timing.CurTime >= actor.NextRefill && SafeForTreatment(uid) &&
           FindMedicalRefill(uid, actor, out var injector, out _) &&
           TryComp<InjectorComponent>(injector, out var component) &&
           _medicalSolutions.TryGetSolution(injector, component.SolutionName, out _, out var solution) &&
           (solution.Volume < 5 || !HasTriageWork(uid, actor)) &&
           CanEquipOwnedItem(uid, actor, injector, false, out _, freeHands: true);

    public void BeginMedicalRefill(EntityUid uid)
    {
        if (!NeedsMedicalRefill(uid) || !AvailableActor(uid, out var actor) ||
            !FindMedicalRefill(uid, actor, out var injector, out var bottle))
            return;
        if (CaptureEquipment(uid, injector, bottle) is not { } equipment)
            return;
        var refill = new MilitiaMedicalRefill(injector, bottle, equipment);
        var committed = false;
        try
        {
            EndGuard(uid);
            _steering.Unregister(uid);
            if (!EquipOwnedItem(uid, actor, injector, false, freeHands: true) ||
                !_hands.TryPickupAnyHand(uid, bottle, animate: false) || !_hands.IsHolding(uid, injector, out var hand) ||
                (_hands.GetActiveHand(uid) != hand && !_hands.SetActiveHand(uid, hand)) || !OwnsItem(uid, bottle) || !AvailableActor(uid, out _))
                return;
            var component = Comp<InjectorComponent>(injector);
            _medicalInjectors.ToggleMode((injector, component), uid, _prototypes.Index(MilitiaRefillMode));
            if (!_medicalInjectors.TrySetTransferAmount((injector, component), FixedPoint2.New(5)))
                return;
            actor.MedicalRefill = refill;
            committed = true;
        }
        catch (Exception exception) { _log.Error($"Militia medicine preparation failed for {uid}: {exception}"); }
        finally
        {
            if (!committed)
            {
                actor.NextRefill = _timing.CurTime + TimeSpan.FromSeconds(2);
                RestoreEquipment(uid, equipment.Items, equipment.ActiveHand, equipment.Wielded, activated: equipment.Activated);
            }
        }
    }

    public HTNOperatorStatus RefillMedicine(EntityUid uid)
    {
        if (!AvailableActor(uid, out var actor) || actor.MedicalRefill is not { } refill || !SafeForTreatment(uid) ||
            !OwnsItem(uid, refill.Injector) || !OwnsItem(uid, refill.Bottle) ||
            _hands.GetActiveItem(uid) != refill.Injector || !_hands.IsHolding(uid, refill.Bottle, out _) ||
            !_blocker.CanInteract(uid, refill.Bottle) || !TryComp<InjectorComponent>(refill.Injector, out var injector) ||
            injector.CurrentTransferAmount != FixedPoint2.New(5) || injector.ActiveModeProtoId != MilitiaRefillMode ||
            !_medicalSolutions.TryGetSolution(refill.Injector, injector.SolutionName, out _, out var solution))
            return HTNOperatorStatus.Failed;
        if (refill.DoAfter is { } current)
        {
            var status = _doAfter.GetStatus(uid, current, null);
            if (status == DoAfterStatus.Running)
                return HTNOperatorStatus.Continuing;
            refill.DoAfter = null;
            if (status != DoAfterStatus.Finished || solution.Volume <= refill.Volume)
                return HTNOperatorStatus.Failed;
        }
        if (solution.Volume >= 5 && (HasTriageWork(uid, actor) || NeedsBloodMedicine(uid)))
            return HTNOperatorStatus.Finished;
        if (solution.AvailableVolume < 5 || !_medicalSolutions.TryGetDrawableSolution(refill.Bottle, out _, out var stock) || stock.Volume < 5)
            return solution.Volume >= 5 ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
        if (!IsMedicineSolution(stock) || !InjectorAcceptsMedicine(injector, stock) ||
            solution.Volume > 0 && !IsMedicineSolution(solution) ||
            !TryComp<DoAfterComponent>(uid, out var doAfter))
            return HTNOperatorStatus.Failed;
        var next = doAfter.NextId;
        refill.Volume = solution.Volume;
        _interaction.UserInteraction(uid, Transform(refill.Bottle).Coordinates, refill.Bottle);
        if (solution.Volume > refill.Volume)
            return HTNOperatorStatus.Continuing;
        if (doAfter.NextId == next)
            return HTNOperatorStatus.Failed;
        refill.DoAfter = next;
        return HTNOperatorStatus.Continuing;
    }

    public void EndMedicalRefill(EntityUid uid)
    {
        if (!TryComp<MilitiaOperationActorComponent>(uid, out var actor) || actor.MedicalRefill is not { } refill)
            return;
        actor.MedicalRefill = null;
        actor.NextRefill = _timing.CurTime + TimeSpan.FromSeconds(2);
        if (refill.DoAfter is { } current && _doAfter.GetStatus(uid, current, null) == DoAfterStatus.Running)
            _doAfter.Cancel(uid, current, null);
        if (!TerminatingOrDeleted(uid) && !_mobs.IsIncapacitated(uid) && !HasPlayerMind(uid))
        {
            var items = refill.Equipment.Items.Where(item => OwnsItem(uid, item.Item) &&
                (item.Container.Owner == uid || OwnsItem(uid, item.Container.Owner))).ToList();
            RestoreEquipment(uid, items, refill.Equipment.ActiveHand,
                refill.Equipment.Wielded.Where(item => items.Any(snapshot => snapshot.Item == item)).ToArray(),
                activated: refill.Equipment.Activated);
        }
    }

    private bool NeedsTreatment(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid patient, DamageableComponent damage)
        => damage.TotalDamage >= actor.TreatmentThreshold || NeedsBloodMedicine(patient) ||
           actor.Treatments.Any(item => OwnsItem(uid, item) && HasComp<HealingComponent>(item) && CanTreat(uid, item, patient, damage));

    public List<(EntityUid Patient, EntityUid Item)> TreatmentCandidates(EntityUid uid)
    {
        var result = new List<(EntityUid, EntityUid)>();
        if (!AvailableActor(uid, out var actor) || actor.MedicalRefill != null || actor.Patient.IsValid() || actor.DoorPry != null || ReceivingTreatment(uid) || _timing.CurTime < actor.NextTreatment)
            return result;
        foreach (var patient in actor.UnreachablePatients.Keys.ToArray())
        {
            if (actor.UnreachablePatients[patient] <= _timing.CurTime || !Exists(patient))
                actor.UnreachablePatients.Remove(patient);
        }
        var patients = new List<EntityUid>();
        if (!actor.Medic)
            patients.Add(uid);
        else
        {
            var query = EntityQueryEnumerator<MilitiaOperationActorComponent>();
            while (query.MoveNext(out var patient, out _))
            {
                if (TerminatingOrDeleted(patient) || !TryComp<DamageableComponent>(patient, out _) ||
                    _mobs.IsDead(patient) || HasPlayerMind(patient) || !SameOperation(uid, patient) ||
                    !InTreatmentArea(uid, patient, actor) ||
                    actor.UnreachablePatients.TryGetValue(patient, out var retry) && retry > _timing.CurTime)
                    continue;
                patients.Add(patient);
            }
        }
        foreach (var patient in patients.OrderByDescending(p => p == uid && NeedsMedic(uid, actor))
                     .ThenByDescending(p => TryComp<DamageableComponent>(p, out var damage) ? damage.TotalDamage : 0))
        {
            if ((!actor.Medic && patient != uid) || !ValidPatient(uid, patient, actor, allowCombatDash: true) ||
                !TryComp<DamageableComponent>(patient, out var damage) ||
                actor.UnreachablePatients.TryGetValue(patient, out var retry) && retry > _timing.CurTime ||
                !NeedsTreatment(uid, actor, patient, damage))
                continue;
            var bleeding = NeedsBloodMedicine(patient);
            foreach (var item in actor.Treatments.OrderBy(item => bleeding && HasComp<InjectorComponent>(item) ? 0 : 1))
            {
                if (CanTreat(uid, item, patient, damage) && CanEquipOwnedItem(uid, actor, item, false, out _))
                {
                    result.Add((patient, item));
                    break;
                }
            }
        }
        return result;
    }

    private bool CanTreat(EntityUid user, EntityUid item, EntityUid patient, DamageableComponent damage)
        => !TerminatingOrDeleted(item) && !EntityManager.IsQueuedForDeletion(item) &&
           (CanInjectMedicine(user, item, patient, damage) || TryComp<HealingComponent>(item, out var healing) &&
               (!TryComp<StackComponent>(item, out var stack) || stack.Count > 0) &&
               (healing.DamageContainers == null || damage.DamageContainerID == null ||
                healing.DamageContainers.Contains(damage.DamageContainerID.Value)) &&
               _healing.HasDamage((item, healing), (patient, damage)));

    private bool CanInjectMedicine(EntityUid user, EntityUid item, EntityUid patient, DamageableComponent damage)
    {
        if (!TryComp<InjectorComponent>(item, out var injector) ||
            (injector.ActiveModeProtoId != MilitiaInjectionMode && injector.ActiveModeProtoId != MilitiaRefillMode) ||
            !_medicalSolutions.TryGetSolution(item, injector.SolutionName, out _, out var medicine) || medicine.Volume < 5 ||
            !IsMedicineSolution(medicine))
            return false;
        return CanReceiveMedicine(user, item, patient, medicine, damage);
    }

    private bool CanReceiveMedicine(EntityUid user, EntityUid item, EntityUid patient, Solution medicine, DamageableComponent damage)
    {
        if (!IsMedicineSolution(medicine) || !HasComp<BloodstreamComponent>(patient) ||
            !_medicalSolutions.TryGetInjectableSolution(patient, out _, out var chemicals) || chemicals.AvailableVolume < 5)
            return false;
        var useful = false;
        foreach (var reagent in MilitiaMedicines)
        {
            var quantity = medicine.GetTotalPrototypeQuantity(reagent);
            if (quantity <= 0)
                continue;
            var current = chemicals.GetTotalPrototypeQuantity(reagent);
            if (current + quantity * 5 / medicine.Volume > (reagent == MilitiaCombatMedicine ? 15 : 10))
                return false;
            useful |= current < 2 && MedicineUseful(reagent, patient, damage);
        }
        if (!useful)
            return false;
        var attempt = new TargetBeforeInjectEvent(user, item, patient);
        RaiseLocalEvent(patient, ref attempt);
        return !attempt.Cancelled;
    }

    private bool HasTreatment(EntityUid uid, MilitiaOperationActorComponent actor, EntityUid patient, DamageableComponent damage)
    {
        if (actor.Treatments.Any(item => OwnsItem(uid, item) && CanTreat(uid, item, patient, damage)))
            return true;
        return actor.Medic && actor.Treatments.Any(injector => OwnsItem(uid, injector) &&
            TryComp<InjectorComponent>(injector, out var component) && SupportsMedicalRefill(component) &&
            actor.MedicineBottles.Any(item => OwnsItem(uid, item) &&
                _medicalSolutions.TryGetDrawableSolution(item, out _, out var stock) && stock.Volume >= 5 &&
                IsMedicineSolution(stock) && InjectorAcceptsMedicine(component, stock) &&
                CanReceiveMedicine(uid, injector, patient, stock, damage)));
    }

    private bool NeedsMedic(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        if (!TryComp<DamageableComponent>(uid, out var damage))
            return false;
        if (_medicalThresholds.TryGetThresholdForState(uid, MobState.PreCritical, out var threshold) ||
            _medicalThresholds.TryGetIncapThreshold(uid, out threshold))
            return damage.TotalDamage.Float() >= threshold.Value.Float() * 0.65f;
        return damage.TotalDamage >= actor.TreatmentThreshold;
    }

    private bool CanMedicalDash(EntityUid uid, EntityUid patient, MilitiaOperationActorComponent actor)
    {
        if (!actor.Medic || uid == patient || actor.Grenades.Length != 0 || !_mobs.IsAlive(uid) || !_blocker.CanMove(uid) ||
            TryComp<StaminaComponent>(uid, out var stamina) && stamina.Critical ||
            !TryComp<DamageableComponent>(uid, out var injury) || !TryComp<DamageableComponent>(patient, out var wounds) ||
            !_medicalThresholds.TryGetIncapThreshold(uid, out var medicThreshold) ||
            injury.TotalDamage.Float() > medicThreshold.Value.Float() * 0.3f ||
            !TryComp<BloodstreamComponent>(uid, out var blood) || blood.BleedAmount > 0 ||
            _medicalBloodstream.GetBloodLevel((uid, blood)) < 0.75f)
            return false;
        return _mobs.IsIncapacitated(patient) ||
               _medicalThresholds.TryGetIncapThreshold(patient, out var threshold) &&
               wounds.TotalDamage.Float() >= threshold.Value.Float() * 0.5f;
    }

    private void TrackMedicalExposure(EntityUid uid, MilitiaOperationActorComponent actor)
    {
        var position = _transform.GetMapCoordinates(uid);
        var exposed = actor.ExposedToThreat;
        if (actor.MedicalDashPosition.IsValid(EntityManager))
        {
            var previous = _transform.ToMapCoordinates(actor.MedicalDashPosition);
            if (previous.MapId != position.MapId)
                actor.MedicalDashExposure = float.PositiveInfinity;
            else if (exposed || actor.MedicalDashExposed)
                actor.MedicalDashExposure += Vector2.Distance(previous.Position, position.Position);
        }
        actor.MedicalDashPosition = Transform(uid).Coordinates;
        actor.MedicalDashExposed = exposed;
    }

    public void RejectTreatmentPath(EntityUid uid, EntityUid patient)
    {
        if (TryComp<MilitiaOperationActorComponent>(uid, out var actor))
            actor.UnreachablePatients[patient] = _timing.CurTime + TimeSpan.FromSeconds(5);
    }

    private EntityUid FindMedic(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damage) || !TryComp<MilitiaOperationActorComponent>(uid, out var actor))
            return EntityUid.Invalid;
        var nearest = EntityUid.Invalid;
        var distance = float.PositiveInfinity;
        var medics = EntityQueryEnumerator<MilitiaOperationActorComponent>();
        while (medics.MoveNext(out var other, out var medic))
        {
            if (other == uid || !medic.Medic || !SquadMember(other) || EntityManager.IsQueuedForDeletion(other) || !_mobs.IsAlive(other) ||
                actor.UnreachableMedics.TryGetValue(other, out var retry) && retry > _timing.CurTime ||
                TryComp<StaminaComponent>(other, out var stamina) && stamina.Critical ||
                !_blocker.CanMove(other) || !_blocker.CanInteract(other, uid) ||
                !SameOperation(uid, other) || Transform(uid).MapID != Transform(other).MapID ||
                !OnDefensiveGrid(uid, Transform(other).Coordinates))
                continue;
            var squared = System.Numerics.Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(other));
            if (squared >= distance || !HasTreatment(other, medic, uid, damage))
                continue;
            nearest = other;
            distance = squared;
        }
        return nearest;
    }

    public void RejectUnreachableMedic(EntityUid uid, MilitiaSquadOrder order)
    {
        if (order.Task != MilitiaSquadTask.SeekMedic || !AvailableActor(uid, out var actor) ||
            !ReferenceEquals(actor.SquadOrder, order))
            return;
        actor.UnreachableMedics[order.Target] = _timing.CurTime + TimeSpan.FromSeconds(10);
        actor.SquadOrder = null;
        _urgentSquadOrders.Add(uid);
    }

    private bool InTreatmentArea(EntityUid uid, EntityUid patient, MilitiaOperationActorComponent actor)
        => Transform(uid).MapID == Transform(patient).MapID &&
           System.Numerics.Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(patient)) <=
               actor.TreatmentRange * actor.TreatmentRange &&
           (!actor.DefensivePosition.IsValid(EntityManager) ||
            System.Numerics.Vector2.DistanceSquared(_transform.ToMapCoordinates(actor.DefensivePosition).Position,
                _transform.GetWorldPosition(patient)) <= actor.DefensiveRange * actor.DefensiveRange);

    private bool ValidPatient(EntityUid uid, EntityUid patient, MilitiaOperationActorComponent actor, bool allowCombatDash = false)
    {
        if (!Exists(patient) || TerminatingOrDeleted(patient) || _mobs.IsDead(patient) || HasPlayerMind(patient) ||
            MetaData(uid).EntityPaused || MetaData(patient).EntityPaused ||
            !TryComp<HTNComponent>(uid, out var medicHtn) || !medicHtn.Enabled ||
            !TryComp<HTNComponent>(patient, out var patientHtn) || !patientHtn.Enabled ||
            (!SafeForTreatment(uid) && (!allowCombatDash || !CanMedicalDash(uid, patient, actor))) || !SafeForTreatment(patient) ||
            actor.Patient == patient && actor.MedicalDashExposure > MedicalDashLimit ||
            !HasComp<MilitiaOperationActorComponent>(patient) ||
            !SameOperation(uid, patient) ||
            (!actor.Medic && patient != uid) || !InTreatmentArea(uid, patient, actor))
            return false;
        if (_treatmentReservations.TryGetValue(patient, out var medics))
        {
            foreach (var other in medics)
            {
                if (other != uid && AvailableActor(other, out var otherActor) && !MetaData(other).EntityPaused && otherActor.Patient == patient &&
                    !(actor.Medic && other == patient) &&
                    !(uid == patient && actor.Patient == uid && otherActor.Medic))
                    return false;
            }
        }
        return true;
    }

    private bool SameOperation(EntityUid uid, EntityUid patient)
    {
        TryComp<MilitiaOperationEnemyComponent>(uid, out var owner);
        TryComp<MilitiaOperationEnemyComponent>(patient, out var target);
        return owner == null || owner.Operation == Guid.Empty
            ? target == null || target.Operation == Guid.Empty
            : target != null && owner.Station == target.Station && owner.Operation == target.Operation && IsActive(owner.Station, owner.Operation);
    }

    public bool SelectTreatment(EntityUid uid, EntityUid patient, EntityUid item, MilitiaTreatmentPath path)
    {
        if (!AvailableActor(uid, out var actor) || ReceivingTreatment(uid) || !ValidPatient(uid, patient, actor, allowCombatDash: true) ||
            !TreatmentPathSafe(uid, patient, path) || !OwnsItem(uid, item) || !TryComp<DamageableComponent>(patient, out var damage) ||
            !CanTreat(uid, item, patient, damage) || !EquipOwnedItem(uid, actor, item, false))
            return false;
        actor.Patient = patient;
        actor.Treatment = item;
        actor.TreatmentPath = path;
        actor.SquadRouteIndex = 0;
        actor.MedicalDashExposure = 0f;
        actor.MedicalDashPosition = Transform(uid).Coordinates;
        actor.MedicalDashExposed = actor.ExposedToThreat;
        var committed = false;
        try
        {
            if (!_treatmentReservations.TryGetValue(patient, out var medics))
                _treatmentReservations[patient] = medics = new HashSet<EntityUid>();
            medics.Add(uid);
            EndGuard(uid);
            _steering.Unregister(uid);
            if (patient != uid)
            {
                var target = Comp<MilitiaOperationActorComponent>(patient);
                target.TreatingMedic = uid;
                if (target.Patient != patient)
                    InterruptActor(patient);
            }
            committed = AvailableActor(uid, out _) && TreatmentPathSafe(uid, patient, path) && OwnsItem(uid, item);
            return committed;
        }
        finally
        {
            if (!committed)
                EndTreatment(uid);
        }
    }

    public HTNOperatorStatus TreatActor(EntityUid uid)
    {
        if (!AvailableActor(uid, out var actor))
            return HTNOperatorStatus.Failed;
        if (actor.Patient != uid && !HasComp<InjectorComponent>(actor.Treatment) &&
            TryComp<MilitiaOperationActorComponent>(actor.Patient, out var target) && target.Patient == actor.Patient)
            return ValidPatient(uid, actor.Patient, actor) && _interaction.InRangeUnobstructed(uid, actor.Patient)
                ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Failed;
        if (actor.HealingDoAfter is { } current)
        {
            var status = _doAfter.GetStatus(uid, current, null);
            if (status == DoAfterStatus.Finished)
                return HTNOperatorStatus.Finished;
            if (status != DoAfterStatus.Running)
                return HTNOperatorStatus.Failed;
            return ValidPatient(uid, actor.Patient, actor) && _interaction.InRangeUnobstructed(uid, actor.Patient)
                ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Failed;
        }
        if (!ValidPatient(uid, actor.Patient, actor) || !OwnsItem(uid, actor.Treatment) ||
            _hands.GetActiveItem(uid) != actor.Treatment || !_interaction.InRangeUnobstructed(uid, actor.Patient) ||
            !_blocker.CanInteract(uid, actor.Patient) || !TryComp<DoAfterComponent>(uid, out var doAfter) ||
            !TryComp<DamageableComponent>(actor.Patient, out var damage) || !CanTreat(uid, actor.Treatment, actor.Patient, damage))
            return HTNOperatorStatus.Failed;
        var next = doAfter.NextId;
        var solution = TryComp<InjectorComponent>(actor.Treatment, out var injector) &&
            _medicalSolutions.TryGetSolution(actor.Treatment, injector.SolutionName, out _, out var medicine) ? medicine : null;
        if (injector != null && !_medicalInjectors.TrySetTransferAmount((actor.Treatment, injector), FixedPoint2.New(5)))
            return HTNOperatorStatus.Failed;
        var volume = solution?.Volume;
        _combat.SetInCombatMode(uid, false);
        if (uid == actor.Patient)
            _interaction.UseInHandInteraction(uid, actor.Treatment);
        else
            _interaction.UserInteraction(uid, Transform(actor.Patient).Coordinates, actor.Patient);
        if (volume != null && solution != null && solution.Volume < volume.Value)
            return HTNOperatorStatus.Finished;
        if (doAfter.NextId == next)
            return HTNOperatorStatus.Failed;
        actor.HealingDoAfter = next;
        return HTNOperatorStatus.Continuing;
    }

    public void EndTreatment(EntityUid uid)
    {
        if (!TryComp<MilitiaOperationActorComponent>(uid, out var actor) ||
            !actor.Patient.IsValid() && !actor.Treatment.IsValid() && actor.HealingDoAfter == null)
            return;
        _squadHostileQueries.Clear();
        var patient = actor.Patient;
        if (_treatmentReservations.TryGetValue(patient, out var medics))
        {
            medics.Remove(uid);
            if (medics.Count == 0)
                _treatmentReservations.Remove(patient);
        }
        var healing = actor.HealingDoAfter;
        actor.HealingDoAfter = null;
        actor.Patient = EntityUid.Invalid;
        actor.Treatment = EntityUid.Invalid;
        actor.TreatmentPath = null;
        actor.MedicalDashExposure = 0f;
        actor.MedicalDashPosition = default;
        actor.MedicalDashExposed = false;
        actor.SquadRouteIndex = 0;
        EndRoutePry(uid);
        _steering.Unregister(uid);
        actor.NextTreatment = _timing.CurTime + actor.TreatmentCooldown;
        try
        {
            if (healing is { } current && _doAfter.GetStatus(uid, current, null) == DoAfterStatus.Running)
                _doAfter.Cancel(uid, current, null);
        }
        finally
        {
            if (patient != uid && TryComp<MilitiaOperationActorComponent>(patient, out var target) && target.TreatingMedic == uid)
            {
                target.TreatingMedic = EntityUid.Invalid;
                if (!TerminatingOrDeleted(patient) && target.Patient != patient)
                    InterruptActor(patient);
            }
            if (!TerminatingOrDeleted(uid) && !MetaData(uid).EntityPaused)
                ArmActor(uid);
        }
    }
}

public sealed record MilitiaMedicalRefill(EntityUid Injector, EntityUid Bottle, MilitiaEquipmentRollback Equipment)
{
    public ushort? DoAfter;
    public FixedPoint2 Volume;
}
