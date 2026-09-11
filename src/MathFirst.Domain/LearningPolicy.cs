namespace MathFirst.Domain;

public static class LearningPolicy
{
    public const long DefaultEasyResponseThresholdMs = 1000;
    public const long DefaultFluentResponseThresholdMs = 2500;
    public const int MinMasteryAttempts = 3;
    public const int MinConsecutiveCorrectForMastery = 3;
    public const double RangeMasteryThresholdRatio = 0.90;
    public const int RemediationInterveningCount = 3;
    public const int ExactFactCooldownDistance = 3;
    public const int MirrorFactCooldownDistance = 3;
    public const int MaxPreferredOperationStreak = 2;
    public const long FixedAnswerDeadlineMs = 30000;
    public const long DeadlineStreak0Ms = FixedAnswerDeadlineMs;

    public static long GetAnswerDeadlineMs(int consecutiveCorrectStreak) => FixedAnswerDeadlineMs;

    public static double GetAnswerDeadlineSeconds(int consecutiveCorrectStreak) =>
        GetAnswerDeadlineMs(consecutiveCorrectStreak) / 1000.0;

    public static string FormatTimerDisplay(long remainingMs, long deadlineMs = FixedAnswerDeadlineMs)
    {
        var clampedMs = Math.Clamp(remainingMs, 0, Math.Max(0, deadlineMs));
        return (clampedMs / 1000.0).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " s";
    }

    public static string FormatTimerDisplay(double remainingSeconds, double deadlineSeconds = 30.0)
    {
        if (double.IsNaN(remainingSeconds) || double.IsInfinity(remainingSeconds) || remainingSeconds <= 0.0)
        {
            return "0.000 s";
        }

        var clamped = Math.Clamp(remainingSeconds, 0.0, Math.Max(0.0, deadlineSeconds));
        return clamped.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " s";
    }

    public static bool EvaluateItemMastery(
        ItemLearningState state,
        bool isCurrentAttemptFluent)
    {
        if (state.NeedsRemediation)
        {
            return false;
        }

        return state.TotalAttempts >= MinMasteryAttempts
            && state.ConsecutiveCorrectStreak >= MinConsecutiveCorrectForMastery
            && isCurrentAttemptFluent;
    }
}
