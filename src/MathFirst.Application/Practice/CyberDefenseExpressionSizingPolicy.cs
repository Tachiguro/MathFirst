namespace MathFirst.Application.Practice;

/// <summary>
/// Deterministic content-aware visual sizing policy for Cyber Defense equations.
/// Keeps Solve-to-Attack panel height constant while scaling typography to maximize
/// readability and available horizontal space.
/// </summary>
public static class CyberDefenseExpressionSizingPolicy
{
    public const string ShortClass = "expression-short";
    public const string MediumClass = "expression-medium";
    public const string LongClass = "expression-long";
    public const string XLongClass = "expression-xlong";

    public static string GetSizeClass(int leftOperand, int rightOperand, int resultDigitCount = 0) =>
        GetSizeClass(leftOperand.ToString(), rightOperand.ToString(), resultDigitCount);

    public static string GetSizeClass(string leftOperand, string rightOperand, int resultDigitCount = 0)
    {
        ArgumentNullException.ThrowIfNull(leftOperand);
        ArgumentNullException.ThrowIfNull(rightOperand);

        var maxOperandDigits = Math.Max(leftOperand.Length, rightOperand.Length);
        var totalDigits = leftOperand.Length + rightOperand.Length + Math.Max(1, resultDigitCount);

        // Single-digit facts such as 0 + 0, 7 + 8, 9 - 4, 6 * 7
        if (maxOperandDigits <= 1 && totalDigits <= 4)
        {
            return ShortClass;
        }

        // Medium facts such as 12 * 12 (2+2+3 = 7 digits)
        if (maxOperandDigits <= 2 && totalDigits <= 7)
        {
            return MediumClass;
        }

        // Long facts such as 99 * 99 (2+2+4 = 8 digits) or 3-digit additions up to 9 digits
        if (totalDigits <= 9)
        {
            return LongClass;
        }

        // Extra-long facts such as 999 + 999 = 1998 (10 digits) or larger
        return XLongClass;
    }
}
