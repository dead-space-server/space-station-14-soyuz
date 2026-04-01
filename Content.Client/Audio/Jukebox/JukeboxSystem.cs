using System;
using System.Collections.Generic;
using Content.Shared.Audio.Jukebox;
using Content.Shared.DeadSpace.Ports.Jukebox;
using Robust.Client.Audio;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;

namespace Content.Client.Audio.Jukebox;


public sealed class JukeboxSystem : SharedJukeboxSystem
{
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly AnimationPlayerSystem _animationPlayer = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    // DS-14 start
    private readonly Dictionary<EntityUid, float> _volumeOverrides = new();
    private const float VolumeOverrideSyncTolerance = 0.01f;
    // DS-14 end

    public override void Initialize()
    {
        base.Initialize();
        // DS-14
        UpdatesAfter.Add(typeof(AudioSystem));
        SubscribeLocalEvent<JukeboxComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeLocalEvent<JukeboxComponent, AnimationCompletedEvent>(OnAnimationCompleted);
        SubscribeLocalEvent<JukeboxComponent, AfterAutoHandleStateEvent>(OnJukeboxAfterState);
        // DS-14
        SubscribeLocalEvent<JukeboxComponent, ComponentShutdown>(OnJukeboxShutdown);

        _protoManager.PrototypesReloaded += OnProtoReload;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _protoManager.PrototypesReloaded -= OnProtoReload;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // DS-14 start
        var query = AllEntityQuery<JukeboxComponent>();

        while (query.MoveNext(out var uid, out var component))
        {
            ApplyClientVolume(component.AudioStream, GetEffectiveVolume(uid, component));
        }
        // DS-14 end
    }

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

    private void OnJukeboxAfterState(Entity<JukeboxComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        var effectiveVolume = GetEffectiveVolume(ent.Owner, ent.Comp);

        if (!_uiSystem.TryGetOpenUi<JukeboxBoundUserInterface>(ent.Owner, JukeboxUiKey.Key, out var bui))
        {
            // DS-14
            ApplyClientVolume(ent.Comp.AudioStream, effectiveVolume);
            return;
        }

        bui.Reload();
        // DS-14
        ApplyClientVolume(ent.Comp.AudioStream, effectiveVolume);
    }

    private void OnJukeboxShutdown(EntityUid uid, JukeboxComponent component, ComponentShutdown args)
    {
        // DS-14
        _volumeOverrides.Remove(uid);
    }

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

    // DS-14 start
    public void SetVolumeOverride(EntityUid jukebox, float volume)
    {
        // DS-14
        _volumeOverrides[jukebox] = JukeboxVolume.Clamp(volume);
    }

    public void ClearVolumeOverride(EntityUid jukebox)
    {
        // DS-14
        _volumeOverrides.Remove(jukebox);
    }

    public bool TryGetVolumeOverride(EntityUid jukebox, out float volume)
    {
        // DS-14
        return _volumeOverrides.TryGetValue(jukebox, out volume);
    }

    public void ApplyClientVolume(EntityUid? audioStream, float volume)
    {
        if (audioStream == null || !TryComp(audioStream, out AudioComponent? audio))
            return;

        var volumeDb = JukeboxVolume.ToDb(JukeboxVolume.Clamp(volume));

        // DS-14 start
        if (Math.Abs(audio.Volume - volumeDb) <= 0.001f)
            return;

        audio.Volume = volumeDb;
        // DS-14 end
    }

    private float GetEffectiveVolume(EntityUid jukebox, JukeboxComponent component)
    {
        // DS-14 start
        if (!_volumeOverrides.TryGetValue(jukebox, out var volume))
            return component.Volume;

        if (Math.Abs(volume - component.Volume) <= VolumeOverrideSyncTolerance)
        {
            _volumeOverrides.Remove(jukebox);
            return component.Volume;
        }

        return volume;
        // DS-14 end
    }
    // DS-14 end
}
