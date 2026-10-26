using Content.Shared.Audio.Jukebox; // DS14
using Content.Shared.DeadSpace.Audio;
using Content.Shared.DeadSpace.CCCCVars;
using Robust.Shared.Audio.Components; // DS14
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration; // DS14
using SoyuzJukeboxSystem = Content.Client.DeadSpace._Soyuz.Jukebox.JukeboxSystem;

namespace Content.Client.Audio.Jukebox;

public sealed class JukeboxSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!; // DS14
    [Dependency] private readonly SharedTransformSystem _transform = default!; // DS14
    [Dependency] private readonly Robust.Client.Audio.AudioSystem _audio = default!;

    private float _jukeboxAutoVolume; // DS14

    public override void Initialize()
    {
        base.Initialize();

        UpdatesOutsidePrediction = true; // DS14
        UpdatesAfter.Add(typeof(Robust.Client.Audio.AudioSystem)); // DS14
        UpdatesAfter.Add(typeof(SoyuzJukeboxSystem));
        Subs.CVar(_cfg, CCCCVars.JukeboxAutoVolume, SetJukeboxAutoVolume, true); // DS14
    }

    // DS14-Start
    private void SetJukeboxAutoVolume(float volume)
    {
        _jukeboxAutoVolume = volume;
    }
    // DS14-End

    // DS14-Start
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var listener = _audio.GetListenerCoordinates();
        var volume = SharedAudioSystem.GainToVolume(_jukeboxAutoVolume / ContentAudioSystem.JukeboxAutoMultiplier);
        var query = AllEntityQuery<JukeboxComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.AudioStream == null || !TryComp<AudioComponent>(comp.AudioStream.Value, out var audioComp))
                continue;

            var distance = (_transform.GetWorldPosition(uid) - listener.Position).Length();
            var gain = Transform(uid).MapID == listener.MapId
                ? SpatialAudio.GetDistanceGain(distance, audioComp.Params.MaxDistance)
                : 0f;
            // Preserve the engine's map/range mute, including stereo buffers that bypass native distance falloff.
            audioComp.Volume += volume + SharedAudioSystem.GainToVolume(gain);
        }
    }
    // DS14-End
}
