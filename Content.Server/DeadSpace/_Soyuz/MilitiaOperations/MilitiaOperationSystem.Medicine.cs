// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Server.NPC.HTN;
using Content.Shared.Body.Components;
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
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.MilitiaOperations;

public sealed partial class MilitiaOperationSystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _medicalSolutions = default!;
    [Dependency] private readonly MobThresholdSystem _medicalThresholds = default!;
    [Dependency] private readonly InjectorSystem _medicalInjectors = default!;
    private static readonly ProtoId<ReagentPrototype> MilitiaMedicine = "Omnizine";
    private static readonly ProtoId<ReagentPrototype> MilitiaCombatMedicine = "Derytracine";
    private static readonly ProtoId<ReagentPrototype>[] MilitiaMedicines = { MilitiaMedicine, MilitiaCombatMedicine };
    private static readonly ProtoId<InjectorModePrototype> MilitiaInjectionMode = "HyposprayInjectMode";
    private static readonly ProtoId<InjectorModePrototype> MilitiaRefillMode = "HyposprayDynamicMode";

    private static bool IsMedicineSolution(Solution solution)
        => solution.Volume > 0 && solution.GetTotalPrototypeQuantity(MilitiaMedicine) +
            solution.GetTotalPrototypeQuantity(MilitiaCombatMedicine) == solution.Volume;

    private static bool MedicineUseful(ProtoId<ReagentPrototype> reagent, DamageableComponent damage)
        => damage.TotalDamage > 0 && (reagent == MilitiaMedicine ||
            new[] { "Piercing", "Heat", "Cold", "Shock" }.Any(type => damage.Damage.DamageDict.TryGetValue(type, out var amount) && amount > 0));

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
        foreach (var item in actor.Treatments)
        {
            if (!OwnsItem(uid, item) || !TryComp<InjectorComponent>(item, out var component) ||
                !SupportsMedicalRefill(component) ||
                !_medicalSolutions.TryGetSolution(item, component.SolutionName, out _, out var solution) || solution.AvailableVolume < 5 ||
                solution.Volume > 0 && !IsMedicineSolution(solution))
                continue;
            foreach (var supply in actor.MedicineBottles)
            {
                if (!OwnsItem(uid, supply) || !_medicalSolutions.TryGetDrawableSolution(supply, out _, out var stock) || stock.Volume < 5 ||
                    !IsMedicineSolution(stock))
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
                RestoreEquipment(uid, equipment.Items, equipment.ActiveHand, equipment.Wielded);
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
        if (solution.Volume >= 5 && HasTriageWork(uid, actor))
            return HTNOperatorStatus.Finished;
        if (solution.AvailableVolume < 5 || !_medicalSolutions.TryGetDrawableSolution(refill.Bottle, out _, out var stock) || stock.Volume < 5)
            return solution.Volume >= 5 ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
        if (!IsMedicineSolution(stock) || solution.Volume > 0 && !IsMedicineSolution(solution) ||
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
                refill.Equipment.Wielded.Where(item => items.Any(snapshot => snapshot.Item == item)).ToArray());
        }
    }

    public List<(EntityUid Patient, EntityUid Item)> TreatmentCandidates(EntityUid uid)
    {
        var result = new List<(EntityUid, EntityUid)>();
        if (!AvailableActor(uid, out var actor) || actor.MedicalRefill != null || actor.Patient.IsValid() || actor.DoorPry != null || ReceivingTreatment(uid) || _timing.CurTime < actor.NextTreatment)
            return result;
        if (!actor.Medic && NeedsMedic(uid, actor) && FindMedic(uid).IsValid())
            return result;
        var patients = new List<EntityUid>();
        var query = EntityQueryEnumerator<MilitiaOperationActorComponent>();
        while (query.MoveNext(out var patient, out _))
            patients.Add(patient);
        foreach (var patient in patients.OrderByDescending(p => TryComp<DamageableComponent>(p, out var damage) ? damage.TotalDamage : 0))
        {
            if ((!actor.Medic && patient != uid) || !ValidPatient(uid, patient, actor) ||
                !TryComp<DamageableComponent>(patient, out var damage) ||
                damage.TotalDamage < actor.TreatmentThreshold &&
                (!TryComp<BloodstreamComponent>(patient, out var blood) || blood.BleedAmount <= 0))
                continue;
            var bleeding = TryComp<BloodstreamComponent>(patient, out var bloodstream) && bloodstream.BleedAmount > 0;
            foreach (var item in actor.Treatments.OrderBy(item => bleeding && HasComp<InjectorComponent>(item) ? 1 : 0))
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
            useful |= current < 2 && MedicineUseful(reagent, damage);
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
        var injector = actor.Treatments.FirstOrDefault(item => OwnsItem(uid, item) && HasComp<InjectorComponent>(item));
        return actor.Medic && injector.IsValid() && actor.MedicineBottles.Any(item => OwnsItem(uid, item) &&
            _medicalSolutions.TryGetDrawableSolution(item, out _, out var stock) && stock.Volume >= 5 &&
            IsMedicineSolution(stock) && CanReceiveMedicine(uid, injector, patient, stock, damage));
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

    private EntityUid FindMedic(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damage))
            return EntityUid.Invalid;
        var nearest = EntityUid.Invalid;
        var distance = float.PositiveInfinity;
        var medics = EntityQueryEnumerator<MilitiaOperationActorComponent>();
        while (medics.MoveNext(out var other, out var medic))
        {
            if (other == uid || !medic.Medic || !SquadMember(other) || EntityManager.IsQueuedForDeletion(other) || !_mobs.IsAlive(other) ||
                TryComp<StaminaComponent>(other, out var stamina) && stamina.Critical ||
                !_blocker.CanMove(other) || !_blocker.CanInteract(other, uid) ||
                !SameOperation(uid, other) || Transform(uid).MapID != Transform(other).MapID ||
                !OnDefensiveGrid(uid, Transform(other).Coordinates) ||
                !HasTreatment(other, medic, uid, damage))
                continue;
            var squared = System.Numerics.Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(other));
            if (squared >= distance)
                continue;
            nearest = other;
            distance = squared;
        }
        return nearest;
    }

    private bool InTreatmentArea(EntityUid uid, EntityUid patient, MilitiaOperationActorComponent actor)
        => Transform(uid).MapID == Transform(patient).MapID &&
           System.Numerics.Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(patient)) <=
               actor.TreatmentRange * actor.TreatmentRange &&
           (!actor.DefensivePosition.IsValid(EntityManager) ||
            System.Numerics.Vector2.DistanceSquared(_transform.ToMapCoordinates(actor.DefensivePosition).Position,
                _transform.GetWorldPosition(patient)) <= actor.DefensiveRange * actor.DefensiveRange);

    private bool ValidPatient(EntityUid uid, EntityUid patient, MilitiaOperationActorComponent actor)
    {
        if (!Exists(patient) || TerminatingOrDeleted(patient) || _mobs.IsDead(patient) || HasPlayerMind(patient) ||
            MetaData(uid).EntityPaused || MetaData(patient).EntityPaused ||
            !TryComp<HTNComponent>(uid, out var medicHtn) || !medicHtn.Enabled ||
            !TryComp<HTNComponent>(patient, out var patientHtn) || !patientHtn.Enabled ||
            !SafeForTreatment(uid) || !SafeForTreatment(patient) ||
            !HasComp<MilitiaOperationActorComponent>(patient) ||
            !SameOperation(uid, patient) ||
            (!actor.Medic && patient != uid) || !InTreatmentArea(uid, patient, actor))
            return false;
        var medics = EntityQueryEnumerator<MilitiaOperationActorComponent>();
        while (medics.MoveNext(out var other, out var otherActor))
        {
            if (other != uid && otherActor.Patient == patient && AvailableActor(other, out _))
                return false;
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
        if (!AvailableActor(uid, out var actor) || ReceivingTreatment(uid) || !ValidPatient(uid, patient, actor) ||
            !TreatmentPathSafe(uid, patient, path) || !OwnsItem(uid, item) || !TryComp<DamageableComponent>(patient, out var damage) ||
            !CanTreat(uid, item, patient, damage) || !EquipOwnedItem(uid, actor, item, false))
            return false;
        actor.Patient = patient;
        actor.Treatment = item;
        actor.TreatmentPath = path;
        actor.SquadRouteIndex = 0;
        var committed = false;
        try
        {
            EndGuard(uid);
            if (patient != uid)
            {
                Comp<MilitiaOperationActorComponent>(patient).TreatingMedic = uid;
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
        var patient = actor.Patient;
        var healing = actor.HealingDoAfter;
        actor.HealingDoAfter = null;
        actor.Patient = EntityUid.Invalid;
        actor.Treatment = EntityUid.Invalid;
        actor.TreatmentPath = null;
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
                if (!TerminatingOrDeleted(patient))
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
