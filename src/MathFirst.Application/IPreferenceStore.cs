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

public static class PracticeOperationPreferenceCoordinator
{
    public static bool TryToggleOperation(
        IPreferenceStore preferenceStore,
        IReadOnlyCollection<ArithmeticOperation> currentEnabled,
        ArithmeticOperation operation,
        out IReadOnlyList<ArithmeticOperation> resultingEnabled)
    {
        ArgumentNullException.ThrowIfNull(preferenceStore);
        return PracticeOperationPreferencePolicy.TryToggleOperation(
            preferenceStore.SetOperationEnabled,
            preferenceStore.GetEnabledOperations,
            currentEnabled,
            operation,
            out resultingEnabled);
    }
}

public static class PreferenceStoreExtensions
{
    public static bool TryToggleOperation(
        this IPreferenceStore preferenceStore,
        IReadOnlyCollection<ArithmeticOperation> currentEnabled,
        ArithmeticOperation operation,
        out IReadOnlyList<ArithmeticOperation> resultingEnabled) =>
        PracticeOperationPreferenceCoordinator.TryToggleOperation(
            preferenceStore,
            currentEnabled,
            operation,
            out resultingEnabled);
}
