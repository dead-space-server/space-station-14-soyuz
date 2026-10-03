// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using System.Numerics;
using Content.Server.Interaction;
using Content.Server.Power.EntitySystems;
using Content.Server.Radio;
using Content.Server.Radio.EntitySystems;
using Content.Shared.ActionBlocker;
using Content.Shared.DeadSpace._Soyuz.Radio;
using Content.Shared.Ghost;
using Content.Shared.Power;
using Content.Shared.Radio.Components;
using Content.Shared.Speech.Components;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace._Soyuz.Radio;

public sealed class TunableRadioSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly RadioDeviceSystem _devices = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly InteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;

    private float _uiElapsed;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TunableRadioComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<TunableRadioComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<TunableRadioComponent, TunableRadioSetFrequencyMessage>(OnSetFrequency);
        SubscribeLocalEvent<TunableRadioComponent, TunableRadioToggleMessage>(OnToggle);
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnReceiveAttempt);
        SubscribeLocalEvent<VocalComponent, ScreamPlayedEvent>(OnScream);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _uiElapsed += frameTime;
        if (_uiElapsed < 0.25f)
            return;
        _uiElapsed = 0;
        var query = EntityQueryEnumerator<TunableRadioComponent>();
        while (query.MoveNext(out var uid, out var radio))
        {
            if (_ui.IsUiOpen(uid, TunableRadioUiKey.Key))
                UpdateUi(uid, radio);
        }
    }

    private bool IsTunable(EntityUid uid) =>
        TryComp<RadioMicrophoneComponent>(uid, out var microphone) &&
        microphone.BroadcastChannel == TunableRadioComponent.Channel;

    private void OnGetVerbs(EntityUid uid, TunableRadioComponent component, GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !IsTunable(uid))
            return;
        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("soyuz-radio-configure"),
            Act = () => _ui.TryOpenUi(uid, TunableRadioUiKey.Key, user),
        });
    }

    private void OnUiOpened(EntityUid uid, TunableRadioComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, component);
    }

    private bool CanUse(EntityUid uid, EntityUid user) =>
        IsTunable(uid) && _ui.IsUiOpen(uid, TunableRadioUiKey.Key, user) &&
        _blocker.CanInteract(user, uid) && _interaction.InRangeUnobstructed(user, uid);

    private void OnSetFrequency(EntityUid uid, TunableRadioComponent component, TunableRadioSetFrequencyMessage args)
    {
        if (!CanUse(uid, args.Actor) || args.Frequency is < TunableRadioComponent.MinimumFrequency or > TunableRadioComponent.MaximumFrequency)
            return;
        component.Frequency = args.Frequency;
        Dirty(uid, component);
        UpdateUi(uid, component);
    }

    private void OnToggle(EntityUid uid, TunableRadioComponent component, TunableRadioToggleMessage args)
    {
        if (!CanUse(uid, args.Actor))
            return;
        if (args.Microphone)
            _devices.SetMicrophoneEnabled(uid, args.Actor, args.Enabled, true);
        else
            _devices.SetSpeakerEnabled(uid, args.Actor, args.Enabled, true);
        UpdateUi(uid, component);
    }

    private void UpdateUi(EntityUid uid, TunableRadioComponent component)
    {
        if (!IsTunable(uid) || !TryComp<RadioMicrophoneComponent>(uid, out var microphone) ||
            !TryComp<RadioSpeakerComponent>(uid, out var speaker))
            return;
        _ui.SetUiState(uid, TunableRadioUiKey.Key,
            new TunableRadioUiState(component.Frequency, microphone.Enabled, speaker.Enabled));
    }

    private void OnReceiveAttempt(ref RadioReceiveAttemptEvent args)
    {
        if (args.Channel.ID != TunableRadioComponent.Channel.Id ||
            TryComp<ActiveRadioComponent>(args.RadioReceiver, out var active) && active.ReceiveAllChannels)
            return;
        var frequency = TryComp<TunableRadioComponent>(args.RadioReceiver, out var tuning) && IsTunable(args.RadioReceiver)
            ? tuning.Frequency : _prototypes.Index(TunableRadioComponent.Channel).Frequency;
        if (frequency != args.Frequency)
            args.Cancelled = true;
    }

    private void OnScream(EntityUid uid, VocalComponent component, ref ScreamPlayedEvent args)
    {
        if (HasComp<SpectralComponent>(uid))
            return;
        var source = Transform(uid);
        var position = _transform.GetWorldPosition(source);
        var sent = new HashSet<int>();
        var query = EntityQueryEnumerator<TunableRadioComponent, RadioMicrophoneComponent, TransformComponent>();
        while (query.MoveNext(out var radioUid, out var tuning, out var microphone, out var transform))
        {
            if (!microphone.Enabled || microphone.BroadcastChannel != TunableRadioComponent.Channel ||
                transform.MapID != source.MapID ||
                Vector2.DistanceSquared(position, _transform.GetWorldPosition(transform)) > microphone.ListenRange * microphone.ListenRange ||
                microphone.PowerRequired && !this.IsPowered(radioUid, EntityManager) ||
                microphone.UnobstructedRequired && !_interaction.InRangeUnobstructed(uid, radioUid, 0) ||
                !sent.Add(tuning.Frequency))
                continue;
            try
            {
                _radio.SendRadioMessage(uid, Loc.GetString("soyuz-radio-scream"), TunableRadioComponent.Channel,
                    radioUid, sound: args.Sound, soundParams: args.SoundParams);
            }
            catch (Exception exception)
            {
                Log.Error($"Relaying scream from {uid} through radio {radioUid} failed: {exception}");
            }
        }
    }
}

[ByRefEvent]
public readonly record struct ScreamPlayedEvent(ResolvedSoundSpecifier Sound, AudioParams SoundParams);
