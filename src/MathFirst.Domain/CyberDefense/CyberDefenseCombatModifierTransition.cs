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
/// (DEC-007-04). The Overdrive streak advances regardless of the Overdrive rank, as in the original
/// pure model; only arming requires a purchased rank.
/// </summary>
public static class CyberDefenseCombatModifierTransition
{
    /// <summary>
    /// A correct answer receives the Overdrive bonus of the window active before it (consuming one
    /// window attack), advances the Critical counter while Critical Strike is active (the current
    /// attack counts toward its own trigger; throws <see cref="OverflowException"/> instead of
    /// wrapping) and the Overdrive streak (saturating), and arms a new window when the streak reaches
    /// the Overdrive threshold. An incorrect answer keeps the Critical counter, resets the streak,
    /// cancels any remaining window attacks and never receives a bonus.
    /// Throws <see cref="ArgumentOutOfRangeException"/> for an invalid rank.
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
        int nextStreak = CyberDefenseOverdrivePolicy.GetStreakAfterAnswer(state.OverdriveStreak, true);

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
