namespace MathFirst.Application.Persistence;

using System;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Immutable, read-only projection of Attack skill upgrade state and costs
/// for future Upgrade Terminal presentation, derived from authoritative progression.
/// </summary>
public sealed record CyberDefenseAttackUpgradeSnapshot
{
    public const int MaxRepresentableAttackRank = int.MaxValue - CyberDefenseAttackUpgradePolicy.BaseAttackDamage;

    public int CurrentAttackRank { get; }
    public int? NextAttackRank { get; }
    public int CurrentBaseDamage { get; }
    public int? NextBaseDamage { get; }
    public int? NextRankCost { get; }
    public int AvailableSkillPoints { get; }
    public bool HasSufficientSkillPoints { get; }
    public bool CanAfford => HasSufficientSkillPoints;
    public int PlayerLevel { get; }
    public long ResetEpoch { get; }
    public long StoreRevision { get; }

    public CyberDefenseAttackUpgradeSnapshot(
        int currentAttackRank,
        int? nextAttackRank,
        int currentBaseDamage,
        int? nextBaseDamage,
        int? nextRankCost,
        int availableSkillPoints,
        bool hasSufficientSkillPoints,
        int playerLevel,
        long resetEpoch,
        long storeRevision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentAttackRank);
        if (nextAttackRank.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(nextAttackRank.Value, 1);
        }
        ArgumentOutOfRangeException.ThrowIfNegative(currentBaseDamage);
        if (nextBaseDamage.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(nextBaseDamage.Value);
        }
        if (nextRankCost.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(nextRankCost.Value, 1);
        }
        ArgumentOutOfRangeException.ThrowIfNegative(availableSkillPoints);
        ArgumentOutOfRangeException.ThrowIfLessThan(playerLevel, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(resetEpoch);
        ArgumentOutOfRangeException.ThrowIfLessThan(storeRevision, 1);

        CurrentAttackRank = currentAttackRank;
        NextAttackRank = nextAttackRank;
        CurrentBaseDamage = currentBaseDamage;
        NextBaseDamage = nextBaseDamage;
        NextRankCost = nextRankCost;
        AvailableSkillPoints = availableSkillPoints;
        HasSufficientSkillPoints = hasSufficientSkillPoints;
        PlayerLevel = playerLevel;
        ResetEpoch = resetEpoch;
        StoreRevision = storeRevision;
    }

    public static CyberDefenseAttackUpgradeSnapshot FromProgression(CyberDefenseProgressionRecord progression)
    {
        ArgumentNullException.ThrowIfNull(progression);

        int currentRank = progression.AttackRank;
        int currentDamage = CyberDefenseAttackUpgradePolicy.GetAttackDamage(currentRank);

        int? nextRank = null;
        int? nextDamage = null;
        int? nextCost = null;
        bool hasSufficientSp = false;

        if (currentRank < MaxRepresentableAttackRank)
        {
            int nRank = checked(currentRank + 1);
            nextRank = nRank;
            nextDamage = CyberDefenseAttackUpgradePolicy.GetAttackDamage(nRank);
            int cost = CyberDefenseAttackUpgradePolicy.GetUpgradeCost(nRank);
            nextCost = cost;
            hasSufficientSp = progression.AvailableSkillPoints >= cost;
        }

        return new CyberDefenseAttackUpgradeSnapshot(
            currentAttackRank: currentRank,
            nextAttackRank: nextRank,
            currentBaseDamage: currentDamage,
            nextBaseDamage: nextDamage,
            nextRankCost: nextCost,
            availableSkillPoints: progression.AvailableSkillPoints,
            hasSufficientSkillPoints: hasSufficientSp,
            playerLevel: progression.PlayerLevel,
            resetEpoch: progression.ResetEpoch,
            storeRevision: progression.StoreRevision);
    }
}
