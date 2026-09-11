namespace MathFirst.Application.Practice;

using MathFirst.Application.Scheduling;
using MathFirst.Domain;

public sealed record AdaptiveAttemptClassification(
    FsrsRating Rating,
    bool IsFluent);

public static class AdaptiveAttemptClassifier
{
    public static AdaptiveAttemptClassification Classify(
        AttemptOutcome outcome,
        long responseLatencyMs,
        long easyThresholdMs,
        long fluencyThresholdMs)
    {
        var rating = FsrsRatingMapper.MapRating(
            outcome,
            responseLatencyMs,
            easyThresholdMs,
            fluencyThresholdMs);
        var isFluent = outcome == AttemptOutcome.Correct
            && responseLatencyMs <= fluencyThresholdMs;
        return new AdaptiveAttemptClassification(rating, isFluent);
    }
}
