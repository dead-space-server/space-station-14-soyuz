// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT

using Content.Shared.DeadSpace._Soyuz.Respawn;

namespace Content.Client.DeadSpace._Soyuz.Respawn;

/// <summary>
/// Показывает окно подтверждения респавна, когда его запрашивает сервер
/// </summary>
public sealed class GhostRespawnConfirmSystem : EntitySystem
{
    private GhostRespawnConfirmWindow? _window;

    public override void Initialize()
    {
        SubscribeNetworkEvent<RespawnConfirmRequestEvent>(OnConfirmRequest);
    }

    private void OnConfirmRequest(RespawnConfirmRequestEvent ev)
    {
        _window?.Close();

        _window = new GhostRespawnConfirmWindow();
        _window.OnConfirm += () => RaiseNetworkEvent(new RespawnConfirmedEvent());
        _window.OpenCentered();
    }
}