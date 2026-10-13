namespace MathFirst.Application.Persistence;

using System;

public sealed record CyberDefenseProgressionRecord
{
    public const string DefaultEconomyVersion = "ECONOMY_V1";

    public long StoreRevision { get; }
    public long ResetEpoch { get; }
    public long CumulativeXp { get; }
    public int PlayerLevel { get; }
    public int EarnedSkillPoints { get; }
    public int SpentSkillPoints { get; }
    public int AvailableSkillPoints { get; }
    public int AttackRank { get; }
    public string EconomyVersion { get; }
    public DateTimeOffset UpdatedAt { get; }

    public CyberDefenseProgressionRecord(
        long storeRevision,
        long resetEpoch,
        long cumulativeXp,
        int playerLevel,
        int earnedSkillPoints,
        int spentSkillPoints,
        int availableSkillPoints,
        int attackRank,
        string economyVersion,
        DateTimeOffset updatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(storeRevision, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(resetEpoch);
        ArgumentOutOfRangeException.ThrowIfNegative(cumulativeXp);
        ArgumentOutOfRangeException.ThrowIfLessThan(playerLevel, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(earnedSkillPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(spentSkillPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(availableSkillPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(attackRank);

        if (string.IsNullOrWhiteSpace(economyVersion))
        {
            throw new ArgumentException("Economy version cannot be null or whitespace.", nameof(economyVersion));
        }

        if (earnedSkillPoints != checked(spentSkillPoints + availableSkillPoints))
        {
            throw new ArgumentException(
                $"Earned SP ({earnedSkillPoints}) must equal spent SP ({spentSkillPoints}) + available SP ({availableSkillPoints}).");
        }

        StoreRevision = storeRevision;
        ResetEpoch = resetEpoch;
        CumulativeXp = cumulativeXp;
        PlayerLevel = playerLevel;
        EarnedSkillPoints = earnedSkillPoints;
        SpentSkillPoints = spentSkillPoints;
        AvailableSkillPoints = availableSkillPoints;
        AttackRank = attackRank;
        EconomyVersion = economyVersion;
        UpdatedAt = updatedAt;
    }

    public static CyberDefenseProgressionRecord Initial(DateTimeOffset updatedAt) =>
        new(
            storeRevision: 1,
            resetEpoch: 0,
            cumulativeXp: 0,
            playerLevel: 1,
            earnedSkillPoints: 0,
            spentSkillPoints: 0,
            availableSkillPoints: 0,
            attackRank: 0,
            economyVersion: DefaultEconomyVersion,
            updatedAt: updatedAt);

    public CyberDefenseAttackUpgradeSnapshot ToAttackUpgradeSnapshot() =>
        CyberDefenseAttackUpgradeSnapshot.FromProgression(this);
}
