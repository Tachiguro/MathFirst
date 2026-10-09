namespace MathFirst.Application.Persistence;

using System;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Immutable representation of a processed combat attempt receipt in the Gameplay receipt ledger.
/// Completely decoupled from SQLite infrastructure and presentation types.
/// </summary>
public sealed record CyberDefenseReceiptRecord
{
    public string SubmissionId { get; }
    public CyberDefenseReceiptKind ReceiptKind { get; }
    public string FactId { get; }
    public bool IsCorrect { get; }
    public bool IsEligible { get; }
    public long ResponseLatencyMs { get; }
    public long ResetEpoch { get; }
    public DateTimeOffset ProcessedAt { get; }
    public CyberDefenseCombatTransitionResult? TransitionResult { get; }

    public CyberDefenseReceiptRecord(
        string submissionId,
        CyberDefenseReceiptKind receiptKind,
        string factId,
        bool isCorrect,
        bool isEligible,
        long responseLatencyMs,
        long resetEpoch,
        DateTimeOffset processedAt,
        CyberDefenseCombatTransitionResult? transitionResult = null)
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

        if (!Enum.IsDefined(typeof(CyberDefenseReceiptKind), receiptKind))
        {
            throw new ArgumentOutOfRangeException(nameof(receiptKind), $"Unsupported receipt kind '{receiptKind}'.");
        }

        if (receiptKind == CyberDefenseReceiptKind.Applied)
        {
            if (transitionResult is null)
            {
                throw new ArgumentException(
                    "Applied receipt requires a non-null combat transition result.",
                    nameof(transitionResult));
            }

            if (transitionResult.IsCorrect != isCorrect)
            {
                throw new ArgumentException(
                    $"Attempt correctness ({isCorrect}) does not match transition result correctness ({transitionResult.IsCorrect}).",
                    nameof(isCorrect));
            }
        }
        else if (receiptKind == CyberDefenseReceiptKind.CalmModeSuppressed)
        {
            if (transitionResult is not null)
            {
                throw new ArgumentException(
                    "CalmModeSuppressed receipt must not contain a combat transition result.",
                    nameof(transitionResult));
            }
        }

        SubmissionId = submissionId;
        ReceiptKind = receiptKind;
        FactId = factId;
        IsCorrect = isCorrect;
        IsEligible = isEligible;
        ResponseLatencyMs = responseLatencyMs;
        ResetEpoch = resetEpoch;
        ProcessedAt = processedAt;
        TransitionResult = transitionResult;
    }

    public static CyberDefenseReceiptRecord CreateApplied(
        string submissionId,
        string factId,
        bool isCorrect,
        bool isEligible,
        long responseLatencyMs,
        long resetEpoch,
        DateTimeOffset processedAt,
        CyberDefenseCombatTransitionResult transitionResult)
    {
        return new CyberDefenseReceiptRecord(
            submissionId,
            CyberDefenseReceiptKind.Applied,
            factId,
            isCorrect,
            isEligible,
            responseLatencyMs,
            resetEpoch,
            processedAt,
            transitionResult);
    }

    public static CyberDefenseReceiptRecord CreateCalmModeSuppressed(
        string submissionId,
        string factId,
        bool isCorrect,
        bool isEligible,
        long responseLatencyMs,
        long resetEpoch,
        DateTimeOffset processedAt)
    {
        return new CyberDefenseReceiptRecord(
            submissionId,
            CyberDefenseReceiptKind.CalmModeSuppressed,
            factId,
            isCorrect,
            isEligible,
            responseLatencyMs,
            resetEpoch,
            processedAt,
            null);
    }
}
