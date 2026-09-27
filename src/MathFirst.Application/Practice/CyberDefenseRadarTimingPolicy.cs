namespace MathFirst.Application.Practice;

public readonly record struct CyberDefenseRadarTimingState(
    long TotalDurationMs,
    long ConsumedElapsedMs,
    long RemainingMs,
    double RemainingFraction,
    long NegativeDelayMs,
    bool IsExhausted);

/// <summary>
/// Authoritative timing policy for the Cyber Defense tactical radar timing arc.
/// Calculates the remaining Critical Hit window and animation offset derived from
/// the learner's active solving elapsed time.
/// </summary>
public static class CyberDefenseRadarTimingPolicy
{
    public static CyberDefenseRadarTimingState CalculateTimingState(long criticalWindowMs, long activeElapsedMs)
    {
        var totalDuration = Math.Max(0, criticalWindowMs);
        if (totalDuration == 0)
        {
            return new CyberDefenseRadarTimingState(
                TotalDurationMs: 0,
                ConsumedElapsedMs: 0,
                RemainingMs: 0,
                RemainingFraction: 0.0,
                NegativeDelayMs: 0,
                IsExhausted: true);
        }

        var clampedElapsed = Math.Clamp(activeElapsedMs, 0, totalDuration);
        var remainingMs = Math.Max(0, totalDuration - clampedElapsed);
        var remainingFraction = (double)remainingMs / totalDuration;
        var isExhausted = remainingMs == 0;

        return new CyberDefenseRadarTimingState(
            TotalDurationMs: totalDuration,
            ConsumedElapsedMs: clampedElapsed,
            RemainingMs: remainingMs,
            RemainingFraction: remainingFraction,
            NegativeDelayMs: -clampedElapsed,
            IsExhausted: isExhausted);
    }
}
