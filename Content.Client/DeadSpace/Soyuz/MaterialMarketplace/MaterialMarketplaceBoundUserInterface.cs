using Content.Shared.DeadSpace.MaterialMarketplace;

namespace Content.Client.DeadSpace.MaterialMarketplace;

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
        _menu.OnBuyPressed += OnBuyPressed;
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

    private void OnBuyPressed(string materialId, int amount)
    {
        if (amount <= 0)
            return;

        SendMessage(new MaterialMarketplaceBuyMessage(materialId, amount));
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
