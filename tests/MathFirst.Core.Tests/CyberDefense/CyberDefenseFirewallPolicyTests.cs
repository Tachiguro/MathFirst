using System.Reflection;
using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseFirewallPolicyTests
{
    [Fact]
    public void FixedProductRules_MatchGdd()
    {
        Assert.Equal(3, CyberDefenseFirewallPolicy.MaxRank);
        Assert.Equal(3, CyberDefenseFirewallPolicy.MaxShieldCapacity);
        Assert.Equal(5, CyberDefenseFirewallPolicy.UnlockLevel);
    }

    [Fact]
    public void RechargeTuningConstants_MatchGddCandidateWeights()
    {
        Assert.Equal(2, CyberDefenseFirewallPolicy.CorrectAttemptRechargeUnits);
        Assert.Equal(1, CyberDefenseFirewallPolicy.IncorrectAttemptRechargeUnits);
        Assert.Equal(30, CyberDefenseFirewallPolicy.RechargeThresholdUnits);
    }

    [Fact]
    public void GetRechargeUnits_CorrectAttempt_AwardsTwoUnits()
    {
        Assert.Equal(2, CyberDefenseFirewallPolicy.GetRechargeUnits(isCorrect: true));
    }

    [Fact]
    public void GetRechargeUnits_IncorrectAttempt_AwardsOneUnit()
    {
        Assert.Equal(1, CyberDefenseFirewallPolicy.GetRechargeUnits(isCorrect: false));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 20)]
    [InlineData(3, 50)]
    public void GetRequiredLevel_MatchesSlotUnlockLevels(int rank, int expectedLevel)
    {
        Assert.Equal(expectedLevel, CyberDefenseFirewallPolicy.GetRequiredLevel(rank));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 8)]
    [InlineData(3, 15)]
    public void GetUpgradeCost_MatchesSlotCosts(int rank, int expectedCost)
    {
        Assert.Equal(expectedCost, CyberDefenseFirewallPolicy.GetUpgradeCost(rank));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    public void GetShieldCapacity_RankZeroIsLockedAndEachRankAddsOneSlot(int rank, int expectedCapacity)
    {
        Assert.Equal(expectedCapacity, CyberDefenseFirewallPolicy.GetShieldCapacity(rank));
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 4, false)]
    [InlineData(1, 5, true)]
    [InlineData(2, 19, false)]
    [InlineData(2, 20, true)]
    [InlineData(3, 49, false)]
    [InlineData(3, 50, true)]
    [InlineData(3, int.MaxValue, true)]
    public void IsRankUnlockedAtLevel_EnforcesLevelGates(int rank, int playerLevel, bool expected)
    {
        Assert.Equal(expected, CyberDefenseFirewallPolicy.IsRankUnlockedAtLevel(rank, playerLevel));
    }

    [Theory]
    [InlineData(0, 4, 100, false)]  // Level 4 rejects first slot
    [InlineData(0, 5, 3, true)]     // Level 5 permits first slot
    [InlineData(0, 5, 2, false)]    // Insufficient SP
    [InlineData(1, 19, 100, false)] // Level 19 rejects second slot
    [InlineData(1, 20, 8, true)]    // Level 20 permits second slot
    [InlineData(1, 20, 7, false)]
    [InlineData(2, 49, 100, false)] // Level 49 rejects third slot
    [InlineData(2, 50, 15, true)]   // Level 50 permits third slot
    [InlineData(2, 50, 14, false)]
    [InlineData(0, int.MaxValue, int.MaxValue, true)]
    public void CanPurchaseNextRank_EnforcesLevelAndSkillPointGates(
        int currentRank,
        int playerLevel,
        int availableSkillPoints,
        bool expected)
    {
        Assert.Equal(
            expected,
            CyberDefenseFirewallPolicy.CanPurchaseNextRank(currentRank, playerLevel, availableSkillPoints));
    }

    [Fact]
    public void CanPurchaseNextRank_MaximumThreeSlots_RejectsFourthPurchase()
    {
        Assert.False(CyberDefenseFirewallPolicy.CanPurchaseNextRank(3, int.MaxValue, int.MaxValue));
    }

    [Fact]
    public void Capacity_NeverExceedsThreeSlots_AcrossAllValidRanks()
    {
        int previous = -1;
        for (int rank = 0; rank <= 3; rank++)
        {
            int capacity = CyberDefenseFirewallPolicy.GetShieldCapacity(rank);
            Assert.InRange(capacity, 0, 3);
            Assert.True(capacity > previous);
            previous = capacity;
        }

        Assert.Equal(3, previous);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void GetRequiredLevel_RejectsInvalidRank(int rank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseFirewallPolicy.GetRequiredLevel(rank));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void GetUpgradeCost_RejectsInvalidRank(int rank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseFirewallPolicy.GetUpgradeCost(rank));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void GetShieldCapacity_RejectsInvalidRank(int rank)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseFirewallPolicy.GetShieldCapacity(rank));
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(4, 50)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, int.MinValue)]
    public void IsRankUnlockedAtLevel_RejectsInvalidArguments(int rank, int playerLevel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseFirewallPolicy.IsRankUnlockedAtLevel(rank, playerLevel));
    }

    [Theory]
    [InlineData(-1, 5, 3)]
    [InlineData(4, 50, 15)]
    [InlineData(int.MinValue, 5, 3)]
    [InlineData(int.MaxValue, 5, 3)]
    [InlineData(0, 0, 3)]
    [InlineData(0, -1, 3)]
    [InlineData(0, 5, -1)]
    [InlineData(0, 5, int.MinValue)]
    public void CanPurchaseNextRank_RejectsInvalidArguments(int currentRank, int playerLevel, int availableSkillPoints)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CyberDefenseFirewallPolicy.CanPurchaseNextRank(currentRank, playerLevel, availableSkillPoints));
    }

    [Fact]
    public void Calculations_AreDeterministicAcrossRepeatedCalls()
    {
        for (int i = 0; i < 100; i++)
        {
            for (int rank = 1; rank <= 3; rank++)
            {
                Assert.Equal(CyberDefenseFirewallPolicy.GetRequiredLevel(rank), CyberDefenseFirewallPolicy.GetRequiredLevel(rank));
                Assert.Equal(CyberDefenseFirewallPolicy.GetUpgradeCost(rank), CyberDefenseFirewallPolicy.GetUpgradeCost(rank));
            }

            Assert.Equal(2, CyberDefenseFirewallPolicy.GetRechargeUnits(true));
            Assert.Equal(1, CyberDefenseFirewallPolicy.GetRechargeUnits(false));
        }
    }

    [Fact]
    public void TotalCostForAllThreeSlots_IsTwentySixSkillPoints()
    {
        int total = 0;
        for (int rank = 1; rank <= 3; rank++)
        {
            total = checked(total + CyberDefenseFirewallPolicy.GetUpgradeCost(rank));
        }

        Assert.Equal(26, total);
    }

    [Fact]
    public void Policy_HasNoMutableStaticState()
    {
        FieldInfo[] fields = typeof(CyberDefenseFirewallPolicy).GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

        Assert.All(fields, f => Assert.True(f.IsLiteral, $"Field {f.Name} must be a compile-time constant."));
    }
}
