namespace MathFirst.Domain;

public static class NumericKeypadLayoutPolicy
{
    private static readonly string[] PhoneDigits = ["1", "2", "3", "4", "5", "6", "7", "8", "9"];
    private static readonly string[] NumpadDigits = ["7", "8", "9", "4", "5", "6", "1", "2", "3"];

    public static NumericKeypadLayout Normalize(int value) =>
        Enum.IsDefined(typeof(NumericKeypadLayout), value)
            ? (NumericKeypadLayout)value
            : NumericKeypadLayout.Phone;

    public static IReadOnlyList<string> GetPrimaryDigits(NumericKeypadLayout layout) =>
        Normalize((int)layout) == NumericKeypadLayout.Numpad ? NumpadDigits : PhoneDigits;

    public static IReadOnlyList<string> GetKeys(NumericKeypadLayout layout, string decimalSeparator)
    {
        ArgumentException.ThrowIfNullOrEmpty(decimalSeparator);

        var normalized = Normalize((int)layout);
        var keys = new List<string>(12);
        keys.AddRange(GetPrimaryDigits(normalized));

        if (normalized == NumericKeypadLayout.Phone)
        {
            keys.Add(decimalSeparator);
            keys.Add("0");
        }
        else
        {
            keys.Add("0");
            keys.Add(decimalSeparator);
        }

        keys.Add("backspace");
        return keys;
    }
}
