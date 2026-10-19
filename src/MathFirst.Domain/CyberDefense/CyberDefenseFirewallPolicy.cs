namespace MathFirst.Domain.CyberDefense;

/// <summary>
/// Pure, deterministic Firewall skill policy (GDD §8.2).
/// Fixed product rules: unlock at Player Level 5, rank 0 is locked with zero slots,
/// at most three permanent shield slots, both confirmed outcomes recharge shields.
/// Preliminary tuning values: slot unlock levels 5/20/50, slot costs 3/8/15 SP,
/// recharge weights +2 correct / +1 incorrect, and a 30-unit restore threshold.
/// Absorption and recharge state transitions are intentionally out of scope here.
/// </summary>
public static class CyberDefenseFirewallPolicy
{
    // [FIXED PRODUCT RULE]
    public const int MaxRank = 3;
    public const int MaxShieldCapacity = 3;
    public const int UnlockLevel = 5;

    // [PRELIMINARY BALANCE VALUE / TUNING]
    public const int CorrectAttemptRechargeUnits = 2;
    public const int IncorrectAttemptRechargeUnits = 1;
    public const int RechargeThresholdUnits = 30;

    // [PRELIMINARY BALANCE VALUE / TUNING]
    private const int Rank2RequiredLevel = 20;
    private const int Rank3RequiredLevel = 50;
    private const int Rank1Cost = 3;
    private const int Rank2Cost = 8;
    private const int Rank3Cost = 15;

    public static int GetRequiredLevel(int rank)
    {
        return rank switch
        {
            1 => UnlockLevel,
            2 => Rank2RequiredLevel,
            3 => Rank3RequiredLevel,
            _ => throw new ArgumentOutOfRangeException(
                nameof(rank),
                rank,
                $"Firewall rank must be between 1 and {MaxRank}.")
        };
    }

    public static int GetUpgradeCost(int rank)
    {
        return rank switch
        {
            1 => Rank1Cost,
            2 => Rank2Cost,
            3 => Rank3Cost,
            _ => throw new ArgumentOutOfRangeException(
                nameof(rank),
                rank,
                $"Firewall rank must be between 1 and {MaxRank}.")
        };
    }

    public static int GetShieldCapacity(int rank)
    {
        ValidateRank(rank);
        return rank;
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

    public static int GetRechargeUnits(bool isCorrect)
    {
        return isCorrect ? CorrectAttemptRechargeUnits : IncorrectAttemptRechargeUnits;
    }

    private static void ValidateRank(int rank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rank, MaxRank);
    }
}
