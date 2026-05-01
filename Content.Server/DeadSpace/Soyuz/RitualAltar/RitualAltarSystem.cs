using Content.Server.Popups;
using Content.Shared.Database;
using Content.Shared.DeadSpace.Soyuz.RitualAltar;
using Content.Shared.DeadSpace.Soyuz.RuneBook;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.DeadSpace.Soyuz.RitualAltar;

public sealed class RitualAltarSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextUse = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RitualAltarComponent, GetVerbsEvent<ActivationVerb>>(OnGetVerbs);
    }

    private void OnGetVerbs(EntityUid uid, RitualAltarComponent comp, GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (!TryComp(args.User, out ActorComponent? actor))
            return;

        var verb = new ActivationVerb
        {
            Text = Loc.GetString("ritual-altar-verb"),
            Icon = new SpriteSpecifier.Rsi(new ResPath("/Textures/Structures/Magic/Cult/rune.rsi"), "cult1"),
            Act = () =>
            {
                if (actor.PlayerSession is { } session)
                    TryStartRitual(uid, comp, session);
            },
            Impact = LogImpact.Low,
        };

        args.Verbs.Add(verb);
    }

    private void TryStartRitual(EntityUid uid, RitualAltarComponent comp, ICommonSession session)
    {
        var now = _timing.CurTime;
        if (_nextUse.TryGetValue(uid, out var next) && next > now)
        {
            _popup.PopupEntity(Loc.GetString("ritual-altar-cooldown"), uid, session, PopupType.Medium);
            return;
        }

        if (!HasRequiredItems(uid, session))
        {
            // Missing item popups are handled inside.
            return;
        }

        _nextUse[uid] = now + TimeSpan.FromSeconds(2);

        RaiseNetworkEvent(
            new RitualEffectMessage(GetNetEntity(uid), comp.EffectRadius, comp.MaxDarkness, (float) comp.EffectDuration.TotalSeconds),
            Filter.Pvs(uid));

        _popup.PopupEntity(Loc.GetString("ritual-altar-started"), uid, session, PopupType.Medium);
    }

    private bool HasRequiredItems(EntityUid altar, ICommonSession session)
    {
        var coords = Transform(altar).Coordinates;

        var ents = new HashSet<EntityUid>();
        _lookup.GetEntitiesInRange(coords, 0.45f, ents);

        var hasBook = false;
        var hasHeart = false;

        foreach (var ent in ents)
        {
            if (ent == altar)
                continue;

            if (!hasBook && HasComp<RuneBookComponent>(ent))
                hasBook = true;

            if (!hasHeart && TryComp(ent, out ItemComponent? item) && item.HeldPrefix == "heart")
                hasHeart = true;

            if (hasBook && hasHeart)
                return true;
        }

        if (!hasBook)
            _popup.PopupEntity(Loc.GetString("ritual-altar-missing-book"), altar, session, PopupType.Medium);

        if (!hasHeart)
            _popup.PopupEntity(Loc.GetString("ritual-altar-missing-heart"), altar, session, PopupType.Medium);

        return false;
    }
}
