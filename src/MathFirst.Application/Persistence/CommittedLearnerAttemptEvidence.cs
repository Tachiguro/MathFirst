namespace MathFirst.Application.Persistence;

using System;

/// <summary>
/// Read-only snapshot of verified learner attempt evidence from attempt_history.
/// Used to verify pending gameplay intents before authoritative state transitions.
/// </summary>
public sealed record CommittedLearnerAttemptEvidence
{
    public string SubmissionId { get; }
    public string FactId { get; }
    public bool IsCorrect { get; }
    public long ResponseLatencyMs { get; }
    public long? PracticePosition { get; }
    public DateTimeOffset Timestamp { get; }

    public CommittedLearnerAttemptEvidence(
        string submissionId,
        string factId,
        bool isCorrect,
        long responseLatencyMs,
        long? practicePosition,
        DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(submissionId))
        {
            throw new ArgumentException("Submission ID cannot be null or whitespace.", nameof(submissionId));
        }

        if (string.IsNullOrWhiteSpace(factId))
        {
            throw new ArgumentException("Fact ID cannot be null or whitespace.", nameof(factId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(responseLatencyMs);

        if (practicePosition is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(practicePosition), "Practice position must be positive when present.");
        }

        SubmissionId = submissionId;
        FactId = factId;
        IsCorrect = isCorrect;
        ResponseLatencyMs = responseLatencyMs;
        PracticePosition = practicePosition;
        Timestamp = timestamp;
    }
}
