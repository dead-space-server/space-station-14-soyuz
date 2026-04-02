using System; // DS-14
using System.Collections.Generic; // DS-14
using System.Linq; // DS-14
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Audio.Jukebox;
using Content.Shared.DeadSpace.Ports.Jukebox; // DS-14
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random; // DS-14
using Robust.Shared.Timing; // DS-14
using JukeboxComponent = Content.Shared.Audio.Jukebox.JukeboxComponent;

namespace Content.Server.Audio.Jukebox;


public sealed class JukeboxSystem : SharedJukeboxSystem
{
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly AppearanceSystem _appearanceSystem = default!;
    // DS-14 Start: The server keeps per-jukebox playback history so next/previous can
    // walk the actual listening session instead of only an alphabetical catalog order.
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private readonly Dictionary<EntityUid, PlaybackHistoryState> _playbackStates = new();

    private sealed class PlaybackHistoryState
    {
        public readonly List<ProtoId<JukeboxPrototype>> History = new();
        public int HistoryIndex = -1;
    }
    // DS-14 End

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JukeboxComponent, JukeboxSelectedMessage>(OnJukeboxSelected);
        SubscribeLocalEvent<JukeboxComponent, JukeboxPlayingMessage>(OnJukeboxPlay);
        SubscribeLocalEvent<JukeboxComponent, JukeboxPauseMessage>(OnJukeboxPause);
        SubscribeLocalEvent<JukeboxComponent, JukeboxStopMessage>(OnJukeboxStop);
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetTimeMessage>(OnJukeboxSetTime);
        // DS-14 Start: These messages back the new client controls for volume, transport,
        // and playback mode toggles.
        SubscribeLocalEvent<JukeboxComponent, JukeboxSetVolumeMessage>(OnJukeboxSetVolume);
        SubscribeLocalEvent<JukeboxComponent, JukeboxNextMessage>(OnJukeboxNext);
        SubscribeLocalEvent<JukeboxComponent, JukeboxPreviousMessage>(OnJukeboxPrevious);
        SubscribeLocalEvent<JukeboxComponent, JukeboxShuffleMessage>(OnJukeboxShuffle);
        SubscribeLocalEvent<JukeboxComponent, JukeboxRepeatMessage>(OnJukeboxRepeat);
        // DS-14 End
        SubscribeLocalEvent<JukeboxComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<JukeboxComponent, ComponentShutdown>(OnComponentShutdown);

        SubscribeLocalEvent<JukeboxComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnComponentInit(EntityUid uid, JukeboxComponent component, ComponentInit args)
    {
        if (HasComp<ApcPowerReceiverComponent>(uid))
        {
            TryUpdateVisualState(uid, component);
        }
    }

    private void OnJukeboxPlay(EntityUid uid, JukeboxComponent component, ref JukeboxPlayingMessage args)
    {
        // DS-14 Start: If the old stream no longer exists, Play falls back to the current
        // selection or a sensible next/shuffle pick instead of becoming a no-op.
        if (Exists(component.AudioStream))
        {
            Audio.SetState(component.AudioStream, AudioState.Playing);
            Dirty(uid, component);
        }
        else
        {
            var selectedSong = component.SelectedSongId;

            if (!TryResolveSong(selectedSong, out _))
            {
                selectedSong = component.ShuffleEnabled
                    ? PickShuffledSong(component.SelectedSongId)
                    : GetAdjacentSong(component.SelectedSongId, 1);
            }

            if (selectedSong == null || !StartSong(uid, component, selectedSong.Value, updateHistory: true))
                return;
        }
        // DS-14 End
    }

    private void OnJukeboxPause(Entity<JukeboxComponent> ent, ref JukeboxPauseMessage args)
    {
        Audio.SetState(ent.Comp.AudioStream, AudioState.Paused);
    }

    private void OnJukeboxSetTime(EntityUid uid, JukeboxComponent component, JukeboxSetTimeMessage args)
    {
        if (TryComp(args.Actor, out ActorComponent? actorComp))
        {
            var offset = actorComp.PlayerSession.Channel.Ping * 1.5f / 1000f;
            Audio.SetPlaybackPosition(component.AudioStream, args.SongTime + offset);
        }
    }

    // DS-14 Start: Server-side handlers keep replicated jukebox state authoritative for
    // transport toggles and persisted volume.
    private void OnJukeboxSetVolume(EntityUid uid, JukeboxComponent component, JukeboxSetVolumeMessage args)
    {
        component.Volume = JukeboxVolume.Clamp(args.Volume);
        Dirty(uid, component);
    }

    private void OnJukeboxNext(EntityUid uid, JukeboxComponent component, ref JukeboxNextMessage args)
    {
        var startPlayback = HasActiveStream(component.AudioStream);
        var songId = ResolveNextSong(uid, component, out var fromHistory);
        if (songId == null)
            return;

        SelectOrPlaySong(uid, component, songId.Value, startPlayback, updateHistory: !fromHistory);
    }

    private void OnJukeboxPrevious(EntityUid uid, JukeboxComponent component, ref JukeboxPreviousMessage args)
    {
        var startPlayback = HasActiveStream(component.AudioStream);
        var songId = ResolvePreviousSong(uid, component, out var fromHistory);
        if (songId == null)
            return;

        SelectOrPlaySong(uid, component, songId.Value, startPlayback, updateHistory: !fromHistory);
    }

    private void OnJukeboxShuffle(EntityUid uid, JukeboxComponent component, ref JukeboxShuffleMessage args)
    {
        component.ShuffleEnabled = args.Enabled;
        Dirty(uid, component);
    }

    private void OnJukeboxRepeat(EntityUid uid, JukeboxComponent component, ref JukeboxRepeatMessage args)
    {
        component.RepeatEnabled = args.Enabled;
        Dirty(uid, component);
    }
    // DS-14 End

    private void OnPowerChanged(Entity<JukeboxComponent> entity, ref PowerChangedEvent args)
    {
        TryUpdateVisualState(entity);

        if (!this.IsPowered(entity.Owner, EntityManager))
        {
            Stop(entity);
        }
    }

    private void OnJukeboxStop(Entity<JukeboxComponent> entity, ref JukeboxStopMessage args)
    {
        Stop(entity);
    }

    private void Stop(Entity<JukeboxComponent> entity)
    {
        Audio.SetState(entity.Comp.AudioStream, AudioState.Stopped);
        Dirty(entity);
    }

    private void OnJukeboxSelected(EntityUid uid, JukeboxComponent component, JukeboxSelectedMessage args)
    {
        // DS-14 Start: While already playing, selecting a track swaps immediately; while
        // idle, it still only arms the next song and shows the select animation.
        if (HasActiveStream(component.AudioStream))
        {
            StartSong(uid, component, args.SongId, updateHistory: true);
            ShowSelectionVisual(uid, component);
            return;
        }

        SelectSong(uid, component, args.SongId);
        // DS-14 End
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<JukeboxComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Selecting)
            {
                comp.SelectAccumulator += frameTime;
                if (comp.SelectAccumulator >= 0.5f)
                {
                    comp.SelectAccumulator = 0f;
                    comp.Selecting = false;

                    TryUpdateVisualState(uid, comp);
                }
            }

            // DS-14 Start: Poll finished playback here so repeat, shuffle, and history
            // all advance through the same authoritative server path.
            if (!TryGetSongLength(comp.SelectedSongId, out var length) ||
                !TryComp(comp.AudioStream, out AudioComponent? audio) ||
                audio.State != AudioState.Playing)
            {
                continue;
            }

            if (GetPlaybackPosition(audio) + 0.05f < length)
                continue;

            HandleSongFinished(uid, comp);
            // DS-14 End
        }
    }

    private void OnComponentShutdown(EntityUid uid, JukeboxComponent component, ComponentShutdown args)
    {
        component.AudioStream = Audio.Stop(component.AudioStream);
        _playbackStates.Remove(uid);
    }

    private void DirectSetVisualState(EntityUid uid, JukeboxVisualState state)
    {
        _appearanceSystem.SetData(uid, JukeboxVisuals.VisualState, state);
    }

    private void TryUpdateVisualState(EntityUid uid, JukeboxComponent? jukeboxComponent = null)
    {
        if (!Resolve(uid, ref jukeboxComponent))
            return;

        var finalState = JukeboxVisualState.On;

        if (!this.IsPowered(uid, EntityManager))
        {
            finalState = JukeboxVisualState.Off;
        }

        _appearanceSystem.SetData(uid, JukeboxVisuals.VisualState, finalState);
    }

    // DS-14 Start: The helpers below centralize song resolution, restart rules, and
    // history traversal so next/previous/repeat share identical behavior.
    private void HandleSongFinished(EntityUid uid, JukeboxComponent component)
    {
        if (component.SelectedSongId == null)
        {
            component.AudioStream = Audio.Stop(component.AudioStream);
            Dirty(uid, component);
            return;
        }

        if (component.RepeatEnabled)
        {
            StartSong(uid, component, component.SelectedSongId.Value, updateHistory: false);
            return;
        }

        var songId = ResolveNextSong(uid, component, out var fromHistory);
        if (songId == null)
        {
            component.AudioStream = Audio.Stop(component.AudioStream);
            Dirty(uid, component);
            return;
        }

        StartSong(uid, component, songId.Value, updateHistory: !fromHistory);
    }

    private void SelectOrPlaySong(
        EntityUid uid,
        JukeboxComponent component,
        ProtoId<JukeboxPrototype> songId,
        bool startPlayback,
        bool updateHistory)
    {
        if (startPlayback)
        {
            StartSong(uid, component, songId, updateHistory);
            ShowSelectionVisual(uid, component);
            return;
        }

        SelectSong(uid, component, songId);
    }

    private bool StartSong(
        EntityUid uid,
        JukeboxComponent component,
        ProtoId<JukeboxPrototype> songId,
        bool updateHistory)
    {
        if (!_protoManager.Resolve(songId, out var jukeboxProto))
            return false;

        component.SelectedSongId = songId;
        component.AudioStream = Audio.Stop(component.AudioStream);
        component.AudioStream = Audio.PlayPvs(
            jukeboxProto.Path,
            uid,
            AudioParams.Default
                .WithMaxDistance(10f)
                .WithVolume(JukeboxVolume.ToDb(component.Volume)))?.Entity;

        if (updateHistory && component.AudioStream != null)
            PushHistory(uid, songId);

        Dirty(uid, component);
        return component.AudioStream != null;
    }

    private void SelectSong(EntityUid uid, JukeboxComponent component, ProtoId<JukeboxPrototype> songId)
    {
        component.SelectedSongId = songId;
        component.AudioStream = Audio.Stop(component.AudioStream);
        ShowSelectionVisual(uid, component);
        Dirty(uid, component);
    }

    private void ShowSelectionVisual(EntityUid uid, JukeboxComponent component)
    {
        DirectSetVisualState(uid, JukeboxVisualState.Select);
        component.Selecting = true;
        component.SelectAccumulator = 0f;
    }

    private bool HasActiveStream(EntityUid? audioStream)
    {
        return TryComp(audioStream, out AudioComponent? audio) && audio.State != AudioState.Stopped;
    }

    private ProtoId<JukeboxPrototype>? ResolveNextSong(EntityUid uid, JukeboxComponent component, out bool fromHistory)
    {
        if (TryMoveHistoryIndex(uid, 1, out var historySong))
        {
            fromHistory = true;
            return historySong;
        }

        fromHistory = false;
        return component.ShuffleEnabled
            ? PickShuffledSong(component.SelectedSongId)
            : GetAdjacentSong(component.SelectedSongId, 1);
    }

    private ProtoId<JukeboxPrototype>? ResolvePreviousSong(EntityUid uid, JukeboxComponent component, out bool fromHistory)
    {
        if (TryMoveHistoryIndex(uid, -1, out var historySong))
        {
            fromHistory = true;
            return historySong;
        }

        fromHistory = false;
        return GetAdjacentSong(component.SelectedSongId, -1);
    }

    private ProtoId<JukeboxPrototype>? GetAdjacentSong(ProtoId<JukeboxPrototype>? currentSongId, int direction)
    {
        var orderedSongs = GetOrderedSongs();
        if (orderedSongs.Count == 0)
            return null;

        if (currentSongId == null)
            return direction < 0 ? orderedSongs[^1].ID : orderedSongs[0].ID;

        var currentIndex = orderedSongs.FindIndex(proto => proto.ID == currentSongId.Value);
        if (currentIndex == -1)
            return direction < 0 ? orderedSongs[^1].ID : orderedSongs[0].ID;

        var nextIndex = (currentIndex + direction + orderedSongs.Count) % orderedSongs.Count;
        return orderedSongs[nextIndex].ID;
    }

    private ProtoId<JukeboxPrototype>? PickShuffledSong(ProtoId<JukeboxPrototype>? currentSongId)
    {
        var orderedSongs = GetOrderedSongs();
        if (orderedSongs.Count == 0)
            return null;

        if (orderedSongs.Count == 1)
            return orderedSongs[0].ID;

        if (currentSongId != null)
            orderedSongs.RemoveAll(proto => proto.ID == currentSongId.Value);

        return orderedSongs.Count == 0 ? null : _random.Pick(orderedSongs).ID;
    }

    private List<JukeboxPrototype> GetOrderedSongs()
    {
        var orderedSongs = _protoManager.EnumeratePrototypes<JukeboxPrototype>().ToList();
        orderedSongs.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return orderedSongs;
    }

    private bool TryGetSongLength(ProtoId<JukeboxPrototype>? songId, out float length)
    {
        length = 0f;
        if (!TryResolveSong(songId, out var proto))
            return false;

        length = (float) Audio.GetAudioLength(Audio.ResolveSound(proto.Path)).TotalSeconds;
        return true;
    }

    private bool TryResolveSong(ProtoId<JukeboxPrototype>? songId, out JukeboxPrototype proto)
    {
        if (songId != null && _protoManager.Resolve(songId.Value, out var resolvedProto))
        {
            proto = resolvedProto;
            return true;
        }

        proto = default!;
        return false;
    }

    private float GetPlaybackPosition(AudioComponent audio)
    {
        if (audio.State == AudioState.Paused)
            return Math.Max(0f, (float) ((audio.PauseTime ?? _timing.CurTime) - audio.AudioStart).TotalSeconds);

        if (audio.State == AudioState.Playing)
            return Math.Max(0f, (float) (_timing.CurTime - audio.AudioStart).TotalSeconds);

        return 0f;
    }

    private PlaybackHistoryState GetPlaybackState(EntityUid uid)
    {
        if (_playbackStates.TryGetValue(uid, out var state))
            return state;

        state = new PlaybackHistoryState();
        _playbackStates[uid] = state;
        return state;
    }

    private void PushHistory(EntityUid uid, ProtoId<JukeboxPrototype> songId)
    {
        var state = GetPlaybackState(uid);

        if (state.HistoryIndex >= 0 &&
            state.HistoryIndex < state.History.Count &&
            state.History[state.HistoryIndex] == songId)
        {
            return;
        }

        if (state.HistoryIndex < state.History.Count - 1)
        {
            state.History.RemoveRange(state.HistoryIndex + 1, state.History.Count - state.HistoryIndex - 1);
        }

        state.History.Add(songId);
        state.HistoryIndex = state.History.Count - 1;
    }

    private bool TryMoveHistoryIndex(EntityUid uid, int direction, out ProtoId<JukeboxPrototype> songId)
    {
        var state = GetPlaybackState(uid);
        var nextIndex = state.HistoryIndex + direction;

        if (nextIndex >= 0 && nextIndex < state.History.Count)
        {
            state.HistoryIndex = nextIndex;
            songId = state.History[state.HistoryIndex];
            return true;
        }

        songId = default;
        return false;
    }
    // DS-14 End
}
