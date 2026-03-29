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

