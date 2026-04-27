using System; // DS-14
using System.Collections.Generic; // DS-14
using Content.Shared.Audio.Jukebox;
using Content.Shared.DeadSpace.CCCCVars; // DS14-jukebox-mute
using Content.Shared.DeadSpace.Ports.Jukebox; // DS-14
using Robust.Client.Audio; // DS-14
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Audio.Components; // DS-14
using Robust.Shared.Configuration; // DS14-jukebox-mute
using Robust.Shared.Prototypes;

namespace Content.Client.Audio.Jukebox;


public sealed class JukeboxSystem : SharedJukeboxSystem
{
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly AnimationPlayerSystem _animationPlayer = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!; // DS14-jukebox-mute
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    // DS-14 Start: Store a transient client-only override per jukebox so menu drags can
    // update the local audio stream before replicated component state arrives.
    private readonly Dictionary<EntityUid, float> _volumeOverrides = new();
    private const float VolumeOverrideSyncTolerance = 0.01f;
    // DS-14 End

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(AudioSystem)); // DS-14
        SubscribeLocalEvent<JukeboxComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeLocalEvent<JukeboxComponent, AnimationCompletedEvent>(OnAnimationCompleted);
        SubscribeLocalEvent<JukeboxComponent, AfterAutoHandleStateEvent>(OnJukeboxAfterState);
        SubscribeLocalEvent<JukeboxComponent, ComponentShutdown>(OnJukeboxShutdown); // DS-14

        _protoManager.PrototypesReloaded += OnProtoReload;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _protoManager.PrototypesReloaded -= OnProtoReload;
    }

    // DS-14 Start: Audio stream entities may be recreated independently of the UI, so the
    // effective client volume is re-applied every frame to whichever stream is active.
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = AllEntityQuery<JukeboxComponent>();

        while (query.MoveNext(out var uid, out var component))
        {
            ApplyClientVolume(component.AudioStream, GetEffectiveVolume(uid, component));
        }
    }
    // DS-14 End

    private void OnProtoReload(PrototypesReloadedEventArgs obj)
    {
        if (!obj.WasModified<JukeboxPrototype>())
            return;

        var query = AllEntityQuery<JukeboxComponent, UserInterfaceComponent>();

        while (query.MoveNext(out var uid, out _, out var ui))
        {
            if (!_uiSystem.TryGetOpenUi<JukeboxBoundUserInterface>((uid, ui), JukeboxUiKey.Key, out var bui))
                continue;

            bui.PopulateMusic();
        }
    }

    // DS-14 Start: When replicated jukebox state updates, reconcile it with any local
    // override and refresh the open menu without snapping mid-drag.
    private void OnJukeboxAfterState(Entity<JukeboxComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        var effectiveVolume = GetEffectiveVolume(ent.Owner, ent.Comp);

        if (!_uiSystem.TryGetOpenUi<JukeboxBoundUserInterface>(ent.Owner, JukeboxUiKey.Key, out var bui))
        {
            ApplyClientVolume(ent.Comp.AudioStream, effectiveVolume);
            return;
        }

        bui.Reload();
        ApplyClientVolume(ent.Comp.AudioStream, effectiveVolume);
    }

    private void OnJukeboxShutdown(EntityUid uid, JukeboxComponent component, ComponentShutdown args)
    {
        _volumeOverrides.Remove(uid);
    }
    // DS-14 End

    private void OnAnimationCompleted(EntityUid uid, JukeboxComponent component, AnimationCompletedEvent args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        if (!TryComp<AppearanceComponent>(uid, out var appearance) ||
            !_appearanceSystem.TryGetData<JukeboxVisualState>(uid, JukeboxVisuals.VisualState, out var visualState, appearance))
        {
            visualState = JukeboxVisualState.On;
        }

        UpdateAppearance((uid, sprite), visualState, component);
    }

    private void OnAppearanceChange(EntityUid uid, JukeboxComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!args.AppearanceData.TryGetValue(JukeboxVisuals.VisualState, out var visualStateObject) ||
            visualStateObject is not JukeboxVisualState visualState)
        {
            visualState = JukeboxVisualState.On;
        }

        UpdateAppearance((uid, args.Sprite), visualState, component);
    }

    private void UpdateAppearance(Entity<SpriteComponent> entity, JukeboxVisualState visualState, JukeboxComponent component)
    {
        SetLayerState(JukeboxVisualLayers.Base, component.OffState, entity);

        switch (visualState)
        {
            case JukeboxVisualState.On:
                SetLayerState(JukeboxVisualLayers.Base, component.OnState, entity);
                break;

            case JukeboxVisualState.Off:
                SetLayerState(JukeboxVisualLayers.Base, component.OffState, entity);
                break;

            case JukeboxVisualState.Select:
                PlayAnimation(entity.Owner, JukeboxVisualLayers.Base, component.SelectState, 1.0f, entity);
                break;
        }
    }

    private void PlayAnimation(EntityUid uid, JukeboxVisualLayers layer, string? state, float animationTime, SpriteComponent sprite)
    {
        if (string.IsNullOrEmpty(state))
            return;

        if (!_animationPlayer.HasRunningAnimation(uid, state))
        {
            var animation = GetAnimation(layer, state, animationTime);
            _sprite.LayerSetVisible((uid, sprite), layer, true);
            _animationPlayer.Play(uid, animation, state);
        }
    }

    private static Animation GetAnimation(JukeboxVisualLayers layer, string state, float animationTime)
    {
        return new Animation
        {
            Length = TimeSpan.FromSeconds(animationTime),
            AnimationTracks =
                {
                    new AnimationTrackSpriteFlick
                    {
                        LayerKey = layer,
                        KeyFrames =
                        {
                            new AnimationTrackSpriteFlick.KeyFrame(state, 0f)
                        }
                    }
                }
        };
    }

    private void SetLayerState(JukeboxVisualLayers layer, string? state, Entity<SpriteComponent> sprite)
    {
        if (string.IsNullOrEmpty(state))
            return;

        _sprite.LayerSetVisible(sprite.AsNullable(), layer, true);
        _sprite.LayerSetAutoAnimated(sprite.AsNullable(), layer, true);
        _sprite.LayerSetRsiState(sprite.AsNullable(), layer, state);
    }

    // DS-14 Start: These helpers are the menu-facing API for previewing, storing, and
    // eventually clearing local-only volume overrides.
    public void SetVolumeOverride(EntityUid jukebox, float volume)
    {
        _volumeOverrides[jukebox] = JukeboxVolume.Clamp(volume);
    }

    public void ClearVolumeOverride(EntityUid jukebox)
    {
        _volumeOverrides.Remove(jukebox);
    }

    public bool TryGetVolumeOverride(EntityUid jukebox, out float volume)
    {
        return _volumeOverrides.TryGetValue(jukebox, out volume);
    }

    public void ApplyClientVolume(EntityUid? audioStream, float volume)
    {
        if (audioStream == null || !TryComp(audioStream, out AudioComponent? audio))
            return;

        var volumeDb = JukeboxVolume.ToDb(JukeboxVolume.Clamp(volume));

        if (Math.Abs(audio.Volume - volumeDb) <= 0.001f)
            return;

        audio.Volume = volumeDb;
    }

    private float GetEffectiveVolume(EntityUid jukebox, JukeboxComponent component)
    {
        // DS14-start: hard mute from options should silence all jukebox streams.
        if (_cfg.GetCVar(CCCCVars.JukeboxMusicMute))
            return 0f;
        // DS14-end

        if (!_volumeOverrides.TryGetValue(jukebox, out var volume))
            return component.Volume;

        if (Math.Abs(volume - component.Volume) <= VolumeOverrideSyncTolerance)
        {
            _volumeOverrides.Remove(jukebox);
            return component.Volume;
        }

        return volume;
    }
    // DS-14 End
}
