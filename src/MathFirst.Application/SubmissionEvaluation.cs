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
    bool RangeUnlocked,
    bool OperationUnlocked)
{
    public SubmissionEvaluation(
        bool isCorrect,
        int correctAnswer,
        long latencyMs,
        SubmissionChangeSet changeSet,
        bool isProvisionallyMastered,
        bool rangeUnlocked,
        bool operationUnlocked)
        : this(
            isCorrect ? AttemptOutcome.Correct : AttemptOutcome.Incorrect,
            isCorrect,
            changeSet.Attempt.SubmittedAnswer,
            correctAnswer,
            latencyMs,
            changeSet,
            isProvisionallyMastered,
            rangeUnlocked,
            operationUnlocked)
    {
    }
}
