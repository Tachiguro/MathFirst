namespace MathFirst.Application.Practice;

public sealed record PracticeSessionSummary(
    int CompletedCount,
    int CorrectCount,
    int CurrentCorrectStreak,
    long? MedianCorrectLatencyMs)
{
    public static PracticeSessionSummary Empty { get; } = new(0, 0, 0, null);
}
