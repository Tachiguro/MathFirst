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

    private const string AdditionEnabledKey = "mathfirst.operation.addition_enabled";
    private const string SubtractionEnabledKey = "mathfirst.operation.subtraction_enabled";
    private const string MultiplicationEnabledKey = "mathfirst.operation.multiplication_enabled";
    private const string DivisionEnabledKey = "mathfirst.operation.division_enabled";
    private const string PracticeTimeSettingKey = "mathfirst.practice_time_setting";

    private static string GetOperationKey(ArithmeticOperation operation) => operation switch
    {
        ArithmeticOperation.Addition => AdditionEnabledKey,
        ArithmeticOperation.Subtraction => SubtractionEnabledKey,
        ArithmeticOperation.Multiplication => MultiplicationEnabledKey,
        ArithmeticOperation.Division => DivisionEnabledKey,
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

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

    public bool GetOperationEnabled(ArithmeticOperation operation) =>
        Preferences.Default.Get(GetOperationKey(operation), true);

    public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) =>
        Preferences.Default.Set(GetOperationKey(operation), enabled);

    public IReadOnlyList<ArithmeticOperation> GetEnabledOperations()
    {
        var list = new List<ArithmeticOperation>(4);
        foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
        {
            if (GetOperationEnabled(op))
            {
                list.Add(op);
            }
        }
        return PracticeOperationPreferencePolicy.NormalizeEnabledOperations(list);
    }

    public PracticeTimeSetting GetPracticeTimeSetting()
    {
        var raw = Preferences.Default.Get(PracticeTimeSettingKey, (int)PracticeTimeSetting.Standard);
        return PracticeTimePreferencePolicy.Normalize(raw);
    }

    public void SetPracticeTimeSetting(PracticeTimeSetting setting)
    {
        var normalized = PracticeTimePreferencePolicy.Normalize((int)setting);
        Preferences.Default.Set(PracticeTimeSettingKey, (int)normalized);
    }

    public void ResetPracticePreferences()
    {
        Preferences.Default.Remove(AdditionEnabledKey);
        Preferences.Default.Remove(SubtractionEnabledKey);
        Preferences.Default.Remove(MultiplicationEnabledKey);
        Preferences.Default.Remove(DivisionEnabledKey);
        Preferences.Default.Remove(PracticeTimeSettingKey);
    }

    public void ResetAllPreferences()
    {
        Preferences.Default.Remove(OnboardingKey);
        Preferences.Default.Remove(ThemeKey);
        Preferences.Default.Remove(LanguageKey);
        Preferences.Default.Remove(NumericKeypadLayoutKey);
        ResetPracticePreferences();
    }
}
