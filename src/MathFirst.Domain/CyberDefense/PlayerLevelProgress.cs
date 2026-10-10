namespace MathFirst.Domain.CyberDefense;

public sealed record PlayerLevelProgress
{
    public int Level { get; }
    public long CumulativeXp { get; }
    public long CurrentLevelThresholdXp { get; }
    public long NextLevelThresholdXp { get; }
    public long XpIntoCurrentLevel { get; }
    public int XpRequiredForNextLevel { get; }
    public int RemainingXpToNextLevel { get; }
    public int EarnedSkillPoints { get; }
    public double ProgressRatio { get; }

    public PlayerLevelProgress(
        int level,
        long cumulativeXp,
        long currentLevelThresholdXp,
        long nextLevelThresholdXp)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(cumulativeXp);
        ArgumentOutOfRangeException.ThrowIfNegative(currentLevelThresholdXp);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(nextLevelThresholdXp, currentLevelThresholdXp);

        if (cumulativeXp < currentLevelThresholdXp || cumulativeXp >= nextLevelThresholdXp)
        {
            throw new ArgumentException(
                $"Cumulative XP {cumulativeXp} must be in range [{currentLevelThresholdXp}, {nextLevelThresholdXp}) for level {level}.");
        }

        Level = level;
        CumulativeXp = cumulativeXp;
        CurrentLevelThresholdXp = currentLevelThresholdXp;
        NextLevelThresholdXp = nextLevelThresholdXp;

        XpIntoCurrentLevel = checked(cumulativeXp - currentLevelThresholdXp);
        XpRequiredForNextLevel = checked((int)(nextLevelThresholdXp - currentLevelThresholdXp));
        RemainingXpToNextLevel = checked(XpRequiredForNextLevel - (int)XpIntoCurrentLevel);
        EarnedSkillPoints = checked(level - 1);
        ProgressRatio = (double)XpIntoCurrentLevel / XpRequiredForNextLevel;
    }
}
