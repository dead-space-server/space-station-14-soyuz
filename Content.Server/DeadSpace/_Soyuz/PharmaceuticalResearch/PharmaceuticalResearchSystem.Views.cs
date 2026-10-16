// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server.Power.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DeadSpace._Soyuz.PharmaceuticalResearch;
using Content.Shared.FixedPoint;
using Content.Shared.EntityEffects;
using Content.Shared.EntityEffects.Effects.Damage;
using Content.Shared.EntityEffects.Effects.Body;
using Content.Shared.EntityEffects.Effects.StatusEffects;
using Content.Shared.EntityConditions.Conditions;
using Content.Shared.Chemistry;
using Content.Shared.EntityEffects.Effects.Solution;
using Content.Shared.EntityEffects.Effects;
using Content.Shared.EntityEffects.Effects.Transform;
using Content.Shared.DeadSpace.AntiAlcohol;
using Content.Shared.Atmos.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.DeadSpace._Soyuz.PharmaceuticalResearch;

public sealed partial class PharmaceuticalResearchSystem
{
    private static string Number(float value) => MathF.Round(value, 1).ToString("0.0", CultureInfo.InvariantCulture);

    private string DamageName(string id) => _prototypes.TryIndex<DamageTypePrototype>(id, out var type) ? type.LocalizedName : id;
    private string ReagentName(string id) => _prototypes.TryIndex<ReagentPrototype>(id, out var reagent) ? reagent.LocalizedName : id;

    private string Ingredients(PharmaceuticalFormula formula)
    {
        var total = formula.Parts.Values.Sum();
        return string.Join(", ", formula.Parts.Select(p => Loc.GetString("pharma-ingredient", ("name", ReagentName(p.Key)), ("fraction", Number(100f * p.Value / total)))));
    }

    private string UnitReport(Dictionary<string, float> changes, float? quality, float? overdose)
    {
        var lines = changes.Where(p => p.Value != 0).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
            Loc.GetString(p.Value < 0 ? "pharma-effect-heal" : "pharma-effect-damage", ("type", DamageName(p.Key)), ("amount", Number(Math.Abs(p.Value)))));
        var effects = string.Join("\n", lines);
        if (effects.Length == 0)
            effects = Loc.GetString("pharma-no-direct-effect");
        return (quality is { } purity ? Loc.GetString("pharma-quality", ("quality", Number(purity))) + "\n" : string.Empty) +
            Loc.GetString("pharma-unit-report", ("effects", effects), ("overdose", overdose is { } limit
                ? Loc.GetString("pharma-overdose-limit", ("amount", Number(limit))) : Loc.GetString("pharma-overdose-unknown")));
    }

    private PharmaceuticalReagentView ReagentView(ReagentId id, PharmaceuticalJournal journal, float amount = 0, bool assumeAnalyzed = false)
    {
        var key = ReagentKey(id);
        if (id.Prototype == PharmaceuticalResearchModel.Product && id.Data?.OfType<PharmaceuticalData>().FirstOrDefault() is { } data &&
            Model.Formulas.TryGetValue(data.Formula, out var formula))
        {
            var properties = Loc.GetString("pharma-quality", ("quality", Number(data.Quality))) + "\n" + Loc.GetString("pharma-untested");
            var tested = false;
            var passed = false;
            if (journal.Studies.TryGetValue(key, out var study) && study.Tested)
            {
                properties = study.Properties;
                tested = true;
                passed = study.Passed;
            }
            else if (journal.Formulas.TryGetValue(formula.Id, out var record) && record.Tested && record.Quality == data.Quality &&
                     record.SourceQuality == data.SourceQuality && record.Temperature.Equals(data.SourceTemperature))
            {
                properties = record.Findings;
                tested = true;
                passed = data.Quality >= 70 && ImpurityToxicity(formula, data.Quality) <= 0.6f;
            }
            var name = data.Registered ? data.Label : journal.Formulas.TryGetValue(data.Formula, out var named) && named.Registered
                ? named.Name : Loc.GetString("pharma-sample-name", ("id", data.Formula));
            return new PharmaceuticalReagentView
            {
                Id = key, Amount = amount, Name = name,
                Description = Loc.GetString("pharma-product-description"), Properties = properties,
                Analyzed = journal.Studies.ContainsKey(key), IsProduct = true, CanPurify = data.Quality < 99, Tested = tested, Passed = passed,
            };
        }
        if (!_prototypes.TryIndex<ReagentPrototype>(id.Prototype, out var reagent))
            return new PharmaceuticalReagentView { Id = key, Name = key, Amount = amount };
        var material = Model.Material(reagent);
        var analyzed = assumeAnalyzed || journal.Analyzed.Contains(id.Prototype);
        return new PharmaceuticalReagentView
        {
            Id = key, Name = reagent.LocalizedName, Description = reagent.LocalizedDescription, Amount = amount,
            Analyzed = analyzed, CanUse = analyzed && !material.Roles.HasFlag(PharmaceuticalRole.Restricted) && id.Data is not { Count: > 0 },
            Properties = ReagentEffects(reagent) + "\n" + OverdoseReport(material.Overdose),
        };
    }

    private string OverdoseReport(float? overdose) => overdose is { } limit
        ? Loc.GetString("pharma-overdose-limit", ("amount", Number(limit))) : Loc.GetString("pharma-overdose-unknown");

    private string ReagentEffects(ReagentPrototype reagent)
    {
        var lines = new List<string>();
        if (reagent.Metabolisms != null)
        {
            foreach (var (group, entry) in reagent.Metabolisms)
                lines.Add(_prototypes.Index(group).LocalizedName + ":\n" + string.Join("\n", entry.Effects.Select(effect =>
                    DescribeEffect(effect, group.Id == "Gas" ? 1f : entry.MetabolismRate > 0 ? 1f / entry.MetabolismRate.Float() : 0,
                        group.Id == "Gas" ? entry.MetabolismRate.Float() : 1f, entry.MetabolismRate.Float()))));
        }
        lines.AddRange(ContactEffects(reagent, 1f, 1f));
        return lines.Count == 0 ? Loc.GetString("pharma-no-metabolic-effect") : string.Join("\n", lines);
    }

    private string FormulaEffects(PharmaceuticalFormula formula, PharmaceuticalData data)
    {
        var purity = data.Quality / 100f;
        var lines = formula.Effects.Where(inherited => !inherited.Healing || inherited.Effect.Conditions is { Length: > 0 })
            .Select(inherited =>
            {
                var scale = inherited.PerUnit * purity * (inherited.Healing ? formula.HealingMultiplier : 1f);
                if (inherited.Effect is MovementSpeedModifier movement)
                    return DescribeEffect(InheritedMovement(movement, inherited.Fraction * purity), scale, 1f, formula.Rate);
                return DescribeEffect(inherited.Effect, scale, formula.Rate * scale, formula.Rate);
            });
        var contacts = formula.Parts.SelectMany(part => ContactEffects(_prototypes.Index<ReagentPrototype>(part.Key),
            part.Value / (float) formula.Parts.Values.Sum() * purity, formula.HealingMultiplier));
        var report = string.Join("\n", lines.Concat(contacts));
        return report.Length == 0 ? string.Empty : "\n" + Loc.GetString("pharma-inherited-effects") + "\n" + report;
    }

    private IEnumerable<string> ContactEffects(ReagentPrototype reagent, float strength, float healingMultiplier)
    {
        if (reagent.ReactiveEffects == null)
            yield break;
        foreach (var reaction in reagent.ReactiveEffects.Values)
        foreach (var method in reaction.Methods.OrderBy(method => method))
        foreach (var effect in reaction.Effects)
        {
            var scale = strength * (PharmaceuticalResearchModel.IsHealing(effect) ? healingMultiplier : 1f);
            yield return Loc.GetString("pharma-contact-effect", ("method", method.ToString().ToLowerInvariant()),
                ("effect", DescribeEffect(effect, scale, scale, 1f)));
        }
    }

    private string DescribeEffect(EntityEffect effect, float perUnit, float cycleScale, float cycleVolume)
    {
        if (!effect.Scaling && cycleVolume > 0)
        {
            cycleScale = Math.Min(cycleScale, 1f);
            perUnit = cycleScale / cycleVolume;
        }
        string description;
        var damage = effect as HealthChange;
        var grouped = effect switch
        {
            EvenHealthChange even => even.Damage,
            DistributedHealthChange distributed => distributed.Damage,
            _ => null,
        };
        if (damage != null)
        {
            var changes = _damage.ApplyUniversalAllModifiers(new DamageSpecifier(damage.Damage));
            description = string.Join("; ", changes.DamageDict.Where(p => p.Value != 0).Select(p =>
                Loc.GetString(p.Value < 0 ? "pharma-effect-heal" : "pharma-effect-damage",
                    ("type", DamageName(p.Key)), ("amount", Number(Math.Abs(p.Value.Float() * perUnit))))));
            description = Loc.GetString("pharma-per-unit", ("effect", description));
        }
        else if (grouped != null)
        {
            description = string.Join("; ", grouped.Select(p =>
                Loc.GetString(p.Value < 0 ? "pharma-effect-heal" : "pharma-effect-damage",
                    ("type", _prototypes.Index(p.Key).LocalizedName),
                    ("amount", Number(Math.Abs(p.Value.Float() * perUnit) * (p.Value < 0
                        ? _damage.UniversalReagentHealModifier : _damage.UniversalReagentDamageModifier))))));
            description = Loc.GetString("pharma-per-unit", ("effect", description));
        }
        else if (effect is ModifyBloodLevel blood)
            description = Loc.GetString("pharma-blood-effect", ("amount", Number(blood.Amount.Float() * perUnit)));
        else if (effect is ModifyBleed bleed)
            description = Loc.GetString("pharma-bleed-effect", ("amount", Number(bleed.Amount * perUnit)));
        else if (effect is AdjustReagent adjusted)
            description = Loc.GetString("pharma-reagent-effect", ("name", ReagentName(adjusted.Reagent.Id)),
                ("amount", Number(adjusted.Amount.Float() * perUnit)));
        else if (effect is AdjustTemperature temperature)
            description = Loc.GetString("pharma-temperature-effect", ("amount", Number(temperature.Amount * perUnit)));
        else if (effect is Oxygenate oxygenate)
            description = Loc.GetString("pharma-oxygenate-effect", ("amount", Number(oxygenate.Factor * perUnit)));
        else if (effect is ModifyLungGas lung)
            description = Loc.GetString("pharma-lung-effect", ("gases", string.Join("; ", lung.Ratios.Select(pair =>
                Loc.GetString(_prototypes.Index<GasPrototype>(pair.Key.ToString()).Name)))));
        else if (effect is AdjustAlert)
            description = Loc.GetString("pharma-alert-effect");
        else if (effect is PopupMessage)
            description = Loc.GetString("pharma-sensation-effect");
        else if (effect is AntiAlcoholImplantEffect)
            description = Loc.GetString("pharma-antialcohol-effect");
        else if (effect is MovementSpeedModifier movement)
            description = Loc.GetString("pharma-movement-effect", ("walk", Number((movement.WalkSpeedModifier - 1f) * 100)),
                ("sprint", Number((movement.SprintSpeedModifier - 1f) * 100)),
                ("duration", movement.Time is { } time ? Number((float) time.TotalSeconds * cycleScale) : Loc.GetString("pharma-indefinite")),
                ("amount", Number(cycleVolume)), ("mode", movement.Type.ToString().ToLowerInvariant()));
        else
        {
            var display = effect switch
            {
                ModifyStatusEffect status => new ModifyStatusEffect
                {
                    EffectProto = status.EffectProto, Time = status.Time * cycleScale, Type = status.Type,
                    Delay = status.Delay, Probability = status.Probability,
                },
                ModifyKnockdown knockdown => new ModifyKnockdown
                {
                    Time = knockdown.Time * cycleScale, Type = knockdown.Type, Probability = knockdown.Probability,
                },
                ModifyParalysis paralysis => new ModifyParalysis
                {
                    Time = paralysis.Time * cycleScale, Type = paralysis.Type, Probability = paralysis.Probability,
                },
                GenericStatusEffect status => new GenericStatusEffect
                {
                    Key = status.Key, Component = status.Component, Time = status.Time * cycleScale,
                    Type = status.Type, Probability = status.Probability,
                },
                _ => effect,
            };
            description = display.EntityEffectGuidebookText(_prototypes, EntityManager.EntitySysManager) ?? Loc.GetString("pharma-effect-undescribed");
            description = Loc.GetString("pharma-cycle-effect", ("effect", description),
                ("amount", Number(cycleVolume)), ("scale", Number(ReferenceEquals(display, effect) ? cycleScale : 1f)));
        }
        var conditions = effect.Conditions?.Select(condition => condition is ReagentCondition threshold
            ? threshold.Max == FixedPoint2.MaxValue
                ? Loc.GetString("pharma-condition-min", ("name", ReagentName(threshold.Reagent.Id)), ("amount", Number(threshold.Min.Float())))
                : Loc.GetString("pharma-condition-range", ("name", ReagentName(threshold.Reagent.Id)),
                    ("min", Number(threshold.Min.Float())), ("max", Number(threshold.Max.Float())))
            : condition.EntityConditionGuidebookText(_prototypes)).ToArray() ?? Array.Empty<string>();
        if (conditions.Length > 0)
            description += " " + Loc.GetString("pharma-effect-conditions", ("conditions", string.Join("; ", conditions)));
        if (effect.Probability < 1f)
            description += " " + Loc.GetString("pharma-effect-probability", ("chance", Number(effect.Probability * 100f)));
        if (effect.MinScale > 0)
            description += " " + Loc.GetString("pharma-effect-minimum", ("amount", Number(cycleScale > 0 ? cycleVolume * effect.MinScale / cycleScale : 0)));
        description = FormattedMessage.RemoveMarkupPermissive(description);
        return Regex.Replace(description, @"(?<![\p{L}\d])\d+[.,]\d+(?![\p{L}\d])", match =>
            float.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? Number(number) : match.Value);
    }

    private string Analyze(Solution solution, PharmaceuticalJournal journal, out List<PharmaceuticalReagentView> studies)
    {
        var lines = new List<string>();
        studies = new List<PharmaceuticalReagentView>();
        foreach (var (id, quantity) in solution.Contents)
        {
            var view = ReagentView(id, journal, assumeAnalyzed: true);
            view.Analyzed = true;
            studies.Add(view);
            lines.Add(view.Name + "\n" + view.Description + "\n" + view.Properties);
        }
        return string.Join("\n\n", lines);
    }

    private void Refresh(EntityUid uid)
    {
        if (Deleted(uid) || Terminating(uid) || !HasComp<PharmaceuticalLaboratoryComponent>(uid))
            return;
        var state = Session(uid);
        var journal = Journal(uid);
        var view = new PharmaceuticalState
        {
            Revision = state.Revision, Powered = this.IsPowered(uid, EntityManager), Busy = state.Operation != null,
            Status = state.Status, Analysis = state.Analysis, Journal = journal.Entries.Take(30).ToArray(),
            Studied = journal.Studies.Values.OrderBy(p => p.Name, StringComparer.CurrentCulture).ToArray(),
        };
        if (state.Operation is { } operation)
        {
            view.Operation = Loc.GetString("pharma-action-" + operation.Action.ToString().ToLowerInvariant());
            view.Progress = (float) Math.Clamp((_timing.CurTime - state.Started).TotalSeconds / Math.Max(1, (state.Ends - state.Started).TotalSeconds), 0, 1);
        }
        view.Input = Loc.GetString("pharma-slot-empty");
        view.Output = Loc.GetString("pharma-slot-empty");
        state.InputSolution = null;
        state.OutputSolution = null;
        if (Sample(uid, InputSlot, out var inputEntity, out var input))
        {
            state.InputSolution = inputEntity.Owner;
            view.Input = Loc.GetString("pharma-slot-volume", ("volume", Number(input.Volume.Float())), ("capacity", Number(input.MaxVolume.Float())));
            view.Sample = input.Contents.Select(r => ReagentView(r.Reagent, journal, r.Quantity.Float())).ToArray();
            view.CanRegister = CanRegister(input, journal);
        }
        if (Sample(uid, OutputSlot, out var outputEntity, out var output))
        {
            state.OutputSolution = outputEntity.Owner;
            view.Output = Loc.GetString("pharma-slot-volume", ("volume", Number(output.Volume.Float())), ("capacity", Number(output.MaxVolume.Float())));
        }
        view.Formulas = journal.Formulas.Where(p => p.Value.Registered && Model.Formulas.ContainsKey(p.Key))
            .OrderBy(p => p.Value.Name, StringComparer.CurrentCulture).Select(p =>
            {
                var formula = Model.Formulas[p.Key];
                return new PharmaceuticalFormulaView
                {
                    Id = p.Key, Name = p.Value.Name, Author = p.Value.Author, Findings = p.Value.Findings,
                    Tested = p.Value.Tested, Registered = true, Tint = formula.Tint, Ingredients = Ingredients(formula),
                    Parts = formula.Parts.Select(part => new PharmaceuticalReagentView
                    {
                        Id = part.Key, Name = ReagentName(part.Key), Amount = part.Value,
                    }).ToArray(),
                };
            }).ToArray();
        _ui.SetUiState(uid, PharmaceuticalUiKey.Key, view);
    }
}
