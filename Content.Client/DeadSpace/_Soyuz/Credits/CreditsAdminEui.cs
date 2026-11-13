// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using Content.Client.Eui;
using Content.Client.DeadSpace._Soyuz.Credits.UI;
using Content.Shared.DeadSpace._Soyuz.Credits;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client.DeadSpace._Soyuz.Credits;

[UsedImplicitly]
public sealed class CreditsAdminEui : BaseEui
{
    private readonly SoyuzCreditsAdminWindow _window = new();

    public CreditsAdminEui()
    {
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnLookup += (target, page, type, actor, reason) => SendMessage(new CreditsAdminLookupMessage
        {
            Target = target,
            Page = page,
            TypeFilter = type,
            ActorFilter = actor,
            ReasonFilter = reason,
        });
        _window.OnTake += (target, amount, reason) =>
            SendMessage(new CreditsAdminTakeMessage { Target = target, Amount = amount, Reason = reason });
        _window.OnRefund += (target, productId, reason) =>
            SendMessage(new CreditsAdminRefundMessage { Target = target, ProductId = productId, Reason = reason });
        _window.OnStats += () => SendMessage(new CreditsAdminStatsMessage());
        _window.OnGive += (target, amount, reason, type) =>
            SendMessage(new CreditsAdminGiveMessage { Target = target, Amount = amount, Reason = reason, Type = type });
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is CreditsAdminState s)
            _window.UpdateState(s);
    }
}
