namespace MathFirst.Domain.CyberDefense;

public static class CyberDefenseLevelPolicy
{
    public const int StartingLevel = 1;
    public const long StartingCumulativeXp = 0L;

    private const int MaxPrecomputedLevel = 5000;
    private static readonly long[] PrecomputedThresholds;

    static CyberDefenseLevelPolicy()
    {
        PrecomputedThresholds = new long[MaxPrecomputedLevel + 1];
        long cumulative = 0L;
        for (int lvl = 1; lvl <= MaxPrecomputedLevel; lvl++)
        {
            PrecomputedThresholds[lvl] = cumulative;
            cumulative = checked(cumulative + CalculateDeltaXp(lvl));
        }
    }

    public static int GetDeltaXp(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        return CalculateDeltaXp(level);
    }

    private static int CalculateDeltaXp(int level)
    {
        if (level <= 100)
        {
            double raw = 80.0 + 24.0 * Math.Pow(level - 1, 0.75);
            return checked((int)Math.Ceiling(raw));
        }
        else
        {
            double raw = 834.0 + 160.0 * Math.Log2((double)level / 100.0);
            return checked((int)Math.Ceiling(raw));
        }
    }

    public static long GetCumulativeXpThreshold(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);

        if (level <= MaxPrecomputedLevel)
        {
            return PrecomputedThresholds[level];
        }

        long threshold = PrecomputedThresholds[MaxPrecomputedLevel];
        for (int lvl = MaxPrecomputedLevel; lvl < level; lvl++)
        {
            threshold = checked(threshold + CalculateDeltaXp(lvl));
        }

        return threshold;
    }

    public static int GetLevel(long cumulativeXp)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cumulativeXp);

        if (cumulativeXp <= PrecomputedThresholds[MaxPrecomputedLevel])
        {
            int index = Array.BinarySearch(PrecomputedThresholds, 1, MaxPrecomputedLevel, cumulativeXp);
            if (index >= 0)
            {
                return index;
            }

            return ~index - 1;
        }

        int low = MaxPrecomputedLevel;
        long xpAbove = cumulativeXp - PrecomputedThresholds[MaxPrecomputedLevel];
        int minDelta = CalculateDeltaXp(MaxPrecomputedLevel);
        long highBound = checked(MaxPrecomputedLevel + (xpAbove / minDelta) + 2L);
        int high = highBound > int.MaxValue ? int.MaxValue : (int)highBound;

        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            long threshold = GetCumulativeXpThreshold(mid);
            if (threshold == cumulativeXp)
            {
                return mid;
            }

            if (threshold < cumulativeXp)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return high;
    }

    public static int GetEarnedSkillPoints(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        return checked(level - 1);
    }

    public static PlayerLevelProgress CalculateProgress(long cumulativeXp)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cumulativeXp);

        int level = GetLevel(cumulativeXp);
        long currentThreshold = GetCumulativeXpThreshold(level);
        long nextThreshold = GetCumulativeXpThreshold(checked(level + 1));

        return new PlayerLevelProgress(level, cumulativeXp, currentThreshold, nextThreshold);
    }
}
