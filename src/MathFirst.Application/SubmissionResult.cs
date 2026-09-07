namespace MathFirst.Application;

public sealed record SubmissionResult(bool IsCorrect, int SubmittedAnswer, int ExpectedAnswer);
