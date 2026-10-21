namespace MathFirst.Domain.CyberDefense;

/// <summary>
/// Pure, deterministic Overdrive skill policy (GDD §8.4).
/// Overdrive is accuracy-based: it depends only on the rank, the count of consecutive confirmed
/// correct combat answers and an explicit remaining-bonus-attack counter, never on response latency
/// or any timer. Fixed product rules: unlock at Player Level 20, rank 0 is disabled, at most two
/// ranks, and any incorrect answer resets the streak to zero (including when a Firewall shield
/// absorbed the counter-damage, which is why no damage or shield input exists on this surface).
/// Preliminary tuning values: rank 2 unlock at Level 80, costs 5/10 SP, rank 1 = after 5 consecutive
/// correct answers the next 2 attacks gain +50%, rank 2 = threshold of 4 consecutive correct answers.
/// Streak and bonus-window state are owned by the caller and passed in explicitly.
/// Intentionally out of scope because the GDD leaves them unresolved: the rank 2 bonus percentage and
/// window length, whether the streak continues or restarts after a trigger, retriggering and
/// overlapping bonus windows. Additive stacking with Critical Strike and the single ceiling rounding
/// of final damage are also out of scope here.
/// </summary>
public static class CyberDefenseOverdrivePolicy
{
    // [FIXED PRODUCT RULE]
    public const int MaxRank = 2;
    public const int UnlockLevel = 20;

    // [PRELIMINARY BALANCE VALUE / TUNING]
    public const int Rank1BonusPercent = 50;
    public const int Rank1BonusAttackCount = 2;
    private const int Rank2RequiredLevel = 80;
    private const int Rank1Cost = 5;
    private const int Rank2Cost = 10;
    private const int Rank1TriggerThreshold = 5;
    private const int Rank2TriggerThreshold = 4;

    public static int GetRequiredLevel(int rank)
    {
        return rank switch
        {
            1 => UnlockLevel,
            2 => Rank2RequiredLevel,
            _ => throw CreateInvalidPurchasableRank(rank)
        };
    }

    public static int GetUpgradeCost(int rank)
    {
        return rank switch
        {
            1 => Rank1Cost,
            2 => Rank2Cost,
            _ => throw CreateInvalidPurchasableRank(rank)
        };
    }

    /// <summary>Consecutive confirmed correct answers required to trigger Overdrive for a purchased rank (1..MaxRank).</summary>
    public static int GetTriggerThreshold(int rank)
    {
        return rank switch
        {
            1 => Rank1TriggerThreshold,
            2 => Rank2TriggerThreshold,
            _ => throw CreateInvalidPurchasableRank(rank)
        };
    }

    public static bool IsRankUnlockedAtLevel(int rank, int playerLevel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(playerLevel, CyberDefenseLevelPolicy.StartingLevel);
        return playerLevel >= GetRequiredLevel(rank);
    }

    public static bool CanPurchaseNextRank(int currentRank, int playerLevel, int availableSkillPoints)
    {
        ValidateRank(currentRank);
        ArgumentOutOfRangeException.ThrowIfLessThan(playerLevel, CyberDefenseLevelPolicy.StartingLevel);
        ArgumentOutOfRangeException.ThrowIfNegative(availableSkillPoints);

        if (currentRank >= MaxRank)
        {
            return false;
        }

        int nextRank = currentRank + 1;
        return IsRankUnlockedAtLevel(nextRank, playerLevel)
            && availableSkillPoints >= GetUpgradeCost(nextRank);
    }

    /// <summary>
    /// Streak after a confirmed combat answer: a correct answer extends it by one (saturating at
    /// <see cref="int.MaxValue"/>); an incorrect answer resets it to zero regardless of whether a
    /// Firewall shield absorbed the resulting damage.
    /// </summary>
    public static int GetStreakAfterAnswer(int streakBeforeAnswer, bool answerWasCorrect)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(streakBeforeAnswer);

        if (!answerWasCorrect)
        {
            return 0;
        }

        return streakBeforeAnswer == int.MaxValue ? int.MaxValue : streakBeforeAnswer + 1;
    }

    /// <summary>
    /// Whether the given consecutive-correct streak has met the rank's trigger threshold.
    /// Rank 0 is disabled and never reaches it. What happens to the streak and any bonus window once
    /// the threshold is met is not defined here.
    /// </summary>
    public static bool HasReachedTriggerThreshold(int rank, int streak)
    {
        ValidateRank(rank);
        ArgumentOutOfRangeException.ThrowIfNegative(streak);

        if (rank == 0)
        {
            return false;
        }

        return streak >= GetTriggerThreshold(rank);
    }

    /// <summary>
    /// Rank 1 bonus percentage (0 or 50) for the attack being resolved, given the bonus attacks that
    /// remained before this attack. The attack that triggers Overdrive is resolved against the window
    /// state held before it armed the window, so it never receives its own bonus.
    /// </summary>
    public static int GetRank1AttackBonusPercent(int remainingBonusAttacks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(remainingBonusAttacks);
        return remainingBonusAttacks > 0 ? Rank1BonusPercent : 0;
    }

    /// <summary>Remaining bonus attacks after resolving one attack; never below zero.</summary>
    public static int ConsumeBonusAttack(int remainingBonusAttacks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(remainingBonusAttacks);
        return remainingBonusAttacks > 0 ? remainingBonusAttacks - 1 : 0;
    }

    private static void ValidateRank(int rank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rank, MaxRank);
    }

    private static ArgumentOutOfRangeException CreateInvalidPurchasableRank(int rank)
    {
        return new ArgumentOutOfRangeException(
            nameof(rank),
            rank,
            $"Overdrive rank must be between 1 and {MaxRank}.");
    }
}
