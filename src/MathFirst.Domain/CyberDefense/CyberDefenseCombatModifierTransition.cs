namespace MathFirst.Domain.CyberDefense;

/// <summary>
/// Pure, deterministic transition of the combat modifier state for one confirmed combat answer
/// (GDD §8.3, §8.4). It depends only on the state, the actual answer correctness and the Critical
/// Strike rank: no time, latency, FSRS or learner inputs, and no damage or Firewall inputs, so
/// shield absorption cannot keep an Overdrive streak alive after a wrong answer.
/// Counting correct attacks at Critical rank 0 is provisional pure-model behavior only.
/// Overdrive bonus-window arming, consumption and retriggering are intentionally out of scope.
/// </summary>
public static class CyberDefenseCombatModifierTransition
{
    /// <summary>
    /// A correct answer increments the Critical counter (the current attack counts toward its own
    /// trigger; throws <see cref="OverflowException"/> instead of wrapping) and the Overdrive streak
    /// (saturating). An incorrect answer keeps the Critical counter, resets the streak and is never
    /// a Critical Strike. Throws <see cref="ArgumentOutOfRangeException"/> for an invalid rank.
    /// </summary>
    public static CyberDefenseCombatModifierOutcome ResolveAttempt(
        CyberDefenseCombatModifierState state,
        bool isCorrect,
        int criticalStrikeRank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(criticalStrikeRank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(criticalStrikeRank, CyberDefenseCriticalStrikePolicy.MaxRank);

        int nextStreak = CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(state.OverdriveStreak, isCorrect);

        if (!isCorrect)
        {
            return new CyberDefenseCombatModifierOutcome(
                false,
                0,
                new CyberDefenseCombatModifierState(state.CriticalCorrectAttackCount, nextStreak));
        }

        int nextCount = checked(state.CriticalCorrectAttackCount + 1);
        int bonusPercent = CyberDefenseCriticalStrikePolicy.GetCriticalBonusPercent(criticalStrikeRank, nextCount);

        return new CyberDefenseCombatModifierOutcome(
            bonusPercent > 0,
            bonusPercent,
            new CyberDefenseCombatModifierState(nextCount, nextStreak));
    }
}
