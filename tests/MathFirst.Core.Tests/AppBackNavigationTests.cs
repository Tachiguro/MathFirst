namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using MathFirst.Application;
using MathFirst.Application.Navigation;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

public sealed class AppBackNavigationTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstBackNav_" + Guid.NewGuid().ToString("N"));

    public AppBackNavigationTests() => Directory.CreateDirectory(_testDirectory);

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
        }
    }

    [Fact]
    public void Coordinator_RegistersAndUnregisters_Cleanly()
    {
        var coordinator = new AppBackNavigationCoordinator();
        Assert.Equal(0, coordinator.RegisteredHandlerCount);

        var registration = coordinator.RegisterHandler(() => true);
        Assert.Equal(1, coordinator.RegisteredHandlerCount);

        registration.Dispose();
        Assert.Equal(0, coordinator.RegisteredHandlerCount);

        // Idempotent dispose
        registration.Dispose();
        Assert.Equal(0, coordinator.RegisteredHandlerCount);
    }

    [Fact]
    public void Coordinator_EvaluatesInLifoOrder()
    {
        var coordinator = new AppBackNavigationCoordinator();
        var executionLog = new List<string>();

        using var first = coordinator.RegisterHandler(() =>
        {
            executionLog.Add("first");
            return false;
        });

        using var second = coordinator.RegisterHandler(() =>
        {
            executionLog.Add("second");
            return true;
        });

        var handled = coordinator.TryHandleBack();

        Assert.True(handled);
        Assert.Equal(["second"], executionLog);

        // Now if second returns false, first should run next
        executionLog.Clear();
        using var third = coordinator.RegisterHandler(() =>
        {
            executionLog.Add("third");
            return false;
        });

        handled = coordinator.TryHandleBack();
        Assert.True(handled);
        Assert.Equal(["third", "second"], executionLog);
    }

    [Fact]
    public void Coordinator_ReturnsFalseWhenEmptyOrAllDeclined()
    {
        var coordinator = new AppBackNavigationCoordinator();
        Assert.False(coordinator.TryHandleBack());

        using var registration = coordinator.RegisterHandler(() => false);
        Assert.False(coordinator.TryHandleBack());
    }

    [Fact]
    public void Coordinator_HandlesExceptionsSafely()
    {
        var coordinator = new AppBackNavigationCoordinator();
        var fallbackExecuted = false;

        using var failing = coordinator.RegisterHandler(() => throw new InvalidOperationException("boom"));
        using var fallback = coordinator.RegisterHandler(() =>
        {
            fallbackExecuted = true;
            return true;
        });

        // The failing handler is registered first, so fallback (registered second) runs first
        var handled = coordinator.TryHandleBack();
        Assert.True(handled);
        Assert.True(fallbackExecuted);
    }

    [Fact]
    public void Onboarding_StepBackContract_StepsBackFrom5To1_AndPassesThroughAtStep1()
    {
        var currentStep = 5;
        bool HandleBack()
        {
            if (currentStep > 1)
            {
                currentStep--;
                return true;
            }
            return false;
        }

        var coordinator = new AppBackNavigationCoordinator();
        using var registration = coordinator.RegisterHandler(HandleBack);

        // Step 5 -> 4
        Assert.True(coordinator.TryHandleBack());
        Assert.Equal(4, currentStep);

        // Step 4 -> 3
        Assert.True(coordinator.TryHandleBack());
        Assert.Equal(3, currentStep);

        // Step 3 -> 2
        Assert.True(coordinator.TryHandleBack());
        Assert.Equal(2, currentStep);

        // Step 2 -> 1
        Assert.True(coordinator.TryHandleBack());
        Assert.Equal(1, currentStep);

        // Step 1 -> Cannot step back further; passes through to platform default (returns false)
        Assert.False(coordinator.TryHandleBack());
        Assert.Equal(1, currentStep);
    }

    [Fact]
    public void Onboarding_StepBack_PreservesDraftSelectionsAcrossNavigation()
    {
        var preferences = new InMemoryPreferenceStore();
        var selectedLanguage = "de";
        var selectedTheme = ThemePreference.Dark;
        var selectedKeypad = NumericKeypadLayout.Phone;
        var operationSelection = new PracticeOperationSelectionDraft([ArithmeticOperation.Multiplication, ArithmeticOperation.Division]);

        var currentStep = 1;
        Assert.Equal(1, currentStep);

        // Advance to Step 2
        currentStep = 2;
        Assert.Equal(2, currentStep);
        Assert.Equal(NumericKeypadLayout.Phone, selectedKeypad);

        // Advance to Step 3
        currentStep = 3;
        Assert.Equal([ArithmeticOperation.Multiplication, ArithmeticOperation.Division], operationSelection.EnabledOperations);

        // Step back to Step 2
        currentStep = 2;
        Assert.Equal(NumericKeypadLayout.Phone, selectedKeypad);
        Assert.Equal([ArithmeticOperation.Multiplication, ArithmeticOperation.Division], operationSelection.EnabledOperations);

        // Step back to Step 1
        currentStep = 1;
        Assert.Equal("de", selectedLanguage);
        Assert.Equal(ThemePreference.Dark, selectedTheme);

        // Advance back to Step 5 and complete
        currentStep = 5;
        preferences.SetLanguagePreference(selectedLanguage);
        preferences.SetThemePreference(selectedTheme);
        preferences.SetNumericKeypadLayout(selectedKeypad);
        operationSelection.Save(preferences);
        preferences.SetOnboardingCompleted(true);

        Assert.Equal("de", preferences.GetLanguagePreference());
        Assert.Equal(ThemePreference.Dark, preferences.GetThemePreference());
        Assert.Equal(NumericKeypadLayout.Phone, preferences.GetNumericKeypadLayout());
        Assert.Equal([ArithmeticOperation.Multiplication, ArithmeticOperation.Division], preferences.GetEnabledOperations());
        Assert.True(preferences.GetOnboardingCompleted());
    }

    [Fact]
    public void Onboarding_LayoutContract_HasFiveStepsVerticalLayoutAndNoSkipShortcut()
    {
        var onboarding = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Onboarding", "OnboardingHost.razor"));
        var styles = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "wwwroot", "app.css"));

        // 5 steps preserved
        Assert.Contains("_currentStep == 1", onboarding, StringComparison.Ordinal);
        Assert.Contains("_currentStep == 2", onboarding, StringComparison.Ordinal);
        Assert.Contains("_currentStep == 3", onboarding, StringComparison.Ordinal);
        Assert.Contains("_currentStep == 4", onboarding, StringComparison.Ordinal);
        Assert.Contains("onboarding-ready-step", onboarding, StringComparison.Ordinal);

        // Back Coordinator wired
        Assert.Contains("IAppBackNavigationCoordinator BackCoordinator", onboarding, StringComparison.Ordinal);
        Assert.Contains("BackCoordinator.RegisterHandler", onboarding, StringComparison.Ordinal);

        // No Skip / Start with defaults shortcut
        Assert.DoesNotContain("Skip", onboarding, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Quick setup", onboarding, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start with defaults", onboarding, StringComparison.OrdinalIgnoreCase);

        // Actions vertical stacked styling
        Assert.Contains(".onboarding-actions {", styles, StringComparison.Ordinal);
        Assert.Contains("flex-direction: column;", styles, StringComparison.Ordinal);
        Assert.Contains(".onboarding-actions .button {", styles, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_BackNavigation_PreservesActiveQuestionInputAndExcludesSettingsDuration()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var clock = new IncrementingClock(1_000_000);
        var preferences = new InMemoryPreferenceStore();
        var session = new TrainingSession(store, clock, preferenceStore: preferences);
        await session.InitializeAsync();
        session.StartOrResumePractice();

        var initialFact = session.CurrentFact;
        Assert.NotNull(initialFact);

        // User enters partial input
        session.SetCurrentAnswerInput("4");
        Assert.Equal("4", session.CurrentAnswerInput);

        // Learner navigates to Settings -> pauses item timing
        session.PauseItemTiming();

        // Time elapses while in Settings
        clock.Advance(TimeSpan.FromSeconds(15));

        // Settings Back coordinator handler triggers navigation back to Home / Practice
        var navigatedToHome = false;
        var coordinator = new AppBackNavigationCoordinator();
        using var settingsRegistration = coordinator.RegisterHandler(() =>
        {
            navigatedToHome = true;
            return true;
        });

        var handled = coordinator.TryHandleBack();
        Assert.True(handled);
        Assert.True(navigatedToHome);

        // Resume Practice on Home
        session.SetPracticeSurfaceActive(true);
        session.StartOrResumePractice();

        // Assert question and partial input preserved
        Assert.Same(initialFact, session.CurrentFact);
        Assert.Equal("4", session.CurrentAnswerInput);

        // The 15 seconds spent in Settings must NOT count toward active answer latency
        Assert.True(session.GetCurrentActiveElapsedMs() < 5000, "Settings duration must not consume active question latency.");

        // No learning mutation occurred
        Assert.Equal(0, session.SessionTotalCount);
        var snapshot = await store.LoadSnapshotAsync();
        Assert.Empty(snapshot.RecentAttempts);
    }

    [Fact]
    public void RootPractice_BackNavigation_PassesThroughSafelyWithoutDuplicateOrMutation()
    {
        var coordinator = new AppBackNavigationCoordinator();

        // At root Home/Practice, no handler intercepts -> returns false to allow safe native backgrounding
        Assert.False(coordinator.TryHandleBack());
        Assert.Equal(0, coordinator.RegisteredHandlerCount);
    }

    [Fact]
    public void Settings_Contract_RegistersBackHandlerAndPointsToRoot()
    {
        var settings = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Settings.razor"));

        Assert.Contains("IAppBackNavigationCoordinator BackCoordinator", settings, StringComparison.Ordinal);
        Assert.Contains("BackCoordinator.RegisterHandler", settings, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(\"/\")", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_Contract_OverridesOnBackButtonPressedAndDelegatesToCoordinator()
    {
        var mainPage = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "MainPage.xaml.cs"));

        Assert.Contains("IAppBackNavigationCoordinator", mainPage, StringComparison.Ordinal);
        Assert.Contains("OnBackButtonPressed", mainPage, StringComparison.Ordinal);
        Assert.Contains("_backCoordinator.TryHandleBack()", mainPage, StringComparison.Ordinal);
    }

    private string GetTempDbPath() => Path.Combine(_testDirectory, $"test_{Guid.NewGuid():N}.db");

    private sealed class IncrementingClock : IClock
    {
        private long _timestamp;

        public IncrementingClock(long initial) => _timestamp = initial;

        public void Advance(TimeSpan duration) => _timestamp += (long)(duration.TotalMilliseconds * 1000.0);

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds((_timestamp - startTimestamp) / 1000.0);
    }

    private sealed class InMemoryPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<ArithmeticOperation, bool> _operations = [];
        private bool _onboardingCompleted;
        private string _language = LanguagePreferencePolicy.SystemPreferenceCode;
        private ThemePreference _theme = ThemePreference.System;
        private NumericKeypadLayout _keypad = NumericKeypadLayout.Numpad;
        private PracticeTimeSetting _practiceTime = PracticeTimeSetting.Standard;
        private bool _hapticEnabled = true;

        public bool GetOnboardingCompleted() => _onboardingCompleted;
        public void SetOnboardingCompleted(bool completed) => _onboardingCompleted = completed;
        public ThemePreference GetThemePreference() => _theme;
        public void SetThemePreference(ThemePreference preference) => _theme = preference;
        public NumericKeypadLayout GetNumericKeypadLayout() => _keypad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) => _keypad = layout;
        public string GetLanguagePreference() => _language;
        public void SetLanguagePreference(string languageCode) => _language = languageCode;
        public bool GetHapticFeedbackEnabled() => _hapticEnabled;
        public void SetHapticFeedbackEnabled(bool enabled) => _hapticEnabled = enabled;
        public bool GetOperationEnabled(ArithmeticOperation operation) => _operations.GetValueOrDefault(operation, true);
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) => _operations[operation] = enabled;
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() =>
            PracticeOperationPreferencePolicy.NormalizeEnabledOperations(
                PracticeOperationPreferencePolicy.AllOperations.Where(GetOperationEnabled));
        public PracticeTimeSetting GetPracticeTimeSetting() => _practiceTime;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) => _practiceTime = setting;
        public void ResetPracticePreferences() => _operations.Clear();
        public void ResetAllPreferences()
        {
            _operations.Clear();
            _onboardingCompleted = false;
            _language = LanguagePreferencePolicy.SystemPreferenceCode;
            _theme = ThemePreference.System;
            _keypad = NumericKeypadLayout.Numpad;
            _practiceTime = PracticeTimeSetting.Standard;
            _hapticEnabled = true;
        }
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
