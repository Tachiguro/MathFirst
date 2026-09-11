namespace MathFirst.Application.Practice;

using MathFirst.Application.Persistence;
using MathFirst.Domain;

public sealed record AdaptivePaceResult(
    long LearnerPaceMs,
    long OperationPaceMs,
    long BandPaceMs,
    long FactPaceMs,
    long InstabilityAllowanceMs,
    long DeadlineMs);

public static class AdaptivePacePolicy
{
    public const long StaticPriorMs = 4500;
    public const long MinimumSampleMs = 600;
    public const long MaximumSampleMs = 12000;
    public const long MinimumDeadlineMs = 3000;
    public const long MaximumDeadlineMs = 30000;

    private const int LearnerSampleLimit = 30;
    private const int OperationSampleLimit = 20;
    private const int BandSampleLimit = 15;
    private const int FactSampleLimit = 5;
    private const int InstabilityOutcomeLimit = 5;

    public static AdaptivePaceResult Calculate(
        ArithmeticFact selectedFact,
        IEnumerable<string> currentOwnedFrontierFactIds,
        IEnumerable<AttemptRecord> boundedAttempts)
    {
        ArgumentNullException.ThrowIfNull(selectedFact);
        ArgumentNullException.ThrowIfNull(currentOwnedFrontierFactIds);
        ArgumentNullException.ThrowIfNull(boundedAttempts);

        var ownedFactIds = currentOwnedFrontierFactIds.ToHashSet(StringComparer.Ordinal);
        var positioned = boundedAttempts
            .Where(attempt => attempt.PracticePosition is > 0)
            .OrderByDescending(attempt => attempt.PracticePosition)
            .ToArray();
        var correct = positioned.Where(attempt => attempt.Outcome == AttemptOutcome.Correct).ToArray();

        var learnerPace = Shrink(
            StaticPriorMs,
            12,
            LatestLatencies(correct, LearnerSampleLimit));
        var operationPace = Shrink(
            learnerPace,
            8,
            LatestLatencies(
                correct.Where(attempt => attempt.Operation == selectedFact.Operation),
                OperationSampleLimit));
        var bandPace = Shrink(
            operationPace,
            6,
            LatestLatencies(
                correct.Where(attempt =>
                    attempt.Operation == selectedFact.Operation
                    && ownedFactIds.Contains(attempt.FactId)),
                BandSampleLimit));
        var factPace = Shrink(
            bandPace,
            4,
            LatestLatencies(
                correct.Where(attempt => StringComparer.Ordinal.Equals(attempt.FactId, selectedFact.Id)),
                FactSampleLimit));

        var latestFactOutcomes = positioned
            .Where(attempt => StringComparer.Ordinal.Equals(attempt.FactId, selectedFact.Id))
            .Take(InstabilityOutcomeLimit)
            .ToArray();
        var incorrectCount = latestFactOutcomes.Count(attempt => attempt.Outcome == AttemptOutcome.Incorrect);
        var timeoutCount = latestFactOutcomes.Count(attempt => attempt.Outcome == AttemptOutcome.Timeout);
        var allowance = Math.Min(3000L, checked(1000L * incorrectCount + 1500L * timeoutCount));
        var deadline = CalculateDeadline(factPace, allowance);

        return new AdaptivePaceResult(
            learnerPace,
            operationPace,
            bandPace,
            factPace,
            allowance,
            deadline);
    }

    public static long ClampLatencySample(long responseLatencyMs) =>
        Math.Clamp(responseLatencyMs, MinimumSampleMs, MaximumSampleMs);

    public static long Median(IEnumerable<long> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var ordered = samples.Order().ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("At least one sample is required to calculate a median.", nameof(samples));
        }

        var upperIndex = ordered.Length / 2;
        if (ordered.Length % 2 != 0)
        {
            return ordered[upperIndex];
        }

        return DivideRoundHalfUp(
            checked(ordered[upperIndex - 1] + ordered[upperIndex]),
            2);
    }

    public static long Shrink(long parentMs, int parentWeight, IEnumerable<long> samples)
    {
        if (parentMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parentMs));
        }
        if (parentWeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parentWeight));
        }
        ArgumentNullException.ThrowIfNull(samples);

        var transformed = samples.Select(ClampLatencySample).ToArray();
        if (transformed.Length == 0)
        {
            return parentMs;
        }

        var numerator = checked(
            parentWeight * parentMs
            + transformed.Length * Median(transformed));
        return DivideRoundHalfUp(numerator, checked(parentWeight + transformed.Length));
    }

    public static long CalculateDeadline(long factPaceMs, long instabilityAllowanceMs)
    {
        if (factPaceMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factPaceMs));
        }
        if (instabilityAllowanceMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(instabilityAllowanceMs));
        }

        var rawDeadline = checked(2 * factPaceMs + instabilityAllowanceMs);
        var roundedDeadline = checked(((rawDeadline + 99) / 100) * 100);
        return Math.Clamp(roundedDeadline, MinimumDeadlineMs, MaximumDeadlineMs);
    }

    private static IEnumerable<long> LatestLatencies(
        IEnumerable<AttemptRecord> orderedNewestFirst,
        int limit) =>
        orderedNewestFirst.Take(limit).Select(attempt => attempt.ResponseLatencyMs);

    private static long DivideRoundHalfUp(long numerator, long denominator)
    {
        if (numerator < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator));
        }
        if (denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator));
        }

        return checked(numerator + denominator / 2) / denominator;
    }
}
