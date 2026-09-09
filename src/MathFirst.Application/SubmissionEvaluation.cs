namespace MathFirst.Application;

using MathFirst.Application.Persistence;
using MathFirst.Domain;

public sealed record SubmissionEvaluation(
    AttemptOutcome Outcome,
    bool IsCorrect,
    int? SubmittedAnswer,
    int CorrectAnswer,
    long LatencyMs,
    SubmissionChangeSet ChangeSet,
    bool IsProvisionallyMastered,
    bool OperationAdvanced)
{
    public decimal? SubmittedNumericAnswer { get; init; }

    public SubmissionEvaluation(
        bool isCorrect,
        int correctAnswer,
        long latencyMs,
        SubmissionChangeSet changeSet,
        bool isProvisionallyMastered,
        bool operationAdvanced)
        : this(
            isCorrect ? AttemptOutcome.Correct : AttemptOutcome.Incorrect,
            isCorrect,
            changeSet.Attempt.SubmittedAnswer,
            correctAnswer,
            latencyMs,
            changeSet,
            isProvisionallyMastered,
            operationAdvanced)
    {
    }
}
