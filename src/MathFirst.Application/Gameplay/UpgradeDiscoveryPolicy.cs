namespace MathFirst.Application.Gameplay;

using System;
using MathFirst.Domain.CyberDefense;

/// <summary>
/// Issue #125 progressive disclosure: the Upgrade capability is advertised only once the player has earned a real
/// purchasing opportunity. It reads the committed progression only: earned Skill Points grow with level-ups and are
/// never reduced by purchases or a GameOver within a reset epoch, so discovery survives spending, defeat and restarts
/// without any additional persisted flag. A full gameplay reset starts a new epoch at zero, which is a fresh learner.
/// </summary>
public static class UpgradeDiscoveryPolicy
{
    /// <summary>The cheapest real purchase: Attack rank 1, which has no player level requirement.</summary>
    public static int FirstOpportunityCost { get; } = CyberDefenseAttackUpgradePolicy.GetUpgradeCost(1);

    public static bool IsDiscovered(UpgradeTerminalStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return status.EarnedSkillPoints >= FirstOpportunityCost;
    }

    /// <summary>
    /// Issue #123: checks whether the given status presents an active, authoritative purchasing opportunity
    /// (at least one eligible, level-unlocked upgrade family that the player can currently afford with available SP).
    /// </summary>
    public static bool HasPurchasingOpportunity(UpgradeTerminalStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return status.CanPurchase ||
               status.CanPurchaseFirewall ||
               status.CanPurchaseCriticalStrike ||
               status.CanPurchaseOverdrive;
    }
}
