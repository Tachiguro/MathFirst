namespace MathFirst.Application.Persistence;

using System;

/// <summary>
/// Immutable representation of a staged pending combat intent in the Gameplay store.
/// Staged after mathematical answer evaluation but before combat execution.
/// </summary>
public sealed record CyberDefensePendingIntentRecord
{
    public string SubmissionId { get; }
    public string FactId { get; }
    public bool IsCorrect { get; }
    public bool IsEligible { get; }
    public long ResponseLatencyMs { get; }
    public long ResetEpoch { get; }
    public DateTimeOffset CreatedAt { get; }

    public CyberDefensePendingIntentRecord(
        string submissionId,
        string factId,
        bool isCorrect,
        bool isEligible,
        long responseLatencyMs,
        long resetEpoch,
        DateTimeOffset createdAt)
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
        ArgumentOutOfRangeException.ThrowIfNegative(resetEpoch);

        SubmissionId = submissionId;
        FactId = factId;
        IsCorrect = isCorrect;
        IsEligible = isEligible;
        ResponseLatencyMs = responseLatencyMs;
        ResetEpoch = resetEpoch;
        CreatedAt = createdAt;
    }
}
