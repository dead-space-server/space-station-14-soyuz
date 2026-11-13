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
public sealed class CreditsEui : BaseEui
{
    private readonly SoyuzCreditsWindow _window = new();

    public CreditsEui()
    {
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.OnBuy += id => SendMessage(new CreditsBuyMessage { ProductId = id });
        _window.OnEquip += (id, equip) => SendMessage(new CreditsEquipMessage { ProductId = id, Equip = equip });
        _window.OnPage += page => SendMessage(new CreditsPageMessage { Page = page });
        _window.OnApplyReferral += code => SendMessage(new CreditsReferralApplyMessage { Code = code });
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
        if (state is CreditsEuiState s)
            _window.UpdateState(s);
    }
}
