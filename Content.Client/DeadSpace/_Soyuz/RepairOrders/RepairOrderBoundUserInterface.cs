using Content.Shared.DeadSpace._Soyuz.RepairOrders;

namespace Content.Client.DeadSpace._Soyuz.RepairOrders;

public sealed class RepairOrderBoundUserInterface : BoundUserInterface
{
    private RepairOrderWindow? _window;

    public RepairOrderBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new RepairOrderWindow();
        _window.OnAccept += runtimeId => SendMessage(new RepairOrderAcceptMessage(runtimeId));
        _window.OnComplete += runtimeId => SendMessage(new RepairOrderCompleteMessage(runtimeId));
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is RepairOrderBoundUserInterfaceState repairState)
            _window?.UpdateState(repairState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        _window?.Close();
        _window?.Dispose();
    }
}
