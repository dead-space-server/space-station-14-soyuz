namespace Content.Shared.Interaction.Events;

// DS14-Soyuz-start
/// <summary>
/// Event raised on an item when attempting to use it in your hands. Cancelling it stops the interaction.
/// </summary>
/// <param name="user">The user of said item</param>
/// <param name="target">The target of an in-range interaction, if any.</param>
public sealed class GettingUsedAttemptEvent(EntityUid user, EntityUid? target = null) : CancellableEntityEventArgs
{
    public EntityUid User = user;
    public EntityUid? Target { get; } = target;
}
// DS14-Soyuz-end
