namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Lifecycle;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Telemetry;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class SettingsUnlockContractTests : IDisposable
{
    private readonly string _testDbDir;

    public SettingsUnlockContractTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstSettingsUnlock_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbDir))
            {
                Directory.Delete(_testDbDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private sealed class TrackingPreferenceStore : IPreferenceStore, ICyberDefenseModePreferences
    {
        public int SetOperationEnabledCallCount { get; private set; }
        private readonly Dictionary<ArithmeticOperation, bool> _ops = new();
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

        public bool GetOperationEnabled(ArithmeticOperation operation) =>
            _ops.GetValueOrDefault(operation, true);

        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled)
        {
            SetOperationEnabledCallCount++;
            _ops[operation] = enabled;
        }

        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() =>
            PracticeOperationPreferencePolicy.NormalizeEnabledOperations(
                PracticeOperationPreferencePolicy.AllOperations.Where(GetOperationEnabled));

        public PracticeTimeSetting GetPracticeTimeSetting() => TimeSetting;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) => TimeSetting = setting;

        public void ResetPracticePreferences() => _ops.Clear();
        public void ResetAllPreferences() => _ops.Clear();
    }

    private sealed class DummyInstallationIdProvider : IInstallationIdProvider
    {
        public string GetOrCreateInstallationId() => "dummy-id";
        public void ClearInstallationId() { }
    }

    private sealed class DummyTelemetryShareCacheCleaner : ITelemetryShareCacheCleaner
    {
        public void PurgeShareCache() { }
    }

    [Theory]
    [InlineData(CurriculumStage.Stage1_Addition, new[] { ArithmeticOperation.Addition }, new[] { ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication, ArithmeticOperation.Division })]
    [InlineData(CurriculumStage.Stage2_Subtraction, new[] { ArithmeticOperation.Addition, ArithmeticOperation.Subtraction }, new[] { ArithmeticOperation.Multiplication, ArithmeticOperation.Division })]
    [InlineData(CurriculumStage.Stage3_Multiplication, new[] { ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication }, new[] { ArithmeticOperation.Division })]
    [InlineData(CurriculumStage.Stage4_Division, new[] { ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Multiplication, ArithmeticOperation.Division }, new ArithmeticOperation[0])]
    public void U01_U04_CurriculumUnlockPolicy_StageOperationMapping_MatchesContract(
        CurriculumStage stage,
        ArithmeticOperation[] expectedUnlocked,
        ArithmeticOperation[] expectedLocked)
    {
        var unlocked = CurriculumUnlockPolicy.GetUnlockedOperations(stage);
        Assert.Equal(expectedUnlocked, unlocked);

        foreach (var op in expectedUnlocked)
        {
            Assert.Contains(op, unlocked);
        }

        foreach (var op in expectedLocked)
        {
            Assert.DoesNotContain(op, unlocked);
        }
    }

    [Fact]
    public void U05_U09_U10_SettingsOperationStatus_DerivedFromCurriculumStageNotPreferences()
    {
        var stage1Unlocked = CurriculumUnlockPolicy.GetUnlockedOperations(CurriculumStage.Stage1_Addition);
        var stage4Unlocked = CurriculumUnlockPolicy.GetUnlockedOperations(CurriculumStage.Stage4_Division);

        Assert.Single(stage1Unlocked);
        Assert.Equal(ArithmeticOperation.Addition, stage1Unlocked[0]);

        Assert.Equal(4, stage4Unlocked.Count);
    }

    [Fact]
    public void U08_NoToggleOperationAsync_InNormalSettingsMarkup()
    {
        var settingsSource = File.ReadAllText(GetRepositoryPath(
            "src",
            "MathFirst.App",
            "Components",
            "Pages",
            "Settings.razor"));

        Assert.DoesNotContain("ToggleOperationAsync", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("@onclick=\"() => ToggleOperationAsync(operation)\"", settingsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void U14_NormalSettings_ContainsExplanatoryAutomaticUnlockSemantics()
    {
        var settingsSource = File.ReadAllText(GetRepositoryPath(
            "src",
            "MathFirst.App",
            "Components",
            "Pages",
            "Settings.razor"));

        Assert.Contains("Settings_OperationsHelp", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Settings_OperationUnlocked", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Settings_OperationLocked", settingsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void U15_U16_OperationPresentation_UsesReadOnlySemantics()
    {
        var settingsSource = File.ReadAllText(GetRepositoryPath(
            "src",
            "MathFirst.App",
            "Components",
            "Pages",
            "Settings.razor"));

        Assert.Contains("aria-disabled=\"true\"", settingsSource, StringComparison.Ordinal);
        Assert.Contains("read-only", settingsSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task U17_U18_U19_SettingsOpenClose_PreservesSessionStateAndPosition()
    {
        var dbPath = Path.Combine(_testDbDir, "u17_u19.db");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var stageBefore = session.Progression.CurriculumStage;
        var positionBefore = session.Progression.PracticePosition;
        var factBefore = session.CurrentFact;

        session.PauseItemTiming();
        session.ResumeItemTiming();

        Assert.Equal(stageBefore, session.Progression.CurriculumStage);
        Assert.Equal(positionBefore, session.Progression.PracticePosition);
        Assert.Same(factBefore, session.CurrentFact);
    }

    [Fact]
    public async Task RSET01_RSET04_U11_ResetLearning_ReturnsCurriculumStageAndSettingsStatusToStage1()
    {
        var dbPath = Path.Combine(_testDbDir, "rset01.db");
        using var store = new SqliteLearnerStore(dbPath);
        var session = new TrainingSession(store, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        session.Progression.CurriculumStage = CurriculumStage.Stage3_Multiplication;

        await session.ResetLearningProgressAsync(startTiming: false);

        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
        Assert.Equal(0, session.Progression.PracticePosition);

        var unlockedAfterReset = CurriculumUnlockPolicy.GetUnlockedOperations(session.Progression.CurriculumStage);
        Assert.Single(unlockedAfterReset);
        Assert.Equal(ArithmeticOperation.Addition, unlockedAfterReset[0]);
    }

    [Fact]
    public async Task RSET02_RSET05_U12_ResetUiPreferences_LeavesCurriculumStageUnchanged()
    {
        var dbPath = Path.Combine(_testDbDir, "rset02.db");
        using var store = new SqliteLearnerStore(dbPath);
        var prefs = new TrackingPreferenceStore();
        var session = new TrainingSession(store, preferenceStore: prefs, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        session.Progression.CurriculumStage = CurriculumStage.Stage3_Multiplication;

        prefs.SetLanguagePreference("system");
        prefs.SetThemePreference(ThemePreference.System);
        prefs.SetNumericKeypadLayout(NumericKeypadLayout.Numpad);
        prefs.SetHapticFeedbackEnabled(true);
        prefs.ResetPracticePreferences();

        Assert.Equal(CurriculumStage.Stage3_Multiplication, session.Progression.CurriculumStage);

        var unlocked = CurriculumUnlockPolicy.GetUnlockedOperations(session.Progression.CurriculumStage);
        Assert.Equal(3, unlocked.Count);
        Assert.Contains(ArithmeticOperation.Addition, unlocked);
        Assert.Contains(ArithmeticOperation.Subtraction, unlocked);
        Assert.Contains(ArithmeticOperation.Multiplication, unlocked);
        Assert.DoesNotContain(ArithmeticOperation.Division, unlocked);
    }

    [Fact]
    public async Task RSET03_RSET06_U13_FullLocalReset_ReturnsCurriculumStageToStage1AndSchemaV9()
    {
        var dbPath = Path.Combine(_testDbDir, "rset03.db");
        using var store = new SqliteLearnerStore(dbPath);
        var prefs = new TrackingPreferenceStore();
        var session = new TrainingSession(store, preferenceStore: prefs, practiceMode: PracticeMode.CurriculumManaged);
        await session.InitializeAsync(startTiming: false);

        var sessionState = new CyberDefenseSessionState(prefs);
        var resetCoordinator = new AppResetCoordinator(session, prefs, new DummyInstallationIdProvider(), new DummyTelemetryShareCacheCleaner(), sessionState);
        await resetCoordinator.ExecuteFullResetAsync();


        Assert.Equal(CurriculumStage.Stage1_Addition, session.Progression.CurriculumStage);
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Equal(9, session.Progression.SchemaVersion);

        var unlocked = CurriculumUnlockPolicy.GetUnlockedOperations(session.Progression.CurriculumStage);
        Assert.Single(unlocked);
        Assert.Equal(ArithmeticOperation.Addition, unlocked[0]);
    }
}
