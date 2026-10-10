namespace MathFirst.Core.Tests.CyberDefense.Simulator;

/// <summary>
/// Immutable outcome of a single deterministic synthetic attempt.
/// </summary>
public readonly record struct SyntheticAttemptResult(
    bool IsCorrect,
    long SyntheticLatencyMilliseconds)
{
    /// <summary>
    /// Convenience alias for <see cref="SyntheticLatencyMilliseconds"/>.
    /// </summary>
    public long LatencyMilliseconds => SyntheticLatencyMilliseconds;
}
