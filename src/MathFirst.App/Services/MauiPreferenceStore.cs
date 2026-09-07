namespace MathFirst.App.Services;

using MathFirst.Application;
using MathFirst.Domain;
using Microsoft.Maui.Storage;

public sealed class MauiPreferenceStore : IPreferenceStore
{
    private const string OnboardingKey = "mathfirst.onboarding_completed";
    private const string ThemeKey = "mathfirst.theme_preference";
    private const string LanguageKey = "mathfirst.language_preference";

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

    public string GetLanguagePreference() =>
        Preferences.Default.Get(LanguageKey, LanguagePreferencePolicy.SystemPreferenceCode);

    public void SetLanguagePreference(string languageCode) =>
        Preferences.Default.Set(LanguageKey, languageCode);

    public void ResetAllPreferences()
    {
        Preferences.Default.Remove(OnboardingKey);
        Preferences.Default.Remove(ThemeKey);
        Preferences.Default.Remove(LanguageKey);
    }
}
