namespace MathFirst.Domain;

public static class ThemePreferencePolicy
{
    public static ThemePreference Normalize(int value) =>
        Enum.IsDefined(typeof(ThemePreference), value)
            ? (ThemePreference)value
            : ThemePreference.System;

    public static ThemePreference Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ThemePreference.System;
        }

        if (int.TryParse(value, out var intValue))
        {
            return Normalize(intValue);
        }

        return Enum.TryParse<ThemePreference>(value.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : ThemePreference.System;
    }
}
