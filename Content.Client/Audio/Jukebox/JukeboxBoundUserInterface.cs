using Content.Shared.Audio.Jukebox;
using Robust.Client.Audio;
using Robust.Client.UserInterface;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;

namespace Content.Client.Audio.Jukebox;

public sealed class JukeboxBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _protoManager = default!;

    [ViewVariables]
    private JukeboxMenu? _menu;
    private bool _volumeStateCommitted; // DS-14

    public JukeboxBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<JukeboxMenu>();
        _menu.SetJukebox(Owner); // DS-14
        _menu.OnClose += CommitVolumeState; // DS-14

        // DS-14 Start: The BUI stays a thin relay, but it now wires the richer transport
        // controls and keeps volume updates on a dedicated callback path.
        _menu.OnPlayPressed += args =>
        {
            if (args)
            {
                SendMessage(new JukeboxPlayingMessage());
            }
            else
            {
                SendMessage(new JukeboxPauseMessage());
            }
        };

        _menu.OnPreviousPressed += () =>
        {
            SendMessage(new JukeboxPreviousMessage());
        };

        _menu.OnNextPressed += () =>
        {
            SendMessage(new JukeboxNextMessage());
        };

        _menu.OnShuffleToggled += enabled =>
        {
            SendMessage(new JukeboxShuffleMessage(enabled));
        };

        _menu.OnRepeatToggled += enabled =>
        {
            SendMessage(new JukeboxRepeatMessage(enabled));
        };

        _menu.OnSongSelected += SelectSong;

        _menu.SetTime += SetTime;
        _menu.SetVolume += SetVolume;
        // DS-14 End
        PopulateMusic();
        Reload();
    }

    // DS-14 Start: Volume changes are previewed locally while the menu is open, so the
    // last state needs to be committed when the window is disposed as well as on close.
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            CommitVolumeState();

        base.Dispose(disposing);
    }
    // DS-14 End

    /// <summary>
    /// Reloads the attached menu if it exists.
    /// </summary>
    public void Reload()
    {
        if (_menu == null || !EntMan.TryGetComponent(Owner, out JukeboxComponent? jukebox))
            return;

        // DS-14 Start: Reload restores both replicated transport state and the selected song
        // identity so reopening the UI does not lose local context.
        _menu.SetAudioStream(jukebox.AudioStream);
        _menu.SetShuffleEnabled(jukebox.ShuffleEnabled);
        _menu.SetRepeatEnabled(jukebox.RepeatEnabled);
        _menu.SetVolumeSlider(jukebox.Volume);

        if (_protoManager.Resolve(jukebox.SelectedSongId, out var songProto))
        {
            var length = EntMan.System<AudioSystem>().GetAudioLength(songProto.Path.Path.ToString());
            _menu.SetSelectedSong(jukebox.SelectedSongId, songProto.Name, (float) length.TotalSeconds);
        }
        else
        {
            _menu.SetSelectedSong(null, string.Empty, 0f);
        }
        // DS-14 End
    }

    public void PopulateMusic()
    {
        _menu?.Populate(_protoManager.EnumeratePrototypes<JukeboxPrototype>());
    }

    public void SelectSong(ProtoId<JukeboxPrototype> songid)
    {
        SendMessage(new JukeboxSelectedMessage(songid));
    }

    public void SetTime(float time)
    {
        var sentTime = time;

        // You may be wondering, what the fuck is this
        // Well we want to be able to predict the playback slider change, of which there are many ways to do it
        // We can't just use SendPredictedMessage because it will reset every tick and audio updates every frame
        // so it will go BRRRRT
        // Using ping gets us close enough that it SHOULD, MOST OF THE TIME, fall within the 0.1 second tolerance
        // that's still on engine so our playback position never gets corrected.
        if (EntMan.TryGetComponent(Owner, out JukeboxComponent? jukebox) &&
            EntMan.TryGetComponent(jukebox.AudioStream, out AudioComponent? audioComp))
        {
            audioComp.PlaybackPosition = time;
        }

        SendMessage(new JukeboxSetTimeMessage(sentTime));
    }

    // DS-14 Start: Volume state is sent separately from playback time so the menu can
    // throttle slider traffic without affecting the playback scrubber.
    public void SetVolume(float volume)
    {
        SendMessage(new JukeboxSetVolumeMessage(volume));
    }

    public bool TryGetLocalVolumeOverride(out float volume)
    {
        if (_menu != null)
            return _menu.TryGetLocalVolumeOverride(out volume);

        volume = default;
        return false;
    }

    private void CommitVolumeState()
    {
        if (_volumeStateCommitted || _menu == null)
            return;

        _volumeStateCommitted = true;
        _menu.CommitVolumeState();
    }
    // DS-14 End
}

