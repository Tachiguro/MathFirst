namespace MathFirst.Core.Tests.CyberDefense;

using System;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using Xunit;

public sealed class AttackUpgradeContractTests
{
    // =========================================================================
    // 1. Attack Rank Cost Contract (Tests 1-6)
    // =========================================================================

    [Fact]
    public void Test01_Rank1_Costs1Sp()
    {
        Assert.Equal(1, CyberDefenseAttackUpgradePolicy.GetUpgradeCost(1));
    }

    [Fact]
    public void Test02_Rank2_Costs2Sp()
    {
        Assert.Equal(2, CyberDefenseAttackUpgradePolicy.GetUpgradeCost(2));
    }

    [Fact]
    public void Test03_Rank3_Costs2Sp()
    {
        Assert.Equal(2, CyberDefenseAttackUpgradePolicy.GetUpgradeCost(3));
    }

    [Fact]
    public void Test04_Rank4_Costs3Sp()
    {
        Assert.Equal(3, CyberDefenseAttackUpgradePolicy.GetUpgradeCost(4));
    }

    [Fact]
    public void Test05_Rank8_Costs4Sp()
    {
        Assert.Equal(4, CyberDefenseAttackUpgradePolicy.GetUpgradeCost(8));
    }

    [Theory]
    [InlineData(1, 1)]      // 2^0: cost = 1
    [InlineData(2, 2)]      // 2^1: cost = 2
    [InlineData(3, 2)]      // 2^1 + 1: cost = 2
    [InlineData(4, 3)]      // 2^2: cost = 3
    [InlineData(7, 3)]      // 2^3 - 1: cost = 3
    [InlineData(8, 4)]      // 2^3: cost = 4
    [InlineData(15, 4)]     // 2^4 - 1: cost = 4
    [InlineData(16, 5)]     // 2^4: cost = 5
    [InlineData(31, 5)]     // 2^5 - 1: cost = 5
    [InlineData(32, 6)]     // 2^5: cost = 6
    [InlineData(63, 6)]     // 2^6 - 1: cost = 6
    [InlineData(64, 7)]     // 2^6: cost = 7
    [InlineData(127, 7)]    // 2^7 - 1: cost = 7
    [InlineData(128, 8)]    // 2^7: cost = 8
    [InlineData(255, 8)]    // 2^8 - 1: cost = 8
    [InlineData(256, 9)]    // 2^8: cost = 9
    [InlineData(511, 9)]    // 2^9 - 1: cost = 9
    [InlineData(512, 10)]   // 2^9: cost = 10
    [InlineData(1023, 10)]  // 2^10 - 1: cost = 10
    [InlineData(1024, 11)]  // 2^10: cost = 11
    public void Test06_ExactPowerOfTwoCostTransitions(int nextRank, int expectedCost)
    {
        Assert.Equal(expectedCost, CyberDefenseAttackUpgradePolicy.GetUpgradeCost(nextRank));
    }

    // =========================================================================
    // 2. Base Damage Projection (Test 7)
    // =========================================================================

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 5)]
    [InlineData(10, 11)]
    [InlineData(100, 101)]
    [InlineData(999, 1000)]
    public void Test07_BaseDamageProjection_IsOnePlusRank(int rank, int expectedDamage)
    {
        Assert.Equal(expectedDamage, CyberDefenseAttackUpgradePolicy.GetAttackDamage(rank));
    }

    // =========================================================================
    // 3. Level 1 Purchase Eligibility (Test 8)
    // =========================================================================

    [Fact]
    public void Test08_Level1PurchaseEligibility_UnlockedAtLevel1WithSp()
    {
        // Attack upgrade is available from Level 1 without gating behind higher levels
        var progWithSp = new CyberDefenseProgressionRecord(
            storeRevision: 1,
            resetEpoch: 0,
            cumulativeXp: 0,
            playerLevel: 1,
            earnedSkillPoints: 1,
            spentSkillPoints: 0,
            availableSkillPoints: 1,
            attackRank: 0,
            economyVersion: CyberDefenseProgressionRecord.DefaultEconomyVersion,
            updatedAt: DateTimeOffset.UtcNow);

        var snapshot = progWithSp.ToAttackUpgradeSnapshot();
        Assert.Equal(1, snapshot.PlayerLevel);
        Assert.Equal(0, snapshot.CurrentAttackRank);
        Assert.Equal(1, snapshot.NextAttackRank);
        Assert.Equal(1, snapshot.NextRankCost);
        Assert.Equal(1, snapshot.AvailableSkillPoints);
        Assert.True(snapshot.HasSufficientSkillPoints);
        Assert.True(snapshot.CanAfford);
    }

    // =========================================================================
    // 4. Large-Rank Integer Cost Boundaries (Test 25)
    // =========================================================================

    [Theory]
    [InlineData(1024, 11)]
    [InlineData(65536, 17)]
    [InlineData(1048576, 21)]
    [InlineData(1_000_000_000, 30)]
    public void Test25_LargeRankIntegerCostBoundaries(int nextRank, int expectedCost)
    {
        int cost = CyberDefenseAttackUpgradePolicy.GetUpgradeCost(nextRank);
        Assert.Equal(expectedCost, cost);
    }

    // =========================================================================
    // 5. Attack Rank Overflow Fails Closed (Test 26)
    // =========================================================================

    [Fact]
    public void Test26_AttackRankOverflow_FailsClosed()
    {
        Assert.Throws<OverflowException>(() =>
            CyberDefenseAttackUpgradePolicy.GetAttackDamage(int.MaxValue));
    }

    // =========================================================================
    // 6. Read-Only Snapshot Reflects Actual Store State (Test 27)
    // =========================================================================

    [Fact]
    public void Test27_ReadOnlySnapshot_ReflectsProgressionStateExactly()
    {
        var prog = new CyberDefenseProgressionRecord(
            storeRevision: 5,
            resetEpoch: 2,
            cumulativeXp: 350,
            playerLevel: 4,
            earnedSkillPoints: 3,
            spentSkillPoints: 1,
            availableSkillPoints: 2,
            attackRank: 1,
            economyVersion: CyberDefenseProgressionRecord.DefaultEconomyVersion,
            updatedAt: DateTimeOffset.UtcNow);

        var snapshot = prog.ToAttackUpgradeSnapshot();

        Assert.Equal(1, snapshot.CurrentAttackRank);
        Assert.Equal(2, snapshot.NextAttackRank);
        Assert.Equal(2, snapshot.CurrentBaseDamage); // 1 + 1
        Assert.Equal(3, snapshot.NextBaseDamage);    // 1 + 2
        Assert.Equal(2, snapshot.NextRankCost);      // Cost to rank 2 is 2 SP
        Assert.Equal(2, snapshot.AvailableSkillPoints);
        Assert.True(snapshot.HasSufficientSkillPoints);
        Assert.True(snapshot.CanAfford);
        Assert.Equal(4, snapshot.PlayerLevel);
        Assert.Equal(2L, snapshot.ResetEpoch);
        Assert.Equal(5L, snapshot.StoreRevision);
    }

    [Fact]
    public void Test27b_ReadOnlySnapshot_WhenInsufficientSp_CanAffordIsFalse()
    {
        var prog = new CyberDefenseProgressionRecord(
            storeRevision: 1,
            resetEpoch: 0,
            cumulativeXp: 0,
            playerLevel: 1,
            earnedSkillPoints: 0,
            spentSkillPoints: 0,
            availableSkillPoints: 0,
            attackRank: 0,
            economyVersion: CyberDefenseProgressionRecord.DefaultEconomyVersion,
            updatedAt: DateTimeOffset.UtcNow);

        var snapshot = prog.ToAttackUpgradeSnapshot();

        Assert.Equal(0, snapshot.CurrentAttackRank);
        Assert.Equal(1, snapshot.NextAttackRank);
        Assert.Equal(1, snapshot.CurrentBaseDamage);
        Assert.Equal(2, snapshot.NextBaseDamage);
        Assert.Equal(1, snapshot.NextRankCost);
        Assert.Equal(0, snapshot.AvailableSkillPoints);
        Assert.False(snapshot.HasSufficientSkillPoints);
        Assert.False(snapshot.CanAfford);
    }

    // =========================================================================
    // 7. Numeric Boundary Safety (Tests 28-29)
    // =========================================================================

    [Fact]
    public void Test28_Snapshot_AtNumericBoundary_DoesNotOverflow_AndPresentsNoPurchasableNextRank()
    {
        // T17: At currentRank == int.MaxValue - 1, snapshot creation does not overflow
        // and presents no purchasable next rank.
        int boundaryRank = int.MaxValue - CyberDefenseAttackUpgradePolicy.BaseAttackDamage; // int.MaxValue - 1
        var prog = new CyberDefenseProgressionRecord(
            storeRevision: 10,
            resetEpoch: 0,
            cumulativeXp: 1_000_000,
            playerLevel: 100,
            earnedSkillPoints: 1000,
            spentSkillPoints: 500,
            availableSkillPoints: 500,
            attackRank: boundaryRank,
            economyVersion: CyberDefenseProgressionRecord.DefaultEconomyVersion,
            updatedAt: DateTimeOffset.UtcNow);

        var snapshot = prog.ToAttackUpgradeSnapshot();

        Assert.Equal(boundaryRank, snapshot.CurrentAttackRank);
        Assert.Equal(int.MaxValue, snapshot.CurrentBaseDamage); // 1 + (int.MaxValue - 1) == int.MaxValue
        Assert.Null(snapshot.NextAttackRank);
        Assert.Null(snapshot.NextBaseDamage);
        Assert.Null(snapshot.NextRankCost);
        Assert.False(snapshot.HasSufficientSkillPoints);
        Assert.False(snapshot.CanAfford);
    }

    [Theory]
    [InlineData(0, 1, 1, 2, 1)]
    [InlineData(1, 2, 2, 3, 2)]
    [InlineData(2, 3, 3, 4, 2)]
    [InlineData(5, 6, 6, 7, 3)]
    [InlineData(10, 11, 11, 12, 4)]
    [InlineData(50, 51, 51, 52, 6)]
    [InlineData(100, 101, 101, 102, 7)]
    public void Test29_RegularAttackRankSnapshots_RemainUnchanged_AtLowAndMediumRanks(
        int currentRank,
        int expectedNextRank,
        int expectedCurrentDmg,
        int expectedNextDmg,
        int expectedCost)
    {
        // T19: Regular Attack rank snapshot calculations remain unchanged at low and medium ranks.
        var prog = new CyberDefenseProgressionRecord(
            storeRevision: 1,
            resetEpoch: 0,
            cumulativeXp: 1000,
            playerLevel: 10,
            earnedSkillPoints: 100,
            spentSkillPoints: 0,
            availableSkillPoints: 100,
            attackRank: currentRank,
            economyVersion: CyberDefenseProgressionRecord.DefaultEconomyVersion,
            updatedAt: DateTimeOffset.UtcNow);

        var snapshot = prog.ToAttackUpgradeSnapshot();

        Assert.Equal(currentRank, snapshot.CurrentAttackRank);
        Assert.Equal(expectedNextRank, snapshot.NextAttackRank);
        Assert.Equal(expectedCurrentDmg, snapshot.CurrentBaseDamage);
        Assert.Equal(expectedNextDmg, snapshot.NextBaseDamage);
        Assert.Equal(expectedCost, snapshot.NextRankCost);
        Assert.True(snapshot.HasSufficientSkillPoints);
        Assert.True(snapshot.CanAfford);
    }
}
