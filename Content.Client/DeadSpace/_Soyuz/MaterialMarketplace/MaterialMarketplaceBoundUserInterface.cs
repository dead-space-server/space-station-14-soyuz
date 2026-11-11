// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using Content.Shared.DeadSpace._Soyuz.MaterialMarketplace;

namespace Content.Client.DeadSpace._Soyuz.MaterialMarketplace;

public sealed class MaterialMarketplaceBoundUserInterface : BoundUserInterface
{
    private MaterialMarketplaceMenu? _menu;

    public MaterialMarketplaceBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        _menu = new MaterialMarketplaceMenu();
        _menu.OnCheckout += OnCheckout;
        _menu.OnRequestPayment += () => SendMessage(new MaterialMarketplaceRequestPaymentMessage());
        _menu.OnEjectCash += () => SendMessage(new MaterialMarketplaceEjectCashMessage());
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_menu == null)
            return;

        if (state is MaterialMarketplaceState marketplaceState)
            _menu.UpdateState(marketplaceState);
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

    private void OnCheckout(List<MarketplaceCartLine> lines, MarketplacePayment payment)
    {
        if (lines.Count == 0)
            return;

        SendMessage(new MaterialMarketplaceCheckoutMessage(lines, payment));
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
