using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;

namespace Content.Shared.DeadSpace.Soyuz.Sterility;

public enum InjectorTransferKind
{
    Inject,
    Draw
}

public sealed class InjectorTransferCompletedEvent : EntityEventArgs
{
    public EntityUid User { get; }
    public EntityUid Target { get; }
    public InjectorTransferKind Kind { get; }
    public FixedPoint2 Amount { get; }
    public Solution TransferredSolution { get; }

    public InjectorTransferCompletedEvent(
        EntityUid user,
        EntityUid target,
        InjectorTransferKind kind,
        FixedPoint2 amount,
        Solution transferredSolution)
    {
        User = user;
        Target = target;
        Kind = kind;
        Amount = amount;
        TransferredSolution = transferredSolution;
    }
}
