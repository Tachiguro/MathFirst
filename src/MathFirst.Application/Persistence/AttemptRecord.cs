namespace MathFirst.Application.Persistence;

using MathFirst.Domain;

public sealed record AttemptRecord
{
    public string SubmissionId { get; }
    public string FactId { get; }
    public ArithmeticOperation Operation { get; }
    public int LeftOperand { get; }
    public int RightOperand { get; }
    public int? SubmittedAnswer { get; }
    public int CorrectAnswer { get; }
    public bool IsCorrect { get; }
    public bool IsFluent { get; }
    public long ResponseLatencyMs { get; }
    public DateTimeOffset Timestamp { get; }
    public AttemptOutcome Outcome { get; }
    public long? PracticePosition { get; }

    public AttemptRecord(
        string submissionId,
        string factId,
        ArithmeticOperation operation,
        int leftOperand,
        int rightOperand,
        int? submittedAnswer,
        int correctAnswer,
        bool isCorrect,
        bool isFluent,
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
        IsFluent = isFluent;
        ResponseLatencyMs = responseLatencyMs;
        Timestamp = timestamp;
        Outcome = outcome ?? (isCorrect ? AttemptOutcome.Correct : (submittedAnswer is null ? AttemptOutcome.Timeout : AttemptOutcome.Incorrect));
        if (IsCorrect != (Outcome == AttemptOutcome.Correct))
        {
            throw new ArgumentException("Attempt outcome and correctness must be consistent.", nameof(outcome));
        }
        if (IsFluent && Outcome != AttemptOutcome.Correct)
        {
            throw new ArgumentException("Only a correct attempt may be fluent.", nameof(isFluent));
        }
        if (practicePosition is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(practicePosition), "Practice position must be positive when present.");
        }
        PracticePosition = practicePosition;
    }
}
