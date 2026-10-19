namespace MathFirst.Domain.CyberDefense;

/// <summary>
/// Pure, deterministic composition of Critical Strike and Overdrive damage bonuses (GDD §8.3, §8.4).
/// The supplied bonus percentages are summed first (additive stacking, never multiplied) and applied
/// to the base damage in a single step with exactly one ceiling rounding:
/// effective = ceil(base * (100 + critical + overdrive) / 100), computed with exact integer
/// arithmetic so no floating-point rounding instability can occur.
/// The policy only composes percentages it is given. Which bonus applies to an attack, rank 2
/// Overdrive tuning, streak reset after a trigger, overlapping bonus windows and trigger scheduling
/// are intentionally out of scope. It has no timing inputs and holds no state.
/// </summary>
public static class CyberDefenseDamageCompositionPolicy
{
    private const int PercentDenominator = 100;

    /// <summary>
    /// Effective damage for a positive base damage and nonnegative Critical Strike and Overdrive bonus
    /// percentages (0 = no bonus). Throws <see cref="ArgumentOutOfRangeException"/> for nonpositive
    /// base damage or negative bonuses, and <see cref="OverflowException"/> when the rounded result
    /// does not fit in <see cref="int"/>; it never wraps.
    /// </summary>
    public static int GetEffectiveDamage(int baseDamage, int criticalBonusPercent, int overdriveBonusPercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseDamage);
        ArgumentOutOfRangeException.ThrowIfNegative(criticalBonusPercent);
        ArgumentOutOfRangeException.ThrowIfNegative(overdriveBonusPercent);

        // Int128 holds base * (100 + c + o) exactly (below 2^65), so only the final narrowing can overflow.
        Int128 numerator = (Int128)baseDamage * (PercentDenominator + (Int128)criticalBonusPercent + overdriveBonusPercent);
        Int128 effective = (numerator + (PercentDenominator - 1)) / PercentDenominator;

        return checked((int)effective);
    }
}
