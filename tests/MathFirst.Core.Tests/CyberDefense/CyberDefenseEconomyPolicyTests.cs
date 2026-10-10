using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseEconomyPolicyTests
{
    [Fact]
    public void Constants_MatchApprovedGddPreliminaryValues()
    {
        Assert.Equal(1, CyberDefenseEconomyPolicy.IncorrectAttemptXp);
        Assert.Equal(6, CyberDefenseEconomyPolicy.CorrectAttemptXp);
        Assert.Equal(1, CyberDefenseEconomyPolicy.NormalDefeatBonusXpPerAttack);
        Assert.Equal(2, CyberDefenseEconomyPolicy.BossDefeatBonusXpPerAttack);
        Assert.Equal(10, CyberDefenseEconomyPolicy.BossMilestoneBonusXp);
    }

    [Theory]
    [InlineData(true, 6)]
    [InlineData(false, 1)]
    public void GetAttemptXp_ReturnsExpectedXp_ForAttemptCorrectness(bool isCorrect, int expectedXp)
    {
        int actualXp = CyberDefenseEconomyPolicy.GetAttemptXp(isCorrect);
        Assert.Equal(expectedXp, actualXp);
    }

    [Theory]
    [InlineData(OpponentKind.Normal, 1, 1)]
    [InlineData(OpponentKind.Normal, 2, 2)]
    [InlineData(OpponentKind.Normal, 5, 5)]
    [InlineData(OpponentKind.Normal, 17, 17)]
    [InlineData(OpponentKind.Boss, 1, 2)]
    [InlineData(OpponentKind.Boss, 2, 4)]
    [InlineData(OpponentKind.Boss, 5, 10)]
    [InlineData(OpponentKind.Boss, 17, 34)]
    public void GetDefeatBonusXp_CalculatesBonusFromActuallyExecutedAttacks(
        OpponentKind kind,
        int actualSuccessfulAttackCount,
        int expectedBonusXp)
    {
        int actualBonus = CyberDefenseEconomyPolicy.GetDefeatBonusXp(kind, actualSuccessfulAttackCount);
        Assert.Equal(expectedBonusXp, actualBonus);
    }

    [Fact]
    public void GetBossMilestoneBonusXp_ReturnsFlatTenXp()
    {
        Assert.Equal(10, CyberDefenseEconomyPolicy.GetBossMilestoneBonusXp());
    }

    [Theory]
    [InlineData(OpponentKind.Normal, 1, 1)]
    [InlineData(OpponentKind.Normal, 4, 4)]
    [InlineData(OpponentKind.Boss, 1, 12)] // 2*1 + 10
    [InlineData(OpponentKind.Boss, 3, 16)] // 2*3 + 10
    public void CalculateTotalDefeatXp_IncludesMilestoneBonusOnlyForBoss(
        OpponentKind kind,
        int actualSuccessfulAttackCount,
        int expectedTotalDefeatXp)
    {
        int totalDefeatXp = CyberDefenseEconomyPolicy.CalculateTotalDefeatXp(kind, actualSuccessfulAttackCount);
        Assert.Equal(expectedTotalDefeatXp, totalDefeatXp);
    }

    [Fact]
    public void Contract_SingleHitBossDefeat_AwardsExactlyEighteenXp()
    {
        // GDD specification contract:
        // +6 XP for correct attempt
        // +2 XP for one actual successful boss attack
        // +10 XP for sector boss milestone
        // Total = 18 XP
        int totalXp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: true,
            opponentDefeated: true,
            kind: OpponentKind.Boss,
            actualSuccessfulAttackCount: 1);

        Assert.Equal(18, totalXp);
    }

    [Fact]
    public void Contract_SingleHitNormalDefeat_AwardsSevenXp()
    {
        // +6 XP for correct attempt
        // +1 XP for one actual successful normal attack
        // 0 XP milestone
        // Total = 7 XP
        int totalXp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: true,
            opponentDefeated: true,
            kind: OpponentKind.Normal,
            actualSuccessfulAttackCount: 1);

        Assert.Equal(7, totalXp);
    }

    [Fact]
    public void Contract_MultiHitNormalDefeat_AwardsCorrectTotalXp()
    {
        // 5 successful attacks before defeat
        // On defeating turn:
        // +6 XP attempt + 5 XP defeat bonus = 11 XP
        int totalXp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: true,
            opponentDefeated: true,
            kind: OpponentKind.Normal,
            actualSuccessfulAttackCount: 5);

        Assert.Equal(11, totalXp);
    }

    [Fact]
    public void Contract_NonDefeatingCorrectAttempt_AwardsSixXpOnly()
    {
        // Attempt does not defeat opponent -> no defeat bonus, no milestone
        int totalXp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: true,
            opponentDefeated: false,
            kind: OpponentKind.Normal,
            actualSuccessfulAttackCount: 0);

        Assert.Equal(6, totalXp);
    }

    [Fact]
    public void Contract_NonDefeatingIncorrectAttempt_AwardsOneXpOnly()
    {
        // Incorrect attempt -> 1 XP, no defeat bonus
        int totalXp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
            isCorrect: false,
            opponentDefeated: false,
            kind: OpponentKind.Boss,
            actualSuccessfulAttackCount: 0);

        Assert.Equal(1, totalXp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void GetDefeatBonusXp_ThrowsArgumentOutOfRangeException_ForZeroOrNegativeAttacks(int invalidAttackCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseEconomyPolicy.GetDefeatBonusXp(OpponentKind.Normal, invalidAttackCount));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseEconomyPolicy.GetDefeatBonusXp(OpponentKind.Boss, invalidAttackCount));
    }

    [Theory]
    [InlineData((OpponentKind)(-1))]
    [InlineData((OpponentKind)2)]
    [InlineData((OpponentKind)99)]
    [InlineData((OpponentKind)int.MaxValue)]
    public void GetDefeatBonusXp_ThrowsArgumentOutOfRangeException_ForInvalidOpponentKind(OpponentKind invalidKind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseEconomyPolicy.GetDefeatBonusXp(invalidKind, 1));
    }

    [Theory]
    [InlineData((OpponentKind)(-1))]
    [InlineData((OpponentKind)2)]
    public void CalculateTotalDefeatXp_ThrowsArgumentOutOfRangeException_ForInvalidOpponentKind(OpponentKind invalidKind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseEconomyPolicy.CalculateTotalDefeatXp(invalidKind, 1));
    }

    [Fact]
    public void GetDefeatBonusXp_UsesCheckedArithmetic_ForLargeAttackCounts()
    {
        // Normal: 1 * int.MaxValue fits in int.MaxValue
        int normalXp = CyberDefenseEconomyPolicy.GetDefeatBonusXp(OpponentKind.Normal, int.MaxValue);
        Assert.Equal(int.MaxValue, normalXp);

        // Boss: 2 * int.MaxValue exceeds int.MaxValue, must throw OverflowException
        Assert.Throws<OverflowException>(() =>
            CyberDefenseEconomyPolicy.GetDefeatBonusXp(OpponentKind.Boss, int.MaxValue));
    }
}
