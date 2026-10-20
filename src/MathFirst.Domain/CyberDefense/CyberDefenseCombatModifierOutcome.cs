namespace MathFirst.Domain.CyberDefense;

/// <summary>
/// Immutable result of resolving one confirmed combat answer against the combat modifier state:
/// whether the attack is a Critical Strike, its Critical and Overdrive bonus percentages and the
/// resulting state. A Critical Strike always carries a positive bonus and a non-critical attack
/// carries none. The Overdrive bonus is either zero or the single window percentage
/// (<see cref="CyberDefenseOverdrivePolicy.Rank1BonusPercent"/>, used by both ranks per DEC-007-04).
/// </summary>
public readonly record struct CyberDefenseCombatModifierOutcome
{
    public bool IsCriticalStrike { get; }
    public int CriticalBonusPercent { get; }
    public int OverdriveBonusPercent { get; }
    public CyberDefenseCombatModifierState NextState { get; }

    public CyberDefenseCombatModifierOutcome(
        bool isCriticalStrike,
        int criticalBonusPercent,
        CyberDefenseCombatModifierState nextState)
        : this(isCriticalStrike, criticalBonusPercent, 0, nextState)
    {
    }

    public CyberDefenseCombatModifierOutcome(
        bool isCriticalStrike,
        int criticalBonusPercent,
        int overdriveBonusPercent,
        CyberDefenseCombatModifierState nextState)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(criticalBonusPercent);

        if (isCriticalStrike != criticalBonusPercent > 0)
        {
            throw new ArgumentException(
                $"Critical bonus percent ({criticalBonusPercent}) must be positive exactly when the attack is a Critical Strike ({isCriticalStrike}).",
                nameof(criticalBonusPercent));
        }

        if (overdriveBonusPercent != 0 && overdriveBonusPercent != CyberDefenseOverdrivePolicy.Rank1BonusPercent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(overdriveBonusPercent),
                overdriveBonusPercent,
                $"Overdrive bonus percent must be 0 or {CyberDefenseOverdrivePolicy.Rank1BonusPercent}.");
        }

        IsCriticalStrike = isCriticalStrike;
        CriticalBonusPercent = criticalBonusPercent;
        OverdriveBonusPercent = overdriveBonusPercent;
        NextState = nextState;
    }
}
