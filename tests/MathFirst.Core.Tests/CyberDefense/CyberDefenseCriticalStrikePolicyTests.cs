using System.Reflection;
using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseCriticalStrikePolicyTests
{
    [Fact]
    public void FixedProductRules_MatchGdd()
    {
        Assert.Equal(2, CyberDefenseCriticalStrikePolicy.MaxRank);
        Assert.Equal(12, CyberDefenseCriticalStrikePolicy.UnlockLevel);
    }

    [Theory]
    [InlineData(1, 12)]
    [InlineData(2, 50)]
    public void GetRequiredLevel_MatchesRankUnlockLevels(int rank, int expectedLevel)
    {
        Assert.Equal(expectedLevel, CyberDefenseCriticalStrikePolicy.GetRequiredLevel(rank));
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 12)]
    public void GetUpgradeCost_MatchesRankCosts(int rank, int expectedCost)
    {
        Assert.Equal(expectedCost, CyberDefenseCriticalStrikePolicy.GetUpgradeCost(rank));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 4)]
    public void GetTriggerInterval_MatchesRankIntervals(int rank, int expectedInterval)
    {
        Assert.Equal(expectedInterval, CyberDefenseCriticalStrikePolicy.GetTriggerInterval(rank));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 50)]
    [InlineData(2, 100)]
    public void GetBonusPercent_MatchesRankBonuses(int rank, int expectedPercent)
    {
        Assert.Equal(expectedPercent, CyberDefenseCriticalStrikePolicy.GetBonusPercent(rank));
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 11, false)]
    [InlineData(1, 12, true)]
    [InlineData(2, 49, false)]
    [InlineData(2, 50, true)]
    [InlineData(2, int.MaxValue, true)]
    public void IsRankUnlockedAtLevel_EnforcesLevelGates(int rank, int playerLevel, bool expected)
    {
        Assert.Equal(expected, CyberDefenseCriticalStrikePolicy.IsRankUnlockedAtLevel(rank, playerLevel));
    }

    [Theory]
    [InlineData(0, 11, 100, false)] // Level 11 rejects rank 1
    [InlineData(0, 12, 6, true)]    // Level 12 permits rank 1
    [InlineData(0, 12, 5, false)]   // Insufficient SP
    [InlineData(1, 49, 100, false)] // Level 49 rejects rank 2
    [InlineData(1, 50, 12, true)]   // Level 50 permits rank 2
    [InlineData(1, 50, 11, false)]
    [InlineData(0, int.MaxValue, int.MaxValue, true)]
    [InlineData(2, int.MaxValue, int.MaxValue, false)] // Max rank reached
    public void CanPurchaseNextRank_EnforcesLevelAndSkillPointGates(
        int currentRank,
        int playerLevel,
        int availableSkillPoints,
        bool expected)
    {
        Assert.Equal(
            expected,
            CyberDefenseCriticalStrikePolicy.CanPurchaseNextRank(currentRank, playerLevel, availableSkillPoints));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void IsCriticalStrike_RankZero_NeverTriggers(int ordinal)
    {
        Assert.False(CyberDefenseCriticalStrikePolicy.IsCriticalStrike(0, ordinal));
        Assert.Equal(0, CyberDefenseCriticalStrikePolicy.GetCriticalBonusPercent(0, ordinal));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    public void IsCriticalStrike_Rank1_TriggersOnEveryFifthConfirmedCorrectAttack(int ordinal, bool expected)
    {
        Assert.Equal(expected, CyberDefenseCriticalStrikePolicy.IsCriticalStrike(1, ordinal));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public void IsCriticalStrike_Rank2_TriggersOnEveryFourthConfirmedCorrectAttack(int ordinal, bool expected)
    {
        Assert.Equal(expected, CyberDefenseCriticalStrikePolicy.IsCriticalStrike(2, ordinal));
    }

    [Theory]
    [InlineData(1, 5, 50)]
    [InlineData(1, 10, 50)]
    [InlineData(1, 4, 0)]
    [InlineData(1, 6, 0)]
    [InlineData(2, 4, 100)]
    [InlineData(2, 8, 100)]
    [InlineData(2, 3, 0)]
    [InlineData(2, 5, 0)]
    public void GetCriticalBonusPercent_ReturnsRankBonusOnlyOnTriggerAttacks(int rank, int ordinal, int expectedPercent)
    {
        Assert.Equal(expectedPercent, CyberDefenseCriticalStrikePolicy.GetCriticalBonusPercent(rank, ordinal));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Ordinal_ZeroIsNotAnAttackAndNeverTriggers(int rank)
    {
        Assert.False(CyberDefenseCriticalStrikePolicy.IsCriticalStrike(rank, 0));
        Assert.Equal(0, CyberDefenseCriticalStrikePolicy.GetCriticalBonusPercent(rank, 0));
    }

    [Fact]
    public void Ordinal_IntMaxValue_IsHandledWithoutOverflow()
    {
        // 2147483647 is not a multiple of 4 or 5.
        Assert.False(CyberDefenseCriticalStrikePolicy.IsCriticalStrike(1, int.MaxValue));
        Assert.False(CyberDefenseCriticalStrikePolicy.IsCriticalStrike(2, int.MaxValue));
        // 2147483645 = 5 * 429496729.
        Assert.True(CyberDefenseCriticalStrikePolicy.IsCriticalStrike(1, int.MaxValue - 2));
        // 2147483644 = 4 * 536870911.
        Assert.True(CyberDefenseCriticalStrikePolicy.IsCriticalStrike(2, int.MaxValue - 3));
    }

    [Fact]
    public void Calculations_AreDeterministicAcrossRepeatedCalls()
    {
        for (int rank = 0; rank <= 2; rank++)
        {
            for (int ordinal = 0; ordinal <= 40; ordinal++)
            {
                Assert.Equal(
                    CyberDefenseCriticalStrikePolicy.GetCriticalBonusPercent(rank, ordinal),
                    CyberDefenseCriticalStrikePolicy.GetCriticalBonusPercent(rank, ordinal));
            }
        }

        // Over 40 attacks: rank 1 fires 8 times, rank 2 fires 10 times.
        Assert.Equal(8, CountCriticals(1, 40));
        Assert.Equal(10, CountCriticals(2, 40));
        Assert.Equal(0, CountCriticals(0, 40));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void RankValidation_RejectsInvalidRanksEverywhere(int rank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.GetBonusPercent(rank));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.IsCriticalStrike(rank, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.GetCriticalBonusPercent(rank, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.CanPurchaseNextRank(rank, 50, 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void RankLookups_RejectRankZeroAndOutOfRange(int rank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.GetRequiredLevel(rank));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.GetUpgradeCost(rank));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.GetTriggerInterval(rank));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.IsRankUnlockedAtLevel(rank, 50));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NegativeAttackOrdinal_IsRejected(int ordinal)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.IsCriticalStrike(1, ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.GetCriticalBonusPercent(2, ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.IsCriticalStrike(0, ordinal));
    }

    [Fact]
    public void InvalidPlayerLevelOrSkillPoints_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.IsRankUnlockedAtLevel(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.CanPurchaseNextRank(0, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseCriticalStrikePolicy.CanPurchaseNextRank(0, 12, -1));
    }

    [Fact]
    public void PublicSurface_IsStatelessAndTimingFree()
    {
        Type type = typeof(CyberDefenseCriticalStrikePolicy);
        Assert.True(type.IsAbstract && type.IsSealed, "Policy must be a static class.");

        // No hidden mutable counters.
        Assert.DoesNotContain(
            type.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            f => !f.IsLiteral);

        // No time, latency or duration parameters.
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                Assert.NotEqual(typeof(TimeSpan), parameter.ParameterType);
                Assert.NotEqual(typeof(DateTime), parameter.ParameterType);
                Assert.NotEqual(typeof(DateTimeOffset), parameter.ParameterType);
                Assert.NotEqual(typeof(double), parameter.ParameterType);
                Assert.NotEqual(typeof(long), parameter.ParameterType);
            }
        }
    }

    private static int CountCriticals(int rank, int attacks)
    {
        int count = 0;
        for (int ordinal = 1; ordinal <= attacks; ordinal++)
        {
            if (CyberDefenseCriticalStrikePolicy.IsCriticalStrike(rank, ordinal))
            {
                count++;
            }
        }

        return count;
    }
}
