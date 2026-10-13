using System.Numerics;

namespace MathFirst.Domain.CyberDefense;

public static class CyberDefenseAttackUpgradePolicy
{
    public const int BaseAttackDamage = 1;

    public static int GetAttackDamage(int rank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rank);
        return checked(BaseAttackDamage + rank);
    }

    public static int GetUpgradeCost(int nextRank)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nextRank, 1);
        int log2 = BitOperations.Log2((uint)nextRank);
        return checked(1 + log2);
    }

    public static long GetCumulativeUpgradeCost(int targetRank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetRank);

        if (targetRank == 0)
        {
            return 0L;
        }

        int m = BitOperations.Log2((uint)targetRank);
        long sumLogs = checked(((long)targetRank + 1L) * (long)m - (1L << (m + 1)) + 2L);
        return checked((long)targetRank + sumLogs);
    }
}
