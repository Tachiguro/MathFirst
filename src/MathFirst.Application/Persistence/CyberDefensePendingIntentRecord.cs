namespace MathFirst.Application.Persistence;

using System;
using MathFirst.Domain.CyberDefense;

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
    public int EffectiveAttackDamage { get; }
    public int BoundCriticalStrikeRank { get; }
    public int BoundOverdriveRank { get; }
    public long? PracticePosition { get; }

    public CyberDefensePendingIntentRecord(
        string submissionId,
        string factId,
        bool isCorrect,
        bool isEligible,
        long responseLatencyMs,
        long resetEpoch,
        DateTimeOffset createdAt)
        : this(
            submissionId,
            factId,
            isCorrect,
            isEligible,
            responseLatencyMs,
            resetEpoch,
            createdAt,
            effectiveAttackDamage: 1,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: null)
    {
    }

    public CyberDefensePendingIntentRecord(
        string submissionId,
        string factId,
        bool isCorrect,
        bool isEligible,
        long responseLatencyMs,
        long resetEpoch,
        DateTimeOffset createdAt,
        int effectiveAttackDamage)
        : this(
            submissionId,
            factId,
            isCorrect,
            isEligible,
            responseLatencyMs,
            resetEpoch,
            createdAt,
            effectiveAttackDamage,
            boundCriticalStrikeRank: 0,
            boundOverdriveRank: 0,
            practicePosition: null)
    {
    }

    public CyberDefensePendingIntentRecord(
        string submissionId,
        string factId,
        bool isCorrect,
        bool isEligible,
        long responseLatencyMs,
        long resetEpoch,
        DateTimeOffset createdAt,
        int effectiveAttackDamage,
        int boundCriticalStrikeRank,
        int boundOverdriveRank)
        : this(
            submissionId,
            factId,
            isCorrect,
            isEligible,
            responseLatencyMs,
            resetEpoch,
            createdAt,
            effectiveAttackDamage,
            boundCriticalStrikeRank,
            boundOverdriveRank,
            practicePosition: null)
    {
    }

    public CyberDefensePendingIntentRecord(
        string submissionId,
        string factId,
        bool isCorrect,
        bool isEligible,
        long responseLatencyMs,
        long resetEpoch,
        DateTimeOffset createdAt,
        int effectiveAttackDamage,
        int boundCriticalStrikeRank,
        int boundOverdriveRank,
        long? practicePosition)
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

        if (effectiveAttackDamage < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectiveAttackDamage),
                effectiveAttackDamage,
                "Effective attack damage must be at least 1.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(boundCriticalStrikeRank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(boundCriticalStrikeRank, CyberDefenseCriticalStrikePolicy.MaxRank);
        ArgumentOutOfRangeException.ThrowIfNegative(boundOverdriveRank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(boundOverdriveRank, CyberDefenseOverdrivePolicy.MaxRank);

        if (practicePosition.HasValue && practicePosition.Value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(practicePosition),
                practicePosition,
                "Practice position must be at least 1 when specified.");
        }

        SubmissionId = submissionId;
        FactId = factId;
        IsCorrect = isCorrect;
        IsEligible = isEligible;
        ResponseLatencyMs = responseLatencyMs;
        ResetEpoch = resetEpoch;
        CreatedAt = createdAt;
        EffectiveAttackDamage = effectiveAttackDamage;
        BoundCriticalStrikeRank = boundCriticalStrikeRank;
        BoundOverdriveRank = boundOverdriveRank;
        PracticePosition = practicePosition;
    }

    public CyberDefensePendingIntentRecord WithPracticePosition(long practicePosition)
    {
        if (practicePosition < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(practicePosition),
                practicePosition,
                "Practice position must be at least 1.");
        }

        return new CyberDefensePendingIntentRecord(
            SubmissionId,
            FactId,
            IsCorrect,
            IsEligible,
            ResponseLatencyMs,
            ResetEpoch,
            CreatedAt,
            EffectiveAttackDamage,
            BoundCriticalStrikeRank,
            BoundOverdriveRank,
            practicePosition);
    }
}
