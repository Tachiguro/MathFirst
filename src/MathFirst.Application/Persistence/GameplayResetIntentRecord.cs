namespace MathFirst.Application.Persistence;

using System;

/// <summary>
/// Immutable representation of the durable reset intent in the Gameplay store (gameplay_reset_intent).
/// Used to fence mutations and guarantee crash-recoverable full resets across persistence boundaries.
/// </summary>
public sealed record GameplayResetIntentRecord
{
    public bool IsPending { get; }
    public long CurrentEpoch { get; }
    public long TargetEpoch { get; }
    public DateTimeOffset? CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }

    public GameplayResetIntentRecord(
        bool isPending,
        long currentEpoch,
        long targetEpoch,
        DateTimeOffset? createdAt,
        DateTimeOffset updatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentEpoch);
        ArgumentOutOfRangeException.ThrowIfNegative(targetEpoch);

        if (targetEpoch < currentEpoch)
        {
            throw new ArgumentException($"Target epoch {targetEpoch} cannot be less than current epoch {currentEpoch}.", nameof(targetEpoch));
        }

        if (isPending && targetEpoch <= currentEpoch)
        {
            throw new ArgumentException($"Pending reset intent must have target epoch ({targetEpoch}) strictly greater than current epoch ({currentEpoch}).", nameof(targetEpoch));
        }

        if (!isPending && targetEpoch != currentEpoch)
        {
            throw new ArgumentException($"Non-pending reset intent must have target epoch ({targetEpoch}) equal to current epoch ({currentEpoch}).", nameof(targetEpoch));
        }

        IsPending = isPending;
        CurrentEpoch = currentEpoch;
        TargetEpoch = targetEpoch;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }
}
