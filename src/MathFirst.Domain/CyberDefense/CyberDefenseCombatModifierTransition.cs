namespace MathFirst.Domain.CyberDefense;

/// <summary>
/// Pure, deterministic transition of the combat modifier state for one confirmed combat answer
/// (GDD §8.3, §8.4; Issue #90 DEC-007-01..04). It depends only on the state, the actual answer
/// correctness and the Critical Strike and Overdrive ranks: no time, latency, FSRS or learner
/// inputs, and no damage or Firewall inputs, so shield absorption cannot keep an Overdrive streak or
/// window alive after a wrong answer.
/// Critical Strike counts confirmed correct attacks only while its rank is at least 1; incorrect
/// answers leave that count unchanged. Overdrive resolves the current attack against the window armed
/// before it, then advances the streak and, at the rank's threshold, restarts the streak at zero and
/// arms one window for the next attacks. Both ranks use the same window size and percentage
/// (DEC-007-04). Only correct answers given while Overdrive rank is at least 1 count toward its
/// streak (OD-1): at rank 0 the streak stays 0, so no pre-purchase progress can be banked, while a
/// rank 1 to rank 2 upgrade keeps the streak earned at rank 1. An armed window at rank 0 is invalid
/// input and is rejected rather than resolved.
/// </summary>
public static class CyberDefenseCombatModifierTransition
{
    /// <summary>
    /// A correct answer receives the Overdrive bonus of the window active before it (consuming one
    /// window attack), advances the Critical counter while Critical Strike is active (the current
    /// attack counts toward its own trigger; throws <see cref="OverflowException"/> instead of
    /// wrapping) and the Overdrive streak (saturating), and arms a new window when the streak reaches
    /// the Overdrive threshold; at Overdrive rank 0 the streak stays 0. An incorrect answer keeps the
    /// Critical counter, resets the streak, cancels any remaining window attacks and never receives a
    /// bonus.
    /// Throws <see cref="ArgumentOutOfRangeException"/> for an invalid rank and
    /// <see cref="ArgumentException"/> for an armed Overdrive window at Overdrive rank 0.
    /// </summary>
    public static CyberDefenseCombatModifierOutcome ResolveAttempt(
        CyberDefenseCombatModifierState state,
        bool isCorrect,
        int criticalStrikeRank,
        int overdriveRank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(criticalStrikeRank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(criticalStrikeRank, CyberDefenseCriticalStrikePolicy.MaxRank);
        ArgumentOutOfRangeException.ThrowIfNegative(overdriveRank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(overdriveRank, CyberDefenseOverdrivePolicy.MaxRank);

        if (overdriveRank == 0 && state.OverdriveRemainingBonusAttacks > 0)
        {
            throw new ArgumentException(
                $"An armed Overdrive window ({state.OverdriveRemainingBonusAttacks} attacks remaining) requires a purchased Overdrive rank.",
                nameof(state));
        }

        if (!isCorrect)
        {
            return new CyberDefenseCombatModifierOutcome(
                false,
                0,
                0,
                new CyberDefenseCombatModifierState(
                    state.CriticalCorrectAttackCount,
                    CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(state.OverdriveStreak, false),
                    0));
        }

        int nextCount = state.CriticalCorrectAttackCount;
        int criticalBonusPercent = 0;
        if (criticalStrikeRank > 0)
        {
            nextCount = checked(nextCount + 1);
            criticalBonusPercent = CyberDefenseCriticalStrikePolicy.GetCriticalBonusPercent(criticalStrikeRank, nextCount);
        }

        // DEC-007-04: rank 2 keeps the rank 1 percentage and window size.
        int overdriveBonusPercent = CyberDefenseOverdrivePolicy.GetRank1AttackBonusPercent(state.OverdriveRemainingBonusAttacks);
        int nextRemaining = CyberDefenseOverdrivePolicy.ConsumeBonusAttack(state.OverdriveRemainingBonusAttacks);
        // OD-1: answers before Overdrive is purchased never count toward its streak.
        int nextStreak = overdriveRank > 0
            ? CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(state.OverdriveStreak, true)
            : 0;

        // The state invariant keeps the streak below any threshold while a window is active, so arming
        // never overlaps or extends an existing window.
        if (CyberDefenseOverdrivePolicy.HasReachedTriggerThreshold(overdriveRank, nextStreak))
        {
            nextStreak = 0;
            nextRemaining = CyberDefenseOverdrivePolicy.Rank1BonusAttackCount;
        }

        return new CyberDefenseCombatModifierOutcome(
            criticalBonusPercent > 0,
            criticalBonusPercent,
            overdriveBonusPercent,
            new CyberDefenseCombatModifierState(nextCount, nextStreak, nextRemaining));
    }

    /// <summary>
    /// Restarts the Critical Strike trigger phase after a Critical rank change (DEC-007-01), leaving
    /// the Overdrive streak and window unchanged.
    /// </summary>
    public static CyberDefenseCombatModifierState ResetCriticalPhase(CyberDefenseCombatModifierState state)
    {
        return new CyberDefenseCombatModifierState(0, state.OverdriveStreak, state.OverdriveRemainingBonusAttacks);
    }

    /// <summary>
    /// Clears all run-scoped modifier state, for game-over and reset-epoch handling (DEC-007-01).
    /// </summary>
    public static CyberDefenseCombatModifierState ResetRunModifiers()
    {
        return CyberDefenseCombatModifierState.Initial;
    }
}
