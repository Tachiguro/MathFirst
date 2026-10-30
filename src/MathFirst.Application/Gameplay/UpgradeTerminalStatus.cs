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
    public int CurrentFirewallRank { get; }
    public int ActivatedFirewallRank { get; }
    public int? NextFirewallRank { get; }
    public int? NextRequiredLevel { get; }
    public int? NextFirewallRequiredLevel => NextRequiredLevel;
    public int? NextFirewallCost { get; }
    public int CurrentPermanentCapacity => CyberDefenseFirewallPolicy.GetShieldCapacity(CurrentFirewallRank);
    public int CurrentShieldCapacity => CurrentPermanentCapacity;
    public int? NextPermanentCapacity => NextFirewallRank.HasValue ? CyberDefenseFirewallPolicy.GetShieldCapacity(NextFirewallRank.Value) : null;
    public int? NextShieldCapacity => NextPermanentCapacity;
    public bool IsFirewallLevelUnlocked { get; }
    public bool HasSufficientFirewallSkillPoints { get; }
    public bool HasSufficientFirewallSp => HasSufficientFirewallSkillPoints;
    public bool IsFirewallMaxRank => !NextFirewallRank.HasValue;
    public bool CanPurchaseFirewall => !IsFirewallMaxRank && IsFirewallLevelUnlocked && HasSufficientFirewallSkillPoints;
    // True only before the first shield slot is reachable; NextFirewallRequiredLevel then equals the policy unlock level.
    public bool IsFirewallShieldLocked => CurrentFirewallRank == 0 && !IsFirewallLevelUnlocked;
    // Null means this legacy projection did not include an authoritative modifier read.
    public int? CurrentCriticalStrikeRank { get; }
    public int? NextCriticalStrikeRank => CurrentCriticalStrikeRank is < CyberDefenseCriticalStrikePolicy.MaxRank ? CurrentCriticalStrikeRank + 1 : null;
    public int? NextCriticalStrikeRequiredLevel => NextCriticalStrikeRank is int rank ? CyberDefenseCriticalStrikePolicy.GetRequiredLevel(rank) : null;
    public int? NextCriticalStrikeCost => NextCriticalStrikeRank is int rank ? CyberDefenseCriticalStrikePolicy.GetUpgradeCost(rank) : null;
    public bool IsCriticalStrikeLevelUnlocked => NextCriticalStrikeRequiredLevel is int level && PlayerLevel >= level;
    public bool HasSufficientCriticalStrikeSkillPoints => NextCriticalStrikeCost is int cost && AvailableSkillPoints >= cost;
    public bool IsCriticalStrikeMaxRank => CurrentCriticalStrikeRank == CyberDefenseCriticalStrikePolicy.MaxRank;
    public bool CanPurchaseCriticalStrike => IsCriticalStrikeLevelUnlocked && HasSufficientCriticalStrikeSkillPoints;
    public int? CurrentOverdriveRank { get; }
    public int? NextOverdriveRank => CurrentOverdriveRank is < CyberDefenseOverdrivePolicy.MaxRank ? CurrentOverdriveRank + 1 : null;
    public int? NextOverdriveRequiredLevel => NextOverdriveRank is int rank ? CyberDefenseOverdrivePolicy.GetRequiredLevel(rank) : null;
    public int? NextOverdriveCost => NextOverdriveRank is int rank ? CyberDefenseOverdrivePolicy.GetUpgradeCost(rank) : null;
    public bool IsOverdriveLevelUnlocked => NextOverdriveRequiredLevel is int level && PlayerLevel >= level;
    public bool HasSufficientOverdriveSkillPoints => NextOverdriveCost is int cost && AvailableSkillPoints >= cost;
    public bool IsOverdriveMaxRank => CurrentOverdriveRank == CyberDefenseOverdrivePolicy.MaxRank;
    public bool CanPurchaseOverdrive => IsOverdriveLevelUnlocked && HasSufficientOverdriveSkillPoints;
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
        long storeRevision,
        int currentFirewallRank = 0,
        int activatedFirewallRank = 0,
        int? currentCriticalStrikeRank = null,
        int? currentOverdriveRank = null)
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

        ArgumentOutOfRangeException.ThrowIfNegative(currentFirewallRank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(currentFirewallRank, CyberDefenseFirewallPolicy.MaxRank);
        ArgumentOutOfRangeException.ThrowIfNegative(activatedFirewallRank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(activatedFirewallRank, currentFirewallRank);
        if (currentCriticalStrikeRank.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(currentCriticalStrikeRank.Value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(currentCriticalStrikeRank.Value, CyberDefenseCriticalStrikePolicy.MaxRank);
        }
        if (currentOverdriveRank.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(currentOverdriveRank.Value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(currentOverdriveRank.Value, CyberDefenseOverdrivePolicy.MaxRank);
        }
        CurrentCriticalStrikeRank = currentCriticalStrikeRank;
        CurrentOverdriveRank = currentOverdriveRank;

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

        CurrentFirewallRank = currentFirewallRank;
        ActivatedFirewallRank = activatedFirewallRank;

        if (currentFirewallRank < CyberDefenseFirewallPolicy.MaxRank)
        {
            int nRank = checked(currentFirewallRank + 1);
            NextFirewallRank = nRank;
            NextRequiredLevel = CyberDefenseFirewallPolicy.GetRequiredLevel(nRank);
            NextFirewallCost = CyberDefenseFirewallPolicy.GetUpgradeCost(nRank);
            IsFirewallLevelUnlocked = CyberDefenseFirewallPolicy.IsRankUnlockedAtLevel(nRank, playerLevel);
            HasSufficientFirewallSkillPoints = availableSkillPoints >= NextFirewallCost.Value;
        }
        else
        {
            NextFirewallRank = null;
            NextRequiredLevel = null;
            NextFirewallCost = null;
            IsFirewallLevelUnlocked = false;
            HasSufficientFirewallSkillPoints = false;
        }
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
            storeRevision: 1,
            currentFirewallRank: 0,
            activatedFirewallRank: 0,
            currentCriticalStrikeRank: 0,
            currentOverdriveRank: 0);
    }

    public static UpgradeTerminalStatus FromProgression(CyberDefenseProgressionRecord progression) =>
        FromProgressionCore(progression, null);

    public static UpgradeTerminalStatus FromProgression(
        CyberDefenseProgressionRecord progression, CyberDefenseCombatModifierSnapshot modifiers)
    {
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(modifiers);
        if (progression.ResetEpoch != modifiers.ResetEpoch || progression.StoreRevision != modifiers.StoreRevision ||
            modifiers.ResetIntent is null || modifiers.ResetIntent.IsPending ||
            modifiers.ResetIntent.CurrentEpoch != modifiers.ResetEpoch || modifiers.ResetIntent.TargetEpoch != modifiers.ResetEpoch)
        {
            throw new InvalidOperationException("Progression and modifier snapshot must have matching revision and reset epoch with no reset pending.");
        }
        return FromProgressionCore(progression, modifiers);
    }

    private static UpgradeTerminalStatus FromProgressionCore(
        CyberDefenseProgressionRecord progression, CyberDefenseCombatModifierSnapshot? modifiers)
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
            storeRevision: progression.StoreRevision,
            currentFirewallRank: progression.FirewallRank,
            activatedFirewallRank: progression.ActivatedFirewallRank,
            currentCriticalStrikeRank: modifiers?.CriticalStrikeRank,
            currentOverdriveRank: modifiers?.OverdriveRank);
    }

    public static UpgradeTerminalStatus FromSnapshot(
        CyberDefenseAttackUpgradeSnapshot snapshot,
        long cumulativeXp = 0L,
        int currentFirewallRank = 0,
        int activatedFirewallRank = 0)
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
            storeRevision: snapshot.StoreRevision,
            currentFirewallRank: currentFirewallRank,
            activatedFirewallRank: activatedFirewallRank);
    }

    public static UpgradeTerminalStatus FromFirewallSnapshot(
        CyberDefenseFirewallUpgradeSnapshot snapshot,
        long cumulativeXp = 0L,
        int currentAttackRank = 0,
        int activatedFirewallRank = 0)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(cumulativeXp);

        var progress = CyberDefenseLevelPolicy.CalculateProgress(cumulativeXp);
        if (progress.Level != snapshot.PlayerLevel)
        {
            cumulativeXp = CyberDefenseLevelPolicy.GetCumulativeXpThreshold(snapshot.PlayerLevel);
            progress = CyberDefenseLevelPolicy.CalculateProgress(cumulativeXp);
        }

        int currentDamage = CyberDefenseAttackUpgradePolicy.GetAttackDamage(currentAttackRank);
        int? nextAttackRank = null;
        int? nextDamage = null;
        int? nextAttackCost = null;
        bool hasSufficientAttackSp = false;

        if (currentAttackRank < CyberDefenseAttackUpgradeSnapshot.MaxRepresentableAttackRank)
        {
            int nRank = checked(currentAttackRank + 1);
            nextAttackRank = nRank;
            nextDamage = CyberDefenseAttackUpgradePolicy.GetAttackDamage(nRank);
            int cost = CyberDefenseAttackUpgradePolicy.GetUpgradeCost(nRank);
            nextAttackCost = cost;
            hasSufficientAttackSp = snapshot.AvailableSkillPoints >= cost;
        }

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
            currentAttackRank: currentAttackRank,
            nextAttackRank: nextAttackRank,
            currentBaseDamage: currentDamage,
            nextBaseDamage: nextDamage,
            nextRankCost: nextAttackCost,
            hasSufficientSkillPoints: hasSufficientAttackSp,
            resetEpoch: snapshot.ResetEpoch,
            storeRevision: snapshot.StoreRevision,
            currentFirewallRank: snapshot.CurrentFirewallRank,
            activatedFirewallRank: activatedFirewallRank);
    }
}
