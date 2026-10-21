namespace MathFirst.Domain.CyberDefense;

/// <summary>
/// Pure, deterministic Critical Strike skill policy (GDD §8.3).
/// Critical Strike is accuracy-based: it depends only on the rank and on the ordinal of a
/// confirmed correct combat attack, never on response latency or any timer.
/// Fixed product rules: unlock at Player Level 12, rank 0 grants no bonus, at most two ranks.
/// Preliminary tuning values: rank 2 unlock at Level 50, costs 6/12 SP,
/// rank 1 = every 5th confirmed correct attack +50%, rank 2 = every 4th +100%.
/// The policy returns bonus percentages only. Additive stacking with other bonuses and the
/// single ceiling rounding of final damage, counter ownership and counter persistence
/// across runs are intentionally out of scope here.
/// </summary>
public static class CyberDefenseCriticalStrikePolicy
{
    // [FIXED PRODUCT RULE]
    public const int MaxRank = 2;
    public const int UnlockLevel = 12;

    // [PRELIMINARY BALANCE VALUE / TUNING]
    private const int Rank2RequiredLevel = 50;
    private const int Rank1Cost = 6;
    private const int Rank2Cost = 12;
    private const int Rank1TriggerInterval = 5;
    private const int Rank2TriggerInterval = 4;
    private const int Rank1BonusPercent = 50;
    private const int Rank2BonusPercent = 100;

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

    /// <summary>Number of confirmed correct attacks between critical strikes for a purchased rank (1..MaxRank).</summary>
    public static int GetTriggerInterval(int rank)
    {
        return rank switch
        {
            1 => Rank1TriggerInterval,
            2 => Rank2TriggerInterval,
            _ => throw CreateInvalidPurchasableRank(rank)
        };
    }

    /// <summary>Bonus damage percentage granted on a trigger attack; rank 0 grants none.</summary>
    public static int GetBonusPercent(int rank)
    {
        ValidateRank(rank);
        return rank switch
        {
            1 => Rank1BonusPercent,
            2 => Rank2BonusPercent,
            _ => 0
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
    /// Whether the given confirmed correct combat attack is a critical strike.
    /// <paramref name="confirmedCorrectAttackOrdinal"/> is the 1-based position of the attack
    /// being resolved among confirmed correct combat attacks (this attack included);
    /// 0 means no attack and never triggers.
    /// </summary>
    public static bool IsCriticalStrike(int rank, int confirmedCorrectAttackOrdinal)
    {
        ValidateRank(rank);
        ArgumentOutOfRangeException.ThrowIfNegative(confirmedCorrectAttackOrdinal);

        if (rank == 0 || confirmedCorrectAttackOrdinal == 0)
        {
            return false;
        }

        return confirmedCorrectAttackOrdinal % GetTriggerInterval(rank) == 0;
    }

    /// <summary>Critical bonus percentage (0, 50 or 100) for the given attack ordinal.</summary>
    public static int GetCriticalBonusPercent(int rank, int confirmedCorrectAttackOrdinal)
    {
        return IsCriticalStrike(rank, confirmedCorrectAttackOrdinal) ? GetBonusPercent(rank) : 0;
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
            $"Critical Strike rank must be between 1 and {MaxRank}.");
    }
}
