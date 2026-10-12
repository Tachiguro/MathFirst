namespace MathFirst.Application.Gameplay;

using System;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;

public sealed record UpgradeTerminalStatus
{
    public int PlayerLevel { get; }
    public long CumulativeXp { get; }
    public long XpIntoCurrentLevel { get; }
    public int XpRequiredForNextLevel { get; }
    public int RemainingXpToNextLevel { get; }
    public double ProgressRatio { get; }
    public PlayerLevelProgress LevelProgress { get; }
    public int AvailableSkillPoints { get; }
    public int EarnedSkillPoints { get; }
    public int SpentSkillPoints { get; }
    public int CurrentAttackRank { get; }
    public int? NextAttackRank { get; }
    public int CurrentBaseDamage { get; }
    public int? NextBaseDamage { get; }
    public int? NextRankCost { get; }
    public bool HasSufficientSkillPoints { get; }
    public bool CanAfford => HasSufficientSkillPoints;
    public bool IsMaxRank => !NextAttackRank.HasValue;
    public bool CanPurchase => CanAfford && !IsMaxRank;
    public long ResetEpoch { get; }
    public long StoreRevision { get; }

    public UpgradeTerminalStatus(
        int playerLevel,
        long cumulativeXp,
        long xpIntoCurrentLevel,
        int xpRequiredForNextLevel,
        int remainingXpToNextLevel,
        double progressRatio,
        PlayerLevelProgress levelProgress,
        int availableSkillPoints,
        int earnedSkillPoints,
        int spentSkillPoints,
        int currentAttackRank,
        int? nextAttackRank,
        int currentBaseDamage,
        int? nextBaseDamage,
        int? nextRankCost,
        bool hasSufficientSkillPoints,
        long resetEpoch,
        long storeRevision)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(playerLevel, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(cumulativeXp);
        ArgumentOutOfRangeException.ThrowIfNegative(xpIntoCurrentLevel);
        ArgumentOutOfRangeException.ThrowIfLessThan(xpRequiredForNextLevel, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(remainingXpToNextLevel);
        ArgumentNullException.ThrowIfNull(levelProgress);
        ArgumentOutOfRangeException.ThrowIfNegative(availableSkillPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(earnedSkillPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(spentSkillPoints);
        ArgumentOutOfRangeException.ThrowIfNegative(currentAttackRank);
        if (nextAttackRank.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(nextAttackRank.Value, 1);
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(currentBaseDamage, 1);
        if (nextBaseDamage.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(nextBaseDamage.Value, 1);
        }
        if (nextRankCost.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(nextRankCost.Value, 1);
        }
        ArgumentOutOfRangeException.ThrowIfNegative(resetEpoch);
        ArgumentOutOfRangeException.ThrowIfLessThan(storeRevision, 1);

        PlayerLevel = playerLevel;
        CumulativeXp = cumulativeXp;
        XpIntoCurrentLevel = xpIntoCurrentLevel;
        XpRequiredForNextLevel = xpRequiredForNextLevel;
        RemainingXpToNextLevel = remainingXpToNextLevel;
        ProgressRatio = progressRatio;
        LevelProgress = levelProgress;
        AvailableSkillPoints = availableSkillPoints;
        EarnedSkillPoints = earnedSkillPoints;
        SpentSkillPoints = spentSkillPoints;
        CurrentAttackRank = currentAttackRank;
        NextAttackRank = nextAttackRank;
        CurrentBaseDamage = currentBaseDamage;
        NextBaseDamage = nextBaseDamage;
        NextRankCost = nextRankCost;
        HasSufficientSkillPoints = hasSufficientSkillPoints;
        ResetEpoch = resetEpoch;
        StoreRevision = storeRevision;
    }

    public static UpgradeTerminalStatus Initial()
    {
        var progress = CyberDefenseLevelPolicy.CalculateProgress(0L);
        return new UpgradeTerminalStatus(
            playerLevel: 1,
            cumulativeXp: 0L,
            xpIntoCurrentLevel: 0L,
            xpRequiredForNextLevel: progress.XpRequiredForNextLevel,
            remainingXpToNextLevel: progress.RemainingXpToNextLevel,
            progressRatio: progress.ProgressRatio,
            levelProgress: progress,
            availableSkillPoints: 0,
            earnedSkillPoints: 0,
            spentSkillPoints: 0,
            currentAttackRank: 0,
            nextAttackRank: 1,
            currentBaseDamage: 1,
            nextBaseDamage: 2,
            nextRankCost: 1,
            hasSufficientSkillPoints: false,
            resetEpoch: 0,
            storeRevision: 1);
    }

    public static UpgradeTerminalStatus FromProgression(CyberDefenseProgressionRecord progression)
    {
        ArgumentNullException.ThrowIfNull(progression);

        var progress = CyberDefenseLevelPolicy.CalculateProgress(progression.CumulativeXp);
        int currentRank = progression.AttackRank;
        int currentDamage = CyberDefenseAttackUpgradePolicy.GetAttackDamage(currentRank);

        int? nextRank = null;
        int? nextDamage = null;
        int? nextCost = null;
        bool hasSufficientSp = false;

        if (currentRank < CyberDefenseAttackUpgradeSnapshot.MaxRepresentableAttackRank)
        {
            int nRank = checked(currentRank + 1);
            nextRank = nRank;
            nextDamage = CyberDefenseAttackUpgradePolicy.GetAttackDamage(nRank);
            int cost = CyberDefenseAttackUpgradePolicy.GetUpgradeCost(nRank);
            nextCost = cost;
            hasSufficientSp = progression.AvailableSkillPoints >= cost;
        }

        return new UpgradeTerminalStatus(
            playerLevel: progression.PlayerLevel,
            cumulativeXp: progression.CumulativeXp,
            xpIntoCurrentLevel: progress.XpIntoCurrentLevel,
            xpRequiredForNextLevel: progress.XpRequiredForNextLevel,
            remainingXpToNextLevel: progress.RemainingXpToNextLevel,
            progressRatio: progress.ProgressRatio,
            levelProgress: progress,
            availableSkillPoints: progression.AvailableSkillPoints,
            earnedSkillPoints: progression.EarnedSkillPoints,
            spentSkillPoints: progression.SpentSkillPoints,
            currentAttackRank: currentRank,
            nextAttackRank: nextRank,
            currentBaseDamage: currentDamage,
            nextBaseDamage: nextDamage,
            nextRankCost: nextCost,
            hasSufficientSkillPoints: hasSufficientSp,
            resetEpoch: progression.ResetEpoch,
            storeRevision: progression.StoreRevision);
    }

    public static UpgradeTerminalStatus FromSnapshot(CyberDefenseAttackUpgradeSnapshot snapshot, long cumulativeXp = 0L)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(cumulativeXp);

        var progress = CyberDefenseLevelPolicy.CalculateProgress(cumulativeXp);
        if (progress.Level != snapshot.PlayerLevel)
        {
            cumulativeXp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(snapshot.PlayerLevel);
            progress = CyberDefenseLevelPolicy.CalculateProgress(cumulativeXp);
        }

        int currentRank = snapshot.CurrentAttackRank;
        int currentDamage = snapshot.CurrentBaseDamage;

        int earnedSp = checked(snapshot.PlayerLevel - 1);
        int spentSp = checked(earnedSp - snapshot.AvailableSkillPoints);
        if (spentSp < 0) spentSp = 0;

        return new UpgradeTerminalStatus(
            playerLevel: snapshot.PlayerLevel,
            cumulativeXp: cumulativeXp,
            xpIntoCurrentLevel: progress.XpIntoCurrentLevel,
            xpRequiredForNextLevel: progress.XpRequiredForNextLevel,
            remainingXpToNextLevel: progress.RemainingXpToNextLevel,
            progressRatio: progress.ProgressRatio,
            levelProgress: progress,
            availableSkillPoints: snapshot.AvailableSkillPoints,
            earnedSkillPoints: earnedSp,
            spentSkillPoints: spentSp,
            currentAttackRank: currentRank,
            nextAttackRank: snapshot.NextAttackRank,
            currentBaseDamage: currentDamage,
            nextBaseDamage: snapshot.NextBaseDamage,
            nextRankCost: snapshot.NextRankCost,
            hasSufficientSkillPoints: snapshot.HasSufficientSkillPoints,
            resetEpoch: snapshot.ResetEpoch,
            storeRevision: snapshot.StoreRevision);
    }
}
