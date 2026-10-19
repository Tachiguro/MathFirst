namespace MathFirst.Domain.CyberDefense;

/// <summary>
/// Immutable counters for the pure combat modifier model (GDD §8.3, §8.4): confirmed correct combat
/// attacks counted for Critical Strike and the consecutive confirmed correct answers counted for
/// Overdrive. Equality is by value; <c>default</c> equals <see cref="Initial"/>.
/// Whether attacks made before a skill is purchased count toward later Critical Strike triggers and
/// whether the Critical counter persists across runs are unresolved product decisions; this type only
/// holds the counters it is given.
/// </summary>
public readonly record struct CyberDefenseCombatModifierState
{
    public static CyberDefenseCombatModifierState Initial => default;

    public int CriticalCorrectAttackCount { get; }
    public int OverdriveStreak { get; }

    public CyberDefenseCombatModifierState(int criticalCorrectAttackCount, int overdriveStreak)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(criticalCorrectAttackCount);
        ArgumentOutOfRangeException.ThrowIfNegative(overdriveStreak);

        CriticalCorrectAttackCount = criticalCorrectAttackCount;
        OverdriveStreak = overdriveStreak;
    }
}
