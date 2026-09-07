using System.Globalization;

namespace MathFirst.Domain;

public readonly record struct DeviceLanguageClassification(
    bool IsSupported,
    string EffectiveLanguage);

public static class LanguagePreferencePolicy
{
    public const string EnglishLanguageCode = "en";
    public const string GermanLanguageCode = "de";
    public const string RussianLanguageCode = "ru";
    public const string SystemPreferenceCode = "system";

    public static readonly IReadOnlyList<string> SupportedLanguageCodes =
        Array.AsReadOnly([EnglishLanguageCode, GermanLanguageCode, RussianLanguageCode]);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return EnglishLanguageCode;
        }

        var candidate = value.Trim()
            .Replace('_', '-')
            .Split('-', 2, StringSplitOptions.TrimEntries)[0]
            .ToLowerInvariant();

        return candidate switch
        {
            GermanLanguageCode => GermanLanguageCode,
            RussianLanguageCode => RussianLanguageCode,
            EnglishLanguageCode => EnglishLanguageCode,
            _ => EnglishLanguageCode
        };
    }

    public static string Resolve(string? savedLanguage, string? deviceCulture)
    {
        if (savedLanguage is not null && !IsExplicitSystemSelector(savedLanguage))
        {
            return TryNormalizeSupportedLanguage(savedLanguage, out var normalizedSavedLanguage)
                ? normalizedSavedLanguage
                : EnglishLanguageCode;
        }

        return Normalize(deviceCulture);
    }

    public static bool IsExplicitSystemSelector(string? value) =>
        value is not null
        && string.Equals(value.Trim(), SystemPreferenceCode, StringComparison.OrdinalIgnoreCase);

    public static bool TryNormalizeSupportedLanguage(string? value, out string normalizedLanguage)
    {
        var candidate = value?.Trim().ToLowerInvariant();

        if (candidate is EnglishLanguageCode or GermanLanguageCode or RussianLanguageCode)
        {
            normalizedLanguage = candidate;
            return true;
        }

        normalizedLanguage = EnglishLanguageCode;
        return false;
    }

    public static DeviceLanguageClassification ClassifyDeviceCulture(string? deviceCulture)
    {
        if (string.IsNullOrWhiteSpace(deviceCulture))
        {
            return new DeviceLanguageClassification(false, EnglishLanguageCode);
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(deviceCulture.Trim().Replace('_', '-'));
            var languageFamily = culture.TwoLetterISOLanguageName.ToLowerInvariant();

            return TryNormalizeSupportedLanguage(languageFamily, out var effectiveLanguage)
                ? new DeviceLanguageClassification(true, effectiveLanguage)
                : new DeviceLanguageClassification(false, EnglishLanguageCode);
        }
        catch (CultureNotFoundException)
        {
            return new DeviceLanguageClassification(false, EnglishLanguageCode);
        }
    }
}
