using System.Numerics;

namespace MathFirst.Domain.CyberDefense;

public static class CyberDefenseScalingPolicy
{
    public static int GetNormalOpponentCount(int sector)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sector, 1);
        return 5 + BitOperations.Log2((uint)sector);
    }

    public static int GetTotalOpponentCount(int sector)
    {
        return GetNormalOpponentCount(sector) + 1;
    }

    public static int GetBossIndex(int sector)
    {
        return GetNormalOpponentCount(sector);
    }

    public static OpponentKind GetOpponentKind(int sector, int opponentIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sector, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(opponentIndex);

        int bossIndex = GetBossIndex(sector);
        if (opponentIndex > bossIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opponentIndex),
                opponentIndex,
                $"Opponent index cannot exceed boss index {bossIndex} for sector {sector}.");
        }

        return opponentIndex == bossIndex ? OpponentKind.Boss : OpponentKind.Normal;
    }

    public static int GetNormalMaxHp(int sector)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sector, 1);
        long n = (long)sector + 15L;
        return checked((int)(IntegerSquareRoot(n) - 2L));
    }

    public static int GetBossMaxHp(int sector)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sector, 1);
        long n = 36L * ((long)sector + 15L);
        return checked((int)(IntegerSquareRoot(n) - 12L));
    }

    public static int GetOpponentMaxHp(int sector, int opponentIndex)
    {
        OpponentKind kind = GetOpponentKind(sector, opponentIndex);
        return kind == OpponentKind.Boss ? GetBossMaxHp(sector) : GetNormalMaxHp(sector);
    }

    private static long IntegerSquareRoot(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (value == 0)
        {
            return 0;
        }

        long root = (long)Math.Sqrt(value);
        while (root > 0 && root > value / root)
        {
            root--;
        }

        while (root + 1 <= value / (root + 1))
        {
            root++;
        }

        return root;
    }
}
