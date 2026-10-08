using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class CyberDefenseScalingPolicyTests
{
    [Theory]
    [InlineData(1, 5, 2, 12)]
    [InlineData(2, 6, 2, 12)]
    [InlineData(4, 7, 2, 14)]
    [InlineData(8, 8, 2, 16)]
    [InlineData(10, 8, 3, 18)]
    [InlineData(16, 9, 3, 21)]
    [InlineData(34, 10, 5, 30)]
    [InlineData(100, 11, 8, 52)]
    [InlineData(1024, 15, 30, 181)]
    [InlineData(10000, 18, 98, 588)]
    [InlineData(int.MaxValue, 35, 46338, 278033)]
    public void ScalingFormulas_MatchAuthoritativeReferenceValues(int sector, int expectedNormalCount, int expectedNormalHp, int expectedBossHp)
    {
        int actualNormalCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(sector);
        int actualNormalHp = CyberDefenseScalingPolicy.GetNormalMaxHp(sector);
        int actualBossHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);

        Assert.Equal(expectedNormalCount, actualNormalCount);
        Assert.Equal(expectedNormalHp, actualNormalHp);
        Assert.Equal(expectedBossHp, actualBossHp);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 6)]
    [InlineData(3, 6)]
    [InlineData(4, 7)]
    [InlineData(7, 7)]
    [InlineData(8, 8)]
    [InlineData(15, 8)]
    [InlineData(16, 9)]
    [InlineData(31, 9)]
    [InlineData(32, 10)]
    [InlineData(63, 10)]
    [InlineData(64, 11)]
    [InlineData(1023, 14)]
    [InlineData(1024, 15)]
    [InlineData(int.MaxValue, 35)]
    public void GetNormalOpponentCount_CalculatesExactIntegerLogarithm(int sector, int expectedCount)
    {
        int actualCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(sector);
        Assert.Equal(expectedCount, actualCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(16)]
    [InlineData(100)]
    [InlineData(1024)]
    [InlineData(int.MaxValue)]
    public void OpponentCountsAndIndices_SatisfyStructuralContracts(int sector)
    {
        int normalCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(sector);
        int totalCount = CyberDefenseScalingPolicy.GetTotalOpponentCount(sector);
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);

        Assert.Equal(normalCount + 1, totalCount);
        Assert.Equal(normalCount, bossIndex);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(100)]
    public void OpponentClassification_EnforcesExactlyOneBossPerSector(int sector)
    {
        int normalCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(sector);
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int totalCount = CyberDefenseScalingPolicy.GetTotalOpponentCount(sector);

        int normalKindCount = 0;
        int bossKindCount = 0;

        for (int i = 0; i < totalCount; i++)
        {
            OpponentKind kind = CyberDefenseScalingPolicy.GetOpponentKind(sector, i);
            int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, i);

            if (i < bossIndex)
            {
                Assert.Equal(OpponentKind.Normal, kind);
                Assert.Equal(CyberDefenseScalingPolicy.GetNormalMaxHp(sector), maxHp);
                normalKindCount++;
            }
            else
            {
                Assert.Equal(bossIndex, i);
                Assert.Equal(OpponentKind.Boss, kind);
                Assert.Equal(CyberDefenseScalingPolicy.GetBossMaxHp(sector), maxHp);
                bossKindCount++;
            }
        }

        Assert.Equal(normalCount, normalKindCount);
        Assert.Equal(1, bossKindCount);
    }

    [Fact]
    public void OpponentKind_HasExpectedEnumValues()
    {
        Assert.Equal(0, (int)OpponentKind.Normal);
        Assert.Equal(1, (int)OpponentKind.Boss);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void AllMethods_ThrowArgumentOutOfRangeException_WhenSectorIsInvalid(int invalidSector)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetNormalOpponentCount(invalidSector));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetTotalOpponentCount(invalidSector));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetBossIndex(invalidSector));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetNormalMaxHp(invalidSector));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetBossMaxHp(invalidSector));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetOpponentKind(invalidSector, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetOpponentMaxHp(invalidSector, 0));
    }

    [Theory]
    [InlineData(1, -1)]
    [InlineData(1, -100)]
    [InlineData(1, int.MinValue)]
    [InlineData(5, -1)]
    public void GetOpponentKindAndMaxHp_ThrowArgumentOutOfRangeException_WhenOpponentIndexIsNegative(int sector, int negativeIndex)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetOpponentKind(sector, negativeIndex));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, negativeIndex));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(100)]
    public void GetOpponentKindAndMaxHp_ThrowArgumentOutOfRangeException_WhenOpponentIndexExceedsBossIndex(int sector)
    {
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int outOfBoundsIndex = bossIndex + 1;

        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetOpponentKind(sector, outOfBoundsIndex));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, outOfBoundsIndex));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetOpponentKind(sector, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, int.MaxValue));
    }

    [Theory]
    [InlineData(1, 2)]   // s+15 = 16 -> isqrt = 4 -> 4-2 = 2
    [InlineData(9, 2)]   // s+15 = 24 -> isqrt = 4 -> 4-2 = 2
    [InlineData(10, 3)]  // s+15 = 25 -> isqrt = 5 -> 5-2 = 3
    [InlineData(20, 3)]  // s+15 = 35 -> isqrt = 5 -> 5-2 = 3
    [InlineData(21, 4)]  // s+15 = 36 -> isqrt = 6 -> 6-2 = 4
    [InlineData(33, 4)]  // s+15 = 48 -> isqrt = 6 -> 6-2 = 4
    [InlineData(34, 5)]  // s+15 = 49 -> isqrt = 7 -> 7-2 = 5
    public void NormalHp_CorrectlyCrossesPerfectSquareBoundaries(int sector, int expectedHp)
    {
        int actualHp = CyberDefenseScalingPolicy.GetNormalMaxHp(sector);
        Assert.Equal(expectedHp, actualHp);
    }

    [Theory]
    [InlineData(1, 12)]   // 36*16 = 576 -> 24-12 = 12
    [InlineData(2, 12)]   // 36*17 = 612 -> 24-12 = 12
    [InlineData(3, 13)]   // 36*18 = 648 -> 25-12 = 13
    [InlineData(4, 14)]   // 36*19 = 684 -> 26-12 = 14
    public void BossHp_CorrectlyCalculatesMultiplicationBeforeFloor(int sector, int expectedBossHp)
    {
        int actualBossHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        Assert.Equal(expectedBossHp, actualBossHp);
    }

    [Fact]
    public void ScalingFunctions_AreMonotonicallyNonDecreasingAcrossFirst2000Sectors()
    {
        int prevCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(1);
        int prevNormalHp = CyberDefenseScalingPolicy.GetNormalMaxHp(1);
        int prevBossHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);

        for (int s = 2; s <= 2000; s++)
        {
            int currentCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(s);
            int currentNormalHp = CyberDefenseScalingPolicy.GetNormalMaxHp(s);
            int currentBossHp = CyberDefenseScalingPolicy.GetBossMaxHp(s);

            Assert.True(currentCount >= prevCount, $"Opponent count decreased at sector {s}: {currentCount} < {prevCount}");
            Assert.True(currentNormalHp >= prevNormalHp, $"Normal HP decreased at sector {s}: {currentNormalHp} < {prevNormalHp}");
            Assert.True(currentBossHp >= prevBossHp, $"Boss HP decreased at sector {s}: {currentBossHp} < {prevBossHp}");
            Assert.True(currentBossHp > currentNormalHp, $"Boss HP not strictly greater than Normal HP at sector {s}: {currentBossHp} <= {currentNormalHp}");

            prevCount = currentCount;
            prevNormalHp = currentNormalHp;
            prevBossHp = currentBossHp;
        }
    }

    [Theory]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    [InlineData(10_000_000)]
    [InlineData(100_000_000)]
    [InlineData(1_000_000_000)]
    [InlineData(int.MaxValue)]
    public void LargeSectors_DoNotOverflowAndProduceValidValues(int sector)
    {
        int normalCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(sector);
        int totalCount = CyberDefenseScalingPolicy.GetTotalOpponentCount(sector);
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int normalHp = CyberDefenseScalingPolicy.GetNormalMaxHp(sector);
        int bossHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);

        Assert.True(normalCount >= 5);
        Assert.Equal(normalCount + 1, totalCount);
        Assert.Equal(normalCount, bossIndex);
        Assert.True(normalHp >= 2);
        Assert.True(bossHp > normalHp);

        Assert.Equal(OpponentKind.Normal, CyberDefenseScalingPolicy.GetOpponentKind(sector, 0));
        Assert.Equal(OpponentKind.Normal, CyberDefenseScalingPolicy.GetOpponentKind(sector, bossIndex - 1));
        Assert.Equal(OpponentKind.Boss, CyberDefenseScalingPolicy.GetOpponentKind(sector, bossIndex));

        Assert.Equal(normalHp, CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, 0));
        Assert.Equal(normalHp, CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, bossIndex - 1));
        Assert.Equal(bossHp, CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, bossIndex));
    }
}
