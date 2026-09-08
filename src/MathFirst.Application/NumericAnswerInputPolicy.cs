namespace MathFirst.Application;

using System.Globalization;

public static class NumericAnswerInputPolicy
{
    // Large enough for future arithmetic ranges while keeping pasted input layout-safe.
    public const int MaximumLength = 28;

    public static bool IsValidEdit(string? value)
    {
        if (value is null || value.Length > MaximumLength)
        {
            return false;
        }

        var separatorSeen = false;
        var integerDigitCount = 0;
        var integerStartsWithZero = false;
        foreach (var character in value)
        {
            if (character is >= '0' and <= '9')
            {
                if (!separatorSeen)
                {
                    if (integerDigitCount == 0)
                    {
                        integerStartsWithZero = character == '0';
                    }

                    integerDigitCount++;
                    if (integerStartsWithZero && integerDigitCount > 1)
                    {
                        return false;
                    }
                }

                continue;
            }

            if (character is '.' or ',')
            {
                if (separatorSeen)
                {
                    return false;
                }

                separatorSeen = true;
                continue;
            }

            return false;
        }

        return true;
    }

    public static string AcceptEditOrKeep(string currentValue, string? proposedValue) =>
        IsValidEdit(proposedValue) ? proposedValue! : currentValue;

    public static string Append(string currentValue, string key)
    {
        ArgumentNullException.ThrowIfNull(currentValue);
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (key.Length != 1 || !(char.IsAsciiDigit(key[0]) || key[0] is '.' or ','))
        {
            return currentValue;
        }

        var proposed = currentValue + key;
        return IsValidEdit(proposed) ? proposed : currentValue;
    }

    public static string Backspace(string currentValue)
    {
        ArgumentNullException.ThrowIfNull(currentValue);
        return currentValue.Length == 0 ? currentValue : currentValue[..^1];
    }

    public static bool TryParseSubmission(string? value, out decimal parsedValue)
    {
        parsedValue = default;
        if (!IsValidEdit(value) || string.IsNullOrEmpty(value) || value is "." or ",")
        {
            return false;
        }

        var invariantValue = value.Replace(',', '.');
        return decimal.TryParse(
            invariantValue,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out parsedValue);
    }

    public static string Format(decimal value, string decimalSeparator)
    {
        ArgumentException.ThrowIfNullOrEmpty(decimalSeparator);
        var invariantValue = value.ToString("0.############################", CultureInfo.InvariantCulture);
        return decimalSeparator == "." ? invariantValue : invariantValue.Replace(".", decimalSeparator);
    }
}
