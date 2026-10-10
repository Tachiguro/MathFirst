using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseAttackUpgradePolicyTests
{
    [Fact]
    public void BaseAttackDamage_IsOne()
    {
        Assert.Equal(1, CyberDefenseAttackUpgradePolicy.BaseAttackDamage);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(5, 6)]
    [InlineData(10, 11)]
    [InlineData(99, 100)]
    [InlineData(1000, 1001)]
    public void GetAttackDamage_CalculatesExactDamage(int rank, int expectedDamage)
    {
        int actualDamage = CyberDefenseAttackUpgradePolicy.GetAttackDamage(rank);
        Assert.Equal(expectedDamage, actualDamage);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(7, 3)]
    [InlineData(8, 4)]
    [InlineData(15, 4)]
    [InlineData(16, 5)]
    [InlineData(31, 5)]
    [InlineData(32, 6)]
    [InlineData(63, 6)]
    [InlineData(64, 7)]
    [InlineData(127, 7)]
    [InlineData(128, 8)]
    public void GetUpgradeCost_MatchesRequiredGddCostCurve(int nextRank, int expectedCost)
    {
        int actualCost = CyberDefenseAttackUpgradePolicy.GetUpgradeCost(nextRank);
        Assert.Equal(expectedCost, actualCost);
    }

    [Theory]
    [InlineData(0, 0L)]
    [InlineData(1, 1L)]
    [InlineData(2, 3L)] // 1 + 2
    [InlineData(3, 5L)] // 1 + 2 + 2
    [InlineData(4, 8L)] // 1 + 2 + 2 + 3
    [InlineData(8, 21L)] // 1 + 2*2 + 4*3 + 4 = 1 + 4 + 12 + 4 = 21
    public void GetCumulativeUpgradeCost_CalculatesExactCumulativeSp(int targetRank, long expectedTotalCost)
    {
        long actualTotal = CyberDefenseAttackUpgradePolicy.GetCumulativeUpgradeCost(targetRank);
        Assert.Equal(expectedTotalCost, actualTotal);
    }

    [Fact]
    public void Invariants_AttackDamageIsStrictlyIncreasingWithRank()
    {
        int prevDamage = CyberDefenseAttackUpgradePolicy.GetAttackDamage(0);

        for (int rank = 1; rank <= 1000; rank++)
        {
            int currentDamage = CyberDefenseAttackUpgradePolicy.GetAttackDamage(rank);
            Assert.True(currentDamage > prevDamage);
            Assert.Equal(prevDamage + 1, currentDamage);
            prevDamage = currentDamage;
        }
    }

    [Fact]
    public void Invariants_UpgradeCostIsMonotonicallyNonDecreasing()
    {
        int prevCost = CyberDefenseAttackUpgradePolicy.GetUpgradeCost(1);

        for (int nextRank = 2; nextRank <= 2048; nextRank++)
        {
            int currentCost = CyberDefenseAttackUpgradePolicy.GetUpgradeCost(nextRank);
            Assert.True(
                currentCost >= prevCost,
                $"Upgrade cost decreased at rank {nextRank}: previous was {prevCost}, current is {currentCost}.");
            prevCost = currentCost;
        }
    }

    [Fact]
    public void Invariants_CumulativeUpgradeCostMatchesLoopSum()
    {
        long runningSum = 0L;

        for (int rank = 1; rank <= 500; rank++)
        {
            runningSum += CyberDefenseAttackUpgradePolicy.GetUpgradeCost(rank);
            long cumulative = CyberDefenseAttackUpgradePolicy.GetCumulativeUpgradeCost(rank);
            Assert.Equal(runningSum, cumulative);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void GetAttackDamage_ThrowsArgumentOutOfRangeException_ForNegativeRank(int invalidRank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseAttackUpgradePolicy.GetAttackDamage(invalidRank));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    [InlineData(int.MinValue)]
    public void GetUpgradeCost_ThrowsArgumentOutOfRangeException_ForNonPositiveNextRank(int invalidNextRank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseAttackUpgradePolicy.GetUpgradeCost(invalidNextRank));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void GetCumulativeUpgradeCost_ThrowsArgumentOutOfRangeException_ForNegativeTargetRank(int invalidRank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseAttackUpgradePolicy.GetCumulativeUpgradeCost(invalidRank));
    }

    [Fact]
    public void GetAttackDamage_UsesCheckedArithmetic_ForMaxRankOverflow()
    {
        Assert.Throws<OverflowException>(() =>
            CyberDefenseAttackUpgradePolicy.GetAttackDamage(int.MaxValue));
    }

    [Fact]
    public void UnboundedScaling_SupportsHighRanksWithoutArbitraryCap()
    {
        int rank10000Damage = CyberDefenseAttackUpgradePolicy.GetAttackDamage(10000);
        Assert.Equal(10001, rank10000Damage);

        int rank10000Cost = CyberDefenseAttackUpgradePolicy.GetUpgradeCost(10000);
        // floor(log2(10000)) = 13 -> cost = 1 + 13 = 14 SP
        Assert.Equal(14, rank10000Cost);

        long rank10000CumulativeCost = CyberDefenseAttackUpgradePolicy.GetCumulativeUpgradeCost(10000);
        Assert.True(rank10000CumulativeCost > 0L);
    }
}
