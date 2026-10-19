namespace MathFirst.Domain.CyberDefense;

/// <summary>
/// Pure, deterministic Fair Beginner Assistance policy (GDD §9, owner decision DEC-01).
/// Eligibility depends only on explicit Cyber Defense game-state events; it never uses
/// age, school grade, learner profiles, arithmetic performance, learner records, or FSRS data.
/// The assistance effect is one temporary Emergency Shield and never generates XP or Skill Points.
/// Combat effects and persistence are intentionally out of scope here.
/// </summary>
public static class CyberDefenseBeginnerAssistancePolicy
{
    // [PRELIMINARY BALANCE VALUE / TUNING]
    public const int RequiredConsecutiveEarlyDefeats = 3;

    // Owner decision DEC-01 (2026-10-13): one temporary Emergency Shield.
    public const int EmergencyShieldCount = 1;

    private const int EarlySector = 1;

    public static bool IsQualifyingEarlyDefeat(int sectorAtGameOver, bool sector1BossReached)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sectorAtGameOver, 1);
        return sectorAtGameOver == EarlySector && !sector1BossReached;
    }

    public static int GetConsecutiveEarlyDefeatsAfterGameOver(
        int currentConsecutiveEarlyDefeats,
        int sectorAtGameOver,
        bool sector1BossReached)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentConsecutiveEarlyDefeats);

        if (!IsQualifyingEarlyDefeat(sectorAtGameOver, sector1BossReached))
        {
            return 0;
        }

        // Saturating increment: bounded at the trigger threshold, never overflows.
        return Math.Min(currentConsecutiveEarlyDefeats, RequiredConsecutiveEarlyDefeats - 1) + 1;
    }

    public static int GetConsecutiveEarlyDefeatsAfterSector1BossReached(int currentConsecutiveEarlyDefeats)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentConsecutiveEarlyDefeats);
        return 0;
    }

    public static bool IsEligibleForActivation(
        int consecutiveEarlyDefeats,
        bool sector1BossReached,
        int firewallRank,
        bool isAssistanceActive)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveEarlyDefeats);
        ValidateFirewallRank(firewallRank);

        return consecutiveEarlyDefeats >= RequiredConsecutiveEarlyDefeats
            && !sector1BossReached
            && firewallRank == 0
            && !isAssistanceActive;
    }

    public static bool ShouldDeactivate(bool isAssistanceActive, bool sector1Cleared, int firewallRank)
    {
        ValidateFirewallRank(firewallRank);
        return isAssistanceActive && (sector1Cleared || firewallRank >= 1);
    }

    private static void ValidateFirewallRank(int firewallRank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firewallRank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firewallRank, CyberDefenseFirewallPolicy.MaxRank);
    }
}
