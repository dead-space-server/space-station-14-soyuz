// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Globalization;
using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DeadSpace._Soyuz.PharmaceuticalResearch;
using Content.Shared.EntityEffects.Effects.Damage;
using Content.Shared.EntityEffects;
using Content.Shared.EntityConditions.Conditions;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.PharmaceuticalResearch;

public sealed class PharmaceuticalMaterial
{
    public bool Profiled;
    public PharmaceuticalRole Roles;
    public float Solubility = 0.5f;
    public float Acidity;
    public float Stability = 0.7f;
    public float Toxicity;
    public readonly Dictionary<string, float> Healing = new();
    public float? Overdose;
}

public sealed class PharmaceuticalFormula
{
    public string Id = string.Empty;
    public string Key = string.Empty;
    public Dictionary<string, int> Parts = new();
    public Dictionary<string, float> Healing = new();
    public readonly List<PharmaceuticalSourceEffect> Effects = new();
    public float HealingMultiplier;
    public float Rate;
    public float Toxicity;
    public float Overdose;
    public float Stability;
    public int InitialQuality;
    public Color Tint;
}

public sealed record PharmaceuticalSourceEffect(string Reagent, string Group, EntityEffect Effect, float Fraction, float PerUnit, bool Healing);

public sealed partial class PharmaceuticalScaledEffect : EntityEffect
{
    [DataField] public EntityEffect Effect = default!;
    [DataField] public float Multiplier;
    [DataField] public EntityUid? Target;

    public override void RaiseEvent(EntityUid target, IEntityEffectRaiser raiser, float scale, EntityUid? user)
    {
        if (raiser is SharedEntityEffectsSystem effects)
            effects.ApplyEffect(Target ?? target, Effect, scale * Multiplier, user);
    }
}

public sealed class PharmaceuticalRegistration
{
    public string Name = string.Empty;
    public string Author = string.Empty;
    public string Findings = string.Empty;
    public bool Tested;
    public bool Registered;
    public float Temperature;
    public int Quality;
    public int SourceQuality;
}

public sealed class PharmaceuticalJournal
{
    public readonly HashSet<string> Analyzed = new();
    public readonly Dictionary<string, PharmaceuticalReagentView> Studies = new();
    public readonly Dictionary<string, PharmaceuticalRegistration> Formulas = new();
    public readonly List<string> Entries = new();

    public void Add(string text)
    {
        Entries.Insert(0, text);
        if (Entries.Count > 100)
            Entries.RemoveAt(Entries.Count - 1);
    }
}

public sealed class PharmaceuticalResearchModel
{
    public const string Product = PharmaceuticalData.PrototypeId;
    public const string Waste = "SoyuzPharmaceuticalResidue";
    private readonly IPrototypeManager _prototypes;
    private readonly int _seed;
    private readonly Dictionary<string, PharmaceuticalMaterial> _materials = new();
    public readonly Dictionary<string, PharmaceuticalFormula> Formulas = new();

    public PharmaceuticalResearchModel(IPrototypeManager prototypes, int seed)
    {
        _prototypes = prototypes;
        _seed = seed;
    }

    public PharmaceuticalMaterial Material(ReagentPrototype prototype)
    {
        if (_materials.TryGetValue(prototype.ID, out var cached))
            return cached;

        var material = new PharmaceuticalMaterial();
        foreach (var profile in _prototypes.EnumeratePrototypes<PharmaceuticalProfilePrototype>()
                     .Where(p => p.Groups.Contains(prototype.Group) && p.Reagents.Count == 0)
                     .Concat(_prototypes.EnumeratePrototypes<PharmaceuticalProfilePrototype>()
                         .Where(p => p.Reagents.Contains(prototype.ID))).OrderBy(p => p.Reagents.Count > 0 ? 1 : 0).ThenBy(p => p.ID, StringComparer.Ordinal))
        {
            material.Profiled = true;
            material.Roles |= profile.Roles;
            material.Solubility = Math.Clamp(profile.Solubility, 0f, 1f);
            material.Acidity = Math.Clamp(profile.Acidity, -1f, 1f);
            material.Stability = Math.Clamp(profile.Stability, 0f, 1f);
            material.Toxicity = Math.Max(material.Toxicity, Math.Clamp(profile.Toxicity, 0f, 10f));
        }

        if (prototype.Metabolisms != null)
        {
            foreach (var (group, entry) in prototype.Metabolisms)
            {
                var rate = Math.Max(0.01f, entry.MetabolismRate.Float());
                foreach (var effect in entry.Effects)
                {
                    var direct = group.Id != "Gas" && entry.MetabolismRate > 0 && effect.Conditions is not { Length: > 0 } && effect.MinScale <= 1f;
                    var grouped = effect switch
                    {
                        EvenHealthChange even => even.Damage,
                        DistributedHealthChange distributed => distributed.Damage,
                        _ => null,
                    };
                    if (grouped != null && direct)
                    {
                        foreach (var (damageGroup, amount) in grouped)
                        {
                            if (amount >= 0 || !_prototypes.TryIndex<DamageGroupPrototype>(damageGroup, out var types))
                                continue;
                            foreach (var type in types.DamageTypes)
                                material.Healing[type.Id] = material.Healing.GetValueOrDefault(type.Id) +
                                    -amount.Float() * Math.Clamp(effect.Probability, 0f, 1f) / rate / Math.Max(1, types.DamageTypes.Count);
                        }
                    }
                    if (effect is not HealthChange change)
                        continue;
                    if (change.Damage.DamageDict.Values.Any(amount => amount > 0) && effect.Conditions != null)
                    {
                        foreach (var threshold in effect.Conditions.OfType<ReagentCondition>().Where(c => c.Reagent.Id == prototype.ID && c.Min > 0))
                        {
                            var safe = MathF.Floor((threshold.Min.Float() - 0.01f) * 10f) / 10f;
                            material.Overdose = Math.Min(material.Overdose.GetValueOrDefault(safe), safe);
                        }
                    }
                    foreach (var (type, amount) in change.Damage.DamageDict)
                    {
                        if (amount < 0 && direct)
                            material.Healing[type] = material.Healing.GetValueOrDefault(type) + -amount.Float() * Math.Clamp(effect.Probability, 0f, 1f) / rate;
                        if (amount > 0 && effect.Conditions == null)
                            material.Toxicity = Math.Min(5f, Math.Max(material.Toxicity, amount.Float() / rate));
                    }
                }
            }
        }
        foreach (var type in material.Healing.Where(p => p.Value <= 0).Select(p => p.Key).ToArray())
            material.Healing.Remove(type);
        if (material.Healing.Count > 0)
            material.Roles |= PharmaceuticalRole.Active;
        if (material.Toxicity >= 1f)
            material.Roles |= PharmaceuticalRole.Hazard;
        if (prototype.ID is Product or Waste)
            material.Roles |= PharmaceuticalRole.Restricted;
        _materials.Add(prototype.ID, material);
        return material;
    }

    public bool TryDevelop(Solution sample, out PharmaceuticalFormula formula, out int quality, out float yield, out string reason)
    {
        formula = default!;
        quality = 0;
        yield = 0;
        reason = "pharma-error-composition";
        if (sample.Volume <= 0 || sample.Contents.Count < 2 || sample.Contents.Count > 64 ||
            sample.Contents.Any(r => r.Reagent.Data is { Count: > 0 }))
            return false;

        var amounts = sample.Contents.GroupBy(r => r.Reagent.Prototype).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity.Value));
        if (amounts.Count < 2 || amounts.Values.Any(amount => amount <= 0))
            return false;
        var materials = new Dictionary<string, PharmaceuticalMaterial>();
        foreach (var reagentId in amounts.Keys)
        {
            if (!_prototypes.TryIndex<ReagentPrototype>(reagentId, out var reagent))
                return false;
            var material = Material(reagent);
            if (material.Roles.HasFlag(PharmaceuticalRole.Restricted))
            {
                reason = "pharma-error-restricted";
                return false;
            }
            materials[reagentId] = material;
        }
        var volume = (float) sample.Volume.Value;
        var divisor = amounts.Values.Aggregate((left, right) =>
        {
            while (right != 0)
                (left, right) = (right, left % right);
            return left;
        });
        var parts = amounts.ToDictionary(p => p.Key, p => p.Value / divisor);
        var key = "mix:" + string.Join(";", parts.Select(p => p.Key + "=" + p.Value.ToString(CultureInfo.InvariantCulture)));
        var hash = Hash(key, _seed);
        var existing = Formulas.Values.FirstOrDefault(candidate => candidate.Key == key);
        if (existing == null)
        {
            if (Formulas.Count >= 512)
            {
                reason = "pharma-error-limit";
                return false;
            }
            var healing = new Dictionary<string, float>();
            foreach (var (source, material) in materials)
            foreach (var (type, amount) in material.Healing)
                healing[type] = healing.GetValueOrDefault(type) + amount * amounts[source] / volume;

            var stability = Math.Clamp(materials.Sum(p => p.Value.Stability * amounts[p.Key] / volume), 0.1f, 1f);
            var acidity = Math.Abs(materials.Sum(p => p.Value.Acidity * amounts[p.Key] / volume));
            var initialQuality = (int) MathF.Round(Math.Clamp(75f + stability * 15f - acidity * 10f - Unit(hash, 0) * 8f -
                Math.Min(10, amounts.Count - 2) * 0.25f, 55f, 90f));
            var healingMultiplier = (1.25f + Unit(hash, 6) * 0.15f) / (initialQuality / 100f);
            foreach (var type in healing.Keys.ToArray())
                healing[type] *= healingMultiplier;
            string formulaId;
            do
            {
                formulaId = "FIL-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
            } while (Formulas.ContainsKey(formulaId));
            var colors = new[] { "#75BDE8", "#A58AE0", "#E3A273", "#8DC77E", "#E285A2", "#72D2BE", "#C4C86D", "#A5B9DB" };
            existing = new PharmaceuticalFormula
            {
                Id = formulaId, Key = key, Parts = parts, Healing = healing,
                Rate = FixedPoint2.New(0.15f + Unit(hash, 2) * 0.3f).Float(),
                Toxicity = Math.Clamp(0.02f + materials.Sum(p => p.Value.Toxicity * amounts[p.Key] / volume) +
                    (1f - stability) * 0.1f, 0.02f, 3f),
                Overdose = MathF.Round(5f + Unit(hash, 3) * 13f, 1),
                Stability = stability, InitialQuality = initialQuality,
                HealingMultiplier = healingMultiplier,
                Tint = Color.FromHex(colors[(int) (hash % (ulong) colors.Length)]),
            };
            var totalWeight = parts.Values.Sum();
            foreach (var (source, weight) in parts)
            {
                var reagent = _prototypes.Index<ReagentPrototype>(source);
                if (reagent.Metabolisms == null)
                    continue;
                foreach (var (group, entry) in reagent.Metabolisms)
                {
                    if (group.Id == "Gas" || entry.MetabolismRate <= 0)
                        continue;
                    foreach (var effect in entry.Effects)
                        existing.Effects.Add(new PharmaceuticalSourceEffect(source, group.Id, effect, weight / (float) totalWeight,
                            weight / (float) totalWeight / entry.MetabolismRate.Float(), IsHealing(effect)));
                }
            }
        }
        formula = existing;
        quality = formula.InitialQuality;
        yield = Math.Clamp(0.45f + quality / 250f, 0.5f, 0.83f);
        reason = string.Empty;
        return true;
    }

    public void Publish(PharmaceuticalFormula formula) => Formulas.TryAdd(formula.Id, formula);

    public static bool IsHealing(EntityEffect effect) => effect switch
    {
        HealthChange change => change.Damage.DamageDict.Values.Any(amount => amount < 0),
        EvenHealthChange even => even.Damage.Values.Any(amount => amount < 0),
        DistributedHealthChange distributed => distributed.Damage.Values.Any(amount => amount < 0),
        _ => false,
    };

    public bool Degrade(Solution sample, float seconds)
    {
        var changed = false;
        foreach (var (reagent, amount) in sample.Contents.ToArray())
        {
            if (reagent.Prototype != Product || reagent.Data?.OfType<PharmaceuticalData>().FirstOrDefault() is not { } data ||
                !Formulas.TryGetValue(data.Formula, out var formula))
                continue;
            var excessiveHeat = Math.Max(0f, sample.Temperature - (330f + formula.Stability * 35f));
            if (excessiveHeat <= 0)
                continue;
            var fraction = 1f - MathF.Exp(-excessiveHeat / 100f * seconds / 120f);
            var loss = FixedPoint2.FromCents(Math.Min(amount.Value, (int) MathF.Ceiling(amount.Value * fraction)));
            if (loss <= 0)
                continue;
            sample.RemoveReagent(reagent, loss);
            sample.AddReagent(Waste, loss);
            changed = true;
        }
        return changed;
    }

    public static ulong Hash(string key, int seed)
    {
        unchecked
        {
            var hash = 14695981039346656037UL ^ (uint) seed;
            foreach (var value in key)
                hash = (hash ^ value) * 1099511628211UL;
            return hash;
        }
    }

    private static float Unit(ulong hash, int field) => ((Hash(field.ToString(CultureInfo.InvariantCulture), unchecked((int) (hash ^ (hash >> 32)))) >> 16) & 65535) / 65535f;
}
