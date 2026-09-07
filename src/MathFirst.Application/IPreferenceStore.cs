namespace MathFirst.Application;

using MathFirst.Domain;

public interface IPreferenceStore
{
    bool GetOnboardingCompleted();
    void SetOnboardingCompleted(bool completed);

    ThemePreference GetThemePreference();
    void SetThemePreference(ThemePreference preference);

    string GetLanguagePreference();
    void SetLanguagePreference(string languageCode);

    void ResetAllPreferences();
}
