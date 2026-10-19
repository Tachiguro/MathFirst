namespace MathFirst.Domain.CyberDefense;

/// <summary>
/// Immutable result of resolving one confirmed combat answer against the combat modifier state:
/// whether the attack is a Critical Strike, its bonus percentage and the resulting state.
/// A Critical Strike always carries a positive bonus and a non-critical attack carries none.
/// </summary>
public readonly record struct CyberDefenseCombatModifierOutcome
{
    public bool IsCriticalStrike { get; }
    public int CriticalBonusPercent { get; }
    public CyberDefenseCombatModifierState NextState { get; }

    public CyberDefenseCombatModifierOutcome(
        bool isCriticalStrike,
        int criticalBonusPercent,
        CyberDefenseCombatModifierState nextState)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(criticalBonusPercent);

        if (isCriticalStrike != criticalBonusPercent > 0)
        {
            throw new ArgumentException(
                $"Critical bonus percent ({criticalBonusPercent}) must be positive exactly when the attack is a Critical Strike ({isCriticalStrike}).",
                nameof(criticalBonusPercent));
        }

        IsCriticalStrike = isCriticalStrike;
        CriticalBonusPercent = criticalBonusPercent;
        NextState = nextState;
    }
}
