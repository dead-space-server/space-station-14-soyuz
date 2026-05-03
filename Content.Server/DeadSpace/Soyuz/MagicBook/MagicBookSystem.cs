using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Popups;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DeadSpace.Soyuz.MagicBook;
using Content.Shared.DeadSpace.Soyuz.RuneBook;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Stunnable;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.Soyuz.MagicBook;

public sealed class MagicBookSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly SharedStealthSystem _stealth = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MagicBookComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MagicBookComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<MagicBookComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<MagicBookComponent, MagicBookSelectPageMessage>(OnSelectPage);
        SubscribeLocalEvent<MagicBookComponent, MagicBookInsertPageMessage>(OnInsertPage);
        SubscribeLocalEvent<MagicBookComponent, MagicBookSetRuneSlotMessage>(OnSetRuneSlot);
        SubscribeLocalEvent<MagicBookComponent, MagicBookClearRuneSlotMessage>(OnClearRuneSlot);
        SubscribeLocalEvent<MagicBookComponent, MagicBookSaveSpellMessage>(OnSaveSpell);
        SubscribeLocalEvent<MagicBookSpellActionComponent, MagicBookCastSpellEvent>(OnCastSpell);
    }

    private void OnMapInit(EntityUid uid, MagicBookComponent component, MapInitEvent args)
    {
        EnsureKnownRunes(component);
        EnsureActivePage(component);
    }

    private void OnUiOpened(EntityUid uid, MagicBookComponent component, BoundUIOpenedEvent args)
    {
        EnsureKnownRunes(component);
        EnsureActivePage(component);
        GrantSavedSpellActions(uid, component, args.Actor);
        UpdateUi(uid, component);
    }

    private void OnInteractUsing(EntityUid uid, MagicBookComponent component, InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<RuneBookRuneSheetComponent>(args.Used))
            return;

        args.Handled = TryInsertPage(uid, component, args.User, args.Used);
    }

    private void OnSelectPage(EntityUid uid, MagicBookComponent component, MagicBookSelectPageMessage args)
    {
        if (component.Pages.Count == 0)
        {
            component.ActivePage = -1;
            UpdateUi(uid, component);
            return;
        }

        component.ActivePage = Math.Clamp(args.Page, 0, component.Pages.Count - 1);
        UpdateUi(uid, component);
    }

    private void OnInsertPage(EntityUid uid, MagicBookComponent component, MagicBookInsertPageMessage args)
    {
        if (!TryComp<HandsComponent>(args.Actor, out var hands) ||
            !_hands.TryGetActiveItem((args.Actor, hands), out var held) ||
            held is not { } page ||
            !HasComp<RuneBookRuneSheetComponent>(page))
        {
            _popup.PopupEntity("Нужна вырванная страница в активной руке.", args.Actor, args.Actor, PopupType.Medium);
            return;
        }

        TryInsertPage(uid, component, args.Actor, page);
    }

    private void OnSetRuneSlot(EntityUid uid, MagicBookComponent component, MagicBookSetRuneSlotMessage args)
    {
        if (!TryGetEditablePage(component, out var page))
            return;

        if (args.Slot < 0 || args.Slot >= MagicBookSpellData.RuneSlotCount)
            return;

        if (!component.KnownRunes.Contains(args.RuneIndex) || !TryGetRune(args.RuneIndex, out var rune))
            return;

        var category = MagicBookRuneRules.GetCategory(rune);
        if (args.Slot == 0 && category != MagicRuneCategory.Form)
            return;

        if (args.Slot > 0 && category == MagicRuneCategory.Form)
            return;

        page.Spell.Runes[args.Slot] = args.RuneIndex;
        page.PageState = MagicBookPageState.Editing;
        page.Spell = BuildSpell(page.Spell.Runes, page.Spell.Name);
        UpdateUi(uid, component);
    }

    private void OnClearRuneSlot(EntityUid uid, MagicBookComponent component, MagicBookClearRuneSlotMessage args)
    {
        if (!TryGetEditablePage(component, out var page))
            return;

        if (args.Slot < 0 || args.Slot >= MagicBookSpellData.RuneSlotCount)
            return;

        page.Spell.Runes[args.Slot] = -1;
        page.PageState = MagicBookPageState.Editing;
        page.Spell = BuildSpell(page.Spell.Runes, page.Spell.Name);
        UpdateUi(uid, component);
    }

    private void OnSaveSpell(EntityUid uid, MagicBookComponent component, MagicBookSaveSpellMessage args)
    {
        if (!TryGetEditablePage(component, out var page))
            return;

        var spellName = string.IsNullOrWhiteSpace(args.Name)
            ? $"Заклинание {component.SavedSpells.Count + 1}"
            : args.Name.Trim();

        if (spellName.Length > 48)
            spellName = spellName[..48];

        var spell = BuildSpell(page.Spell.Runes, spellName);
        if (!spell.IsValid)
        {
            page.Spell = spell;
            UpdateUi(uid, component);
            return;
        }

        spell.Id = string.IsNullOrWhiteSpace(spell.Id)
            ? Guid.NewGuid().ToString("N")
            : spell.Id;

        page.Spell = spell.Clone();
        page.PageState = MagicBookPageState.Saved;
        component.SavedSpells.RemoveAll(existing => existing.Id == spell.Id);
        component.SavedSpells.Add(spell.Clone());

        GrantSpellAction(uid, component, args.Actor, spell);
        _popup.PopupEntity("Заклинание сохранено и добавлено в меню способностей.", args.Actor, args.Actor, PopupType.Medium);
        UpdateUi(uid, component);
    }

    private void OnCastSpell(Entity<MagicBookSpellActionComponent> ent, ref MagicBookCastSpellEvent args)
    {
        if (args.Handled || !ent.Comp.Spell.IsValid)
            return;

        args.Handled = true;
        CastSpell(args.Performer, _transform.ToMapCoordinates(args.Target), ent.Comp.Spell.Clone());
    }

    private bool TryInsertPage(EntityUid book, MagicBookComponent component, EntityUid user, EntityUid tornPage)
    {
        if (!component.PagesUnlocked)
        {
            _popup.PopupEntity("Требуется завершить ритуал.", user, user, PopupType.Medium);
            UpdateUi(book, component);
            return true;
        }

        if (component.Pages.Count >= component.MaxPages)
        {
            _popup.PopupEntity("В книге больше нет места для страниц.", user, user, PopupType.Medium);
            UpdateUi(book, component);
            return true;
        }

        var page = new MagicBookPageData
        {
            Id = Guid.NewGuid().ToString("N"),
            Inserted = true,
            PageState = MagicBookPageState.Editing,
            Spell = BuildSpell(MagicBookSpellData.EmptyRunes(), string.Empty),
        };

        component.Pages.Add(page);
        component.ActivePage = component.Pages.Count - 1;

        if (TryComp<RuneBookRuneSheetComponent>(tornPage, out var runeSheet) && runeSheet.RuneIndex >= 0)
            component.KnownRunes.Add(runeSheet.RuneIndex);

        QueueDel(tornPage);
        _popup.PopupEntity("Страница вставлена в книгу магии.", user, user, PopupType.Medium);
        UpdateUi(book, component);
        return true;
    }

    private bool TryGetEditablePage(MagicBookComponent component, out MagicBookPageData page)
    {
        page = default!;
        if (component.ActivePage < 0 || component.ActivePage >= component.Pages.Count)
            return false;

        page = component.Pages[component.ActivePage];
        return page.Inserted && page.PageState != MagicBookPageState.Broken;
    }

    private void EnsureKnownRunes(MagicBookComponent component)
    {
        if (!component.KnowAllRunes)
            return;

        for (var i = 0; i < RuneBookRuneLibrary.RuneCount; i++)
            component.KnownRunes.Add(i);
    }

    private void EnsureActivePage(MagicBookComponent component)
    {
        if (component.Pages.Count == 0)
        {
            component.ActivePage = -1;
            return;
        }

        component.ActivePage = Math.Clamp(component.ActivePage, 0, component.Pages.Count - 1);
    }

    private void UpdateUi(EntityUid uid, MagicBookComponent component)
    {
        EnsureKnownRunes(component);
        EnsureActivePage(component);

        var pages = component.Pages
            .Select(page => new MagicBookPageUiState(page.Id, page.PageState, page.Spell.Clone()))
            .ToArray();

        var knownRunes = component.KnownRunes
            .OrderBy(index => index)
            .Select(BuildRuneUiEntry)
            .Where(entry => entry != null)
            .Cast<MagicBookRuneUiEntry>()
            .ToArray();

        var preview = component.ActivePage >= 0 && component.ActivePage < component.Pages.Count
            ? BuildSpell(component.Pages[component.ActivePage].Spell.Runes, component.Pages[component.ActivePage].Spell.Name)
            : MagicBookSpellData.Empty();

        if (component.ActivePage >= 0 && component.ActivePage < component.Pages.Count &&
            component.Pages[component.ActivePage].PageState != MagicBookPageState.Saved)
        {
            component.Pages[component.ActivePage].Spell = preview.Clone();
        }

        _ui.SetUiState(uid,
            MagicBookUiKey.Key,
            new MagicBookBoundUserInterfaceState(
                component.PagesUnlocked,
                component.MaxPages,
                component.ActivePage,
                pages,
                knownRunes,
                preview));
    }

    private MagicBookRuneUiEntry? BuildRuneUiEntry(int index)
    {
        if (!TryGetRune(index, out var rune) ||
            !RuneBookRuneLibrary.TryGetRunePrototypeId(index, out var prototypeId))
            return null;

        var tags = MagicBookRuneRules.GetTags(rune);
        return new MagicBookRuneUiEntry(
            index,
            prototypeId,
            rune.Name,
            MagicBookRuneRules.GetCategory(rune),
            tags.OrderBy(tag => tag).ToArray(),
            MagicBookRuneRules.GetManaCost(rune),
            MagicBookRuneRules.IsStackable(rune),
            MagicBookRuneRules.GetMaxStacks(rune),
            MagicBookRuneRules.GetEffectHandler(rune));
    }

    private bool TryGetRune(int index, out RuneBookRunePrototype rune)
    {
        if (RuneBookRuneLibrary.TryGetRunePrototypeId(index, out var prototypeId) &&
            _proto.TryIndex<RuneBookRunePrototype>(prototypeId, out var found))
        {
            rune = found!;
            return true;
        }

        rune = default!;
        return false;
    }

    private MagicBookSpellData BuildSpell(int[] inputRunes, string name)
    {
        var runes = MagicBookSpellData.EmptyRunes();
        for (var i = 0; i < Math.Min(runes.Length, inputRunes.Length); i++)
            runes[i] = inputRunes[i];

        var errors = new List<string>();
        var modifiers = new List<int>();
        var effects = new List<int>();
        var manaCost = 0;
        var form = -1;
        var stability = 100f;

        if (runes.Length != MagicBookSpellData.RuneSlotCount || runes.Any(rune => rune < 0))
            errors.Add("В заклинании должно быть ровно 5 рун.");

        for (var i = 0; i < runes.Length; i++)
        {
            if (!TryGetRune(runes[i], out var rune))
                continue;

            manaCost += MagicBookRuneRules.GetManaCost(rune);
            var category = MagicBookRuneRules.GetCategory(rune);

            if (i == 0)
            {
                if (category != MagicRuneCategory.Form)
                    errors.Add("Первый слот принимает только руну формы.");
                else
                    form = runes[i];

                continue;
            }

            if (category == MagicRuneCategory.Form)
                errors.Add($"Слот {i + 1} принимает только модификаторы и эффекты.");
            else if (category == MagicRuneCategory.Modifier)
                modifiers.Add(runes[i]);
            else
                effects.Add(runes[i]);
        }

        if (effects.Count == 0)
        {
            errors.Add("В слотах 2-5 должна быть хотя бы одна EFFECT-руна.");
            errors.Add("Нельзя сохранить заклинание только из формы и модификаторов.");
        }

        ValidateRandomRule(runes, errors);
        ValidateResetScopedConflicts(runes, errors);
        ValidateFormRules(form, modifiers, effects, errors);
        ValidateStacks(runes, errors);
        ValidateCombatRitualMix(effects, errors);

        var stats = BuildRuntimeStats(runes);
        manaCost = Math.Max(1, (int) MathF.Round(manaCost * stats.ManaCostMultiplier));

        stability -= MathF.Max(0, manaCost - 45) * 0.7f;
        stability -= modifiers.Count * 2f;
        stability -= effects.Count > 1 ? (effects.Count - 1) * 4f : 0f;

        if (stats.RandomEffect)
            stability -= 8f;

        if (stats.PersistentArea)
            stability -= 8f;

        if (stats.ProjectileCount > 1)
            stability -= stats.ProjectileCount * 3f;

        stability = Math.Clamp(stability, 0f, 100f);

        return new MagicBookSpellData
        {
            Id = string.Empty,
            Name = name,
            Runes = runes,
            FormRune = form,
            Modifiers = modifiers.ToArray(),
            Effects = effects.ToArray(),
            ManaCost = manaCost,
            Stability = stability,
            Risk = 100f - stability,
            IsValid = errors.Count == 0,
            ValidationErrors = errors.Distinct().ToArray(),
            PowerMultiplier = stats.PowerMultiplier,
            Radius = stats.Radius,
            ProjectileSpeed = stats.ProjectileSpeed,
            CastSpeed = stats.CastSpeed,
            PenetrationCount = stats.PenetrationCount,
            ProjectileCount = stats.ProjectileCount,
            ChainTargets = stats.ChainTargets,
            BounceCount = stats.BounceCount,
            DelayedActivation = stats.DelayedActivation,
            PersistentArea = stats.PersistentArea,
            OrbitAroundCaster = stats.OrbitAroundCaster,
            RandomEffect = stats.RandomEffect,
        };
    }

    private void ValidateRandomRule(int[] runes, List<string> errors)
    {
        for (var i = 1; i < runes.Length; i++)
        {
            if (runes[i] != MagicBookRuneRules.ModifierRandom)
                continue;

            var followingEffects = 0;
            for (var j = i + 1; j < runes.Length; j++)
            {
                if (TryGetRune(runes[j], out var rune) &&
                    MagicBookRuneRules.GetCategory(rune) == MagicRuneCategory.Effect)
                {
                    followingEffects++;
                }
            }

            if (followingEffects < 2)
                errors.Add("Если есть Случайность, после нее должно быть минимум 2 эффекта.");
        }
    }

    private void ValidateResetScopedConflicts(int[] runes, List<string> errors)
    {
        var hasAcceleration = false;
        var hasSlow = false;

        for (var i = 1; i < runes.Length; i++)
        {
            switch (runes[i])
            {
                case MagicBookRuneRules.ModifierReset:
                    hasAcceleration = false;
                    hasSlow = false;
                    break;
                case MagicBookRuneRules.ModifierAcceleration:
                    hasAcceleration = true;
                    break;
                case MagicBookRuneRules.ModifierSlow:
                    hasSlow = true;
                    break;
            }

            if (hasAcceleration && hasSlow)
            {
                errors.Add("Ускорение и Замедление конфликтуют без Сброса между ними.");
                return;
            }
        }
    }

    private void ValidateFormRules(int form, List<int> modifiers, List<int> effects, List<string> errors)
    {
        if (modifiers.Contains(MagicBookRuneRules.ModifierOrbit) && form != MagicBookRuneRules.FormSelf)
            errors.Add("Орбита работает только с формой На себя.");

        if ((modifiers.Contains(MagicBookRuneRules.ModifierBounce) ||
             modifiers.Contains(MagicBookRuneRules.ModifierPierce) ||
             modifiers.Contains(MagicBookRuneRules.ModifierSplit)) &&
            !MagicBookRuneRules.ProjectileForms.Contains(form))
        {
            errors.Add("Отскок, Пробивание и Разделение работают только со снарядными формами.");
        }

        if (modifiers.Contains(MagicBookRuneRules.ModifierMist) &&
            !effects.Any(MagicBookRuneRules.AreaOrDurationEffects.Contains))
        {
            errors.Add("Туман работает только с эффектами области или длительными эффектами.");
        }

        if (modifiers.Contains(MagicBookRuneRules.ModifierSensitivity) &&
            !CanHitMultipleTargets(form, modifiers, effects))
        {
            errors.Add("Чувствительность работает только если заклинание может задеть несколько целей.");
        }
    }

    private static bool CanHitMultipleTargets(int form, List<int> modifiers, List<int> effects)
    {
        return form == MagicBookRuneRules.FormUnderFeet ||
               modifiers.Contains(MagicBookRuneRules.ModifierArea) ||
               modifiers.Contains(MagicBookRuneRules.ModifierSplit) ||
               modifiers.Contains(MagicBookRuneRules.ModifierArc) ||
               modifiers.Contains(MagicBookRuneRules.ModifierMist) ||
               effects.Any(MagicBookRuneRules.AreaOrDurationEffects.Contains);
    }

    private void ValidateStacks(int[] runes, List<string> errors)
    {
        var counts = new Dictionary<int, int>();
        foreach (var runeIndex in runes)
        {
            if (runeIndex < 0 || !TryGetRune(runeIndex, out var rune))
                continue;

            counts[runeIndex] = counts.GetValueOrDefault(runeIndex) + 1;
            var count = counts[runeIndex];
            var maxStacks = MagicBookRuneRules.GetMaxStacks(rune);

            if (count > maxStacks)
                errors.Add($"{rune.Name}: превышен лимит стаков ({maxStacks}).");

            if (count > 1 && !MagicBookRuneRules.IsStackable(rune))
                errors.Add($"{rune.Name}: эта руна не стакается.");
        }
    }

    private static void ValidateCombatRitualMix(List<int> effects, List<string> errors)
    {
        if (effects.Any(MagicBookRuneRules.DirectCombatEffects.Contains) &&
            effects.Any(MagicBookRuneRules.RitualOnlyEffects.Contains))
        {
            errors.Add("Прямые боевые эффекты нельзя совмещать с чисто ритуальными эффектами.");
        }
    }

    private MagicBookRuntimeStats BuildRuntimeStats(int[] runes)
    {
        var stats = new MagicBookRuntimeStats();

        for (var i = 1; i < runes.Length; i++)
        {
            switch (runes[i])
            {
                case MagicBookRuneRules.ModifierAmplify:
                    stats.PowerMultiplier += 0.35f;
                    stats.ManaCostMultiplier += 0.22f;
                    break;
                case MagicBookRuneRules.ModifierWeaken:
                    stats.PowerMultiplier = MathF.Max(0.25f, stats.PowerMultiplier - 0.25f);
                    stats.ManaCostMultiplier = MathF.Max(0.25f, stats.ManaCostMultiplier - 0.15f);
                    break;
                case MagicBookRuneRules.ModifierArea:
                    stats.Radius += 1.25f;
                    stats.ManaCostMultiplier += 0.18f;
                    break;
                case MagicBookRuneRules.ModifierAcceleration:
                    stats.ProjectileSpeed += 0.45f;
                    stats.CastSpeed += 0.25f;
                    break;
                case MagicBookRuneRules.ModifierSlow:
                    stats.SlowPower += 1f;
                    break;
                case MagicBookRuneRules.ModifierPierce:
                    stats.PenetrationCount++;
                    break;
                case MagicBookRuneRules.ModifierSplit:
                    stats.ProjectileCount++;
                    stats.ManaCostMultiplier += 0.2f;
                    break;
                case MagicBookRuneRules.ModifierArc:
                    stats.ChainTargets += 2;
                    stats.ManaCostMultiplier += 0.15f;
                    break;
                case MagicBookRuneRules.ModifierBounce:
                    stats.BounceCount++;
                    break;
                case MagicBookRuneRules.ModifierDelay:
                    stats.DelayedActivation = true;
                    break;
                case MagicBookRuneRules.ModifierMist:
                    stats.PersistentArea = true;
                    stats.Radius = MathF.Max(stats.Radius, 2.5f);
                    break;
                case MagicBookRuneRules.ModifierOrbit:
                    stats.OrbitAroundCaster = true;
                    stats.Radius = MathF.Max(stats.Radius, 2f);
                    break;
                case MagicBookRuneRules.ModifierRandom:
                    stats.RandomEffect = true;
                    break;
                case MagicBookRuneRules.ModifierShortenDuration:
                    stats.DurationMultiplier = MathF.Max(0.25f, stats.DurationMultiplier - 0.25f);
                    stats.ManaCostMultiplier = MathF.Max(0.5f, stats.ManaCostMultiplier - 0.1f);
                    break;
                case MagicBookRuneRules.ModifierExtendTime:
                    stats.DurationMultiplier += 0.5f;
                    stats.ManaCostMultiplier += 0.12f;
                    break;
                case MagicBookRuneRules.ModifierReset:
                    stats.ResetTransientModifiers();
                    break;
            }
        }

        if (stats.ProjectileCount < 1)
            stats.ProjectileCount = 1;

        return stats;
    }

    private void GrantSavedSpellActions(EntityUid book, MagicBookComponent component, EntityUid actor)
    {
        foreach (var spell in component.SavedSpells.Where(spell => spell.IsValid))
            GrantSpellAction(book, component, actor, spell);
    }

    private void GrantSpellAction(EntityUid book, MagicBookComponent component, EntityUid actor, MagicBookSpellData spell)
    {
        if (HasSpellAction(actor, spell.Id))
            return;

        EntityUid? action = null;
        if (!_actions.AddAction(actor, ref action, component.SpellActionPrototype))
            return;

        var actionUid = action.Value;
        var actionComp = EnsureComp<MagicBookSpellActionComponent>(actionUid);
        actionComp.Spell = spell.Clone();

        _meta.SetEntityName(actionUid, spell.Name);
        _meta.SetEntityDescription(actionUid, $"Мана: {spell.ManaCost}. Риск: {spell.Risk:0}%.");

        if (TryComp<ActionComponent>(actionUid, out var baseAction))
            _actions.SetUseDelay((actionUid, baseAction), TimeSpan.FromSeconds(Math.Clamp(12f / MathF.Max(spell.CastSpeed, 0.25f), 3f, 30f)));
    }

    private bool HasSpellAction(EntityUid actor, string spellId)
    {
        foreach (var action in _actions.GetActions(actor))
        {
            if (TryComp<MagicBookSpellActionComponent>(action.Owner, out var spellAction) &&
                spellAction.Spell.Id == spellId)
            {
                return true;
            }
        }

        return false;
    }

    private void CastSpell(EntityUid caster, MapCoordinates target, MagicBookSpellData spell)
    {
        var center = spell.FormRune switch
        {
            MagicBookRuneRules.FormSelf => _transform.GetMapCoordinates(caster),
            MagicBookRuneRules.FormUnderFeet => _transform.GetMapCoordinates(caster),
            _ => target
        };

        var delay = spell.DelayedActivation ? TimeSpan.FromSeconds(2.5f) : TimeSpan.Zero;
        if (delay > TimeSpan.Zero)
        {
            Timer.Spawn(delay, () =>
            {
                if (!Deleted(caster))
                    ApplySpell(caster, center, spell);
            });
            return;
        }

        ApplySpell(caster, center, spell);
    }

    private void ApplySpell(EntityUid caster, MapCoordinates center, MagicBookSpellData spell)
    {
        var effects = spell.RandomEffect && spell.Effects.Length > 0
            ? new[] { _random.Pick(spell.Effects) }
            : spell.Effects;

        var tickCount = spell.PersistentArea ? 4 : 1;
        for (var tick = 0; tick < tickCount; tick++)
        {
            var currentTick = tick;
            Timer.Spawn(TimeSpan.FromSeconds(currentTick), () =>
            {
                if (Deleted(caster))
                    return;

                var targets = CollectTargets(caster, center, spell);
                foreach (var effect in effects)
                    ApplyEffect(caster, center, targets, spell, effect);
            });
        }
    }

    private List<EntityUid> CollectTargets(EntityUid caster, MapCoordinates center, MagicBookSpellData spell)
    {
        if (spell.FormRune == MagicBookRuneRules.FormSelf)
            return new List<EntityUid> { caster };

        var radius = spell.Radius > 0 ? spell.Radius : 0.85f;
        if (spell.FormRune == MagicBookRuneRules.FormUnderFeet)
            radius = MathF.Max(radius, 1.5f);

        var candidates = new HashSet<EntityUid>();
        _lookup.GetEntitiesInRange(center.MapId, center.Position, radius, candidates, LookupFlags.Dynamic | LookupFlags.Sundries);

        candidates.Remove(caster);

        var targetLimit = spell.Radius > 0 || spell.PersistentArea
            ? int.MaxValue
            : Math.Max(1, spell.ProjectileCount + spell.ChainTargets + spell.BounceCount + spell.PenetrationCount);

        return candidates
            .OrderBy(ent => (_transform.GetMapCoordinates(ent).Position - center.Position).LengthSquared())
            .Take(targetLimit)
            .ToList();
    }

    private void ApplyEffect(EntityUid caster, MapCoordinates center, List<EntityUid> targets, MagicBookSpellData spell, int effect)
    {
        var power = MathF.Max(0.1f, spell.PowerMultiplier);

        switch (effect)
        {
            case MagicBookRuneRules.EffectCut:
                DamageTargets(targets, "Slash", 10f * power, caster);
                break;
            case MagicBookRuneRules.EffectDamage:
                DamageTargets(targets, "Caustic", 12f * power, caster);
                break;
            case MagicBookRuneRules.EffectIgnite:
                IgniteTargets(targets, 2.2f * power, caster);
                break;
            case MagicBookRuneRules.EffectKnockback:
                PushTargets(caster, targets, 900f * power, awayFromCaster: true);
                break;
            case MagicBookRuneRules.EffectCrush:
                DamageTargets(targets, "Blunt", 18f * power, caster);
                KnockdownTargets(targets, TimeSpan.FromSeconds(2 * power));
                break;
            case MagicBookRuneRules.EffectDischarge:
                Spawn("Lightning", center);
                DamageTargets(targets, "Shock", 11f * power, caster);
                StunTargets(targets, TimeSpan.FromSeconds(1.5f * power));
                break;
            case MagicBookRuneRules.EffectPoison:
                DamageTargets(targets, "Poison", 8f * power, caster);
                ScheduleDamage(targets, "Poison", 2f * power, caster, 5);
                break;
            case MagicBookRuneRules.EffectExplosion:
                _explosion.QueueExplosion(center, ExplosionSystem.DefaultExplosionPrototypeId, 18f * power, 2f, 10f * power, caster, maxTileBreak: 0);
                break;
            case MagicBookRuneRules.EffectPoisonSpores:
                Spawn("EffectAnomalyFloraBulb", center);
                DamageTargets(targets, "Poison", 6f * power, caster);
                break;
            case MagicBookRuneRules.EffectSpike:
                DamageTargets(targets, "Piercing", 14f * power, caster);
                break;
            case MagicBookRuneRules.EffectDestroy:
                DestroyWalls(center, Math.Max(1, (int) MathF.Ceiling(power)));
                DamageTargets(targets, "Structural", 30f * power, caster);
                break;
            case MagicBookRuneRules.EffectCreateWalls:
                SpawnWallLine(center, 3);
                break;
            case MagicBookRuneRules.EffectCreateLight:
                Spawn("DS14SoyuzMagicLight", center);
                break;
            case MagicBookRuneRules.EffectCreate:
                Spawn("DS14SoyuzMagicShard", center);
                break;
            case MagicBookRuneRules.EffectEvaporate:
            case MagicBookRuneRules.EffectWindShift:
                Spawn("DS14SoyuzMagicAirPulse", center);
                KnockdownTargets(targets, TimeSpan.FromSeconds(0.25f));
                break;
            case MagicBookRuneRules.EffectChop:
            case MagicBookRuneRules.EffectGather:
                DamageTargets(targets, "Slash", 8f * power, caster);
                DamageTargets(targets, "Structural", 18f * power, caster);
                break;
            case MagicBookRuneRules.EffectInteract:
                StunTargets(targets, TimeSpan.FromSeconds(0.75f));
                break;
            case MagicBookRuneRules.EffectTemporaryWall:
                SpawnWallLine(center, 3, timed: true);
                break;
            case MagicBookRuneRules.EffectJump:
                JumpCaster(caster, center, power);
                break;
            case MagicBookRuneRules.EffectPull:
                PushTargets(caster, targets, 750f * power, awayFromCaster: false);
                KnockdownTargets(targets, TimeSpan.FromSeconds(1.5f));
                break;
            case MagicBookRuneRules.EffectTeleport:
                SwapWithFirstTarget(caster, targets);
                break;
            case MagicBookRuneRules.EffectSlide:
                PushTargets(caster, new List<EntityUid> { caster }, 600f * power, awayFromCaster: true);
                break;
            case MagicBookRuneRules.EffectRewind:
                foreach (var target in targets)
                    _transform.SetCoordinates(target, Transform(caster).Coordinates);
                break;
            case MagicBookRuneRules.EffectThrow:
                PushTargets(caster, targets, 1300f * power, awayFromCaster: true);
                KnockdownTargets(targets, TimeSpan.FromSeconds(2f));
                break;
            case MagicBookRuneRules.EffectLuck:
            case MagicBookRuneRules.EffectFirework:
            case MagicBookRuneRules.EffectSignalFlare:
                Spawn("EffectSparks", center);
                Spawn("ExplosionLight", center);
                break;
            case MagicBookRuneRules.EffectHeal:
                HealTargets(targets.Count == 0 ? new List<EntityUid> { caster } : targets, 18f * power, caster);
                break;
            case MagicBookRuneRules.EffectSatiate:
            case MagicBookRuneRules.EffectManaBubble:
                HealTargets(targets.Count == 0 ? new List<EntityUid> { caster } : targets, 8f * power, caster);
                Spawn("DS14SoyuzMagicLight", center);
                break;
            case MagicBookRuneRules.EffectInvisibility:
                ApplyStealth(targets.Count == 0 ? new List<EntityUid> { caster } : targets, TimeSpan.FromSeconds(10f * power));
                break;
            case MagicBookRuneRules.EffectEthereal:
                ApplyStealth(new List<EntityUid> { caster }, TimeSpan.FromSeconds(6f * power));
                StunTargets(targets, TimeSpan.FromSeconds(0.5f));
                break;
            case MagicBookRuneRules.EffectLifeLink:
                HealTargets(targets, 6f * power, caster);
                DamageTargets(targets, "Blunt", 2f, caster);
                break;
            case MagicBookRuneRules.EffectStorageAccess:
                Spawn("DS14SoyuzMagicShard", center);
                break;
            case MagicBookRuneRules.EffectTrap:
                Spawn("DS14SoyuzMagicTrap", center);
                break;
            case MagicBookRuneRules.EffectSummonFauna:
                Spawn("MobCarpMagic", center);
                break;
            case MagicBookRuneRules.EffectWololo:
            case MagicBookRuneRules.EffectCharm:
                PacifyTargets(targets, TimeSpan.FromSeconds(10f * power));
                break;
            case MagicBookRuneRules.EffectPhantomGrip:
                StunTargets(targets, TimeSpan.FromSeconds(3f * power));
                break;
            case MagicBookRuneRules.EffectSummonDecoy:
                Spawn("MobMouse", center);
                break;
            case MagicBookRuneRules.EffectSummonUndead:
                for (var i = 0; i < 3; i++)
                    Spawn("MobSkeletonCloset", center.Offset(new Vector2(i - 1, 0)));
                break;
            case MagicBookRuneRules.EffectSummonVex:
                Spawn("MobCarpMagic", center);
                break;
        }
    }

    private void DamageTargets(IEnumerable<EntityUid> targets, string damageType, float amount, EntityUid caster)
    {
        var damage = new DamageSpecifier
        {
            DamageDict = new()
            {
                { damageType, amount }
            }
        };

        foreach (var target in targets)
            _damage.TryChangeDamage(target, damage, origin: caster);
    }

    private void HealTargets(IEnumerable<EntityUid> targets, float amount, EntityUid caster)
    {
        foreach (var target in targets)
            _damage.HealEvenly(target, -amount, origin: caster);
    }

    private void ScheduleDamage(IEnumerable<EntityUid> targets, string damageType, float amount, EntityUid caster, int ticks)
    {
        var snapshot = targets.ToArray();
        for (var i = 1; i <= ticks; i++)
        {
            Timer.Spawn(TimeSpan.FromSeconds(i), () =>
            {
                var validTargets = snapshot.Where(target => !Deleted(target));
                DamageTargets(validTargets, damageType, amount, caster);
            });
        }
    }

    private void IgniteTargets(IEnumerable<EntityUid> targets, float stacks, EntityUid caster)
    {
        foreach (var target in targets)
        {
            if (!TryComp<FlammableComponent>(target, out var flammable))
                continue;

            _flammable.AdjustFireStacks(target, stacks, flammable, ignite: true);
            _flammable.Ignite(target, caster, flammable);
        }
    }

    private void StunTargets(IEnumerable<EntityUid> targets, TimeSpan duration)
    {
        foreach (var target in targets)
            _stun.TryUpdateStunDuration(target, duration);
    }

    private void KnockdownTargets(IEnumerable<EntityUid> targets, TimeSpan duration)
    {
        foreach (var target in targets)
            _stun.TryKnockdown(target, duration, force: true);
    }

    private void PushTargets(EntityUid caster, IEnumerable<EntityUid> targets, float impulse, bool awayFromCaster)
    {
        var casterPos = _transform.GetMapCoordinates(caster).Position;
        foreach (var target in targets)
        {
            var targetPos = _transform.GetMapCoordinates(target).Position;
            var direction = awayFromCaster ? targetPos - casterPos : casterPos - targetPos;
            if (direction.LengthSquared() < 0.01f)
                direction = Transform(caster).LocalRotation.ToWorldVec();
            else
                direction = Vector2.Normalize(direction);

            _physics.ApplyLinearImpulse(target, direction * impulse);
        }
    }

    private void JumpCaster(EntityUid caster, MapCoordinates target, float power)
    {
        var xform = Transform(caster);
        var direction = target.Position - _transform.GetMapCoordinates(caster).Position;
        if (direction.LengthSquared() < 0.01f)
            direction = xform.LocalRotation.ToWorldVec();

        direction = Vector2.Normalize(direction);
        _transform.SetCoordinates(caster, xform.Coordinates.Offset(direction * Math.Clamp(2.5f * power, 1.5f, 6f)));
        _transform.AttachToGridOrMap(caster, xform);
    }

    private void SwapWithFirstTarget(EntityUid caster, List<EntityUid> targets)
    {
        if (targets.Count == 0)
            return;

        _transform.SwapPositions(caster, targets[0]);
    }

    private void DestroyWalls(MapCoordinates center, int count)
    {
        var entities = new HashSet<EntityUid>();
        _lookup.GetEntitiesInRange(center.MapId, center.Position, 1f, entities, LookupFlags.Static | LookupFlags.Approximate);

        foreach (var ent in entities.Where(ent => _tag.HasTag(ent, WallTag)).Take(count))
            QueueDel(ent);
    }

    private void SpawnWallLine(MapCoordinates center, int count, bool timed = false)
    {
        for (var i = 0; i < count; i++)
        {
            var wall = Spawn("WallForce", center.Offset(new Vector2(i - count / 2, 0)));
            if (timed)
            {
                Timer.Spawn(TimeSpan.FromSeconds(12), () =>
                {
                    if (!Deleted(wall))
                        QueueDel(wall);
                });
            }
        }
    }

    private void ApplyStealth(IEnumerable<EntityUid> targets, TimeSpan duration)
    {
        foreach (var target in targets)
        {
            var stealth = EnsureComp<StealthComponent>(target);
            _stealth.SetEnabled(target, true, stealth);
            _stealth.SetVisibility(target, -1f, stealth);

            Timer.Spawn(duration, () =>
            {
                if (Deleted(target))
                    return;

                RemComp<StealthComponent>(target);
            });
        }
    }

    private void PacifyTargets(IEnumerable<EntityUid> targets, TimeSpan duration)
    {
        foreach (var target in targets)
        {
            EnsureComp<PacifiedComponent>(target);

            Timer.Spawn(duration, () =>
            {
                if (Deleted(target))
                    return;

                RemComp<PacifiedComponent>(target);
            });
        }
    }

    private sealed class MagicBookRuntimeStats
    {
        public float PowerMultiplier = 1f;
        public float ManaCostMultiplier = 1f;
        public float Radius;
        public float ProjectileSpeed = 1f;
        public float CastSpeed = 1f;
        public float SlowPower;
        public float DurationMultiplier = 1f;
        public int PenetrationCount;
        public int ProjectileCount = 1;
        public int ChainTargets;
        public int BounceCount;
        public bool DelayedActivation;
        public bool PersistentArea;
        public bool OrbitAroundCaster;
        public bool RandomEffect;

        public void ResetTransientModifiers()
        {
            PowerMultiplier = 1f;
            Radius = 0f;
            ProjectileSpeed = 1f;
            CastSpeed = 1f;
            SlowPower = 0f;
            DurationMultiplier = 1f;
            PenetrationCount = 0;
            ProjectileCount = 1;
            ChainTargets = 0;
            BounceCount = 0;
            DelayedActivation = false;
            PersistentArea = false;
            OrbitAroundCaster = false;
            RandomEffect = false;
        }
    }
}
