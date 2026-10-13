namespace MathFirst.Application.Persistence;

using System;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Immutable, read-only projection of Firewall skill upgrade state and costs
/// for future Upgrade Terminal presentation, derived from authoritative progression.
/// </summary>
public sealed record CyberDefenseFirewallUpgradeSnapshot
{
    public int CurrentFirewallRank { get; }
    public int CurrentRank => CurrentFirewallRank;
    public int? NextFirewallRank { get; }
    public int? NextRank => NextFirewallRank;
    public int? RequiredPlayerLevel { get; }
    public int? NextRankCost { get; }
    public int? NextUpgradeCost => NextRankCost;
    public int AvailableSkillPoints { get; }
    public int PlayerLevel { get; }
    public bool IsLevelUnlocked { get; }
    public bool MeetsUnlockGate => IsLevelUnlocked;
    public bool HasSufficientSkillPoints { get; }
    public bool HasSufficientSp => HasSufficientSkillPoints;
    public bool CanAfford => HasSufficientSkillPoints;
    public bool CanPurchaseNextRank { get; }
    public bool CanPurchase => CanPurchaseNextRank;
    public long ResetEpoch { get; }
    public long StoreRevision { get; }

    public CyberDefenseFirewallUpgradeSnapshot(
        int currentFirewallRank,
        int? nextFirewallRank,
        int? requiredPlayerLevel,
        int? nextRankCost,
        int availableSkillPoints,
        int playerLevel,
        bool isLevelUnlocked,
        bool hasSufficientSkillPoints,
        bool canPurchaseNextRank,
        long resetEpoch,
        long storeRevision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentFirewallRank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(currentFirewallRank, CyberDefenseFirewallPolicy.MaxRank);
        if (nextFirewallRank.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(nextFirewallRank.Value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(nextFirewallRank.Value, CyberDefenseFirewallPolicy.MaxRank);
        }
        if (requiredPlayerLevel.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(requiredPlayerLevel.Value, CyberDefenseLevelPolicy.StartingLevel);
        }
        if (nextRankCost.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(nextRankCost.Value, 1);
        }
        ArgumentOutOfRangeException.ThrowIfNegative(availableSkillPoints);
        ArgumentOutOfRangeException.ThrowIfLessThan(playerLevel, CyberDefenseLevelPolicy.StartingLevel);
        ArgumentOutOfRangeException.ThrowIfNegative(resetEpoch);
        ArgumentOutOfRangeException.ThrowIfLessThan(storeRevision, 1);

        CurrentFirewallRank = currentFirewallRank;
        NextFirewallRank = nextFirewallRank;
        RequiredPlayerLevel = requiredPlayerLevel;
        NextRankCost = nextRankCost;
        AvailableSkillPoints = availableSkillPoints;
        PlayerLevel = playerLevel;
        IsLevelUnlocked = isLevelUnlocked;
        HasSufficientSkillPoints = hasSufficientSkillPoints;
        CanPurchaseNextRank = canPurchaseNextRank;
        ResetEpoch = resetEpoch;
        StoreRevision = storeRevision;
    }

    public static CyberDefenseFirewallUpgradeSnapshot FromProgression(CyberDefenseProgressionRecord progression)
    {
        ArgumentNullException.ThrowIfNull(progression);

        int currentRank = progression.FirewallRank;
        int? nextRank = null;
        int? requiredLevel = null;
        int? nextCost = null;
        bool isLevelUnlocked = false;
        bool hasSufficientSp = false;
        bool canPurchase = false;

        if (currentRank < CyberDefenseFirewallPolicy.MaxRank)
        {
            int nRank = checked(currentRank + 1);
            nextRank = nRank;
            requiredLevel = CyberDefenseFirewallPolicy.GetRequiredLevel(nRank);
            nextCost = CyberDefenseFirewallPolicy.GetUpgradeCost(nRank);
            isLevelUnlocked = CyberDefenseFirewallPolicy.IsRankUnlockedAtLevel(nRank, progression.PlayerLevel);
            hasSufficientSp = progression.AvailableSkillPoints >= nextCost.Value;
            canPurchase = isLevelUnlocked && hasSufficientSp;
        }

        return new CyberDefenseFirewallUpgradeSnapshot(
            currentFirewallRank: currentRank,
            nextFirewallRank: nextRank,
            requiredPlayerLevel: requiredLevel,
            nextRankCost: nextCost,
            availableSkillPoints: progression.AvailableSkillPoints,
            playerLevel: progression.PlayerLevel,
            isLevelUnlocked: isLevelUnlocked,
            hasSufficientSkillPoints: hasSufficientSp,
            canPurchaseNextRank: canPurchase,
            resetEpoch: progression.ResetEpoch,
            storeRevision: progression.StoreRevision);
    }
}
