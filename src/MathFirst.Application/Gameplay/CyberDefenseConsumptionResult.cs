namespace MathFirst.Application.Gameplay;

using System;
using MathFirst.Application.Persistence;

public enum CyberDefenseConsumptionStatus
{
    Success,
    AlreadyConsumed,
    UncommittedLearnerAttempt,
    NoPendingIntent
}

public sealed record CyberDefenseConsumptionResult
{
    public CyberDefenseConsumptionStatus Status { get; }
    public string SubmissionId { get; }
    public CyberDefenseReceiptRecord? Receipt { get; }

    private CyberDefenseConsumptionResult(
        CyberDefenseConsumptionStatus status,
        string submissionId,
        CyberDefenseReceiptRecord? receipt)
    {
        Status = status;
        SubmissionId = submissionId ?? string.Empty;
        Receipt = receipt;
    }

    public static CyberDefenseConsumptionResult Success(CyberDefenseReceiptRecord receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new CyberDefenseConsumptionResult(CyberDefenseConsumptionStatus.Success, receipt.SubmissionId, receipt);
    }

    public static CyberDefenseConsumptionResult AlreadyConsumed(CyberDefenseReceiptRecord receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new CyberDefenseConsumptionResult(CyberDefenseConsumptionStatus.AlreadyConsumed, receipt.SubmissionId, receipt);
    }

    public static CyberDefenseConsumptionResult UncommittedLearnerAttempt(string submissionId)
    {
        return new CyberDefenseConsumptionResult(CyberDefenseConsumptionStatus.UncommittedLearnerAttempt, submissionId, null);
    }

    public static CyberDefenseConsumptionResult NoPendingIntent(string submissionId)
    {
        return new CyberDefenseConsumptionResult(CyberDefenseConsumptionStatus.NoPendingIntent, submissionId, null);
    }
}
