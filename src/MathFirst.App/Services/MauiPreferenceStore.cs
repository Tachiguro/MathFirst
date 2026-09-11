namespace MathFirst.App.Services;

using MathFirst.Application;
using MathFirst.Domain;
using Microsoft.Maui.Storage;

public sealed class MauiPreferenceStore : IPreferenceStore
{
    private const string OnboardingKey = "mathfirst.onboarding_completed";
    private const string ThemeKey = "mathfirst.theme_preference";
    private const string LanguageKey = "mathfirst.language_preference";
    private const string NumericKeypadLayoutKey = "mathfirst.numeric_keypad_layout";

    public bool GetOnboardingCompleted() => Preferences.Default.Get(OnboardingKey, false);

    public void SetOnboardingCompleted(bool completed) => Preferences.Default.Set(OnboardingKey, completed);

    public ThemePreference GetThemePreference()
    {
        var raw = Preferences.Default.Get(ThemeKey, (int)ThemePreference.System);
        return ThemePreferencePolicy.Normalize(raw);
    }

    public void SetThemePreference(ThemePreference preference)
    {
        var normalized = ThemePreferencePolicy.Normalize((int)preference);
        Preferences.Default.Set(ThemeKey, (int)normalized);
    }

    public NumericKeypadLayout GetNumericKeypadLayout()
    {
        var raw = Preferences.Default.Get(NumericKeypadLayoutKey, (int)NumericKeypadLayout.Numpad);
        return NumericKeypadLayoutPolicy.Normalize(raw);
    }

    public void SetNumericKeypadLayout(NumericKeypadLayout layout)
    {
        var normalized = NumericKeypadLayoutPolicy.Normalize((int)layout);
        Preferences.Default.Set(NumericKeypadLayoutKey, (int)normalized);
    }

    public string GetLanguagePreference() =>
        Preferences.Default.Get(LanguageKey, LanguagePreferencePolicy.SystemPreferenceCode);

    public void SetLanguagePreference(string languageCode) =>
        Preferences.Default.Set(LanguageKey, languageCode);

    public void ResetAllPreferences()
    {
        Preferences.Default.Remove(OnboardingKey);
        Preferences.Default.Remove(ThemeKey);
        Preferences.Default.Remove(LanguageKey);
        Preferences.Default.Remove(NumericKeypadLayoutKey);
    }
}
