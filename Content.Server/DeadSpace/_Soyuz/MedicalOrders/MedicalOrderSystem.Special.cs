// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Shared.Damage.Components;
using Content.Shared.DeadSpace._Soyuz.MedicalOrders;
using Content.Shared.FixedPoint;

namespace Content.Server.DeadSpace._Soyuz.MedicalOrders;

public sealed partial class MedicalOrderSystem
{
    private void RefreshSpecialOffers(MedicalOrderStationComponent state, MedicalOrderConfigPrototype config)
    {
        var offers = new Dictionary<int, MedicalSpecialOffer>();
        var weight = config.SpecialDifficulties.Sum(d => d.Weight);
        for (var i = 0; i < config.SpecialOfferCount; i++)
        {
            var terms = SelectWeighted(config.SpecialDifficulties, d => d.Weight, _random.Next(weight));
            var scenario = config.SpecialScenarios[_random.Next(config.SpecialScenarios.Count)];
            var offer = new MedicalSpecialOffer
            {
                RuntimeId = state.NextSpecialRuntimeId++,
                Difficulty = config.SpecialDifficulties.IndexOf(terms),
                Terms = terms,
                Title = scenario.Title,
                Description = scenario.Description,
                Site = config.SpecialSites[_random.Next(config.SpecialSites.Count)],
            };
            var patientCount = _random.Next(terms.MinPatients, terms.MaxPatients + 1);
            var difficulties = config.Difficulties.Skip(terms.MinPatientDifficulty - 1)
                .Take(terms.MaxPatientDifficulty - terms.MinPatientDifficulty + 1).ToArray();
            var patientWeight = difficulties.Sum(d => d.Weight);
            for (var p = 0; p < patientCount; p++)
            {
                var definition = SelectWeighted(difficulties, d => d.Weight, _random.Next(patientWeight));
                var patient = GeneratePatientOffer(state, config, config.Difficulties.IndexOf(definition),
                    allocateRuntimeId: false);
                if (patient == null)
                    break;
                patient.RuntimeId = p + 1;
                offer.Patients.Add(patient);
            }
            if (offer.Patients.Count != patientCount)
                continue;

            var remaining = _random.Next(terms.MinReagents, terms.MaxReagents + 1);
            var futureMin = terms.Reagents.Values.Sum(q => q.Minimum);
            var futureMax = terms.Reagents.Values.Sum(q => q.Maximum);
            foreach (var (tier, quota) in terms.Reagents)
            {
                futureMin -= quota.Minimum;
                futureMax -= quota.Maximum;
                var count = _random.Next(Math.Max(quota.Minimum, remaining - futureMax),
                    Math.Min(quota.Maximum, remaining - futureMin) + 1);
                remaining -= count;
                var pool = config.MarketReagents.Where(r => r.Enabled && r.Difficulty == tier).ToList();
                for (var r = 0; r < count; r++)
                {
                    var index = _random.Next(pool.Count);
                    var reagent = pool[index];
                    pool.RemoveAt(index);
                    var volume = _random.Next(terms.MinVolume / terms.VolumeStep,
                        terms.MaxVolume / terms.VolumeStep + 1) * terms.VolumeStep;
                    offer.Reagents.Add(new MedicalSpecialReagent { ID = reagent.Reagent.Id, Amount = volume });
                    offer.ChemicalPoints += volume * GetMarketBasePrice(config, reagent);
                    offer.ChemicalReputation += volume * GetMarketBaseReputation(config, reagent);
                }
            }
            var patientReputation = offer.Patients.Sum(p => (long) config.Difficulties[p.Difficulty].BaseReputation);
            offer.Reputation = Math.Min(terms.ReputationCap,
                (offer.ChemicalReputation + patientReputation) * terms.ReputationPercent / 100m + terms.ReputationBonus);
            offers.Add(offer.RuntimeId, offer);
        }
        state.SpecialOffers.Clear();
        foreach (var (id, offer) in offers)
            state.SpecialOffers.Add(id, offer);
        state.NextSpecialRefresh = _timing.CurTime + config.SpecialRefreshInterval;
    }

    private MedicalSpecialActive? GetActiveSpecial(EntityUid machine, EntityUid actor,
        MedicalOrderStationComponent state, int runtimeId)
    {
        if (!state.Busy && state.SpecialActive is { } active && active.Offer.RuntimeId == runtimeId)
        {
            if (_timing.CurTime < active.Deadline)
                return active;
            FinalizeSpecial(state, active, expired: true);
        }
        Reject(machine, actor, "medical-orders-error-unavailable");
        return null;
    }

    private void TryAcceptSpecial(EntityUid station, EntityUid receiver, EntityUid actor,
        MedicalOrderStationComponent state, MedicalOrderConfigPrototype config, int runtimeId)
    {
        if (state.Busy || state.SpecialActive != null || state.PatientActive != null || state.PatientAccepting ||
            _timing.CurTime >= state.NextSpecialRefresh ||
            !state.SpecialOffers.TryGetValue(runtimeId, out var offer))
        {
            Reject(receiver, actor, "medical-orders-error-unavailable");
            return;
        }
        var active = new MedicalSpecialActive { Offer = offer, Deadline = _timing.CurTime + offer.Terms.TimeLimit };
        for (var i = 0; i < offer.Patients.Count; i++)
            active.Patients.Add(new MedicalSpecialPatient());

        MedicalOrderActive? prepared = null;
        state.Busy = true;
        try
        {
            prepared = PreparePatient(station, receiver, actor, config, offer.Patients[0], active.Deadline, runtimeId);
            if (prepared == null)
                return;
            if (state.SpecialActive != null || state.PatientActive != null || state.PatientAccepting ||
                _timing.CurTime >= state.NextSpecialRefresh ||
                !state.SpecialOffers.Remove(runtimeId))
                return;
            active.Patients[0].Active = prepared;
            state.SpecialActive = active;
            prepared = null;
        }
        finally
        {
            try
            {
                DiscardPreparedPatient(prepared);
            }
            finally
            {
                state.Busy = false;
            }
        }
    }

    private void TryIssueSpecialPatient(EntityUid station, EntityUid receiver, EntityUid actor,
        MedicalOrderStationComponent state, MedicalOrderConfigPrototype config, int runtimeId)
    {
        var active = GetActiveSpecial(receiver, actor, state, runtimeId);
        if (active == null)
            return;
        var index = active.Patients.FindIndex(p => p.Active == null);
        if (index < 0)
        {
            Reject(receiver, actor, "medical-orders-error-unavailable");
            return;
        }
        MedicalOrderActive? prepared = null;
        state.Busy = true;
        try
        {
            prepared = PreparePatient(station, receiver, actor, config, active.Offer.Patients[index],
                active.Deadline, runtimeId);
            if (prepared == null || state.SpecialActive != active || _timing.CurTime >= active.Deadline ||
                active.Patients[index].Active != null)
                return;
            active.Patients[index].Active = prepared;
            prepared = null;
        }
        finally
        {
            try
            {
                DiscardPreparedPatient(prepared);
            }
            finally
            {
                state.Busy = false;
            }
        }
    }

    private void TrySubmitSpecialPatient(EntityUid station, EntityUid sender, EntityUid actor,
        MedicalOrderStationComponent state, MedicalOrderConfigPrototype config, int runtimeId)
    {
        var active = GetActiveSpecial(sender, actor, state, runtimeId);
        if (active == null)
            return;
        var error = "medical-orders-error-no-patient";
        var patient = active.Patients.FirstOrDefault(p => !p.Submitted && p.Active is { } issued &&
            CanSubmitPatient(station, sender, issued, config, out error, runtimeId));
        if (patient?.Active is not { } body)
        {
            Reject(sender, actor, error);
            return;
        }
        state.Busy = true;
        try
        {
            patient.Score = Math.Max(0, GetScore(body, config));
            patient.Submitted = true;
            if (body.Patient is { } uid && Exists(uid))
                QueueDel(uid);
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Evacuating special patient {runtimeId} failed (submitted: {patient.Submitted}): {exception}");
        }
        finally
        {
            state.Busy = false;
        }
        FinalizeSpecial(state, active);
    }

    private void TrySupplySpecialReagents(EntityUid station, EntityUid terminal, EntityUid actor,
        MedicalOrderStationComponent state, int runtimeId)
    {
        var active = GetActiveSpecial(terminal, actor, state, runtimeId);
        if (active == null)
            return;
        state.Busy = true;
        var committed = false;
        try
        {
            if (!TryGetMedicalBeaker(terminal, out var solutionEntity, out var solution))
            {
                Reject(terminal, actor, "medical-orders-error-no-beaker");
                return;
            }
            var remaining = solution.Clone();
            remaining.Name = solution.Name;
            var submitted = new Dictionary<string, FixedPoint2>(active.Submitted);
            var supplied = false;
            foreach (var line in active.Offer.Reagents)
            {
                submitted.TryGetValue(line.ID, out var previous);
                var needed = FixedPoint2.Max(FixedPoint2.Zero, FixedPoint2.New(line.Amount) - previous);
                var amount = FixedPoint2.Min(needed, remaining.GetTotalPrototypeQuantity(line.ID));
                if (amount <= FixedPoint2.Zero)
                    continue;
                var removed = remaining.RemoveReagent(line.ID, amount, ignoreReagentData: true);
                if (removed <= FixedPoint2.Zero)
                    continue;
                submitted[line.ID] = previous + removed;
                supplied = true;
            }
            if (!supplied)
            {
                Reject(terminal, actor, "medical-special-no-reagents");
                return;
            }
            if (state.SpecialActive != active || _timing.CurTime >= active.Deadline ||
                _stations.GetOwningStation(terminal) != station)
                return;

            solutionEntity.Value.Comp.Solution = remaining;
            active.Submitted = submitted;
            committed = true;
            _solutions.UpdateChemicals(solutionEntity.Value);
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Special reagent supply {runtimeId} failed (committed: {committed}): {exception}");
            if (!committed)
                Reject(terminal, actor, "medical-orders-error-unavailable");
        }
        finally
        {
            state.Busy = false;
        }
        if (committed)
            FinalizeSpecial(state, active);
    }

    private static bool IsSpecialComplete(MedicalSpecialActive active)
    {
        return active.Patients.All(p => p.Submitted) && active.Offer.Reagents.All(r =>
            active.Submitted.TryGetValue(r.ID, out var amount) && amount >= FixedPoint2.New(r.Amount));
    }

    private bool FinalizeSpecial(MedicalOrderStationComponent state, MedicalSpecialActive active, bool expired = false)
    {
        if (state.Busy || state.SpecialActive != active)
            return false;
        expired |= _timing.CurTime >= active.Deadline;
        if (!expired && !IsSpecialComplete(active))
            return false;

        state.Busy = true;
        try
        {
            var offer = active.Offer;
            var terms = offer.Terms;
            long points;
            long reputation;
            var pointRemainder = state.SpecialPointRemainder;
            var reputationRemainder = state.SpecialReputationRemainder;
            if (expired)
            {
                points = (long) decimal.Floor((decimal) state.Points * terms.PenaltyPercent / 100m);
                reputation = (long) decimal.Floor((decimal) state.Reputation * terms.PenaltyPercent / 100m);
                pointRemainder *= (100 - terms.PenaltyPercent) / 100m;
                reputationRemainder *= (100 - terms.PenaltyPercent) / 100m;
            }
            else
            {
                var totalPoints = offer.ChemicalPoints + active.Patients.Sum(p => (long) p.Score) +
                    terms.PointBonus + pointRemainder;
                var totalReputation = offer.Reputation + reputationRemainder;
                points = (long) Math.Min(decimal.Floor(totalPoints), long.MaxValue - state.Points);
                reputation = (long) Math.Min(decimal.Floor(totalReputation), long.MaxValue - state.Reputation);
                pointRemainder = totalPoints - decimal.Floor(totalPoints);
                reputationRemainder = totalReputation - decimal.Floor(totalReputation);
            }
            var result = new MedicalSpecialResultView(offer.Title, offer.Site, expired, points, reputation);
            state.Points = expired ? state.Points - points : state.Points + points;
            state.Reputation = expired ? state.Reputation - reputation : state.Reputation + reputation;
            state.SpecialPointRemainder = pointRemainder;
            state.SpecialReputationRemainder = reputationRemainder;
            state.LastSpecial = result;
            state.SpecialActive = null;

            RemoveSpecialPatientMarkers(active);
            return true;
        }
        finally
        {
            state.Busy = false;
        }
    }

    private void RemoveSpecialPatientMarkers(MedicalSpecialActive active)
    {
        foreach (var patient in active.Patients)
        {
            if (patient.Active?.Patient is not { } uid || !Exists(uid) || Terminating(uid))
                continue;
            try
            {
                if (TryComp<MedicalOrderPatientComponent>(uid, out var marker) &&
                    marker.SpecialRuntimeId == active.Offer.RuntimeId)
                    RemComp<MedicalOrderPatientComponent>(uid);
            }
            catch (Exception exception)
            {
                _sawmill.Error($"Cleaning up special patient {uid} failed: {exception}");
            }
        }
    }

    private MedicalSpecialContractView SpecialView(EntityUid machine, MedicalOrderMachineKind kind,
        MedicalSpecialOffer offer, MedicalSpecialActive? active, MedicalOrderConfigPrototype config)
    {
        var patients = new MedicalSpecialPatientView[offer.Patients.Count];
        for (var i = 0; i < patients.Length; i++)
        {
            var progress = active?.Patients[i];
            var issued = progress?.Active;
            var status = progress?.Submitted == true ? MedicalSpecialPatientStatus.Submitted :
                issued == null ? MedicalSpecialPatientStatus.Pending : IsPatientMissing(issued)
                    ? MedicalSpecialPatientStatus.Lost : MedicalSpecialPatientStatus.Issued;
            float? damage = null;
            if (issued?.Patient is { } uid && TryComp<DamageableComponent>(uid, out var damageable))
                damage = damageable.TotalDamage.Float();
            var score = progress?.Submitted == true ? progress.Score : issued == null ? 0 : GetScore(issued, config);
            patients[i] = new MedicalSpecialPatientView(View(offer.Patients[i], issued, score, config),
                status, damage, kind == MedicalOrderMachineKind.PatientSender &&
                status == MedicalSpecialPatientStatus.Issued && issued != null &&
                CanSubmitPatient(_stations.GetOwningStation(machine) ?? EntityUid.Invalid, machine,
                    issued, config, out _, offer.RuntimeId), issued?.PatientName ?? string.Empty);
        }
        var reagents = offer.Reagents.Select(r => new MedicalOrderLineView(r.ID, r.Amount,
            active != null && active.Submitted.TryGetValue(r.ID, out var amount) ? amount.Float() : 0, 0)).ToArray();
        var maximum = (long) decimal.Floor(offer.ChemicalPoints + offer.Patients.Sum(p => (long) p.MaximumScore) +
            offer.Terms.PointBonus);
        return new MedicalSpecialContractView(offer.RuntimeId, offer.Difficulty, offer.Title, offer.Description,
            offer.Site, offer.Terms.TimeLimit, active?.Deadline, maximum, (int) decimal.Floor(offer.Reputation),
            offer.Terms.PenaltyPercent, reagents, patients,
            kind == MedicalOrderMachineKind.PatientReceiver && CanReceivePatient(machine) &&
            (active == null || active.Patients.Any(p => p.Active == null)));
    }
}
