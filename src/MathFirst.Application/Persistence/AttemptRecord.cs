namespace MathFirst.Application.Persistence;

using MathFirst.Domain;

public sealed record AttemptRecord
{
    public string SubmissionId { get; init; }
    public string FactId { get; init; }
    public ArithmeticOperation Operation { get; init; }
    public int LeftOperand { get; init; }
    public int RightOperand { get; init; }
    public int? SubmittedAnswer { get; init; }
    public int CorrectAnswer { get; init; }
    public bool IsCorrect { get; init; }
    public long ResponseLatencyMs { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public AttemptOutcome Outcome { get; init; }
    public long? PracticePosition { get; init; }

    public AttemptRecord(
        string submissionId,
        string factId,
        ArithmeticOperation operation,
        int leftOperand,
        int rightOperand,
        int? submittedAnswer,
        int correctAnswer,
        bool isCorrect,
        long responseLatencyMs,
        DateTimeOffset timestamp,
        AttemptOutcome? outcome = null,
        long? practicePosition = null)
    {
        SubmissionId = submissionId;
        FactId = factId;
        Operation = operation;
        LeftOperand = leftOperand;
        RightOperand = rightOperand;
        SubmittedAnswer = submittedAnswer;
        CorrectAnswer = correctAnswer;
        IsCorrect = isCorrect;
        ResponseLatencyMs = responseLatencyMs;
        Timestamp = timestamp;
        Outcome = outcome ?? (isCorrect ? AttemptOutcome.Correct : (submittedAnswer is null ? AttemptOutcome.Timeout : AttemptOutcome.Incorrect));
        if (practicePosition is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(practicePosition), "Practice position must be positive when present.");
        }
        PracticePosition = practicePosition;
    }
}
