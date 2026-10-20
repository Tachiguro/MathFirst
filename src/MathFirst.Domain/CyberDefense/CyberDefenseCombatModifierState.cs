namespace MathFirst.Domain.CyberDefense;

/// <summary>
/// Immutable run-scoped state of the pure combat modifier model (GDD §8.3, §8.4; Issue #90
/// DEC-007-01..04): the Critical Strike phase counter of confirmed correct combat attacks made while
/// Critical Strike is active, the consecutive confirmed correct answers counted for Overdrive and the
/// attacks remaining in the single armed Overdrive bonus window. Equality is by value;
/// <c>default</c> equals <see cref="Initial"/>.
/// The window holds at most <see cref="CyberDefenseOverdrivePolicy.Rank1BonusAttackCount"/> attacks.
/// Arming restarts the streak at zero and every window attack advances it by one, so while a window
/// is active the streak equals the window attacks already used; any other combination would describe
/// an overlapping window and is rejected.
/// </summary>
public readonly record struct CyberDefenseCombatModifierState
{
    public static CyberDefenseCombatModifierState Initial => default;

    public int CriticalCorrectAttackCount { get; }
    public int OverdriveStreak { get; }
    public int OverdriveRemainingBonusAttacks { get; }

    public CyberDefenseCombatModifierState(int criticalCorrectAttackCount, int overdriveStreak)
        : this(criticalCorrectAttackCount, overdriveStreak, 0)
    {
    }

    public CyberDefenseCombatModifierState(int criticalCorrectAttackCount, int overdriveStreak, int overdriveRemainingBonusAttacks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(criticalCorrectAttackCount);
        ArgumentOutOfRangeException.ThrowIfNegative(overdriveStreak);
        ArgumentOutOfRangeException.ThrowIfNegative(overdriveRemainingBonusAttacks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(overdriveRemainingBonusAttacks, CyberDefenseOverdrivePolicy.Rank1BonusAttackCount);

        if (overdriveRemainingBonusAttacks > 0
            && overdriveStreak != CyberDefenseOverdrivePolicy.Rank1BonusAttackCount - overdriveRemainingBonusAttacks)
        {
            throw new ArgumentException(
                $"An active Overdrive window ({overdriveRemainingBonusAttacks} attacks remaining) requires a streak of {CyberDefenseOverdrivePolicy.Rank1BonusAttackCount - overdriveRemainingBonusAttacks}, not {overdriveStreak}.",
                nameof(overdriveStreak));
        }

        CriticalCorrectAttackCount = criticalCorrectAttackCount;
        OverdriveStreak = overdriveStreak;
        OverdriveRemainingBonusAttacks = overdriveRemainingBonusAttacks;
    }
}
