using System.Reflection;
using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseOverdrivePolicyTests
{
    [Fact]
    public void FixedProductRules_MatchGdd()
    {
        Assert.Equal(2, CyberDefenseOverdrivePolicy.MaxRank);
        Assert.Equal(20, CyberDefenseOverdrivePolicy.UnlockLevel);
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 80)]
    public void GetRequiredLevel_MatchesRankUnlockLevels(int rank, int expectedLevel)
    {
        Assert.Equal(expectedLevel, CyberDefenseOverdrivePolicy.GetRequiredLevel(rank));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    public void GetUpgradeCost_MatchesRankCosts(int rank, int expectedCost)
    {
        Assert.Equal(expectedCost, CyberDefenseOverdrivePolicy.GetUpgradeCost(rank));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 4)]
    public void GetTriggerThreshold_MatchesRankThresholds(int rank, int expectedThreshold)
    {
        Assert.Equal(expectedThreshold, CyberDefenseOverdrivePolicy.GetTriggerThreshold(rank));
    }

    [Fact]
    public void Rank1Effect_IsFiftyPercentForNextTwoAttacks()
    {
        Assert.Equal(50, CyberDefenseOverdrivePolicy.Rank1BonusPercent);
        Assert.Equal(2, CyberDefenseOverdrivePolicy.Rank1BonusAttackCount);
    }

    [Theory]
    [InlineData(1, 19, false)]
    [InlineData(1, 20, true)]
    [InlineData(1, int.MaxValue, true)]
    [InlineData(2, 79, false)]
    [InlineData(2, 80, true)]
    [InlineData(2, int.MaxValue, true)]
    public void IsRankUnlockedAtLevel_EnforcesLevelGates(int rank, int playerLevel, bool expected)
    {
        Assert.Equal(expected, CyberDefenseOverdrivePolicy.IsRankUnlockedAtLevel(rank, playerLevel));
    }

    [Theory]
    [InlineData(0, 19, 100, false)] // Level 19 rejects rank 1
    [InlineData(0, 20, 5, true)]    // Level 20 with exact SP permits rank 1
    [InlineData(0, 20, 4, false)]   // Insufficient SP
    [InlineData(1, 79, 100, false)] // Level 79 rejects rank 2
    [InlineData(1, 80, 10, true)]   // Level 80 with exact SP permits rank 2
    [InlineData(1, 80, 9, false)]
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
            CyberDefenseOverdrivePolicy.CanPurchaseNextRank(currentRank, playerLevel, availableSkillPoints));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void RankZero_IsDisabledAndNeverReachesThreshold(int streak)
    {
        Assert.False(CyberDefenseOverdrivePolicy.HasReachedTriggerThreshold(0, streak));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(int.MaxValue, true)]
    public void Rank1_ReachesThresholdAtFiveConsecutiveCorrectAnswers(int streak, bool expected)
    {
        Assert.Equal(expected, CyberDefenseOverdrivePolicy.HasReachedTriggerThreshold(1, streak));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(int.MaxValue, true)]
    public void Rank2_ReachesThresholdAtFourConsecutiveCorrectAnswers(int streak, bool expected)
    {
        Assert.Equal(expected, CyberDefenseOverdrivePolicy.HasReachedTriggerThreshold(2, streak));
    }

    [Fact]
    public void CorrectAnswers_AccumulateTheStreakByOne()
    {
        int streak = 0;
        for (int expected = 1; expected <= 12; expected++)
        {
            streak = CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(streak, answerWasCorrect: true);
            Assert.Equal(expected, streak);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void IncorrectAnswer_ResetsTheStreakToZeroFromAnyStreak(int streakBefore)
    {
        Assert.Equal(0, CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(streakBefore, answerWasCorrect: false));
    }

    [Fact]
    public void IncorrectAnswer_MidStreak_RestartsAccumulationFromZero()
    {
        int streak = 0;
        for (int i = 0; i < 4; i++)
        {
            streak = CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(streak, true);
        }

        Assert.False(CyberDefenseOverdrivePolicy.HasReachedTriggerThreshold(1, streak));

        streak = CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(streak, false);
        Assert.Equal(0, streak);

        // Four more correct answers after the reset are still not five in a row.
        for (int i = 0; i < 4; i++)
        {
            streak = CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(streak, true);
        }

        Assert.Equal(4, streak);
        Assert.False(CyberDefenseOverdrivePolicy.HasReachedTriggerThreshold(1, streak));
        Assert.True(CyberDefenseOverdrivePolicy.HasReachedTriggerThreshold(2, streak));
    }

    [Fact]
    public void FirewallAbsorption_CannotPreserveTheStreak()
    {
        // The streak API takes only answer correctness: there is no input through which an absorbed
        // (zero player damage) incorrect answer could keep a streak alive.
        MethodInfo streakMethod = typeof(CyberDefenseOverdrivePolicy).GetMethod(
            nameof(CyberDefenseOverdrivePolicy.GetStreakAfterAnswer))!;
        Assert.Equal(new[] { typeof(int), typeof(bool) }, streakMethod.GetParameters().Select(p => p.ParameterType));

        // An incorrect answer resets the streak whether or not a Firewall shield absorbed the counter-damage.
        int streakAtThreshold = CyberDefenseOverdrivePolicy.GetTriggerThreshold(1) - 1;
        Assert.Equal(0, CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(streakAtThreshold, answerWasCorrect: false));
    }

    [Fact]
    public void StreakAccumulation_AtIntMaxValue_SaturatesWithoutOverflow()
    {
        Assert.Equal(
            int.MaxValue,
            CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(int.MaxValue, answerWasCorrect: true));
        Assert.Equal(
            int.MaxValue,
            CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(int.MaxValue - 1, answerWasCorrect: true));
    }

    [Theory]
    [InlineData(2, 50)]
    [InlineData(1, 50)]
    [InlineData(0, 0)]
    public void Rank1AttackBonus_AppliesOnlyWhileBonusAttacksRemain(int remainingBonusAttacks, int expectedPercent)
    {
        Assert.Equal(expectedPercent, CyberDefenseOverdrivePolicy.GetRank1AttackBonusPercent(remainingBonusAttacks));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void ConsumeBonusAttack_DecrementsAndNeverGoesBelowZero(int remaining, int expected)
    {
        Assert.Equal(expected, CyberDefenseOverdrivePolicy.ConsumeBonusAttack(remaining));
    }

    [Fact]
    public void Rank1_TriggeringAnswerDoesNotReceiveItsOwnBonus_NextTwoAttacksDo()
    {
        const int rank = 1;
        int streak = 0;
        int remainingBonusAttacks = 0;
        var bonusPerAnswer = new List<int>();

        // Seven consecutive correct answers. The window is armed explicitly by the caller from the
        // trigger answer; every attack is resolved against the window state held before that answer.
        for (int answer = 1; answer <= 7; answer++)
        {
            int bonusForThisAttack = CyberDefenseOverdrivePolicy.GetRank1AttackBonusPercent(remainingBonusAttacks);
            bonusPerAnswer.Add(bonusForThisAttack);
            remainingBonusAttacks = CyberDefenseOverdrivePolicy.ConsumeBonusAttack(remainingBonusAttacks);

            streak = CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(streak, answerWasCorrect: true);
            if (answer == 5 && CyberDefenseOverdrivePolicy.HasReachedTriggerThreshold(rank, streak))
            {
                remainingBonusAttacks = CyberDefenseOverdrivePolicy.Rank1BonusAttackCount;
            }
        }

        // Answers 1..5 get nothing (including the triggering 5th); answers 6 and 7 get +50%.
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 50, 50 }, bonusPerAnswer);
        Assert.Equal(0, remainingBonusAttacks);
    }

    [Fact]
    public void Rank1_BonusWindowEndsAfterTwoAttacks()
    {
        int remaining = CyberDefenseOverdrivePolicy.Rank1BonusAttackCount;
        var bonuses = new List<int>();
        for (int attack = 0; attack < 4; attack++)
        {
            bonuses.Add(CyberDefenseOverdrivePolicy.GetRank1AttackBonusPercent(remaining));
            remaining = CyberDefenseOverdrivePolicy.ConsumeBonusAttack(remaining);
        }

        Assert.Equal(new[] { 50, 50, 0, 0 }, bonuses);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void RankValidation_RejectsInvalidRanksEverywhere(int rank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.HasReachedTriggerThreshold(rank, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.CanPurchaseNextRank(rank, 80, 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void RankLookups_RejectRankZeroAndOutOfRange(int rank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.GetRequiredLevel(rank));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.GetUpgradeCost(rank));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.GetTriggerThreshold(rank));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.IsRankUnlockedAtLevel(rank, 80));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NegativeStreakOrBonusCounter_IsRejected(int negative)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(negative, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(negative, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.HasReachedTriggerThreshold(0, negative));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.HasReachedTriggerThreshold(1, negative));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.GetRank1AttackBonusPercent(negative));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.ConsumeBonusAttack(negative));
    }

    [Fact]
    public void InvalidPlayerLevelOrSkillPoints_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.IsRankUnlockedAtLevel(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.CanPurchaseNextRank(0, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseOverdrivePolicy.CanPurchaseNextRank(0, 20, -1));
    }

    [Fact]
    public void Calculations_AreDeterministicAcrossRepeatedCalls()
    {
        for (int rank = 0; rank <= 2; rank++)
        {
            for (int streak = 0; streak <= 40; streak++)
            {
                Assert.Equal(
                    CyberDefenseOverdrivePolicy.HasReachedTriggerThreshold(rank, streak),
                    CyberDefenseOverdrivePolicy.HasReachedTriggerThreshold(rank, streak));
                Assert.Equal(
                    CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(streak, true),
                    CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(streak, true));
            }
        }
    }

    [Fact]
    public void PublicSurface_IsStatelessAndTimingFree()
    {
        Type type = typeof(CyberDefenseOverdrivePolicy);
        Assert.True(type.IsAbstract && type.IsSealed, "Policy must be a static class.");

        // No hidden mutable counters.
        Assert.DoesNotContain(
            type.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            f => !f.IsLiteral);
        Assert.Empty(type.GetProperties(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

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
}
