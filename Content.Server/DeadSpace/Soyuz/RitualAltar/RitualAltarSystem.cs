using Content.Shared.Tag;
using Content.Server.Popups;
using Content.Shared.Database;
using Content.Shared.DeadSpace.Soyuz.RitualAltar;
using Content.Shared.DeadSpace.Soyuz.RuneBook;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Server.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.Audio;

namespace Content.Server.DeadSpace.Soyuz.RitualAltar;

public sealed class RitualAltarSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextUse = new();
    private static readonly ProtoId<TagPrototype> BookTag = "Book";
    private static readonly SoundSpecifier RitualSound = new SoundPathSpecifier("/Audio/_DeadSpace/Necromorfs/TheCircle/the-circle-start.ogg");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RitualAltarComponent, GetVerbsEvent<ActivationVerb>>(OnGetVerbs);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<RitualAltarInProgressComponent, RitualAltarComponent>();
        while (query.MoveNext(out var uid, out var inProgress, out var altar))
        {
            if (!inProgress.HeartSacrificed)
                TrySacrificeHeart(uid, inProgress);

            if (now < inProgress.EndTime)
                continue;

            RemCompDeferred<RitualAltarInProgressComponent>(uid);

            if (!inProgress.HeartSacrificed)
            {
                _popup.PopupEntity(Loc.GetString("ritual-altar-failed"), uid, Filter.Pvs(uid), true, PopupType.Medium);
                continue;
            }

            Spawn("DS14SoyuzRuneBook", Transform(uid).Coordinates);
            _popup.PopupEntity(Loc.GetString("ritual-altar-complete"), uid, Filter.Pvs(uid), true, PopupType.Medium);
        }
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

        if (HasComp<RitualAltarInProgressComponent>(uid))
        {
            _popup.PopupEntity(Loc.GetString("ritual-altar-already-running"), uid, session, PopupType.Medium);
            return;
        }

        if (_nextUse.TryGetValue(uid, out var next) && next > now)
        {
            _popup.PopupEntity(Loc.GetString("ritual-altar-cooldown"), uid, session, PopupType.Medium);
            return;
        }

        var book = FindSacrificeBook(uid);
        if (book == null)
        {
            _popup.PopupEntity(Loc.GetString("ritual-altar-missing-book"), uid, session, PopupType.Medium);
            return;
        }

        // Sacrifice the book first.
        QueueDel(book.Value);

        var inProgress = EnsureComp<RitualAltarInProgressComponent>(uid);
        inProgress.EndTime = now + comp.EffectDuration;
        inProgress.HeartSacrificed = false;

        _nextUse[uid] = now + TimeSpan.FromSeconds(2);

        RaiseNetworkEvent(
            new RitualEffectMessage(GetNetEntity(uid), comp.EffectRadius, comp.MaxDarkness, (float) comp.EffectDuration.TotalSeconds),
            Filter.Pvs(uid));

        _audio.PlayPvs(RitualSound, uid, AudioParams.Default.WithVolume(-6f));

        _popup.PopupEntity(Loc.GetString("ritual-altar-started"), uid, session, PopupType.Medium);
    }

    private EntityUid? FindSacrificeBook(EntityUid altar)
    {
        var coords = Transform(altar).Coordinates;

        var ents = new HashSet<EntityUid>();
        _lookup.GetEntitiesInRange(coords, 0.45f, ents);

        foreach (var ent in ents)
        {
            if (ent == altar)
                continue;

            // Any "normal" book (tagged as Book) counts. Rune book is not tagged as Book.
            if (_tags.HasTag(ent, BookTag))
                return ent;
        }

        return null;
    }

    private void TrySacrificeHeart(EntityUid altar, RitualAltarInProgressComponent inProgress)
    {
        var coords = Transform(altar).Coordinates;

        var ents = new HashSet<EntityUid>();
        _lookup.GetEntitiesInRange(coords, 0.45f, ents);

        foreach (var ent in ents)
        {
            if (ent == altar)
                continue;

            if (TryComp(ent, out ItemComponent? item) && item.HeldPrefix == "heart")
            {
                QueueDel(ent);
                inProgress.HeartSacrificed = true;
                _popup.PopupEntity(Loc.GetString("ritual-altar-heart-sacrificed"), altar, Filter.Pvs(altar), true, PopupType.Medium);
                break;
            }
        }
    }
}
