// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Content.Shared.DeadSpace._Soyuz.MaterialMarketplace;
using Content.Shared.DeadSpace._Soyuz.PublicMarket;

namespace Content.Client.DeadSpace._Soyuz.PublicMarket;

public sealed class PublicMarketBoundUserInterface : BoundUserInterface
{
    private PublicMarketMenu? _menu;

    public PublicMarketBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new PublicMarketMenu();
        _menu.OnBuy += (lots, payment) => SendMessage(new PublicMarketBuyMessage(lots, payment));
        _menu.OnRequestPayment += () => SendMessage(new MaterialMarketplaceRequestPaymentMessage());
        _menu.OnEjectCash += () => SendMessage(new MaterialMarketplaceEjectCashMessage());
        _menu.OnSetPrice += (lot, price) => SendMessage(new PublicMarketSetPriceMessage(lot, price));
        _menu.OnWithdraw += lot => SendMessage(new PublicMarketWithdrawMessage(lot));
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is PublicMarketState marketState)
            _menu?.UpdateState(marketState);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        switch (message)
        {
            case MaterialMarketplacePaymentOptionsMessage options:
                _menu?.SetPaymentOptions(options);
                break;
            case MaterialMarketplaceCheckoutResultMessage result:
                _menu?.OnCheckoutResult(result.Success);
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _menu?.Close();
            _menu?.Dispose();
        }
    }
}