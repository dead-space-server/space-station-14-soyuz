namespace Content.Shared.Chasm;

public sealed class ChasmFallingAttemptEvent(EntityUid tripper, EntityUid chasm) : CancellableEntityEventArgs
{
    public EntityUid Tripper { get; } = tripper;

    public EntityUid Chasm { get; } = chasm;
}

// DS14-Soyuz-start
[ByRefEvent]
public readonly record struct ChasmFallingEvent(EntityUid Chasm);
// DS14-Soyuz-end
