namespace MathFirst.Application.Practice;

public sealed record PracticeCheckInSummary(
    int CorrectCount,
    int TotalCount,
    long? MedianCorrectLatencyMs);
