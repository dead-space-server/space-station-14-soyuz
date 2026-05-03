using Content.Shared.Tag;
using Content.Server.Popups;
using Content.Server.Chat.Systems;
using Content.Shared.Database;
using Content.Shared.DeadSpace.Soyuz.RitualAltar;
using Content.Shared.DeadSpace.Soyuz.RuneBook;
using Content.Shared.Chat;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Speech;
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
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextUse = new();
    private static readonly ProtoId<TagPrototype> BookTag = "Book";
    private static readonly SoundSpecifier RitualSound = new SoundPathSpecifier("/Audio/_DeadSpace/Necromorfs/TheCircle/the-circle-start.ogg");
    private static readonly TimeSpan DialogueResponseTime = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SpeechDelay = TimeSpan.FromSeconds(0.5);
    private const string RuneBookRuneEffect = "RitualRuneBookRuneEffect";
    private const string BrainRuneEffect = "RitualBrainRuneEffect";
    private const float SacrificeRange = 0.45f;
    private const float DialogueRange = 6f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RitualAltarComponent, GetVerbsEvent<ActivationVerb>>(OnGetVerbs);
        SubscribeLocalEvent<EntitySpokeEvent>(OnEntitySpoke);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<RitualAltarInProgressComponent, RitualAltarComponent>();
        while (query.MoveNext(out var uid, out var inProgress, out var altar))
        {
            if (inProgress.Sacrifice == RitualAltarSacrifice.None)
                TrySacrificeOrgan(uid, inProgress);

            if (inProgress.Sacrifice == RitualAltarSacrifice.Brain)
            {
                if (now >= inProgress.ResponseDeadline)
                    FailBrainRitual(uid, inProgress, "ritual-altar-apostle-timeout");

                continue;
            }

            if (now < inProgress.EndTime)
                continue;

            RemCompDeferred<RitualAltarInProgressComponent>(uid);

            if (inProgress.Sacrifice != RitualAltarSacrifice.Heart)
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
        inProgress.Sacrifice = RitualAltarSacrifice.None;
        inProgress.DialogueStage = RitualAltarDialogueStage.None;
        inProgress.ResponseDeadline = TimeSpan.Zero;
        inProgress.Questioner = null;
        inProgress.Ritualist = session.AttachedEntity;

        _nextUse[uid] = now + TimeSpan.FromSeconds(2);

        _audio.PlayPvs(RitualSound, uid, AudioParams.Default.WithVolume(-6f));

        _popup.PopupEntity(Loc.GetString("ritual-altar-started"), uid, session, PopupType.Medium);
    }

    private EntityUid? FindSacrificeBook(EntityUid altar)
    {
        var coords = Transform(altar).Coordinates;

        var ents = new HashSet<EntityUid>();
        _lookup.GetEntitiesInRange(coords, SacrificeRange, ents);

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

    private void TrySacrificeOrgan(EntityUid altar, RitualAltarInProgressComponent inProgress)
    {
        var coords = Transform(altar).Coordinates;

        var ents = new HashSet<EntityUid>();
        _lookup.GetEntitiesInRange(coords, SacrificeRange, ents);

        foreach (var ent in ents)
        {
            if (ent == altar)
                continue;

            if (!TryComp(ent, out ItemComponent? item))
                continue;

            if (item.HeldPrefix == "heart")
            {
                QueueDel(ent);
                inProgress.Sacrifice = RitualAltarSacrifice.Heart;
                PlayRitualEffect(altar, 5f, 0.8f, GetRemainingDuration(inProgress.EndTime), RuneBookRuneEffect);
                _popup.PopupEntity(Loc.GetString("ritual-altar-heart-sacrificed"), altar, Filter.Pvs(altar), true, PopupType.Medium);
                return;
            }

            if (item.HeldPrefix == "brain")
            {
                QueueDel(ent);
                StartBrainRitual(altar, inProgress);
                return;
            }
        }
    }

    private void StartBrainRitual(EntityUid altar, RitualAltarInProgressComponent inProgress)
    {
        inProgress.Sacrifice = RitualAltarSacrifice.Brain;
        inProgress.DialogueStage = RitualAltarDialogueStage.AwaitingFirstAnswer;
        inProgress.ResponseDeadline = _timing.CurTime + DialogueResponseTime + SpeechDelay;
        inProgress.Questioner = Spawn("DS14SoyuzRitualUrist", Transform(altar).Coordinates);

        PlayRitualEffect(altar, 5f, 0.8f, (float) DialogueResponseTime.TotalSeconds, BrainRuneEffect);
        _popup.PopupEntity(Loc.GetString("ritual-altar-brain-sacrificed"), altar, Filter.Pvs(altar), true, PopupType.Medium);
        SayDelayed(inProgress.Questioner.Value, "ritual-altar-apostle-question-one");
    }

    private void OnEntitySpoke(EntitySpokeEvent args)
    {
        var query = EntityQueryEnumerator<RitualAltarInProgressComponent>();
        while (query.MoveNext(out var altar, out var inProgress))
        {
            if (inProgress.Sacrifice != RitualAltarSacrifice.Brain ||
                inProgress.Ritualist != args.Source ||
                inProgress.Questioner is not { } questioner ||
                Deleted(questioner) ||
                !IsInDialogueRange(args.Source, questioner))
            {
                continue;
            }

            if (IsYes(args.Message))
            {
                AcceptBrainRitualAnswer(altar, inProgress);
                return;
            }

            if (IsNo(args.Message))
            {
                FailBrainRitual(altar, inProgress, "ritual-altar-apostle-unworthy");
                return;
            }
        }
    }

    private void AcceptBrainRitualAnswer(EntityUid altar, RitualAltarInProgressComponent inProgress)
    {
        if (inProgress.Questioner is not { } questioner || Deleted(questioner))
            return;

        if (inProgress.DialogueStage == RitualAltarDialogueStage.AwaitingFirstAnswer)
        {
            inProgress.DialogueStage = RitualAltarDialogueStage.AwaitingSecondAnswer;
            inProgress.ResponseDeadline = _timing.CurTime + DialogueResponseTime + SpeechDelay;
            PlayRitualEffect(altar, 5f, 0.8f, (float) DialogueResponseTime.TotalSeconds, BrainRuneEffect);
            SayDelayed(questioner, "ritual-altar-apostle-question-two");
            return;
        }

        Spawn("DS14SoyuzApostleGrimoire", Transform(altar).Coordinates);
        _popup.PopupEntity(Loc.GetString("ritual-altar-apostle-complete"), altar, Filter.Pvs(altar), true, PopupType.Medium);
        FinishBrainRitual(altar, inProgress);
    }

    private void FailBrainRitual(EntityUid altar, RitualAltarInProgressComponent inProgress, string locId)
    {
        if (inProgress.Questioner is { } questioner && !Deleted(questioner))
        {
            SayDelayed(questioner, locId);
            Timer.Spawn(SpeechDelay + TimeSpan.FromSeconds(0.1), () =>
            {
                if (!Deleted(questioner))
                    QueueDel(questioner);
            });
        }

        RemCompDeferred<RitualAltarInProgressComponent>(altar);
    }

    private void FinishBrainRitual(EntityUid altar, RitualAltarInProgressComponent inProgress)
    {
        if (inProgress.Questioner is { } questioner && !Deleted(questioner))
            QueueDel(questioner);

        RemCompDeferred<RitualAltarInProgressComponent>(altar);
    }

    private void Say(EntityUid speaker, string locId)
    {
        _chat.TrySendInGameICMessage(
            speaker,
            Loc.GetString(locId),
            InGameICChatType.Speak,
            ChatTransmitRange.Normal,
            hideLog: true,
            checkRadioPrefix: false,
            ignoreActionBlocker: true);
    }

    private void SayDelayed(EntityUid speaker, string locId)
    {
        Timer.Spawn(SpeechDelay, () =>
        {
            if (Deleted(speaker))
                return;

            Say(speaker, locId);
        });
    }

    private void PlayRitualEffect(EntityUid altar, float radius, float maxDarkness, float durationSeconds, string runeEffect)
    {
        RaiseNetworkEvent(
            new RitualEffectMessage(GetNetEntity(altar), radius, maxDarkness, durationSeconds, runeEffect),
            Filter.Pvs(altar));
    }

    private float GetRemainingDuration(TimeSpan endTime)
    {
        return MathF.Max(0.1f, (float) (endTime - _timing.CurTime).TotalSeconds);
    }

    private bool IsInDialogueRange(EntityUid source, EntityUid questioner)
    {
        var sourceXform = Transform(source);
        var questionerXform = Transform(questioner);

        if (sourceXform.MapID != questionerXform.MapID)
            return false;

        return (_transform.GetWorldPosition(sourceXform) - _transform.GetWorldPosition(questionerXform)).LengthSquared() <= DialogueRange * DialogueRange;
    }

    private static bool IsYes(string message)
    {
        return NormalizeAnswer(message) switch
        {
            "\u0434\u0430" or "\u0434" or "\u0430\u0433\u0430" or "\u0443\u0433\u0443" or "\u0441\u043e\u0433\u043b\u0430\u0441\u0435\u043d" or "\u0441\u043e\u0433\u043b\u0430\u0441\u043d\u0430" or "yes" or "y" or "yeah" or "yep" or "yea" or "sure" or "agree" or "agreed" => true,
            _ => false,
        };
    }

    private static bool IsNo(string message)
    {
        return NormalizeAnswer(message) switch
        {
            "\u043d\u0435\u0442" or "\u043d" or "\u043d\u0435" or "\u043d\u0435\u0430" or "no" or "n" or "nope" => true,
            _ => false,
        };
    }

    private static string NormalizeAnswer(string message)
    {
        return message.Trim().Trim('.', '!', '?', ',', ';', ':', '"', '\'').ToLowerInvariant();
    }
}
