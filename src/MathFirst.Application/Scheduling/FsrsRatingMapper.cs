namespace MathFirst.Application.Scheduling;

using MathFirst.Domain;

public static class FsrsRatingMapper
{
    public static FsrsRating MapRating(
        AttemptOutcome outcome,
        long latencyMs,
        long easyThresholdMs = LearningPolicy.DefaultEasyResponseThresholdMs,
        long fluentThresholdMs = LearningPolicy.DefaultFluentResponseThresholdMs)
    {
        if (outcome is AttemptOutcome.Incorrect or AttemptOutcome.Timeout)
        {
            return FsrsRating.Again;
        }

        if (latencyMs > fluentThresholdMs)
        {
            return FsrsRating.Hard;
        }

        if (latencyMs > easyThresholdMs)
        {
            return FsrsRating.Good;
        }

        return FsrsRating.Easy;
    }
}
