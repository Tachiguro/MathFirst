using System.Numerics;
using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class SectorScalingFormulaPropertyTests
{
    // =========================================================================
    // 1. FULL SECTOR 1..10000 PROPERTY SWEEP
    // =========================================================================

    [Fact]
    public void SectorScaling_AcrossSectors1Through10000_SatisfiesAllMonotonicityAndBoundaryInvariants()
    {
        int prevNormalCount = 0;
        int prevNormalHp = 0;
        int prevBossHp = 0;

        for (int sector = 1; sector <= 10000; sector++)
        {
            int normalCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(sector);
            int totalCount = CyberDefenseScalingPolicy.GetTotalOpponentCount(sector);
            int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
            int normalHp = CyberDefenseScalingPolicy.GetNormalMaxHp(sector);
            int bossHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);

            // Invariant: Normal count >= 5
            Assert.True(normalCount >= 5, $"Sector {sector} normal count must be >= 5, got {normalCount}.");

            // Invariant: Total opponents = normal count + 1
            Assert.Equal(normalCount + 1, totalCount);

            // Invariant: Boss index = normal count
            Assert.Equal(normalCount, bossIndex);

            // Invariant: Normal HP >= 1 (at sector 1, Hnormal(1) = isqrt(16)-2 = 2)
            Assert.True(normalHp >= 1, $"Sector {sector} normal HP must be >= 1, got {normalHp}.");

            // Invariant: Boss HP > Normal HP
            Assert.True(bossHp > normalHp, $"Sector {sector} boss HP ({bossHp}) must exceed normal HP ({normalHp}).");

            // Invariant: Monotonicity across consecutive sectors
            if (sector > 1)
            {
                Assert.True(normalCount >= prevNormalCount, $"Sector {sector} normal count ({normalCount}) decreased from previous ({prevNormalCount}).");
                Assert.True(normalHp >= prevNormalHp, $"Sector {sector} normal HP ({normalHp}) decreased from previous ({prevNormalHp}).");
                Assert.True(bossHp >= prevBossHp, $"Sector {sector} boss HP ({bossHp}) decreased from previous ({prevBossHp}).");
            }

            // Invariant: Reference mathematical formulas
            int expectedRefNormalCount = 5 + BitOperations.Log2((uint)sector);
            int expectedRefNormalHp = (int)(ReferenceIntegerSquareRoot(sector + 15L) - 2L);
            int expectedRefBossHp = (int)(ReferenceIntegerSquareRoot(36L * (sector + 15L)) - 12L);

            Assert.Equal(expectedRefNormalCount, normalCount);
            Assert.Equal(expectedRefNormalHp, normalHp);
            Assert.Equal(expectedRefBossHp, bossHp);

            // Invariant: Opponent kind classification and max HP consistency
            OpponentKind firstKind = CyberDefenseScalingPolicy.GetOpponentKind(sector, 0);
            Assert.Equal(OpponentKind.Normal, firstKind);
            Assert.Equal(normalHp, CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, 0));

            OpponentKind bossKind = CyberDefenseScalingPolicy.GetOpponentKind(sector, bossIndex);
            Assert.Equal(OpponentKind.Boss, bossKind);
            Assert.Equal(bossHp, CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, bossIndex));

            prevNormalCount = normalCount;
            prevNormalHp = normalHp;
            prevBossHp = bossHp;
        }
    }

    // =========================================================================
    // 2. AUTHORITATIVE REFERENCE VALUE VERIFICATION
    // =========================================================================

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
    public void SectorScaling_ReferenceValues_MatchAuthoritativeTable(
        int sector,
        int expectedNormalCount,
        int expectedNormalHp,
        int expectedBossHp)
    {
        int actualNormalCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(sector);
        int actualNormalHp = CyberDefenseScalingPolicy.GetNormalMaxHp(sector);
        int actualBossHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);
        int totalCount = CyberDefenseScalingPolicy.GetTotalOpponentCount(sector);
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);

        Assert.Equal(expectedNormalCount, actualNormalCount);
        Assert.Equal(expectedNormalHp, actualNormalHp);
        Assert.Equal(expectedBossHp, actualBossHp);
        Assert.Equal(expectedNormalCount + 1, totalCount);
        Assert.Equal(expectedNormalCount, bossIndex);
    }

    // =========================================================================
    // 3. POWER-OF-TWO OPPONENT COUNT TRANSITION INVARIANTS
    // =========================================================================

    [Theory]
    [InlineData(1, 0, 5)]
    [InlineData(2, 1, 6)]
    [InlineData(4, 2, 7)]
    [InlineData(8, 3, 8)]
    [InlineData(16, 4, 9)]
    [InlineData(32, 5, 10)]
    [InlineData(64, 6, 11)]
    [InlineData(128, 7, 12)]
    [InlineData(256, 8, 13)]
    [InlineData(512, 9, 14)]
    [InlineData(1024, 10, 15)]
    [InlineData(2048, 11, 16)]
    [InlineData(4096, 12, 17)]
    [InlineData(8192, 13, 18)]
    [InlineData(16384, 14, 19)]
    [InlineData(1073741824, 30, 35)]
    public void SectorScaling_PowerOfTwoTransitions_IncrementCountExclusivelyAtPowersOfTwo(
        int powerOfTwoSector,
        int powerExponent,
        int expectedCountAtPower)
    {
        int countAtPower = CyberDefenseScalingPolicy.GetNormalOpponentCount(powerOfTwoSector);
        Assert.Equal(expectedCountAtPower, countAtPower);
        Assert.Equal(5 + powerExponent, countAtPower);

        if (powerOfTwoSector > 1)
        {
            int sectorBefore = powerOfTwoSector - 1;
            int countBefore = CyberDefenseScalingPolicy.GetNormalOpponentCount(sectorBefore);
            Assert.Equal(expectedCountAtPower - 1, countBefore);
        }
    }

    // =========================================================================
    // 4. INTEGER SQUARE-ROOT THRESHOLD INVARIANTS
    // =========================================================================

    [Theory]
    [InlineData(1, 2)]      // 1+15 = 16 = 4^2 -> 4 - 2 = 2
    [InlineData(9, 2)]      // 9+15 = 24 < 25 -> 4 - 2 = 2
    [InlineData(10, 3)]     // 10+15 = 25 = 5^2 -> 5 - 2 = 3
    [InlineData(20, 3)]     // 20+15 = 35 < 36 -> 5 - 2 = 3
    [InlineData(21, 4)]     // 21+15 = 36 = 6^2 -> 6 - 2 = 4
    [InlineData(33, 4)]     // 33+15 = 48 < 49 -> 6 - 2 = 4
    [InlineData(34, 5)]     // 34+15 = 49 = 7^2 -> 7 - 2 = 5
    [InlineData(48, 5)]     // 48+15 = 63 < 64 -> 7 - 2 = 5
    [InlineData(49, 6)]     // 49+15 = 64 = 8^2 -> 8 - 2 = 6
    [InlineData(65, 6)]     // 65+15 = 80 < 81 -> 8 - 2 = 6
    [InlineData(66, 7)]     // 66+15 = 81 = 9^2 -> 9 - 2 = 7
    [InlineData(84, 7)]     // 84+15 = 99 < 100 -> 9 - 2 = 7
    [InlineData(85, 8)]     // 85+15 = 100 = 10^2 -> 10 - 2 = 8
    public void SectorScaling_IntegerSquareRootThresholds_IncrementNormalHpPreciselyAtBoundaries(
        int sector,
        int expectedNormalHp)
    {
        int actualHp = CyberDefenseScalingPolicy.GetNormalMaxHp(sector);
        Assert.Equal(expectedNormalHp, actualHp);
    }

    // =========================================================================
    // 5. OPPONENT INDEX CLASSIFICATION & SINGLE BOSS INVARIANT
    // =========================================================================

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void SectorScaling_OpponentIndexScan_VerifiesExactlyOneBossAtFinalIndex(int sector)
    {
        int normalCount = CyberDefenseScalingPolicy.GetNormalOpponentCount(sector);
        int totalCount = CyberDefenseScalingPolicy.GetTotalOpponentCount(sector);
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(sector);
        int normalHp = CyberDefenseScalingPolicy.GetNormalMaxHp(sector);
        int bossHp = CyberDefenseScalingPolicy.GetBossMaxHp(sector);

        int observedNormalCount = 0;
        int observedBossCount = 0;

        for (int i = 0; i < totalCount; i++)
        {
            OpponentKind kind = CyberDefenseScalingPolicy.GetOpponentKind(sector, i);
            int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, i);

            if (i < bossIndex)
            {
                Assert.Equal(OpponentKind.Normal, kind);
                Assert.Equal(normalHp, maxHp);
                observedNormalCount++;
            }
            else
            {
                Assert.Equal(bossIndex, i);
                Assert.Equal(OpponentKind.Boss, kind);
                Assert.Equal(bossHp, maxHp);
                observedBossCount++;
            }
        }

        Assert.Equal(normalCount, observedNormalCount);
        Assert.Equal(1, observedBossCount);
    }

    // =========================================================================
    // 6. FAIL-CLOSED BOUNDARY VALIDATION
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void SectorScaling_InvalidSector_ThrowsArgumentOutOfRangeException(int invalidSector)
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
    [InlineData(1, 6)]
    [InlineData(1, 10)]
    [InlineData(10, -1)]
    [InlineData(10, 9)]
    [InlineData(100, -5)]
    [InlineData(100, 12)]
    public void SectorScaling_InvalidOpponentIndex_ThrowsArgumentOutOfRangeException(int sector, int invalidIndex)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetOpponentKind(sector, invalidIndex));
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, invalidIndex));
    }

    // =========================================================================
    // 7. INDEPENDENT MATHEMATICAL REFERENCE HELPER
    // =========================================================================

    private static long ReferenceIntegerSquareRoot(long n)
    {
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "Square root of negative number is undefined.");
        }

        if (n == 0)
        {
            return 0;
        }

        long x = (long)Math.Sqrt(n);
        while (x * x > n)
        {
            x--;
        }

        while ((x + 1) * (x + 1) <= n)
        {
            x++;
        }

        return x;
    }
}
