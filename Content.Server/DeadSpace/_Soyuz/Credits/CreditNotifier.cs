// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System;
using Content.Server.Chat.Managers;
using Robust.Server.Player;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Server.DeadSpace._Soyuz.Credits;

public interface ICreditNotifier
{
    /// <summary>
    /// Сообщение игроку в чат
    /// </summary>
    void Notify(Guid userId, string message);
}

public sealed class CreditNotifier : ICreditNotifier
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IChatManager _chat = default!;

    public void Notify(Guid userId, string message)
    {
        if (_players.TryGetSessionById(new NetUserId(userId), out var session))
            _chat.DispatchServerMessage(session, message);
    }
}