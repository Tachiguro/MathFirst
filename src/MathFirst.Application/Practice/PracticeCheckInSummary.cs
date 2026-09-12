namespace MathFirst.Application.Practice;

using MathFirst.Domain;

public sealed record PracticeCheckInSummary(
    int CorrectCount,
    int TotalCount,
    long? MedianCorrectLatencyMs)
{
    public IReadOnlyList<PracticeProgressionChange> ProgressionChanges { get; init; } = [];
}

public sealed record PracticeProgressionChange(
    ArithmeticOperation Operation,
    int FromStage,
    int ToStage);
