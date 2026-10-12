using System.Numerics;
using Content.Client.Camera;
using Content.Shared.Camera;
using Content.Shared.DeadSpace.Camera;
using Content.Shared.DeadSpace.Sectants;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Client.DeadSpace.Sectants;

public sealed class ClientSectantAnnouncementSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly CameraRecoilSystem _recoil = default!;
    [Dependency] private readonly ScreenshakeSystem _screenshake = default!;
    [Dependency] private readonly IResourceCache _cache = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private SectantAnnouncementOverlay? _text;
    private SectantFlashOverlay? _flash;
    private SectantVignetteOverlay? _vignette;

    public override void Initialize()
    {
        base.Initialize();

        _text = new SectantAnnouncementOverlay(_cache);
        _ui.OnPostDrawUIRoot += DrawText;

        SubscribeNetworkEvent<SectantAnnouncementEvent>(OnAnnounce);
    }

    public override void Shutdown()
    {
        _ui.OnPostDrawUIRoot -= DrawText;
        _text = null;
        base.Shutdown();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _text?.FrameUpdate(frameTime);
    }

    private void DrawText(PostDrawUIRootEventArgs args)
    {
        _text?.Draw(args.DrawingHandle, args.Root.PixelSizeBox);
    }

    private void OnAnnounce(SectantAnnouncementEvent ev)
    {
        // 1) Вспышка + винетка
        if (_flash != null) _overlay.RemoveOverlay(_flash);
        _flash = new SectantFlashOverlay(ev.Color, ev.FlashDuration, _overlay);
        _overlay.AddOverlay(_flash);

        if (_vignette != null) _overlay.RemoveOverlay(_vignette);
        _vignette = new SectantVignetteOverlay(ev.Color, ev.VignetteDuration, _overlay);
        _overlay.AddOverlay(_vignette);

        // 2) Стилизованный текст
        _text?.Show(ev.Message, ev.TextDuration);

        // 3) Тряска
        if (_player.LocalEntity is not { } local) return;

        var kick = new Vector2(
            _random.NextFloat(-ev.ShakeIntensity, ev.ShakeIntensity),
            _random.NextFloat(-ev.ShakeIntensity, ev.ShakeIntensity));
        _recoil.KickCamera(local, kick);

        _screenshake.Screenshake(local, null, new ScreenshakeParameters
        {
            Trauma = 0.5f,
            DecayRate = 1.2f,
            Frequency = 0.008f,
        });

        // 4) Шёпот — тихий повторяющийся звук поверх
        if (ev.Whisper)
        {
            _audio.PlayGlobal(
                "/Audio/_DeadSpace/TEMP_FOR_EVENT/little_gabry/laughAl.ogg",
                Filter.Local(),
                false,
                AudioParams.Default.WithVolume(-12f).WithLoop(false));
        }
    }
}
