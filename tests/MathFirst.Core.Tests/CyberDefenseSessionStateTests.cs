namespace MathFirst.Core.Tests;

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Telemetry;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

public sealed class CyberDefenseSessionStateTests : IDisposable
{
    private readonly string _tempDirectory;

    public CyberDefenseSessionStateTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "MathFirstCyberDefenseState_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    public sealed class InMemoryCyberDefensePreferences : ICyberDefenseModePreferences
    {
        private bool? _enabled;

        public bool GetCyberDefenseEnabled() => _enabled ?? true;

        public void SetCyberDefenseEnabled(bool enabled) => _enabled = enabled;

        public void ResetToDefault() => _enabled = null;
    }

    // =========================================================================
    // SOURCE CONTRACT TESTS
    // =========================================================================

    [Fact]
    public void MauiPreferenceStore_SourceContract_ImplementsCyberDefensePreferences()
    {
        var storePath = GetRepositoryPath("src", "MathFirst.App", "Services", "MauiPreferenceStore.cs");
        var source = File.ReadAllText(storePath);

        Assert.Contains("ICyberDefenseModePreferences", source, StringComparison.Ordinal);
        Assert.Contains("mathfirst.cyber_defense_enabled", source, StringComparison.Ordinal);
        Assert.Contains("GetCyberDefenseEnabled()", source, StringComparison.Ordinal);
        Assert.Contains("SetCyberDefenseEnabled(bool", source, StringComparison.Ordinal);
        Assert.Contains("Preferences.Default.Remove(CyberDefenseEnabledKey)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MauiProgram_SourceContract_RegistersCyberDefensePreferencesAndSessionState()
    {
        var programPath = GetRepositoryPath("src", "MathFirst.App", "MauiProgram.cs");
        var source = File.ReadAllText(programPath);

        Assert.Contains("AddSingleton<ICyberDefenseModePreferences>", source, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<CyberDefenseSessionState>", source, StringComparison.Ordinal);
    }

    // =========================================================================
    // SCENARIO A: Default mode is enabled when unconfigured
    // =========================================================================

    [Fact]
    public void ScenarioA_DefaultMode_IsEnabledWhenUnconfigured()
    {
        var preferences = new InMemoryCyberDefensePreferences();
        var sessionState = new CyberDefenseSessionState(preferences);

        Assert.True(preferences.GetCyberDefenseEnabled());
        Assert.True(sessionState.IsCyberDefenseEnabled);
    }

    // =========================================================================
    // SCENARIO B: Set disabled, then retrieve disabled
    // =========================================================================

    [Fact]
    public void ScenarioB_SetDisabled_RetrievesDisabled()
    {
        var preferences = new InMemoryCyberDefensePreferences();
        var sessionState = new CyberDefenseSessionState(preferences);

        preferences.SetCyberDefenseEnabled(false);

        Assert.False(preferences.GetCyberDefenseEnabled());
        Assert.False(sessionState.IsCyberDefenseEnabled);
    }

    // =========================================================================
    // SCENARIO C: Re-enabling restores enabled state
    // =========================================================================

    [Fact]
    public void ScenarioC_ReEnabling_RestoresEnabledState()
    {
        var preferences = new InMemoryCyberDefensePreferences();
        var sessionState = new CyberDefenseSessionState(preferences);

        preferences.SetCyberDefenseEnabled(false);
        Assert.False(sessionState.IsCyberDefenseEnabled);

        preferences.SetCyberDefenseEnabled(true);
        Assert.True(sessionState.IsCyberDefenseEnabled);
    }

    // =========================================================================
    // SCENARIO D: A disabled state holder allocates no encounter
    // =========================================================================

    [Fact]
    public void ScenarioD_DisabledStateHolder_AllocatesNoEncounter()
    {
        var preferences = new InMemoryCyberDefensePreferences();
        preferences.SetCyberDefenseEnabled(false);

        var factoryCallCount = 0;
        var sessionState = new CyberDefenseSessionState(preferences, () =>
        {
            factoryCallCount++;
            return new CyberDefenseEncounterState();
        });

        var encounter = sessionState.ActiveEncounter;

        Assert.Null(encounter);
        Assert.False(sessionState.HasActiveEncounter);
        Assert.Equal(0, factoryCallCount);
    }

    // =========================================================================
    // SCENARIO E: The first enabled query lazily creates exactly one encounter
    // =========================================================================

    [Fact]
    public void ScenarioE_FirstEnabledQuery_LazilyCreatesExactlyOneEncounter()
    {
        var preferences = new InMemoryCyberDefensePreferences();
        var factoryCallCount = 0;
        var sessionState = new CyberDefenseSessionState(preferences, () =>
        {
            factoryCallCount++;
            return new CyberDefenseEncounterState();
        });

        Assert.Equal(0, factoryCallCount);
        Assert.False(sessionState.HasActiveEncounter);

        var encounter = sessionState.ActiveEncounter;

        Assert.NotNull(encounter);
        Assert.True(sessionState.HasActiveEncounter);
        Assert.Equal(1, factoryCallCount);
    }

    // =========================================================================
    // SCENARIO F: Repeated enabled queries return the same instance
    // =========================================================================

    [Fact]
    public void ScenarioF_RepeatedEnabledQueries_ReturnSameInstance()
    {
        var preferences = new InMemoryCyberDefensePreferences();
        var factoryCallCount = 0;
        var sessionState = new CyberDefenseSessionState(preferences, () =>
        {
            factoryCallCount++;
            return new CyberDefenseEncounterState();
        });

        var encounter1 = sessionState.ActiveEncounter;
        var encounter2 = sessionState.ActiveEncounter;
        var encounter3 = sessionState.ActiveEncounter;

        Assert.NotNull(encounter1);
        Assert.Same(encounter1, encounter2);
        Assert.Same(encounter2, encounter3);
        Assert.Equal(1, factoryCallCount);
    }

    // =========================================================================
    // SCENARIO G: Disabling and re-enabling preserves encounter identity and combat state
    // =========================================================================

    [Fact]
    public void ScenarioG_DisablingAndReEnabling_PreservesEncounterIdentitySectorEnemyHpAndShields()
    {
        var preferences = new InMemoryCyberDefensePreferences();
        var sessionState = new CyberDefenseSessionState(preferences);

        var initialEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(initialEncounter);

        // Mutate combat state: hit enemy, take damage/shield decrement
        initialEncounter.RecordHit(isCritical: false);
        initialEncounter.RecordIncorrectAnswer();

        var expectedHp = initialEncounter.EnemyHitPoints;
        var expectedShields = initialEncounter.ShieldSegments;
        var expectedSector = initialEncounter.SectorNumber;
        var expectedEnemyIndex = initialEncounter.EnemyIndex;

        // Disable gameplay
        preferences.SetCyberDefenseEnabled(false);
        Assert.False(sessionState.IsCyberDefenseEnabled);
        Assert.Null(sessionState.ActiveEncounter);
        Assert.True(sessionState.HasActiveEncounter);

        // Re-enable gameplay
        preferences.SetCyberDefenseEnabled(true);
        Assert.True(sessionState.IsCyberDefenseEnabled);

        var resumedEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(resumedEncounter);
        Assert.Same(initialEncounter, resumedEncounter);
        Assert.Equal(expectedHp, resumedEncounter.EnemyHitPoints);
        Assert.Equal(expectedShields, resumedEncounter.ShieldSegments);
        Assert.Equal(expectedSector, resumedEncounter.SectorNumber);
        Assert.Equal(expectedEnemyIndex, resumedEncounter.EnemyIndex);
    }

    // =========================================================================
    // SCENARIO H: Full reset clears the held encounter
    // =========================================================================

    [Fact]
    public async Task ScenarioH_FullReset_ClearsHeldEncounter()
    {
        var dbPath = Path.Combine(_tempDirectory, "full_reset_encounter.db");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        var preferences = new InMemoryPreferenceStoreAdapter();
        var sessionState = new CyberDefenseSessionState(preferences);
        var initialEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(initialEncounter);
        initialEncounter.RecordHit(false);

        var idProvider = new TestInstallationIdProvider();
        var cacheCleaner = new TestCacheCleaner();
        var coordinator = new AppResetCoordinator(session, preferences, idProvider, cacheCleaner, sessionState);

        await coordinator.ExecuteFullResetAsync();

        Assert.False(sessionState.HasActiveEncounter);

        var nextEncounter = sessionState.ActiveEncounter;
        Assert.NotNull(nextEncounter);
        Assert.NotSame(initialEncounter, nextEncounter);
        Assert.Equal(CyberDefenseEncounterState.PrototypeEnemyHitPoints, nextEncounter.EnemyHitPoints);
        Assert.Equal(CyberDefenseEncounterState.PrototypeShieldSegments, nextEncounter.ShieldSegments);
    }

    // =========================================================================
    // SCENARIO I: Learning-only reset does not clear the encounter
    // =========================================================================

    [Fact]
    public async Task ScenarioI_LearningOnlyReset_DoesNotClearHeldEncounter()
    {
        var dbPath = Path.Combine(_tempDirectory, "learn_only_reset.db");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        var preferences = new InMemoryCyberDefensePreferences();
        var sessionState = new CyberDefenseSessionState(preferences);
        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        encounter.RecordHit(false);
        var hpBefore = encounter.EnemyHitPoints;

        await session.ResetLearningProgressAsync(startTiming: false);

        Assert.True(sessionState.HasActiveEncounter);
        var activeAfterReset = sessionState.ActiveEncounter;
        Assert.NotNull(activeAfterReset);
        Assert.Same(encounter, activeAfterReset);
        Assert.Equal(hpBefore, activeAfterReset.EnemyHitPoints);
    }



    // =========================================================================
    // SCENARIO J: Preference reset restores default mode without destroying encounter
    // =========================================================================

    [Fact]
    public void ScenarioJ_PreferenceReset_RestoresDefaultMode_WithoutDestroyingPreservedEncounter()
    {
        var preferences = new InMemoryPreferenceStoreAdapter();
        var sessionState = new CyberDefenseSessionState(preferences);

        var encounter = sessionState.ActiveEncounter;
        Assert.NotNull(encounter);
        encounter.RecordHit(false);
        var hp = encounter.EnemyHitPoints;

        // Disable mode
        preferences.SetCyberDefenseEnabled(false);
        Assert.Null(sessionState.ActiveEncounter);

        // Reset preferences (e.g. Restore Default Preferences)
        preferences.ResetPracticePreferences();

        Assert.True(preferences.GetCyberDefenseEnabled());
        Assert.True(sessionState.IsCyberDefenseEnabled);

        var activeAfterPrefReset = sessionState.ActiveEncounter;
        Assert.NotNull(activeAfterPrefReset);
        Assert.Same(encounter, activeAfterPrefReset);
        Assert.Equal(hp, activeAfterPrefReset.EnemyHitPoints);
    }

    // =========================================================================
    // SCENARIO K: No learner progression or persistence mutation occurs when changing mode
    // =========================================================================

    [Fact]
    public async Task ScenarioK_ChangingMode_PerformsZeroLearnerProgressionOrPersistenceMutation()
    {
        var dbPath = Path.Combine(_tempDirectory, "mode_change_isolation.db");
        using var store = new SqliteLearnerStore(dbPath);
        var preferences = new InMemoryPreferenceStoreAdapter();
        var session = new TrainingSession(store, preferenceStore: preferences);
        await session.InitializeAsync(startTiming: false);

        // Commit an answer to establish learner baseline
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        var snapshotBefore = await store.LoadSnapshotAsync();
        var positionBefore = session.Progression.PracticePosition;
        var stageBefore = session.Progression.CurriculumStage;
        var revisionBefore = session.LearnerStateGenerationRevision;
        var itemStatesBeforeCount = session.ItemStates.Count;

        var sessionState = new CyberDefenseSessionState(preferences);

        // Toggle mode disabled then enabled
        preferences.SetCyberDefenseEnabled(false);
        var encDisabled = sessionState.ActiveEncounter;
        Assert.Null(encDisabled);

        preferences.SetCyberDefenseEnabled(true);
        var encEnabled = sessionState.ActiveEncounter;
        Assert.NotNull(encEnabled);

        var snapshotAfter = await store.LoadSnapshotAsync();

        Assert.Equal(snapshotBefore.Revision, snapshotAfter.Revision);
        Assert.Equal(snapshotBefore.ItemStates.Count, snapshotAfter.ItemStates.Count);
        Assert.Equal(positionBefore, session.Progression.PracticePosition);
        Assert.Equal(stageBefore, session.Progression.CurriculumStage);
        Assert.Equal(revisionBefore, session.LearnerStateGenerationRevision);
        Assert.Equal(itemStatesBeforeCount, session.ItemStates.Count);
    }

    // =========================================================================
    // TEST DOUBLES
    // =========================================================================

    private sealed class InMemoryPreferenceStoreAdapter : IPreferenceStore, ICyberDefenseModePreferences
    {
        private bool? _cyberDefenseEnabled;
        public string Language { get; set; } = "system";
        public ThemePreference Theme { get; set; } = ThemePreference.System;
        public NumericKeypadLayout KeypadLayout { get; set; } = NumericKeypadLayout.Numpad;
        public bool HapticFeedbackEnabled { get; set; } = true;
        public PracticeTimeSetting TimeSetting { get; set; } = PracticeTimeSetting.Standard;

        public bool GetCyberDefenseEnabled() => _cyberDefenseEnabled ?? true;
        public void SetCyberDefenseEnabled(bool enabled) => _cyberDefenseEnabled = enabled;

        public string GetLanguagePreference() => Language;
        public void SetLanguagePreference(string preference) => Language = preference;
        public ThemePreference GetThemePreference() => Theme;
        public void SetThemePreference(ThemePreference preference) => Theme = preference;
        public NumericKeypadLayout GetNumericKeypadLayout() => KeypadLayout;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) => KeypadLayout = layout;
        public bool GetHapticFeedbackEnabled() => HapticFeedbackEnabled;
        public void SetHapticFeedbackEnabled(bool enabled) => HapticFeedbackEnabled = enabled;
        public bool GetOperationEnabled(ArithmeticOperation operation) => true;
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) { }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => PracticeOperationPreferencePolicy.AllOperations;
        public PracticeTimeSetting GetPracticeTimeSetting() => TimeSetting;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) => TimeSetting = setting;

        public void ResetPracticePreferences()
        {
            _cyberDefenseEnabled = null;
        }

        public void ResetAllPreferences()
        {
            Language = "system";
            Theme = ThemePreference.System;
            KeypadLayout = NumericKeypadLayout.Numpad;
            HapticFeedbackEnabled = true;
            ResetPracticePreferences();
        }
    }

    private sealed class TestInstallationIdProvider : IInstallationIdProvider
    {
        public void ClearInstallationId() { }
        public string GetOrCreateInstallationId() => "test-id";
    }

    private sealed class TestCacheCleaner : ITelemetryShareCacheCleaner
    {
        public void PurgeShareCache() { }
    }
}
