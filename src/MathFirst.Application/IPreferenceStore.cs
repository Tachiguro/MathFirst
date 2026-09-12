namespace MathFirst.Application;

using MathFirst.Domain;

public interface IPreferenceStore
{
    bool GetOnboardingCompleted();
    void SetOnboardingCompleted(bool completed);

    ThemePreference GetThemePreference();
    void SetThemePreference(ThemePreference preference);

    NumericKeypadLayout GetNumericKeypadLayout();
    void SetNumericKeypadLayout(NumericKeypadLayout layout);

    string GetLanguagePreference();
    void SetLanguagePreference(string languageCode);

    bool GetOperationEnabled(ArithmeticOperation operation);
    void SetOperationEnabled(ArithmeticOperation operation, bool enabled);
    IReadOnlyList<ArithmeticOperation> GetEnabledOperations();

    PracticeTimeSetting GetPracticeTimeSetting();
    void SetPracticeTimeSetting(PracticeTimeSetting setting);

    void ResetPracticePreferences();
    void ResetAllPreferences();
}
