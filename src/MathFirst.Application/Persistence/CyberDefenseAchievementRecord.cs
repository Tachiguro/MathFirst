namespace MathFirst.Application.Persistence;

using System;

/// <summary>
/// Typed immutable record representing an unlocked offline achievement.
/// </summary>
public sealed record CyberDefenseAchievementRecord
{
    public string AchievementId { get; }
    public long ResetEpoch { get; }
    public DateTimeOffset UnlockedAtUtc { get; }

    public CyberDefenseAchievementRecord(string achievementId, long resetEpoch, DateTimeOffset unlockedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(achievementId);
        ArgumentOutOfRangeException.ThrowIfNegative(resetEpoch);

        if (unlockedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("UnlockedAtUtc must be in UTC.", nameof(unlockedAtUtc));
        }

        AchievementId = achievementId;
        ResetEpoch = resetEpoch;
        UnlockedAtUtc = unlockedAtUtc;
    }
}
