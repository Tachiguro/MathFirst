namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Navigation;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Telemetry;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

/// <summary>
/// Permanent contract hardening and regression suite for P4 Settings Simplification.
/// Protects the removal of Practice Time UI, retention of required Settings surfaces,
/// read-only operation presentation, CurriculumManaged authority, Custom mode non-interference,
/// reset boundaries, and the Settings open/close pause/resume lifecycle.
/// </summary>
public sealed class SettingsSimplificationContractTests : IDisposable
{
    private readonly string _testDirectory;

    public SettingsSimplificationContractTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstSettingsSimplification_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of isolated temporary test resources.
        }
    }

    private string GetTempDatabasePath(string prefix = "p4_test") =>
        Path.Combine(_testDirectory, $"{prefix}_{Guid.NewGuid():N}.db");

    // =========================================================================
    // A. NORMAL SETTINGS HAS NO PRACTICE TIME UI
    // =========================================================================

    [Fact]
    public void P4_A_NormalSettings_ContainsNoPracticeTimeMarkupOrBindings()
    {
        var settingsSource = ReadSourceWithoutComments("src", "MathFirst.App", "Components", "Pages", "Settings.razor");

        // The 8 removed Settings-exclusive practice-time keys must not appear in Settings markup
        Assert.DoesNotContain("Settings_PracticeTimeTitle", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings_PracticeTimeHelp", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PracticeTime_Standard", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PracticeTime_NoTimePressure", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PracticeTime_30s", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PracticeTime_45s", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PracticeTime_60s", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings_PracticeTimeChangedTo", settingsSource, StringComparison.Ordinal);

        // Internal UI fields, methods, and CSS classes removed in P4
        Assert.DoesNotContain("PracticeTimeSetting", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectedPracticeTime", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectPracticeTime", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("choice-grid-practice-time", settingsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void P4_A_PracticeTimePlumbing_RetainedBelowUiLayer()
    {
        // PracticeTimeSetting and PracticeTimePreferencePolicy are intentionally retained
        // below the UI layer for lower-level domain compatibility and Custom mode testing.
        var normalizedStandard = PracticeTimePreferencePolicy.Normalize((int)PracticeTimeSetting.Standard);
        var normalizedNtp = PracticeTimePreferencePolicy.Normalize((int)PracticeTimeSetting.NoTimePressure);
        var normalized30 = PracticeTimePreferencePolicy.Normalize((int)PracticeTimeSetting.Seconds30);

        Assert.Equal(PracticeTimeSetting.Standard, normalizedStandard);
        Assert.Equal(PracticeTimeSetting.NoTimePressure, normalizedNtp);
        Assert.Equal(PracticeTimeSetting.Seconds30, normalized30);

        Assert.Equal(0L, PracticeTimePreferencePolicy.GetDeadlineFloorMs(PracticeTimeSetting.Standard));
        Assert.Equal(30_000L, PracticeTimePreferencePolicy.GetDeadlineFloorMs(PracticeTimeSetting.Seconds30));
        Assert.False(PracticeTimePreferencePolicy.HasEnforcedDeadline(PracticeTimeSetting.Standard));
        Assert.True(PracticeTimePreferencePolicy.IsNoTimePressure(PracticeTimeSetting.NoTimePressure));
    }

    // =========================================================================
    // B. REQUIRED SETTINGS SURFACES REMAIN
    // =========================================================================

    [Fact]
    public void P4_B_NormalSettings_RetainsAllRequiredSectionsAndControls()
    {
        var settingsSource = ReadSourceWithoutComments("src", "MathFirst.App", "Components", "Pages", "Settings.razor");

        // 1. Language selection
        Assert.Contains("ui-language-select", settingsSource, StringComparison.Ordinal);
        Assert.Contains("HandleLanguageChanged", settingsSource, StringComparison.Ordinal);

        // 2. Theme / Appearance selection (System / Light / Dark)
        Assert.Contains("Settings_Appearance", settingsSource, StringComparison.Ordinal);
        Assert.Contains("ThemePreference.System", settingsSource, StringComparison.Ordinal);
        Assert.Contains("ThemePreference.Light", settingsSource, StringComparison.Ordinal);
        Assert.Contains("ThemePreference.Dark", settingsSource, StringComparison.Ordinal);
        Assert.Contains("SelectTheme", settingsSource, StringComparison.Ordinal);

        // 3. Operations status (read-only)
        Assert.Contains("Settings_OperationsTitle", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Settings_OperationsHelp", settingsSource, StringComparison.Ordinal);
        Assert.Contains("CurriculumUnlockPolicy.GetUnlockedOperations", settingsSource, StringComparison.Ordinal);

        // 4. Number keypad layout (Numpad / Phone)
        Assert.Contains("Keypad_NumberKeypad", settingsSource, StringComparison.Ordinal);
        Assert.Contains("NumericKeypadLayout.Numpad", settingsSource, StringComparison.Ordinal);
        Assert.Contains("NumericKeypadLayout.Phone", settingsSource, StringComparison.Ordinal);
        Assert.Contains("SelectKeypadLayout", settingsSource, StringComparison.Ordinal);

        // 5. Haptic feedback toggle
        Assert.Contains("Settings_HapticFeedbackTitle", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Settings_HapticFeedbackHelp", settingsSource, StringComparison.Ordinal);
        Assert.Contains("SetHapticFeedback", settingsSource, StringComparison.Ordinal);

        // 6. Privacy policy navigation
        Assert.Contains("href=\"privacy\"", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Settings_Privacy", settingsSource, StringComparison.Ordinal);

        // 7. Reset Learning Progress
        Assert.Contains("Reset_LearningProgress_Title", settingsSource, StringComparison.Ordinal);
        Assert.Contains("ExecuteResetLearning", settingsSource, StringComparison.Ordinal);

        // 8. Restore Default Settings
        Assert.Contains("Reset_UiPreferences_Title", settingsSource, StringComparison.Ordinal);
        Assert.Contains("ExecuteResetUiPreferences", settingsSource, StringComparison.Ordinal);

        // 9. Full Local Reset
        Assert.Contains("Reset_FullLocal_Title", settingsSource, StringComparison.Ordinal);
        Assert.Contains("ExecuteFullLocalReset", settingsSource, StringComparison.Ordinal);

        // 10. Version & Build metadata
        Assert.Contains("Settings_VersionBuild", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Settings_Build", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Settings_Source", settingsSource, StringComparison.Ordinal);

        // 11. Tester diagnostics & telemetry component invocation
        Assert.Contains("TesterDiagnosticsSection", settingsSource, StringComparison.Ordinal);
    }

    // =========================================================================
    // C. OPERATION UI REMAINS READ-ONLY
    // =========================================================================

    [Fact]
    public void P4_C_OperationPresentation_IsStrictlyReadOnly_AndDerivedFromCurriculumStage()
    {
        var settingsSource = ReadSourceWithoutComments("src", "MathFirst.App", "Components", "Pages", "Settings.razor");

        // Derived strictly from CurriculumUnlockPolicy
        Assert.Contains("CurriculumUnlockPolicy.GetUnlockedOperations(Session.Progression.CurriculumStage)", settingsSource, StringComparison.Ordinal);

        // Visual and accessible read-only markers
        Assert.Contains("read-only", settingsSource, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"true\"", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Settings_OperationUnlocked", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Settings_OperationLocked", settingsSource, StringComparison.Ordinal);

        // No mutation handlers or toggles in Settings
        Assert.DoesNotContain("ToggleOperationAsync", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetOperationEnabled", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PracticeOperationPreferenceCoordinator", settingsSource, StringComparison.Ordinal);
    }

    // =========================================================================
    // D. CURRICULUMMANAGED AUTHORITY
    // =========================================================================

    [Fact]
    public async Task P4_D_CurriculumManagedAuthority_DerivesFromCurriculumStage_NotPreferences()
    {
        var dbPath = GetTempDatabasePath("curriculum_authority");
        using var store = new SqliteLearnerStore(dbPath);
        var prefStore = new TestPreferenceStore();

        // Stale / mismatched preferences enabling all 4 operations
        prefStore.SetEnabledOperations(PracticeOperationPreferencePolicy.AllOperations);

        var session = new TrainingSession(store, preferenceStore: prefStore, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        // Fresh session starts at Stage 1 (Addition only)
        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
        Assert.Equal(ArithmeticOperation.Addition, session.CurrentFact.Operation);

        // Preferences cannot expand the active operations beyond Stage 1
        var unlockedOperations = CurriculumUnlockPolicy.GetUnlockedOperations(session.Progression.CurriculumStage);
        Assert.Single(unlockedOperations);
        Assert.Equal(ArithmeticOperation.Addition, unlockedOperations[0]);
    }

    // =========================================================================
    // E. CUSTOM MODE NON-INTERFERENCE
    // =========================================================================

    [Fact]
    public async Task P4_E_CustomModeNonInterference_ConsumesOperationPreferences()
    {
        var dbPath = GetTempDatabasePath("custom_authority");
        using var store = new SqliteLearnerStore(dbPath);
        var prefStore = new TestPreferenceStore();

        // Explicit Custom mode subset: Multiplication only
        prefStore.SetEnabledOperations([ArithmeticOperation.Multiplication]);

        var session = new TrainingSession(store, preferenceStore: prefStore, practiceMode: PracticeMode.Custom);
        await session.InitializeAsync(startTiming: false);

        // In Custom mode, preferences govern operation selection independent of CurriculumStage
        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        Assert.Equal([ArithmeticOperation.Multiplication], prefStore.GetEnabledOperations());
    }

    // =========================================================================
    // F. RESET SEMANTICS
    // =========================================================================

    [Fact]
    public async Task P4_F_ResetSemantics_BoundariesRemainDurable()
    {
        var dbPath = GetTempDatabasePath("reset_boundaries");
        using var store = new SqliteLearnerStore(dbPath);
        var prefStore = new TestPreferenceStore
        {
            Language = "de",
            Theme = ThemePreference.Dark,
            KeypadLayout = NumericKeypadLayout.Phone,
            HapticFeedbackEnabled = false
        };

        var session = new TrainingSession(store, preferenceStore: prefStore, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        // Simulate earned Stage 3 progression
        session.Progression.CurriculumStage = CurriculumStage.Stage3_Multiplication;

        // 1. Reset UI Preferences: preserves earned learning stage and progress
        prefStore.ResetPracticePreferences();
        prefStore.SetLanguagePreference("system");
        prefStore.SetThemePreference(ThemePreference.System);
        prefStore.SetNumericKeypadLayout(NumericKeypadLayout.Numpad);
        prefStore.SetHapticFeedbackEnabled(true);

        Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);
        Assert.Equal("system", prefStore.GetLanguagePreference());
        Assert.Equal(ThemePreference.System, prefStore.GetThemePreference());
        Assert.Equal(NumericKeypadLayout.Numpad, prefStore.GetNumericKeypadLayout());
        Assert.True(prefStore.GetHapticFeedbackEnabled());

        // 2. Reset Learning Progress: resets CurriculumStage to Stage 1, preserves UI preferences
        prefStore.SetLanguagePreference("ru");
        prefStore.SetThemePreference(ThemePreference.Dark);
        await session.ResetLearningProgressAsync(startTiming: false);

        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Equal("ru", prefStore.GetLanguagePreference());
        Assert.Equal(ThemePreference.Dark, prefStore.GetThemePreference());

        // 3. Full Local Reset: resets database to Stage 1, Schema V9, and clears preferences
        var idProvider = new TestInstallationIdProvider();
        var cacheCleaner = new TestTelemetryShareCacheCleaner();
        var coordinator = new AppResetCoordinator(session, prefStore, idProvider, cacheCleaner);

        await coordinator.ExecuteFullResetAsync();

        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Equal(9, session.Progression.SchemaVersion);
        Assert.Equal(1, idProvider.ClearCallCount);
        Assert.Equal(1, cacheCleaner.PurgeCallCount);
    }

    // =========================================================================
    // G. SETTINGS OPEN/CLOSE LIFECYCLE
    // =========================================================================

    [Fact]
    public async Task P4_G_SettingsLifecycle_PauseResume_PreservesFactPositionAndInput()
    {
        var dbPath = GetTempDatabasePath("lifecycle_preserve");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: true);

        var initialFact = session.CurrentFact;
        var initialPosition = session.Progression.PracticePosition;
        session.SetCurrentAnswerInput("14");

        // Entering Settings pauses item timing
        session.PauseItemTiming();
        Assert.False(session.IsTimingActive);

        // Exiting Settings / resuming practice restores timing
        session.ResumeItemTiming();
        Assert.True(session.IsTimingActive);

        // Fact, position, and user input are completely preserved
        Assert.Same(initialFact, session.CurrentFact);
        Assert.Equal(initialPosition, session.Progression.PracticePosition);
        Assert.Equal("14", session.CurrentAnswerInput);
        Assert.Equal(0, session.SessionTotalCount);
    }

    // =========================================================================
    // TEST HELPERS & STUBS
    // =========================================================================

    private static string ReadSourceWithoutComments(params string[] pathSegments)
    {
        var fullPath = GetRepositoryPath(pathSegments);
        Assert.True(File.Exists(fullPath), $"Expected source file at {fullPath}");
        var text = File.ReadAllText(fullPath);
        var noRazorComments = Regex.Replace(text, @"@\*[\s\S]*?\*@", "");
        var noBlockComments = Regex.Replace(noRazorComments, @"/\*[\s\S]*?\*/", "");
        var noLineComments = Regex.Replace(noBlockComments, @"//.*$", "", RegexOptions.Multiline);
        return noLineComments;
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private sealed class TestPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<ArithmeticOperation, bool> _ops = new();
        public string Language { get; set; } = "system";
        public ThemePreference Theme { get; set; } = ThemePreference.System;
        public NumericKeypadLayout KeypadLayout { get; set; } = NumericKeypadLayout.Numpad;
        public bool HapticFeedbackEnabled { get; set; } = true;
        public PracticeTimeSetting TimeSetting { get; set; } = PracticeTimeSetting.Standard;

        public string GetLanguagePreference() => Language;
        public void SetLanguagePreference(string preference) => Language = preference;
        public ThemePreference GetThemePreference() => Theme;
        public void SetThemePreference(ThemePreference preference) => Theme = preference;
        public NumericKeypadLayout GetNumericKeypadLayout() => KeypadLayout;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) => KeypadLayout = layout;
        public bool GetHapticFeedbackEnabled() => HapticFeedbackEnabled;
        public void SetHapticFeedbackEnabled(bool enabled) => HapticFeedbackEnabled = enabled;

        public bool GetOperationEnabled(ArithmeticOperation operation) =>
            _ops.GetValueOrDefault(operation, true);

        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) =>
            _ops[operation] = enabled;

        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() =>
            PracticeOperationPreferencePolicy.NormalizeEnabledOperations(
                PracticeOperationPreferencePolicy.AllOperations.Where(GetOperationEnabled));

        public void SetEnabledOperations(IEnumerable<ArithmeticOperation> operations)
        {
            var set = operations.ToHashSet();
            foreach (var op in PracticeOperationPreferencePolicy.AllOperations)
            {
                _ops[op] = set.Contains(op);
            }
        }

        public PracticeTimeSetting GetPracticeTimeSetting() => TimeSetting;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) => TimeSetting = setting;

        public void ResetPracticePreferences() => _ops.Clear();
        public void ResetAllPreferences() => _ops.Clear();
    }

    private sealed class TestInstallationIdProvider : IInstallationIdProvider
    {
        public int ClearCallCount { get; private set; }
        public string GetOrCreateInstallationId() => "test-install-id";
        public void ClearInstallationId() => ClearCallCount++;
    }

    private sealed class TestTelemetryShareCacheCleaner : ITelemetryShareCacheCleaner
    {
        public int PurgeCallCount { get; private set; }
        public void PurgeShareCache() => PurgeCallCount++;
    }
}
