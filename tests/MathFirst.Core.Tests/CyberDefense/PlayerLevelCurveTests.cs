using MathFirst.Domain.CyberDefense;
using Xunit;

namespace MathFirst.Core.Tests.CyberDefense;

public class PlayerLevelCurveTests
{
    [Fact]
    public void StartingBaseline_StartsAtLevelOneWithZeroCumulativeXp()
    {
        Assert.Equal(1, CyberDefenseLevelPolicy.StartingLevel);
        Assert.Equal(0L, CyberDefenseLevelPolicy.StartingCumulativeXp);
    }

    [Theory]
    [InlineData(1, 80)]
    [InlineData(2, 104)]
    [InlineData(100, 834)]
    [InlineData(101, 837)]
    [InlineData(200, 994)]
    [InlineData(1000, 1366)]
    public void GetDeltaXp_MatchesRequiredGddBoundaryExamples(int level, int expectedDeltaXp)
    {
        int actualDeltaXp = CyberDefenseLevelPolicy.GetDeltaXp(level);
        Assert.Equal(expectedDeltaXp, actualDeltaXp);
    }

    [Fact]
    public void MathematicalCorrection_PlateauExamplesProduceEqualDeltaXp()
    {
        // Proof of non-decreasing rather than strictly-increasing per-level curve
        int delta242 = CyberDefenseLevelPolicy.GetDeltaXp(242);
        int delta243 = CyberDefenseLevelPolicy.GetDeltaXp(243);

        Assert.Equal(1039, delta242);
        Assert.Equal(1039, delta243);
    }

    [Fact]
    public void Invariants_PerLevelDeltaXpIsMonotonicallyNonDecreasing()
    {
        int prevDelta = CyberDefenseLevelPolicy.GetDeltaXp(1);

        for (int level = 2; level <= 3000; level++)
        {
            int currentDelta = CyberDefenseLevelPolicy.GetDeltaXp(level);
            Assert.True(
                currentDelta >= prevDelta,
                $"DeltaXp decreased at level {level}: previous was {prevDelta}, current is {currentDelta}.");
            prevDelta = currentDelta;
        }
    }

    [Fact]
    public void Invariants_CumulativeXpThresholdsAreStrictlyIncreasing()
    {
        long prevThreshold = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(1);
        Assert.Equal(0L, prevThreshold);

        for (int level = 2; level <= 1500; level++)
        {
            long currentThreshold = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(level);
            Assert.True(
                currentThreshold > prevThreshold,
                $"Threshold at level {level} ({currentThreshold}) is not strictly greater than level {level - 1} ({prevThreshold}).");
            Assert.Equal(prevThreshold + CyberDefenseLevelPolicy.GetDeltaXp(level - 1), currentThreshold);
            prevThreshold = currentThreshold;
        }
    }

    [Theory]
    [InlineData(1, 0L)]
    [InlineData(2, 80L)]
    [InlineData(3, 184L)]
    [InlineData(100, 50195L)]
    [InlineData(101, 51029L)]
    [InlineData(1000, 1124747L)]
    public void GetCumulativeXpThreshold_MatchesKnownCumulativeValues(int level, long expectedThreshold)
    {
        long actualThreshold = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(level);
        Assert.Equal(expectedThreshold, actualThreshold);
    }

    [Theory]
    [InlineData(0L, 1)]
    [InlineData(79L, 1)]
    [InlineData(80L, 2)]
    [InlineData(183L, 2)]
    [InlineData(184L, 3)]
    [InlineData(50194L, 99)]
    [InlineData(50195L, 100)]
    [InlineData(51028L, 100)]
    [InlineData(51029L, 101)]
    [InlineData(1124746L, 999)]
    [InlineData(1124747L, 1000)]
    public void GetLevel_DerivesExactPlayerLevelFromCumulativeXp(long cumulativeXp, int expectedLevel)
    {
        int actualLevel = CyberDefenseLevelPolicy.GetLevel(cumulativeXp);
        Assert.Equal(expectedLevel, actualLevel);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(10, 9)]
    [InlineData(100, 99)]
    [InlineData(1000, 999)]
    public void GetEarnedSkillPoints_AwardsExactlyOnePointPerLevelGained(int level, int expectedSkillPoints)
    {
        int actualSp = CyberDefenseLevelPolicy.GetEarnedSkillPoints(level);
        Assert.Equal(expectedSkillPoints, actualSp);
    }

    [Fact]
    public void CalculateProgress_ProvidesCompleteImmutableSnapshot()
    {
        // At 150 cumulative XP:
        // Level 2 threshold is 80, Level 3 threshold is 184
        // XpIntoCurrentLevel = 150 - 80 = 70
        // XpRequiredForNextLevel = DeltaXp(2) = 104
        // RemainingXpToNextLevel = 104 - 70 = 34
        // EarnedSkillPoints = 2 - 1 = 1
        PlayerLevelProgress progress = CyberDefenseLevelPolicy.CalculateProgress(150L);

        Assert.Equal(2, progress.Level);
        Assert.Equal(150L, progress.CumulativeXp);
        Assert.Equal(80L, progress.CurrentLevelThresholdXp);
        Assert.Equal(184L, progress.NextLevelThresholdXp);
        Assert.Equal(70L, progress.XpIntoCurrentLevel);
        Assert.Equal(104, progress.XpRequiredForNextLevel);
        Assert.Equal(34, progress.RemainingXpToNextLevel);
        Assert.Equal(1, progress.EarnedSkillPoints);
        Assert.Equal(70.0 / 104.0, progress.ProgressRatio, precision: 6);
    }

    [Fact]
    public void CalculateProgress_HandlesZeroXpLevelOneCleanly()
    {
        PlayerLevelProgress progress = CyberDefenseLevelPolicy.CalculateProgress(0L);

        Assert.Equal(1, progress.Level);
        Assert.Equal(0L, progress.CumulativeXp);
        Assert.Equal(0L, progress.CurrentLevelThresholdXp);
        Assert.Equal(80L, progress.NextLevelThresholdXp);
        Assert.Equal(0L, progress.XpIntoCurrentLevel);
        Assert.Equal(80, progress.XpRequiredForNextLevel);
        Assert.Equal(80, progress.RemainingXpToNextLevel);
        Assert.Equal(0, progress.EarnedSkillPoints);
        Assert.Equal(0.0, progress.ProgressRatio);
    }

    [Fact]
    public void CalculateProgress_HandlesExactLevelThresholdCleanly()
    {
        PlayerLevelProgress progress = CyberDefenseLevelPolicy.CalculateProgress(80L);

        Assert.Equal(2, progress.Level);
        Assert.Equal(80L, progress.CumulativeXp);
        Assert.Equal(80L, progress.CurrentLevelThresholdXp);
        Assert.Equal(184L, progress.NextLevelThresholdXp);
        Assert.Equal(0L, progress.XpIntoCurrentLevel);
        Assert.Equal(104, progress.XpRequiredForNextLevel);
        Assert.Equal(104, progress.RemainingXpToNextLevel);
        Assert.Equal(1, progress.EarnedSkillPoints);
        Assert.Equal(0.0, progress.ProgressRatio);
    }

    [Fact]
    public void CalculateProgress_HandlesOneXpBeforeNextLevelCleanly()
    {
        PlayerLevelProgress progress = CyberDefenseLevelPolicy.CalculateProgress(183L);

        Assert.Equal(2, progress.Level);
        Assert.Equal(183L, progress.CumulativeXp);
        Assert.Equal(80L, progress.CurrentLevelThresholdXp);
        Assert.Equal(184L, progress.NextLevelThresholdXp);
        Assert.Equal(103L, progress.XpIntoCurrentLevel);
        Assert.Equal(104, progress.XpRequiredForNextLevel);
        Assert.Equal(1, progress.RemainingXpToNextLevel);
        Assert.Equal(1, progress.EarnedSkillPoints);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void GetDeltaXp_ThrowsArgumentOutOfRangeException_ForInvalidLevel(int invalidLevel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseLevelPolicy.GetDeltaXp(invalidLevel));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetCumulativeXpThreshold_ThrowsArgumentOutOfRangeException_ForInvalidLevel(int invalidLevel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseLevelPolicy.GetCumulativeXpThreshold(invalidLevel));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(-500L)]
    [InlineData(long.MinValue)]
    public void GetLevel_ThrowsArgumentOutOfRangeException_ForNegativeCumulativeXp(long invalidXp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseLevelPolicy.GetLevel(invalidXp));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetEarnedSkillPoints_ThrowsArgumentOutOfRangeException_ForInvalidLevel(int invalidLevel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseLevelPolicy.GetEarnedSkillPoints(invalidLevel));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void CalculateProgress_ThrowsArgumentOutOfRangeException_ForNegativeCumulativeXp(long invalidXp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CyberDefenseLevelPolicy.CalculateProgress(invalidXp));
    }

    [Fact]
    public void PlayerLevelProgress_ConstructorValidatesInvariants()
    {
        // Level < 1
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlayerLevelProgress(0, 0, 0, 80));

        // Negative CumulativeXp
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlayerLevelProgress(1, -1, 0, 80));

        // Negative CurrentLevelThresholdXp
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlayerLevelProgress(1, 0, -1, 80));

        // NextThreshold <= CurrentThreshold
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlayerLevelProgress(1, 0, 80, 80));

        // CumulativeXp < CurrentThreshold
        Assert.Throws<ArgumentException>(() =>
            new PlayerLevelProgress(2, 50, 80, 184));

        // CumulativeXp >= NextThreshold
        Assert.Throws<ArgumentException>(() =>
            new PlayerLevelProgress(2, 184, 80, 184));
    }

    [Fact]
    public void BeyondLevelThousand_CalculatesDeterministicProgressWithoutArbitraryCap()
    {
        // Test high levels up to Level 5000
        int delta5000 = CyberDefenseLevelPolicy.GetDeltaXp(5000);
        Assert.True(delta5000 > 1366);

        long threshold5000 = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(5000);
        Assert.True(threshold5000 > 1124747L);

        int derivedLevel = CyberDefenseLevelPolicy.GetLevel(threshold5000);
        Assert.Equal(5000, derivedLevel);

        PlayerLevelProgress progress = CyberDefenseLevelPolicy.CalculateProgress(threshold5000);
        Assert.Equal(5000, progress.Level);
        Assert.Equal(4999, progress.EarnedSkillPoints);
    }
}
