namespace MathFirst.Application.Progression;

public sealed record BandAttemptEvidence
{
    public long PracticePosition { get; }
    public string FactId { get; }
    public bool IsCorrect { get; }
    public bool IsFluent { get; }
    public long ResponseLatencyMs { get; }
    public bool IsInterrupted { get; }
    public bool IsTimingEligible => !IsInterrupted;

    public BandAttemptEvidence(
        long practicePosition,
        string factId,
        bool isCorrect,
        bool isFluent,
        long responseLatencyMs,
        bool isInterrupted = false)
    {
        if (practicePosition <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(practicePosition),
                practicePosition,
                "Rolling advancement evidence requires a positive practice position.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(factId);
        if (responseLatencyMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(responseLatencyMs),
                responseLatencyMs,
                "Response latency must be non-negative.");
        }

        PracticePosition = practicePosition;
        FactId = factId;
        IsCorrect = isCorrect;
        if (isFluent && !isCorrect)
        {
            throw new ArgumentException("Only correct advancement evidence may be fluent.", nameof(isFluent));
        }
        IsFluent = isFluent;
        ResponseLatencyMs = responseLatencyMs;
        IsInterrupted = isInterrupted;
    }
}
