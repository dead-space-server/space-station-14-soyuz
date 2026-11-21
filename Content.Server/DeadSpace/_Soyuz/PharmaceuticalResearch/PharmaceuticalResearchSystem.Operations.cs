// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Globalization;
using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DeadSpace._Soyuz.PharmaceuticalResearch;
using Content.Shared.FixedPoint;
using Content.Shared.Paper;
using Robust.Shared.Containers;

namespace Content.Server.DeadSpace._Soyuz.PharmaceuticalResearch;

public sealed partial class PharmaceuticalResearchSystem
{
    private void Complete(EntityUid uid, LaboratorySession state)
    {
        var operation = state.Operation!;
        if (Journal(uid) != operation.Journal)
        {
            Finish(uid, state, operation.Action == PharmaceuticalAction.Assay ? "pharma-error-assay-interrupted" : "pharma-error-changed");
            return;
        }
        if (operation.Action == PharmaceuticalAction.Assay)
        {
            CompleteAssay(uid, state, operation);
            return;
        }
        if (!Sample(uid, InputSlot, out var input, out var liquid) || input.Owner != operation.Input || !Unchanged(liquid, operation.OriginalInput))
        {
            Finish(uid, state, "pharma-error-changed");
            return;
        }
        if (operation.Action == PharmaceuticalAction.Analyze)
        {
            var report = Analyze(liquid, operation.Journal, out var studies);
            var analysisEntry = Loc.GetString("pharma-journal-analysis", ("author", operation.Author)) + "\n" + report;
            state.Operation = null;
            foreach (var reagent in liquid.Contents)
                operation.Journal.Analyzed.Add(reagent.Reagent.Prototype);
            foreach (var study in studies)
                operation.Journal.Studies[study.Id] = study;
            operation.Journal.Add(analysisEntry);
            state.Analysis = report;
            Finish(uid, state, "pharma-operation-complete");
            return;
        }
        if (operation.Action == PharmaceuticalAction.Register)
        {
            Register(uid, state, operation, liquid);
            return;
        }
        var remaining = liquid.Clone();
        remaining.Name = liquid.Name;
        Entity<SolutionComponent>? output = null;
        Solution? result = null;
        if (operation.Output != null)
        {
            if (!Sample(uid, OutputSlot, out var target, out var targetLiquid) || target.Owner != operation.Output ||
                operation.OriginalOutput == null || !Unchanged(targetLiquid, operation.OriginalOutput))
            {
                Finish(uid, state, "pharma-error-changed");
                return;
            }
            output = target;
            result = targetLiquid.Clone();
            result.Name = targetLiquid.Name;
        }
        PharmaceuticalFormula? published = null;
        var quality = 0;
        var reason = "pharma-operation-complete";
        switch (operation.Action)
        {
            case PharmaceuticalAction.Synthesize:
            {
                var chosen = operation.Ingredients.ToHashSet(StringComparer.Ordinal);
                var components = remaining.Contents.Where(r => chosen.Contains(r.Reagent.Prototype)).ToArray();
                if (chosen.Count < 2 || components.Length != chosen.Count || components.Any(r => r.Reagent.Data is { Count: > 0 }) ||
                    chosen.Any(id => !operation.Journal.Analyzed.Contains(id)))
                {
                    Finish(uid, state, "pharma-error-composition");
                    return;
                }
                var mixture = new Solution(components);
                var portion = mixture.SplitSolution(FixedPoint2.Min(operation.Amount, mixture.Volume));
                if (!Model.TryDevelop(portion, out var formula, out quality, out var yield, out reason))
                {
                    Finish(uid, state, reason);
                    return;
                }
                var quantity = FixedPoint2.FromCents((int) (portion.Volume.Value * yield));
                if (quantity <= 0 || result!.AvailableVolume < quantity)
                {
                    Finish(uid, state, "pharma-error-capacity");
                    return;
                }
                foreach (var reagent in portion.Contents)
                    remaining.RemoveReagent(reagent.Reagent, reagent.Quantity);
                var registered = operation.Journal.Formulas.TryGetValue(formula.Id, out var record) && record.Registered;
                AddProcessed(result!, new ReagentId(PharmaceuticalResearchModel.Product, new List<ReagentData>
                {
                    new PharmaceuticalData
                    {
                        Formula = formula.Id, Quality = quality, SourceQuality = quality, SourceTemperature = operation.Temperature,
                        Label = registered ? record!.Name : formula.Id, Registered = registered, Tint = registered ? formula.Tint : null,
                    },
                }), quantity, operation.Temperature);
                AddProcessed(remaining, new ReagentId(PharmaceuticalResearchModel.Waste, null), portion.Volume - quantity, operation.Temperature);
                published = formula;
                reason = "pharma-synthesis-complete";
                break;
            }
            case PharmaceuticalAction.Reproduce:
                if (!Reproduce(operation, remaining, result!, out published, out quality, out reason))
                {
                    Finish(uid, state, reason);
                    return;
                }
                reason = "pharma-synthesis-complete";
                break;
            case PharmaceuticalAction.Separate:
            {
                if (!TryProduct(liquid, out var data, out _) || data.Quality >= 99 || data.Quality <= 0)
                {
                    Finish(uid, state, "pharma-error-purify");
                    return;
                }
                var selected = remaining.Contents[0];
                var amount = FixedPoint2.Min(operation.Amount, selected.Quantity);
                var nextQuality = Math.Min(99, data.Quality + Math.Max(1, (100 - data.Quality) / 2));
                var quantity = FixedPoint2.FromCents((int) ((long) amount.Value * data.Quality / nextQuality));
                if (quantity <= 0 || result!.AvailableVolume < quantity)
                {
                    Finish(uid, state, "pharma-error-capacity");
                    return;
                }
                var purified = (PharmaceuticalData) data.Clone();
                purified.Quality = nextQuality;
                remaining.RemoveReagent(selected.Reagent, amount);
                AddProcessed(result!, new ReagentId(PharmaceuticalResearchModel.Product, new List<ReagentData> { purified }), quantity, operation.Temperature);
                AddProcessed(remaining, new ReagentId(PharmaceuticalResearchModel.Waste, null), amount - quantity, operation.Temperature);
                reason = "pharma-separation-complete";
                break;
            }
            case PharmaceuticalAction.Dispose:
                remaining.RemoveReagent(PharmaceuticalResearchModel.Waste, remaining.Volume, ignoreReagentData: true);
                break;
            default:
                Finish(uid, state, "pharma-error-selection");
                return;
        }
        if (remaining.Volume > remaining.MaxVolume || remaining.Contents.Count > MaximumComponents ||
            result != null && (result.Volume > result.MaxVolume || result.Contents.Count > MaximumComponents))
        {
            Finish(uid, state, "pharma-error-capacity");
            return;
        }
        var journalEntry = published != null
            ? Loc.GetString("pharma-journal-synthesis", ("author", operation.Author), ("formula", published.Id), ("quality", Number(quality)))
            : Loc.GetString("pharma-journal-operation", ("author", operation.Author),
                ("action", Loc.GetString("pharma-action-" + operation.Action.ToString().ToLowerInvariant())));
        var registration = new PharmaceuticalRegistration { Temperature = operation.Temperature, Quality = quality, SourceQuality = quality };
        input.Comp.Solution = remaining;
        if (output is { } destination && result != null)
            destination.Comp.Solution = result;
        state.Operation = null;
        if (published != null)
        {
            Model.Publish(published);
            operation.Journal.Formulas.TryAdd(published.Id, registration);
        }
        operation.Journal.Add(journalEntry);
        var notified = output is { } outputEntity ? NotifySolutions(input, outputEntity) : NotifySolutions(input);
        Finish(uid, state, notified ? reason : "pharma-error-notification");
    }

    private bool TryProduct(Solution sample, out PharmaceuticalData data, out PharmaceuticalFormula formula)
    {
        data = default!;
        formula = default!;
        if (sample.Contents.Count != 1 || sample.Contents[0].Reagent.Prototype != PharmaceuticalResearchModel.Product ||
            sample.Contents[0].Reagent.Data?.OfType<PharmaceuticalData>().FirstOrDefault() is not { } found ||
            !Model.Formulas.TryGetValue(found.Formula, out var definition))
            return false;
        data = found;
        formula = definition;
        return true;
    }

    private bool StartAssay(EntityUid uid, LaboratorySession state, LaboratoryOperation operation)
    {
        if (!TryProduct(operation.OriginalInput, out var data, out _) || !Sample(uid, InputSlot, out var input, out var sample) ||
            input.Owner != operation.Input || !Unchanged(sample, operation.OriginalInput) || sample.Volume < 1)
            return false;
        var remaining = sample.Clone();
        remaining.Name = sample.Name;
        var portion = remaining.SplitSolution(FixedPoint2.New(1));
        var container = _containers.EnsureContainer<Container>(uid, "biomodels");
        var body = Spawn("SoyuzPharmaceuticalBiomodel", Transform(uid).Coordinates);
        state.Models.Add(body);
        if (!_containers.Insert(body, container) || !_solutions.TryGetSolution(body, "chemicals", out var solution, out _))
            return false;
        var initial = new DamageSpecifier();
        foreach (var type in _prototypes.EnumeratePrototypes<DamageTypePrototype>())
            initial.DamageDict[type.ID] = FixedPoint2.New(40);
        if (!_damage.TryChangeDamage(body, initial, ignoreResistances: true))
            return false;
        state.Baselines[body] = new Dictionary<string, FixedPoint2>(Comp<DamageableComponent>(body).Damage.DamageDict);
        var trial = solution.Value.Comp.Solution.Clone();
        trial.Name = solution.Value.Comp.Solution.Name;
        trial.AddSolution(portion, _prototypes);
        if (Deleted(uid) || Terminating(uid) || operation.Interrupted || !this.IsPowered(uid, EntityManager) ||
            Journal(uid) != operation.Journal || state.Operation != operation || !Sample(uid, InputSlot, out var current, out var currentSample) ||
            current.Owner != operation.Input || !Unchanged(currentSample, operation.OriginalInput) || Deleted(body) || Terminating(body))
            return false;
        operation.Selection = data.Formula;
        current.Comp.Solution = remaining;
        solution.Value.Comp.Solution = trial;
        NotifySolutions(current, solution.Value);
        return true;
    }

    private void CompleteAssay(EntityUid uid, LaboratorySession state, LaboratoryOperation operation)
    {
        if (state.Models.Count != 1 || !_containers.TryGetContainer(uid, "biomodels", out var container))
        {
            Finish(uid, state, "pharma-error-assay");
            return;
        }
        var body = state.Models[0];
        if (Deleted(body) || Terminating(body) || !container.Contains(body) || !TryComp<DamageableComponent>(body, out var damage) ||
            !state.Baselines.TryGetValue(body, out var baseline) || !TryProduct(operation.OriginalInput, out var data, out var formula))
        {
            Finish(uid, state, "pharma-error-assay");
            return;
        }
        if (!_solutions.TryGetSolution(body, "chemicals", out _, out var trial))
        {
            Finish(uid, state, "pharma-error-assay");
            return;
        }
        if (trial.Volume > 0)
        {
            if ((_timing.CurTime - state.Started).TotalSeconds >= 120)
                Finish(uid, state, "pharma-error-assay");
            else
                state.Ends = _timing.CurTime + TimeSpan.FromSeconds(0.5);
            return;
        }
        var changes = damage.Damage.DamageDict.ToDictionary(p => p.Key, p => p.Value - baseline.GetValueOrDefault(p.Key));
        var effective = formula.Healing.Keys.Any(type => changes.GetValueOrDefault(type) < 0) ||
            formula.Effects.Any(inherited => inherited.Effect.Probability > 0) ||
            formula.Parts.Keys.Any(ingredient => _prototypes.Index<ReagentPrototype>(ingredient).ReactiveEffects is { Count: > 0 });
        var acceptable = effective && changes.GetValueOrDefault("Poison") <= FixedPoint2.New(1);
        var toxic = changes.GetValueOrDefault("Poison") > FixedPoint2.New(1) || ImpurityToxicity(formula, data.Quality) > 0.6f;
        var passed = acceptable && data.Quality >= 70 && !toxic;
        var verdict = !effective ? "pharma-verdict-ineffective" : toxic ? "pharma-verdict-toxic" :
            !passed ? "pharma-verdict-impure" : "pharma-verdict-passed";
        var findings = UnitReport(changes.ToDictionary(p => p.Key, p => p.Value.Float()), data.Quality, formula.Overdose) +
            FormulaEffects(formula, data) + "\n" + Loc.GetString(verdict);
        var view = ReagentView(operation.OriginalInput.Contents[0].Reagent, operation.Journal);
        view.Properties = findings;
        view.Tested = true;
        view.Passed = passed;
        var journalEntry = Loc.GetString("pharma-journal-assay", ("author", operation.Author), ("formula", data.Formula)) + "\n" + findings;
        state.Operation = null;
        operation.Journal.Formulas.TryAdd(formula.Id, new PharmaceuticalRegistration());
        var record = operation.Journal.Formulas[formula.Id];
        if (!record.Registered)
        {
            record.Tested = acceptable;
            record.Quality = data.Quality;
            record.SourceQuality = data.SourceQuality;
            record.Temperature = data.SourceTemperature;
            record.Findings = findings;
        }
        operation.Journal.Studies[view.Id] = view;
        state.Analysis = findings;
        operation.Journal.Add(journalEntry);
        Finish(uid, state, acceptable ? "pharma-assay-complete" : "pharma-assay-ineffective");
    }

    private bool CanRegister(Solution sample, PharmaceuticalJournal journal) => RegistrationError(sample, journal) == null;

    private string? RegistrationError(Solution sample, PharmaceuticalJournal journal)
    {
        if (!TryProduct(sample, out var data, out var formula))
            return "pharma-registration-sample";
        if (!journal.Formulas.TryGetValue(data.Formula, out var record))
            return "pharma-registration-untested";
        if (record.Registered)
            return "pharma-registration-existing";
        if (data.Quality < 70)
            return "pharma-registration-impure";
        if (ImpurityToxicity(formula, data.Quality) > 0.6f)
            return "pharma-registration-toxic";
        if (!journal.Studies.TryGetValue(ReagentKey(sample.Contents[0].Reagent), out var study) || !study.Tested)
            return "pharma-registration-untested";
        return study.Passed ? null : "pharma-registration-failed";
    }

    private void Register(EntityUid uid, LaboratorySession state, LaboratoryOperation operation, Solution sample)
    {
        if (RegistrationError(sample, operation.Journal) is { } error)
        {
            Finish(uid, state, error);
            return;
        }
        if (!TryProduct(sample, out var data, out var formula))
        {
            Finish(uid, state, "pharma-registration-sample");
            return;
        }
        var name = operation.Name;
        if (name.Length == 0 || name.Any(c => !char.IsLetterOrDigit(c) && c is not ' ' and not '-' and not '.') ||
            operation.Journal.Formulas.Values.Any(r => r.Registered && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Finish(uid, state, "pharma-error-name");
            return;
        }
        var renamed = new List<(Entity<SolutionComponent> Entity, Solution Solution)>();
        foreach (var owner in _products.Append(operation.Input).Distinct().ToArray())
        {
            if (Deleted(owner) || Terminating(owner) || !TryComp<SolutionComponent>(owner, out var component) ||
                !component.Solution.Contents.Any(r => r.Reagent.Prototype == PharmaceuticalResearchModel.Product &&
                    r.Reagent.Data?.OfType<PharmaceuticalData>().Any(d => d.Formula == formula.Id && (!d.Registered || owner == operation.Input)) == true))
                continue;
            var copy = component.Solution.Clone();
            copy.Name = component.Solution.Name;
            foreach (var entry in copy.Contents)
            foreach (var label in entry.Reagent.Data?.OfType<PharmaceuticalData>() ?? Enumerable.Empty<PharmaceuticalData>())
            {
                if (entry.Reagent.Prototype != PharmaceuticalResearchModel.Product || label.Formula != formula.Id || label.Registered && owner != operation.Input)
                    continue;
                label.Label = name;
                label.Registered = true;
                label.Tint = formula.Tint;
            }
            renamed.Add(((owner, component), copy));
        }
        var journalEntry = Loc.GetString("pharma-journal-register", ("author", operation.Author), ("formula", data.Formula), ("name", name));
        var record = operation.Journal.Formulas[data.Formula];
        var findings = operation.Journal.Studies[ReagentKey(sample.Contents[0].Reagent)].Properties;
        var studies = operation.Journal.Studies.Where(p => p.Key.StartsWith(formula.Id + "@", StringComparison.Ordinal)).Select(p => p.Value).ToArray();
        var changed = renamed.Select(p => p.Entity).ToArray();
        state.Operation = null;
        foreach (var (entity, copy) in renamed)
            entity.Comp.Solution = copy;
        record.Tested = true;
        record.Quality = data.Quality;
        record.SourceQuality = data.SourceQuality;
        record.Temperature = data.SourceTemperature;
        record.Findings = findings;
        record.Name = name;
        record.Author = operation.Author;
        record.Registered = true;
        foreach (var study in studies)
            study.Name = name;
        operation.Journal.Add(journalEntry);
        var notified = NotifySolutions(changed);
        Finish(uid, state, notified ? "pharma-register-complete" : "pharma-error-notification");
    }

    private bool Reproduce(LaboratoryOperation operation, Solution source, Solution output,
        out PharmaceuticalFormula? formula, out int quality, out string reason)
    {
        formula = null;
        quality = 0;
        reason = "pharma-error-formula";
        if (!operation.Journal.Formulas.TryGetValue(operation.Selection, out var record) || !record.Registered ||
            !Model.Formulas.TryGetValue(operation.Selection, out var known) || record.SourceQuality <= 0)
            return false;
        reason = "pharma-error-ingredients";
        var total = known.Parts.Values.Sum();
        var consumed = FixedPoint2.Zero;
        foreach (var (ingredientId, ratio) in known.Parts)
        {
            var raw = ((long) operation.Amount.Value * ratio + total - 1) / total;
            var amount = FixedPoint2.FromCents((int) ((raw + 9) / 10 * 10));
            var reagent = source.Contents.FirstOrDefault(r => r.Reagent.Prototype == ingredientId && r.Reagent.Data is not { Count: > 0 });
            if (reagent.Quantity < amount)
                return false;
            source.RemoveReagent(reagent.Reagent, amount);
            consumed += amount;
        }
        var yield = Math.Clamp(0.45f + record.SourceQuality / 250f, 0.5f, 0.83f);
        var quantity = FixedPoint2.FromCents((int) (operation.Amount.Value * yield * Math.Min(1f, record.SourceQuality / (float) record.Quality)));
        if (quantity <= 0 || output.AvailableVolume < quantity)
        {
            reason = "pharma-error-capacity";
            return false;
        }
        AddProcessed(output, new ReagentId(PharmaceuticalResearchModel.Product, new List<ReagentData>
        {
            new PharmaceuticalData
            {
                Formula = known.Id, Quality = record.Quality, SourceQuality = record.SourceQuality,
                SourceTemperature = record.Temperature, Label = record.Name, Registered = true, Tint = known.Tint,
            },
        }), quantity, record.Temperature);
        AddProcessed(source, new ReagentId(PharmaceuticalResearchModel.Waste, null), consumed - quantity, record.Temperature);
        formula = known;
        quality = record.Quality;
        operation.Temperature = record.Temperature;
        return true;
    }

    private void Print(EntityUid uid, LaboratorySession state, PharmaceuticalJournal journal, string selection)
    {
        if (_timing.CurTime < state.NextPrint || !journal.Formulas.TryGetValue(selection, out var entry) || !entry.Registered ||
            !Model.Formulas.TryGetValue(selection, out var formula))
            return;
        var content = Loc.GetString("pharma-passport", ("name", entry.Name), ("id", formula.Id), ("author", entry.Author),
            ("ingredients", Ingredients(formula)), ("findings", entry.Findings));
        var paper = Spawn("Paper", Transform(uid).Coordinates);
        try
        {
            _paper.SetContent((paper, Comp<PaperComponent>(paper)), content);
            state.NextPrint = _timing.CurTime + TimeSpan.FromSeconds(10);
        }
        catch
        {
            QueueDel(paper);
            throw;
        }
    }

    private void AddProcessed(Solution destination, ReagentId reagent, FixedPoint2 quantity, float temperature)
    {
        if (quantity <= 0)
            return;
        var portion = new Solution(reagent.Prototype, quantity, reagent.Data) { Temperature = temperature };
        destination.AddSolution(portion, _prototypes);
    }

    private static string ReagentKey(ReagentId reagent) => reagent.Prototype == PharmaceuticalResearchModel.Product &&
        reagent.Data?.OfType<PharmaceuticalData>().FirstOrDefault() is { } data
        ? data.Formula + "@" + data.Quality + "@" + data.SourceQuality + "@" + data.SourceTemperature.ToString("R", CultureInfo.InvariantCulture)
        : reagent.Prototype;
}
