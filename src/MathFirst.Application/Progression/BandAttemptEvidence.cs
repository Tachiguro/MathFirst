namespace MathFirst.Application.Progression;

public sealed record BandAttemptEvidence
{
    public long PracticePosition { get; }
    public string FactId { get; }
    public bool IsCorrect { get; }
    public long ResponseLatencyMs { get; }

    public BandAttemptEvidence(
        long practicePosition,
        string factId,
        bool isCorrect,
        long responseLatencyMs)
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
        ResponseLatencyMs = responseLatencyMs;
    }
}
