using Content.Server.Chat.Managers;
using Content.Shared.Chat;
using Content.Shared.DeadSpace.Sectants;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server.DeadSpace.Sectants;

public sealed class SectantAnnouncementSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public void Announce(
        string message,
        SoundSpecifier? sound = null,
        Color? color = null,
        float flashDuration = 1.2f,
        float vignetteDuration = 5f,
        float textDuration = 4f,
        float shakeIntensity = 5f,
        bool whisper = true,
        float whisperDuration = 6f)
    {
        var col = color ?? Color.FromHex("#8b0000");

        _chat.DispatchServerAnnouncement(message, col);

        if (sound != null)
            _audio.PlayGlobal(sound, Filter.Broadcast(), recordReplay: true);

        var ev = new SectantAnnouncementEvent
        {
            Message = message,
            Color = col,
            FlashDuration = flashDuration,
            ShakeIntensity = shakeIntensity,
            VignetteDuration = vignetteDuration,
            TextDuration = textDuration,
            Whisper = whisper,
            WhisperDuration = whisperDuration,
        };

        foreach (var session in _players.Sessions)
            RaiseNetworkEvent(ev, session.Channel);
    }

    /// <summary>Показать анонс только одной сущности-игроку.</summary>
    public void AnnounceTo(
        EntityUid target,
        string message,
        SoundSpecifier? sound = null,
        Color? color = null,
        float flashDuration = 1.2f,
        float vignetteDuration = 5f,
        float textDuration = 4f,
        float shakeIntensity = 5f,
        bool whisper = true,
        float whisperDuration = 6f)
    {
        var col = color ?? Color.FromHex("#8b0000");

        // Найти сессию цели
        if (!_players.TryGetSessionByEntity(target, out var session))
            return;

        // Звук — только цели
        if (sound != null)
            _audio.PlayGlobal(sound, Filter.Entities(target), recordReplay: false);

        // Событие — только цели
        var ev = new SectantAnnouncementEvent
        {
            Message = message,
            Color = col,
            FlashDuration = flashDuration,
            ShakeIntensity = shakeIntensity,
            VignetteDuration = vignetteDuration,
            TextDuration = textDuration,
            Whisper = whisper,
            WhisperDuration = whisperDuration,
        };

        RaiseNetworkEvent(ev, session.Channel);
    }

}
