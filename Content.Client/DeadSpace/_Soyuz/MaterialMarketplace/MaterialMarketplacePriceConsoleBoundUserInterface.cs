// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT
using Content.Shared.DeadSpace._Soyuz.MaterialMarketplace;

namespace Content.Client.DeadSpace._Soyuz.MaterialMarketplace;

public sealed class MaterialMarketplacePriceConsoleBoundUserInterface : BoundUserInterface
{
    private MaterialMarketplacePriceConsoleMenu? _menu;

    public MaterialMarketplacePriceConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = new MaterialMarketplacePriceConsoleMenu();
        _menu.OnSetPrice += (isItem, id, price) =>
            SendMessage(new MaterialMarketplaceSetPriceMessage(isItem, id, price));
        _menu.OnSetDynamicPricing += enabled =>
            SendMessage(new MaterialMarketplaceSetDynamicPricingMessage(enabled));
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is MaterialMarketplacePriceConsoleState consoleState)
            _menu?.UpdateState(consoleState);
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