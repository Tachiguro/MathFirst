namespace MathFirst.Application.Scheduling;

public sealed record FsrsCardState(
    string FactId,
    Guid CardId,
    int State,
    int? Step,
    double? Stability,
    double? Difficulty,
    long DuePracticePosition,
    long? LastReviewPracticePosition,
    FsrsRating? LastRating);
