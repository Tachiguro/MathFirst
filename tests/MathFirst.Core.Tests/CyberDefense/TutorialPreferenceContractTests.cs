namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using MathFirst.Application;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class TutorialPreferenceContractTests
{
    private const string ExpectedPreferenceKey = "mathfirst.cyber_defense.tutorial_completed";

    // =========================================================================
    // A. INTERFACE CONTRACT
    // =========================================================================

    [Fact]
    public void ICyberDefenseTutorialPreferences_InterfaceContract_ExposesOnlyRequiredMethods()
    {
        var type = typeof(ICyberDefenseTutorialPreferences);

        Assert.True(type.IsInterface, "ICyberDefenseTutorialPreferences must be an interface.");
        Assert.Equal("MathFirst.Application.Practice", type.Namespace);

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(2, methods.Length);

        var getMethod = type.GetMethod("GetTutorialCompleted", Type.EmptyTypes);
        Assert.NotNull(getMethod);
        Assert.Equal(typeof(bool), getMethod.ReturnType);

        var setMethod = type.GetMethod("SetTutorialCompleted", [typeof(bool)]);
        Assert.NotNull(setMethod);
        Assert.Equal(typeof(void), setMethod.ReturnType);

        Assert.Empty(type.GetProperties(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(type.GetEvents(BindingFlags.Public | BindingFlags.Instance));
    }

    // =========================================================================
    // B. PRODUCTION ADAPTER SOURCE CONTRACTS
    // =========================================================================

    [Fact]
    public void MauiPreferenceStore_SourceContract_ImplementsCyberDefenseTutorialPreferences()
    {
        var storePath = GetRepositoryPath("src", "MathFirst.App", "Services", "MauiPreferenceStore.cs");
        var source = File.ReadAllText(storePath);

        Assert.Contains("ICyberDefenseTutorialPreferences", source, StringComparison.Ordinal);
        Assert.Contains(ExpectedPreferenceKey, source, StringComparison.Ordinal);
        Assert.Contains("bool GetTutorialCompleted()", source, StringComparison.Ordinal);
        Assert.Contains("void SetTutorialCompleted(bool", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MauiPreferenceStore_SourceContract_ResetSemantics_RemovesKeyOnlyInResetAllPreferences()
    {
        var storePath = GetRepositoryPath("src", "MathFirst.App", "Services", "MauiPreferenceStore.cs");
        var source = File.ReadAllText(storePath);

        // Find ResetPracticePreferences method body
        var practiceResetStart = source.IndexOf("public void ResetPracticePreferences()", StringComparison.Ordinal);
        Assert.True(practiceResetStart >= 0, "ResetPracticePreferences method not found.");
        var practiceResetEnd = source.IndexOf('}', practiceResetStart);
        var practiceResetBody = source.Substring(practiceResetStart, practiceResetEnd - practiceResetStart + 1);

        // ResetPracticePreferences must NOT remove the tutorial completion key
        Assert.DoesNotContain(ExpectedPreferenceKey, practiceResetBody, StringComparison.Ordinal);
        Assert.DoesNotContain("TutorialCompletedKey", practiceResetBody, StringComparison.Ordinal);

        // Find ResetAllPreferences method body
        var allResetStart = source.IndexOf("public void ResetAllPreferences()", StringComparison.Ordinal);
        Assert.True(allResetStart >= 0, "ResetAllPreferences method not found.");
        var allResetEnd = source.IndexOf('}', allResetStart);
        var allResetBody = source.Substring(allResetStart, allResetEnd - allResetStart + 1);

        // ResetAllPreferences MUST remove the tutorial completion key
        Assert.True(
            allResetBody.Contains("TutorialCompletedKey", StringComparison.Ordinal) ||
            allResetBody.Contains(ExpectedPreferenceKey, StringComparison.Ordinal),
            "ResetAllPreferences must remove the tutorial completion preference key.");
    }

    [Fact]
    public void MauiPreferenceStore_SourceContract_MalformedValueConservativeHandling()
    {
        var storePath = GetRepositoryPath("src", "MathFirst.App", "Services", "MauiPreferenceStore.cs");
        var source = File.ReadAllText(storePath);

        // Malformed handling in GetTutorialCompleted must conservatively catch invalid casts / formats
        Assert.Contains("catch (InvalidCastException)", source, StringComparison.Ordinal);
        Assert.Contains("catch (FormatException)", source, StringComparison.Ordinal);
        Assert.Contains("Java.Lang.ClassCastException", source, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (Exception)", source, StringComparison.Ordinal);
    }

    // =========================================================================
    // C. DEPENDENCY INJECTION SOURCE & RUNTIME CONTRACTS
    // =========================================================================

    [Fact]
    public void MauiProgram_SourceContract_RegistersCyberDefenseTutorialPreferencesSingletonAlias()
    {
        var programPath = GetRepositoryPath("src", "MathFirst.App", "MauiProgram.cs");
        var source = File.ReadAllText(programPath);

        Assert.Contains("AddSingleton<ICyberDefenseTutorialPreferences>", source, StringComparison.Ordinal);
        Assert.Contains("sp.GetRequiredService<MauiPreferenceStore>()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyInjection_RegistrationContract_SharesSameSingletonInstance()
    {
        var services = new ServiceCollection();

        // Mirrors MauiProgram.cs registration pattern
        services.AddSingleton<SimulatedPreferenceStoreAdapter>();
        services.AddSingleton<IPreferenceStore>(sp => sp.GetRequiredService<SimulatedPreferenceStoreAdapter>());
        services.AddSingleton<ICyberDefenseModePreferences>(sp => sp.GetRequiredService<SimulatedPreferenceStoreAdapter>());
        services.AddSingleton<ICyberDefenseTutorialPreferences>(sp => sp.GetRequiredService<SimulatedPreferenceStoreAdapter>());

        using var provider = services.BuildServiceProvider();

        var concrete = provider.GetRequiredService<SimulatedPreferenceStoreAdapter>();
        var prefStore = provider.GetRequiredService<IPreferenceStore>();
        var modePrefs = provider.GetRequiredService<ICyberDefenseModePreferences>();
        var tutorialPrefs = provider.GetRequiredService<ICyberDefenseTutorialPreferences>();

        Assert.Same(concrete, prefStore);
        Assert.Same(concrete, modePrefs);
        Assert.Same(concrete, tutorialPrefs);
    }

    // =========================================================================
    // D. BEHAVIORAL CONTRACT TESTS (SIMULATED STORAGE ADAPTER)
    // =========================================================================

    [Fact]
    public void SimulatedStorage_DefaultUnsetKey_ReturnsFalse()
    {
        var backing = new Dictionary<string, object>(StringComparer.Ordinal);
        var adapter = new SimulatedPreferenceStoreAdapter(backing);

        Assert.False(adapter.GetTutorialCompleted());
    }

    [Fact]
    public void SimulatedStorage_ExplicitPersistence_RoundTripsTrueAndFalse()
    {
        var backing = new Dictionary<string, object>(StringComparer.Ordinal);
        var adapter = new SimulatedPreferenceStoreAdapter(backing);

        adapter.SetTutorialCompleted(true);
        Assert.True(adapter.GetTutorialCompleted());
        Assert.Equal(true, backing[ExpectedPreferenceKey]);

        adapter.SetTutorialCompleted(false);
        Assert.False(adapter.GetTutorialCompleted());
        Assert.Equal(false, backing[ExpectedPreferenceKey]);
    }

    [Fact]
    public void SimulatedStorage_RecreatedAdapter_SharesSameUnderlyingStore()
    {
        var backing = new Dictionary<string, object>(StringComparer.Ordinal);
        var adapterA = new SimulatedPreferenceStoreAdapter(backing);

        adapterA.SetTutorialCompleted(true);
        Assert.True(adapterA.GetTutorialCompleted());

        // Recreate adapter against same backing store (simulates app restart)
        var adapterB = new SimulatedPreferenceStoreAdapter(backing);
        Assert.True(adapterB.GetTutorialCompleted());

        adapterB.SetTutorialCompleted(false);
        Assert.False(adapterA.GetTutorialCompleted());
    }

    [Fact]
    public void SimulatedStorage_ResetPracticePreferences_PreservesTutorialCompletion()
    {
        var backing = new Dictionary<string, object>(StringComparer.Ordinal);
        var adapter = new SimulatedPreferenceStoreAdapter(backing);

        adapter.SetCyberDefenseEnabled(false);
        adapter.SetTutorialCompleted(true);
        Assert.True(adapter.GetTutorialCompleted());

        adapter.ResetPracticePreferences();

        // Practice preferences reset to default (cyber defense enabled reverts to true)
        Assert.True(adapter.GetCyberDefenseEnabled());
        // Tutorial completion is preserved
        Assert.True(adapter.GetTutorialCompleted());
        Assert.True(backing.ContainsKey(ExpectedPreferenceKey));
    }

    [Fact]
    public void SimulatedStorage_ResetAllPreferences_ClearsTutorialCompletion()
    {
        var backing = new Dictionary<string, object>(StringComparer.Ordinal);
        var adapter = new SimulatedPreferenceStoreAdapter(backing);

        adapter.SetLanguagePreference("de");
        adapter.SetThemePreference(ThemePreference.Dark);
        adapter.SetTutorialCompleted(true);
        Assert.True(adapter.GetTutorialCompleted());

        adapter.ResetAllPreferences();

        // All preferences cleared including tutorial completion
        Assert.False(adapter.GetTutorialCompleted());
        Assert.False(backing.ContainsKey(ExpectedPreferenceKey));
    }

    [Fact]
    public void SimulatedStorage_TutorialStateChanges_DoNotAffectUnrelatedPreferences()
    {
        var backing = new Dictionary<string, object>(StringComparer.Ordinal);
        var adapter = new SimulatedPreferenceStoreAdapter(backing);

        adapter.SetLanguagePreference("de");
        adapter.SetThemePreference(ThemePreference.Dark);
        adapter.SetCyberDefenseEnabled(true);
        adapter.SetOperationEnabled(ArithmeticOperation.Multiplication, false);

        adapter.SetTutorialCompleted(true);

        Assert.Equal("de", adapter.GetLanguagePreference());
        Assert.Equal(ThemePreference.Dark, adapter.GetThemePreference());
        Assert.True(adapter.GetCyberDefenseEnabled());
        Assert.False(adapter.GetOperationEnabled(ArithmeticOperation.Multiplication));

        adapter.SetCyberDefenseEnabled(false);
        Assert.True(adapter.GetTutorialCompleted());
        Assert.False(adapter.GetCyberDefenseEnabled());
    }

    [Fact]
    public void SimulatedStorage_MalformedValue_ConservativelyFallsBackToFalse()
    {
        var backing = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [ExpectedPreferenceKey] = "not-a-boolean-value"
        };
        var adapter = new SimulatedPreferenceStoreAdapter(backing);

        // Conservative fallback must return false when value is unreadable or malformed
        Assert.False(adapter.GetTutorialCompleted());
    }

    // =========================================================================
    // HELPER UTILITIES & SIMULATED ADAPTER
    // =========================================================================

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = GetRepositoryRoot();
        return Path.Combine([root, .. segments]);
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", ".."));

    /// <summary>
    /// Simulated preference adapter faithfully reproducing MauiPreferenceStore's contract
    /// against an in-memory dictionary.
    /// </summary>
    public sealed class SimulatedPreferenceStoreAdapter : IPreferenceStore, ICyberDefenseModePreferences, ICyberDefenseTutorialPreferences
    {
        private readonly IDictionary<string, object> _store;

        private const string ThemeKey = "mathfirst.theme_preference";
        private const string LanguageKey = "mathfirst.language_preference";
        private const string NumericKeypadLayoutKey = "mathfirst.numeric_keypad_layout";
        private const string AdditionEnabledKey = "mathfirst.operation.addition_enabled";
        private const string SubtractionEnabledKey = "mathfirst.operation.subtraction_enabled";
        private const string MultiplicationEnabledKey = "mathfirst.operation.multiplication_enabled";
        private const string DivisionEnabledKey = "mathfirst.operation.division_enabled";
        private const string PracticeTimeSettingKey = "mathfirst.practice_time_setting";
        private const string HapticFeedbackEnabledKey = "mathfirst.haptic_feedback_enabled";
        private const string CyberDefenseEnabledKey = "mathfirst.cyber_defense_enabled";
        private const string TutorialCompletedKey = ExpectedPreferenceKey;

        public SimulatedPreferenceStoreAdapter() : this(new Dictionary<string, object>(StringComparer.Ordinal)) { }

        public SimulatedPreferenceStoreAdapter(IDictionary<string, object> store)
        {
            _store = store;
        }

        public bool GetTutorialCompleted()
        {
            try
            {
                if (!_store.TryGetValue(TutorialCompletedKey, out var val))
                {
                    return false;
                }

                return val switch
                {
                    bool b => b,
                    _ => throw new InvalidCastException($"Expected bool for key {TutorialCompletedKey}, got {val?.GetType().Name}")
                };
            }
            catch (InvalidCastException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public void SetTutorialCompleted(bool completed)
        {
            _store[TutorialCompletedKey] = completed;
        }

        public ThemePreference GetThemePreference()
        {
            if (_store.TryGetValue(ThemeKey, out var raw) && raw is int i)
            {
                return ThemePreferencePolicy.Normalize(i);
            }
            return ThemePreference.System;
        }

        public void SetThemePreference(ThemePreference preference) =>
            _store[ThemeKey] = (int)ThemePreferencePolicy.Normalize((int)preference);

        public NumericKeypadLayout GetNumericKeypadLayout()
        {
            if (_store.TryGetValue(NumericKeypadLayoutKey, out var raw) && raw is int i)
            {
                return NumericKeypadLayoutPolicy.Normalize(i);
            }
            return NumericKeypadLayout.Numpad;
        }

        public void SetNumericKeypadLayout(NumericKeypadLayout layout) =>
            _store[NumericKeypadLayoutKey] = (int)NumericKeypadLayoutPolicy.Normalize((int)layout);

        public string GetLanguagePreference() =>
            _store.TryGetValue(LanguageKey, out var raw) && raw is string s ? s : LanguagePreferencePolicy.SystemPreferenceCode;

        public void SetLanguagePreference(string languageCode) =>
            _store[LanguageKey] = languageCode;

        public bool GetHapticFeedbackEnabled() =>
            !_store.TryGetValue(HapticFeedbackEnabledKey, out var raw) || (raw is bool b && b);

        public void SetHapticFeedbackEnabled(bool enabled) =>
            _store[HapticFeedbackEnabledKey] = enabled;

        public bool GetOperationEnabled(ArithmeticOperation operation) =>
            !_store.TryGetValue(GetOpKey(operation), out var raw) ? (operation == ArithmeticOperation.Addition) : (raw is bool b && b);

        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) =>
            _store[GetOpKey(operation)] = enabled;

        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations()
        {
            var list = new List<ArithmeticOperation>();
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
            if (_store.TryGetValue(PracticeTimeSettingKey, out var raw) && raw is int i)
            {
                return PracticeTimePreferencePolicy.Normalize(i);
            }
            return PracticeTimeSetting.Standard;
        }

        public void SetPracticeTimeSetting(PracticeTimeSetting setting) =>
            _store[PracticeTimeSettingKey] = (int)PracticeTimePreferencePolicy.Normalize((int)setting);

        public bool GetCyberDefenseEnabled() =>
            !_store.TryGetValue(CyberDefenseEnabledKey, out var raw) || (raw is bool b && b);

        public void SetCyberDefenseEnabled(bool enabled) =>
            _store[CyberDefenseEnabledKey] = enabled;

        public void ResetPracticePreferences()
        {
            _store.Remove(AdditionEnabledKey);
            _store.Remove(SubtractionEnabledKey);
            _store.Remove(MultiplicationEnabledKey);
            _store.Remove(DivisionEnabledKey);
            _store.Remove(PracticeTimeSettingKey);
            _store.Remove(CyberDefenseEnabledKey);
        }

        public void ResetAllPreferences()
        {
            _store.Remove(ThemeKey);
            _store.Remove(LanguageKey);
            _store.Remove(NumericKeypadLayoutKey);
            _store.Remove(HapticFeedbackEnabledKey);
            _store.Remove(TutorialCompletedKey);
            ResetPracticePreferences();
        }

        private static string GetOpKey(ArithmeticOperation operation) => operation switch
        {
            ArithmeticOperation.Addition => AdditionEnabledKey,
            ArithmeticOperation.Subtraction => SubtractionEnabledKey,
            ArithmeticOperation.Multiplication => MultiplicationEnabledKey,
            ArithmeticOperation.Division => DivisionEnabledKey,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }
}
