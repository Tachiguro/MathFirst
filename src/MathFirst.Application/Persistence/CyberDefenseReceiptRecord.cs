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
    public int? AwardedXp { get; }
    public int? PlayerLevelAfter { get; }
    public long? CumulativeXpAfter { get; }
    public int? BaseAttackDamage { get; }
    public int? BoundCriticalStrikeRank { get; }
    public int? BoundOverdriveRank { get; }
    public int? CriticalBonusPercent { get; }
    public int? OverdriveBonusPercent { get; }

    public CyberDefenseReceiptRecord(
        string submissionId,
        CyberDefenseReceiptKind receiptKind,
        string factId,
        bool isCorrect,
        bool isEligible,
        long responseLatencyMs,
        long resetEpoch,
        DateTimeOffset processedAt,
        CyberDefenseCombatTransitionResult? transitionResult = null,
        int? awardedXp = null,
        int? playerLevelAfter = null,
        long? cumulativeXpAfter = null,
        int? baseAttackDamage = null,
        int? boundCriticalStrikeRank = null,
        int? boundOverdriveRank = null,
        int? criticalBonusPercent = null,
        int? overdriveBonusPercent = null)
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

        bool anyModifierPresent = baseAttackDamage.HasValue ||
                                  boundCriticalStrikeRank.HasValue ||
                                  boundOverdriveRank.HasValue ||
                                  criticalBonusPercent.HasValue ||
                                  overdriveBonusPercent.HasValue;

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

            if (awardedXp.HasValue || playerLevelAfter.HasValue || cumulativeXpAfter.HasValue)
            {
                if (!awardedXp.HasValue || !playerLevelAfter.HasValue || !cumulativeXpAfter.HasValue)
                {
                    throw new ArgumentException(
                        "Reward metadata (awarded XP, player level, cumulative XP) must either all be present or all be null.",
                        nameof(awardedXp));
                }

                ArgumentOutOfRangeException.ThrowIfNegative(awardedXp.Value, nameof(awardedXp));
                ArgumentOutOfRangeException.ThrowIfLessThan(playerLevelAfter.Value, 1, nameof(playerLevelAfter));
                ArgumentOutOfRangeException.ThrowIfNegative(cumulativeXpAfter.Value, nameof(cumulativeXpAfter));
            }

            if (anyModifierPresent)
            {
                if (!baseAttackDamage.HasValue ||
                    !boundCriticalStrikeRank.HasValue ||
                    !boundOverdriveRank.HasValue ||
                    !criticalBonusPercent.HasValue ||
                    !overdriveBonusPercent.HasValue)
                {
                    throw new ArgumentException(
                        "Combat modifier receipt metadata (base attack damage, bound ranks, and bonus percentages) must either all be present or all be null.",
                        nameof(baseAttackDamage));
                }

                ArgumentOutOfRangeException.ThrowIfLessThan(baseAttackDamage.Value, 1, nameof(baseAttackDamage));

                if (boundCriticalStrikeRank.Value < 0 || boundCriticalStrikeRank.Value > CyberDefenseCriticalStrikePolicy.MaxRank)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(boundCriticalStrikeRank),
                        boundCriticalStrikeRank.Value,
                        $"Bound Critical Strike rank must be between 0 and {CyberDefenseCriticalStrikePolicy.MaxRank}.");
                }

                if (boundOverdriveRank.Value < 0 || boundOverdriveRank.Value > CyberDefenseOverdrivePolicy.MaxRank)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(boundOverdriveRank),
                        boundOverdriveRank.Value,
                        $"Bound Overdrive rank must be between 0 and {CyberDefenseOverdrivePolicy.MaxRank}.");
                }

                int maxCriticalBonusPercent = CyberDefenseCriticalStrikePolicy.GetBonusPercent(CyberDefenseCriticalStrikePolicy.MaxRank);
                if (criticalBonusPercent.Value < 0 || criticalBonusPercent.Value > maxCriticalBonusPercent)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(criticalBonusPercent),
                        criticalBonusPercent.Value,
                        $"Critical Strike bonus percent must be between 0 and {maxCriticalBonusPercent}.");
                }

                if (overdriveBonusPercent.Value < 0 || overdriveBonusPercent.Value > CyberDefenseOverdrivePolicy.Rank1BonusPercent)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(overdriveBonusPercent),
                        overdriveBonusPercent.Value,
                        $"Overdrive bonus percent must be between 0 and {CyberDefenseOverdrivePolicy.Rank1BonusPercent}.");
                }

                int maxAllowedCriticalBonus = CyberDefenseCriticalStrikePolicy.GetBonusPercent(boundCriticalStrikeRank.Value);
                if (criticalBonusPercent.Value != 0 && criticalBonusPercent.Value != maxAllowedCriticalBonus)
                {
                    throw new ArgumentException(
                        $"Critical Strike bonus percent ({criticalBonusPercent.Value}) is invalid for bound Critical Strike rank {boundCriticalStrikeRank.Value}.",
                        nameof(criticalBonusPercent));
                }

                int maxAllowedOverdriveBonus = boundOverdriveRank.Value == 0 ? 0 : CyberDefenseOverdrivePolicy.Rank1BonusPercent;
                if (overdriveBonusPercent.Value != 0 && overdriveBonusPercent.Value != maxAllowedOverdriveBonus)
                {
                    throw new ArgumentException(
                        $"Overdrive bonus percent ({overdriveBonusPercent.Value}) is invalid for bound Overdrive rank {boundOverdriveRank.Value}.",
                        nameof(overdriveBonusPercent));
                }

                if (!isCorrect)
                {
                    if (criticalBonusPercent.Value != 0 || overdriveBonusPercent.Value != 0)
                    {
                        throw new ArgumentException(
                            "Incorrect combat attempt receipt cannot have nonzero modifier bonus percentages.",
                            nameof(criticalBonusPercent));
                    }
                }
                else
                {
                    int expectedEffectiveDamage;
                    try
                    {
                        expectedEffectiveDamage = CyberDefenseDamageCompositionPolicy.GetEffectiveDamage(
                            baseAttackDamage.Value,
                            criticalBonusPercent.Value,
                            overdriveBonusPercent.Value);
                    }
                    catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
                    {
                        throw new ArgumentException(
                            "Invalid combat modifier damage composition in receipt metadata.",
                            nameof(baseAttackDamage),
                            ex);
                    }

                    if (transitionResult.RequestedAttackDamage != expectedEffectiveDamage)
                    {
                        throw new ArgumentException(
                            $"Receipt requested attack damage ({transitionResult.RequestedAttackDamage}) does not match composed damage ({expectedEffectiveDamage}) for base damage {baseAttackDamage.Value}, critical bonus {criticalBonusPercent.Value}%, and overdrive bonus {overdriveBonusPercent.Value}%.",
                            nameof(baseAttackDamage));
                    }
                }
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

            if (awardedXp.HasValue || playerLevelAfter.HasValue || cumulativeXpAfter.HasValue)
            {
                throw new ArgumentException(
                    "CalmModeSuppressed receipt must not contain reward metadata.",
                    nameof(awardedXp));
            }

            if (anyModifierPresent)
            {
                throw new ArgumentException(
                    "CalmModeSuppressed receipt must not contain combat modifier metadata.",
                    nameof(baseAttackDamage));
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
        AwardedXp = awardedXp;
        PlayerLevelAfter = playerLevelAfter;
        CumulativeXpAfter = cumulativeXpAfter;
        BaseAttackDamage = baseAttackDamage;
        BoundCriticalStrikeRank = boundCriticalStrikeRank;
        BoundOverdriveRank = boundOverdriveRank;
        CriticalBonusPercent = criticalBonusPercent;
        OverdriveBonusPercent = overdriveBonusPercent;
    }

    public static CyberDefenseReceiptRecord CreateApplied(
        string submissionId,
        string factId,
        bool isCorrect,
        bool isEligible,
        long responseLatencyMs,
        long resetEpoch,
        DateTimeOffset processedAt,
        CyberDefenseCombatTransitionResult transitionResult,
        int? awardedXp = null,
        int? playerLevelAfter = null,
        long? cumulativeXpAfter = null,
        int? baseAttackDamage = null,
        int? boundCriticalStrikeRank = null,
        int? boundOverdriveRank = null,
        int? criticalBonusPercent = null,
        int? overdriveBonusPercent = null)
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
            transitionResult,
            awardedXp,
            playerLevelAfter,
            cumulativeXpAfter,
            baseAttackDamage,
            boundCriticalStrikeRank,
            boundOverdriveRank,
            criticalBonusPercent,
            overdriveBonusPercent);
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
