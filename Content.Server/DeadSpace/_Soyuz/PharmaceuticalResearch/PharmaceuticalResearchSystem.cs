// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Chemistry;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DeadSpace._Soyuz.PharmaceuticalResearch;
using Content.Shared.EntityEffects;
using Content.Shared.EntityEffects.Effects.Damage;
using Content.Shared.EntityEffects.Effects.Body;
using Content.Shared.EntityEffects.Effects.Solution;
using Content.Shared.EntityEffects.Effects.StatusEffects;
using Content.Shared.EntityConditions.Conditions;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.IdentityManagement;
using Content.Shared.Paper;
using Content.Shared.Power;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace._Soyuz.PharmaceuticalResearch;

public sealed partial class PharmaceuticalResearchSystem : EntitySystem
{
    private const string InputSlot = "beakerSlot";
    private const string OutputSlot = "outputSlot";
    private const int MaximumComponents = 64;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly StationSystem _stations = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly MetabolizerSystem _metabolizer = default!;
    [Dependency] private readonly BodySystem _bodies = default!;
    [Dependency] private readonly SharedEntityEffectsSystem _effects = default!;

    private PharmaceuticalResearchModel? _model;
    private PharmaceuticalResearchModel Model => _model ??= new PharmaceuticalResearchModel(_prototypes, _random.Next());
    private readonly Dictionary<EntityUid, LaboratorySession> _sessions = new();
    private readonly Dictionary<EntityUid, PharmaceuticalJournal> _journals = new();
    private readonly HashSet<EntityUid> _products = new();
    private TimeSpan _nextDegradation;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PharmaceuticalLaboratoryComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<PharmaceuticalLaboratoryComponent, PharmaceuticalCommand>(OnCommand);
        SubscribeLocalEvent<PharmaceuticalLaboratoryComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<PharmaceuticalLaboratoryComponent, EntityUnpausedEvent>(OnUnpaused);
        SubscribeLocalEvent<PharmaceuticalLaboratoryComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<PharmaceuticalReactionEvent>(OnPharmaceuticalReaction);
        SubscribeLocalEvent<PharmaceuticalLaboratoryComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<PharmaceuticalLaboratoryComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<ResolveReagentMetabolismEvent>(OnMetabolism);
        SubscribeLocalEvent<SolutionComponent, SolutionChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _sessions.Clear();
            _journals.Clear();
            _model = null;
            _products.Clear();
            _nextDegradation = TimeSpan.Zero;
        });
    }

    private LaboratorySession Session(EntityUid laboratory)
    {
        if (!_sessions.TryGetValue(laboratory, out var state))
            _sessions[laboratory] = state = new LaboratorySession();
        return state;
    }

    private PharmaceuticalJournal Journal(EntityUid laboratory)
    {
        var owner = _stations.GetOwningStation(laboratory) ?? laboratory;
        if (!_journals.TryGetValue(owner, out var journal))
            _journals[owner] = journal = new PharmaceuticalJournal();
        return journal;
    }

    private void OnOpened(Entity<PharmaceuticalLaboratoryComponent> ent, ref BoundUIOpenedEvent args) => Refresh(ent.Owner);

    private void OnShutdown(Entity<PharmaceuticalLaboratoryComponent> ent, ref ComponentShutdown args)
    {
        if (_sessions.TryGetValue(ent.Owner, out var session))
        {
            session.Executing = true;
            session.Operation = null;
            CleanupModels(session);
            if (!Terminating(ent.Owner))
            {
                _slots.SetLock(ent.Owner, InputSlot, false);
                _slots.SetLock(ent.Owner, OutputSlot, false);
            }
            _sessions.Remove(ent.Owner);
        }
    }

    private void OnUnpaused(Entity<PharmaceuticalLaboratoryComponent> ent, ref EntityUnpausedEvent args)
    {
        if (_sessions.TryGetValue(ent.Owner, out var session) && session.Operation != null)
        {
            session.Started += args.PausedTime;
            session.Ends += args.PausedTime;
        }
    }

    private void OnInserted(Entity<PharmaceuticalLaboratoryComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID is not InputSlot and not OutputSlot)
            return;
        OnPortChanged(ent.Owner);
    }

    private void OnPowerChanged(Entity<PharmaceuticalLaboratoryComponent> ent, ref PowerChangedEvent args)
    {
        if (this.IsPowered(ent.Owner, EntityManager) || !_sessions.TryGetValue(ent.Owner, out var state) || state.Operation == null)
            return;
        state.Operation.Interrupted = true;
        if (!state.Executing)
            Finish(ent.Owner, state, "pharma-error-power");
    }

    private void OnPortChanged(EntityUid uid)
    {
        if (Terminating(uid))
            return;
        var state = Session(uid);
        state.Revision++;
        if (!state.Executing)
        {
            if (state.Operation != null)
                Finish(uid, state, state.Operation.Action == PharmaceuticalAction.Assay ? "pharma-error-assay-interrupted" : "pharma-error-changed");
            else
                Refresh(uid);
        }
    }

    private void OnRemoved(Entity<PharmaceuticalLaboratoryComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID is not InputSlot and not OutputSlot)
            return;
        OnPortChanged(ent.Owner);
    }

    private bool Sample(EntityUid machine, string slot, out Entity<SolutionComponent> entity, out Solution solution)
    {
        entity = default;
        solution = default!;
        if (_slots.GetItemOrNull(machine, slot) is not { } beaker ||
            !_solutions.TryGetFitsInDispenser(beaker, out var found, out var liquid))
            return false;
        entity = found.Value;
        solution = liquid;
        return true;
    }

    private void OnCommand(Entity<PharmaceuticalLaboratoryComponent> ent, ref PharmaceuticalCommand args)
    {
        var state = Session(ent.Owner);
        if (state.Executing || Paused(ent.Owner) || Terminating(ent.Owner) || Deleted(args.Actor) ||
            !_ui.IsUiOpen(ent.Owner, PharmaceuticalUiKey.Key, args.Actor) || args.UiKey is not PharmaceuticalUiKey.Key ||
            !this.IsPowered(ent.Owner, EntityManager) || !_access.IsAllowed(args.Actor, ent.Owner) ||
            args.Revision != state.Revision || !Enum.IsDefined(args.Action) ||
            !float.IsFinite(args.Amount) || args.Amount < 0.1f || args.Amount > 200f || args.Selection == null || args.Name == null ||
            args.Selection.Length > 200 || args.Name.Length > 48 || args.Ingredients == null || args.Ingredients.Length > MaximumComponents ||
            args.Ingredients.Any(id => string.IsNullOrEmpty(id) || id.Length > 200) || args.Ingredients.Distinct().Count() != args.Ingredients.Length)
            return;
        if (state.Operation != null && args.Action != PharmaceuticalAction.Cancel)
            return;

        state.Executing = true;
        state.Revision++;
        try
        {
            ExecuteCommand(ent, args, state);
        }
        catch (Exception exception)
        {
            Log.Error($"Pharmaceutical command failed at {ent.Owner}: {exception}");
            Finish(ent.Owner, state, "pharma-error-operation");
        }
        finally
        {
            state.Executing = false;
            Refresh(ent.Owner);
        }
    }

    private void ExecuteCommand(Entity<PharmaceuticalLaboratoryComponent> ent, PharmaceuticalCommand args, LaboratorySession state)
    {
        if (args.Action == PharmaceuticalAction.Cancel)
        {
            if (state.Operation != null)
                Finish(ent.Owner, state, "pharma-operation-cancelled");
            return;
        }
        if (state.Operation != null)
            return;
        if (args.Action is PharmaceuticalAction.EjectInput or PharmaceuticalAction.EjectOutput)
        {
            if (_slots.TryGetSlot(ent.Owner, args.Action == PharmaceuticalAction.EjectInput ? InputSlot : OutputSlot, out var slot))
                _slots.TryEjectToHands(ent.Owner, slot, args.Actor);
            return;
        }
        if (args.Action == PharmaceuticalAction.Print)
        {
            Print(ent.Owner, state, Journal(ent.Owner), args.Selection);
            return;
        }
        if (!Sample(ent.Owner, InputSlot, out var input, out var liquid) || liquid.Volume <= 0 || liquid.Contents.Count > MaximumComponents)
        {
            Notice(ent.Owner, "pharma-error-input");
            return;
        }

        var request = new LaboratoryOperation
        {
            Action = args.Action, Selection = args.Selection, Ingredients = args.Ingredients.ToArray(),
            Amount = args.Action == PharmaceuticalAction.Separate ? liquid.Volume : FixedPoint2.New(MathF.Round(args.Amount, 1)),
            Name = args.Name.Trim(), Author = Identity.Name(args.Actor, EntityManager),
            Input = input.Owner, OriginalInput = liquid.Clone(),
            Journal = Journal(ent.Owner),
        };
        if (args.Action is PharmaceuticalAction.Synthesize or PharmaceuticalAction.Reproduce or PharmaceuticalAction.Separate)
        {
            if (!Sample(ent.Owner, OutputSlot, out var output, out var outputLiquid) || output.Owner == input.Owner ||
                outputLiquid.Contents.Count > MaximumComponents)
            {
                Notice(ent.Owner, "pharma-error-output");
                return;
            }
            request.Output = output.Owner;
            request.OriginalOutput = outputLiquid.Clone();
        }
        state.Operation = request;
        state.Started = _timing.CurTime;
        state.Ends = state.Started + TimeSpan.FromSeconds(args.Action == PharmaceuticalAction.Assay ? 5 : 3);
        state.Revision++;
        _slots.SetLock(ent.Owner, InputSlot, true);
        _slots.SetLock(ent.Owner, OutputSlot, true);
        if (args.Action == PharmaceuticalAction.Assay && !StartAssay(ent.Owner, state, request))
            Finish(ent.Owner, state, "pharma-error-assay");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime >= _nextDegradation)
        {
            _nextDegradation = _timing.CurTime + TimeSpan.FromSeconds(5);
            foreach (var product in _products.ToArray())
            {
                if (!TryComp<SolutionComponent>(product, out var solution) || Deleted(product) || Terminating(product))
                {
                    _products.Remove(product);
                    continue;
                }
                if (!Paused(product) && Model.Degrade(solution.Solution, 5f))
                    NotifySolutions((product, solution));
            }
        }
        foreach (var (uid, session) in _sessions.ToArray())
        {
            if (Deleted(uid) || Terminating(uid) || Paused(uid) || session.Executing)
                continue;
            if (session.Operation != null)
            {
                if (session.Operation.Interrupted || !this.IsPowered(uid, EntityManager))
                    Finish(uid, session, "pharma-error-power");
                else if (_timing.CurTime >= session.Ends)
                {
                    session.Executing = true;
                    try
                    {
                        Complete(uid, session);
                    }
                    catch (Exception exception)
                    {
                        Log.Error($"Pharmaceutical operation failed at {uid}: {exception}");
                        Finish(uid, session, session.Operation == null ? "pharma-error-notification" : "pharma-error-operation");
                    }
                    finally
                    {
                        session.Executing = false;
                        Refresh(uid);
                    }
                }
            }
            if (_timing.CurTime < session.NextRefresh || !_ui.IsUiOpen(uid, PharmaceuticalUiKey.Key))
                continue;
            session.NextRefresh = _timing.CurTime + TimeSpan.FromSeconds(1);
            Refresh(uid);
        }
    }

    private void OnMetabolism(ref ResolveReagentMetabolismEvent args)
    {
        if (HasComp<PharmaceuticalBiomodelComponent>(args.Body))
        {
            args.Entry = new ReagentEffectsEntry
            {
                MetabolismRate = args.Entry.MetabolismRate,
                Effects = args.Entry.Effects.Where(effect => effect is HealthChange or EvenHealthChange or DistributedHealthChange).ToArray(),
            };
        }
        if (args.Reagent.Prototype != PharmaceuticalResearchModel.Product)
            return;
        var data = args.Reagent.Data?.OfType<PharmaceuticalData>().FirstOrDefault();
        if (data == null || !Model.Formulas.TryGetValue(data.Formula, out var formula))
            return;
        var rate = FixedPoint2.Min(MetabolicShare(formula, args.Reagent, args.Reagents), args.Solution.GetReagentQuantity(args.Reagent));
        if (rate <= 0)
        {
            args.Skip = true;
            return;
        }
        var purity = Math.Clamp(data.Quality / 100f, 0f, 1f);
        var reagents = args.Reagents;
        var exposure = reagents.Where(r => r.Reagent.Prototype == PharmaceuticalResearchModel.Product).Sum(r => r.Quantity.Float());
        var relatives = formula.Parts.Keys.Where(id => _prototypes.TryIndex<ReagentPrototype>(id, out var reagent) && Model.Material(reagent).Healing.Count > 0).ToHashSet();
        exposure += reagents.Where(r => relatives.Contains(r.Reagent.Prototype)).Sum(r => r.Quantity.Float());
        var overdose = Math.Max(0f, exposure / formula.Overdose - 1f);
        var effects = new List<EntityEffect>();
        var movementEffects = new Dictionary<(EntProtoId, StatusEffectMetabolismType), MovementSpeedModifier>();
        var biomodel = HasComp<PharmaceuticalBiomodelComponent>(args.Body);
        foreach (var inherited in formula.Effects)
        {
            var effect = inherited.Effect;
            if (inherited.Group != args.Group || biomodel && effect is not (HealthChange or EvenHealthChange or DistributedHealthChange))
                continue;
            if (args.IsDead && !_prototypes.Index<ReagentPrototype>(inherited.Reagent).WorksOnTheDead)
                continue;
            var multiplier = inherited.PerUnit * purity * rate.Float() *
                (inherited.Healing ? formula.HealingMultiplier / (1f + overdose) : 1f);
            if (effect is MovementSpeedModifier movement)
            {
                effect = InheritedMovement(movement, inherited.Fraction * purity);
                multiplier = 1f;
            }
            if (multiplier < inherited.Effect.MinScale || !InheritedConditions(ref args, effect))
                continue;
            if (effect is MovementSpeedModifier speed && effect.Probability >= 1f)
            {
                speed.Conditions = null;
                var key = (speed.EffectProto, speed.Type);
                if (movementEffects.TryGetValue(key, out var previous))
                {
                    previous.WalkSpeedModifier += speed.WalkSpeedModifier - 1f;
                    previous.SprintSpeedModifier += speed.SprintSpeedModifier - 1f;
                    if (previous.Time != null && (speed.Time == null || speed.Time > previous.Time))
                        previous.Time = speed.Time;
                }
                else
                    movementEffects.Add(key, speed);
                continue;
            }
            if (effect is ModifyLungGas)
            {
                var lungs = _bodies.GetBodyOrganEntityComps<LungComponent>((args.Body, null));
                foreach (var lung in lungs)
                    effects.Add(new PharmaceuticalScaledEffect
                    {
                        Effect = effect, Multiplier = multiplier / lungs.Count, Probability = effect.Probability, Target = lung.Owner,
                    });
                continue;
            }
            effects.Add(new PharmaceuticalScaledEffect
            {
                Effect = effect, Multiplier = multiplier, Probability = effect.Probability,
                Target = effect is AdjustReagent ? args.SolutionEntity.Owner : null,
            });
        }
        foreach (var movement in movementEffects.Values)
        {
            movement.WalkSpeedModifier = Math.Max(0.01f, movement.WalkSpeedModifier);
            movement.SprintSpeedModifier = Math.Max(0.01f, movement.SprintSpeedModifier);
            effects.Add(movement);
        }
        if (args.Group == "Medicine")
        {
            var damage = new DamageSpecifier();
            damage.DamageDict["Poison"] =
                FixedPoint2.NewCeiling((ImpurityToxicity(formula, data.Quality) + overdose * 2f) * rate.Float());
            effects.Add(new HealthChange { Damage = damage });
        }
        var group = args.Group;
        if (group != "Medicine" && !formula.Effects.Any(inherited => inherited.Group == group))
        {
            args.Skip = true;
            return;
        }
        args.Entry = new ReagentEffectsEntry
        {
            MetabolismRate = rate,
            Effects = effects.ToArray(),
        };
    }

    private static MovementSpeedModifier InheritedMovement(MovementSpeedModifier source, float strength) => new()
    {
        WalkSpeedModifier = 1f + (source.WalkSpeedModifier - 1f) * strength,
        SprintSpeedModifier = 1f + (source.SprintSpeedModifier - 1f) * strength,
        EffectProto = source.EffectProto, Time = source.Time, Type = source.Type, Delay = source.Delay,
        Probability = source.Probability, Conditions = source.Conditions,
    };

    private void OnPharmaceuticalReaction(ref PharmaceuticalReactionEvent notification)
    {
        if (!TryComp<ReactiveComponent>(notification.Target, out var reactive))
            return;
        Entity<ReactiveComponent> entity = (notification.Target, reactive);
        var args = notification.Reaction;
        if (args.Reagent.ID != PharmaceuticalResearchModel.Product || entity.Comp.ReactiveGroups == null ||
            args.ReagentQuantity.Reagent.Data?.OfType<PharmaceuticalData>().FirstOrDefault() is not { } data ||
            !Model.Formulas.TryGetValue(data.Formula, out var formula))
            return;
        var total = formula.Parts.Values.Sum();
        foreach (var (ingredient, weight) in formula.Parts)
        {
            var reagent = _prototypes.Index<ReagentPrototype>(ingredient);
            if (reagent.ReactiveEffects == null)
                continue;
            foreach (var (group, reaction) in reagent.ReactiveEffects)
            {
                if (!reaction.Methods.Contains(args.Method) || !entity.Comp.ReactiveGroups.TryGetValue(group, out var methods) || !methods.Contains(args.Method))
                    continue;
                foreach (var effect in reaction.Effects)
                {
                    var scale = weight / (float) total * (data.Quality / 100f) *
                        (PharmaceuticalResearchModel.IsHealing(effect) ? formula.HealingMultiplier : 1f);
                    if (args.ReagentQuantity.Quantity.Float() * scale < effect.MinScale)
                        continue;
                    _effects.TryApplyEffect(entity, new PharmaceuticalScaledEffect
                    {
                        Effect = effect, Multiplier = scale, Probability = effect.Probability, Conditions = effect.Conditions,
                    }, args.ReagentQuantity.Quantity.Float());
                    if (Deleted(entity) || Terminating(entity))
                        return;
                }
            }
        }
    }

    private static float ImpurityToxicity(PharmaceuticalFormula formula, int quality) =>
        (formula.Toxicity + 0.8f) * (1f - Math.Clamp(quality / 100f, 0f, 1f));

    private bool InheritedConditions(ref ResolveReagentMetabolismEvent args, EntityEffect effect)
    {
        if (effect.Conditions == null)
            return true;
        foreach (var condition in effect.Conditions)
        {
            if (condition is not ReagentCondition reagentCondition)
            {
                if (!_metabolizer.CanMetabolizeEffect(args.Body, args.Organ, args.SolutionEntity, new[] { condition }))
                    return false;
                continue;
            }
            var quantity = 0f;
            foreach (var reagent in args.Reagents)
            {
                if (reagent.Reagent.Prototype == reagentCondition.Reagent.Id)
                    quantity += reagent.Quantity.Float();
                if (reagent.Reagent.Prototype != PharmaceuticalResearchModel.Product ||
                    reagent.Reagent.Data?.OfType<PharmaceuticalData>().FirstOrDefault() is not { } data ||
                    !Model.Formulas.TryGetValue(data.Formula, out var formula))
                    continue;
                quantity += reagent.Quantity.Float() * formula.Parts.GetValueOrDefault(reagentCondition.Reagent.Id) / formula.Parts.Values.Sum();
            }
            if (quantity < reagentCondition.Min.Float() || quantity > reagentCondition.Max.Float())
                return false;
        }
        return true;
    }

    private static FixedPoint2 MetabolicShare(PharmaceuticalFormula formula, ReagentId reagent, IReadOnlyList<ReagentQuantity> reagents)
    {
        var batches = reagents.Where(r => r.Reagent.Prototype == PharmaceuticalResearchModel.Product &&
            r.Reagent.Data?.OfType<PharmaceuticalData>().FirstOrDefault()?.Formula == formula.Id)
            .OrderBy(r => ReagentKey(r.Reagent), StringComparer.Ordinal).ToArray();
        var volume = batches.Sum(r => (long) r.Quantity.Value);
        var budget = Math.Min((long) FixedPoint2.New(formula.Rate).Value, volume);
        foreach (var batch in batches)
        {
            if (volume <= 0)
                break;
            var share = budget * batch.Quantity.Value / volume;
            if (batch.Reagent == reagent)
                return FixedPoint2.FromCents((int) share);
            budget -= share;
            volume -= batch.Quantity.Value;
        }
        return FixedPoint2.Zero;
    }

    private void Notice(EntityUid uid, string key)
    {
        var state = Session(uid);
        state.Status = Loc.GetString(key);
        if (!state.Executing)
            Refresh(uid);
    }

    private void OnSolutionChanged(Entity<SolutionComponent> ent, ref SolutionChangedEvent args)
    {
        if (ent.Comp.Solution.Contents.Any(r => r.Reagent.Prototype == PharmaceuticalResearchModel.Product))
            _products.Add(ent.Owner);
        else
            _products.Remove(ent.Owner);
        foreach (var (uid, session) in _sessions.ToArray())
        {
            if (session.InputSolution != ent.Owner && session.OutputSolution != ent.Owner)
                continue;
            session.Revision++;
            if (!session.Executing && _ui.IsUiOpen(uid, PharmaceuticalUiKey.Key))
                Refresh(uid);
        }
    }

    private void Finish(EntityUid uid, LaboratorySession state, string key)
    {
        state.Operation = null;
        state.Revision++;
        state.Status = Loc.GetString(key);
        if (!Deleted(uid) && !Terminating(uid))
        {
            _slots.SetLock(uid, InputSlot, false);
            _slots.SetLock(uid, OutputSlot, false);
        }
        CleanupModels(state);
        if (!state.Executing)
            Refresh(uid);
    }

    private void CleanupModels(LaboratorySession state)
    {
        var models = state.Models.ToArray();
        state.Models.Clear();
        state.Baselines.Clear();
        foreach (var uid in models)
            if (!Deleted(uid) && !Terminating(uid))
                QueueDel(uid);
    }

    private bool NotifySolutions(params Entity<SolutionComponent>[] solutions)
    {
        var success = true;
        foreach (var solution in solutions)
        {
            if (Deleted(solution.Owner) || Terminating(solution.Owner))
                continue;
            try
            {
                Dirty(solution);
                if (solution.Comp.Solution.Contents.Any(r => r.Reagent.Prototype == PharmaceuticalResearchModel.Product))
                    _products.Add(solution.Owner);
                else
                    _products.Remove(solution.Owner);
            }
            catch (Exception exception)
            {
                success = false;
                Log.Error($"Pharmaceutical solution state notification failed at {solution.Owner}: {exception}");
            }
        }
        foreach (var solution in solutions)
        {
            if (Deleted(solution.Owner) || Terminating(solution.Owner))
                continue;
            try
            {
                _solutions.UpdateChemicals(solution);
            }
            catch (Exception exception)
            {
                success = false;
                Log.Error($"Pharmaceutical solution notification failed at {solution.Owner}: {exception}");
            }
        }
        return success;
    }

    private static bool Unchanged(Solution current, Solution expected) => current.Volume == expected.Volume &&
        current.MaxVolume == expected.MaxVolume && current.CanReact == expected.CanReact && Math.Abs(current.Temperature - expected.Temperature) < 0.01f &&
        current.Contents.Count == expected.Contents.Count && expected.Contents.All(r => current.Contents.Contains(r));

    private sealed class LaboratorySession
    {
        public long Revision = 1;
        public bool Executing;
        public string Status = string.Empty;
        public string Analysis = string.Empty;
        public TimeSpan Started;
        public TimeSpan Ends;
        public TimeSpan NextRefresh;
        public TimeSpan NextPrint;
        public LaboratoryOperation? Operation;
        public EntityUid? InputSolution;
        public EntityUid? OutputSolution;
        public readonly List<EntityUid> Models = new();
        public readonly Dictionary<EntityUid, Dictionary<string, FixedPoint2>> Baselines = new();
    }

    private sealed class LaboratoryOperation
    {
        public PharmaceuticalAction Action;
        public bool Interrupted;
        public string Selection = string.Empty;
        public string Name = string.Empty;
        public string Author = string.Empty;
        public string[] Ingredients = Array.Empty<string>();
        public float Temperature = 293.15f;
        public FixedPoint2 Amount;
        public EntityUid Input;
        public EntityUid? Output;
        public Solution OriginalInput = default!;
        public Solution? OriginalOutput;
        public PharmaceuticalJournal Journal = default!;
    }
}
