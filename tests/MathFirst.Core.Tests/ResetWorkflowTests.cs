namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using Xunit;

public sealed class ResetWorkflowTests : IDisposable
{
    private readonly string _testDbDir;

    public ResetWorkflowTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstResetTests_" + Guid.NewGuid().ToString("N"));
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

    private sealed class InMemoryPreferenceStore : IPreferenceStore
    {
        public bool OnboardingCompleted { get; set; }
        public string Language { get; set; } = "system";
        public ThemePreference Theme { get; set; } = ThemePreference.System;

        public bool GetOnboardingCompleted() => OnboardingCompleted;
        public void SetOnboardingCompleted(bool completed) => OnboardingCompleted = completed;
        public string GetLanguagePreference() => Language;
        public void SetLanguagePreference(string preference) => Language = preference;
        public ThemePreference GetThemePreference() => Theme;
        public void SetThemePreference(ThemePreference preference) => Theme = preference;
        public void ResetAllPreferences()
        {
            OnboardingCompleted = false;
            Language = "system";
            Theme = ThemePreference.System;
        }
    }

    [Fact]
    public async Task ResetWorkflow_ResetLearningProgress_PreservesPreferencesAndOnboarding()
    {
        var dbPath = Path.Combine(_testDbDir, "reset_learning.db");
        using var store = new SqliteLearnerStore(dbPath);
        var prefs = new InMemoryPreferenceStore
        {
            OnboardingCompleted = true,
            Language = "de",
            Theme = ThemePreference.Dark
        };

        var session = new TrainingSession(store);
        await session.InitializeAsync();

        // Advance and submit
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        // Perform learning reset
        await session.ResetLearningProgressAsync();

        // Verify DB was reset to Addition 0..1
        Assert.Equal(ArithmeticOperation.Addition, session.Progression.CurrentOperation);
        Assert.Equal(1, session.Progression.CurrentMaxOperand);
        Assert.Empty(session.ItemStates);

        // Verify UI preferences and onboarding were PRESERVED
        Assert.True(prefs.GetOnboardingCompleted());
        Assert.Equal("de", prefs.GetLanguagePreference());
        Assert.Equal(ThemePreference.Dark, prefs.GetThemePreference());
    }

    [Fact]
    public async Task ResetWorkflow_ResetUiPreferences_PreservesLearnerDatabase()
    {
        var dbPath = Path.Combine(_testDbDir, "reset_ui.db");
        using var store = new SqliteLearnerStore(dbPath);
        var prefs = new InMemoryPreferenceStore
        {
            OnboardingCompleted = true,
            Language = "de",
            Theme = ThemePreference.Dark
        };

        var session = new TrainingSession(store);
        await session.InitializeAsync();

        // Advance and submit
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        // Reset UI Preferences
        prefs.SetOnboardingCompleted(false);
        prefs.SetLanguagePreference("system");
        prefs.SetThemePreference(ThemePreference.System);

        // Verify UI prefs reset
        Assert.False(prefs.GetOnboardingCompleted());
        Assert.Equal("system", prefs.GetLanguagePreference());
        Assert.Equal(ThemePreference.System, prefs.GetThemePreference());

        // Verify DB still holds committed progress
        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(2, snapshot.Revision);
        Assert.Single(snapshot.ItemStates);
    }

    [Fact]
    public async Task ResetWorkflow_FullLocalReset_ResetsBothDatabaseAndPreferences()
    {
        var dbPath = Path.Combine(_testDbDir, "reset_full.db");
        using var store = new SqliteLearnerStore(dbPath);
        var prefs = new InMemoryPreferenceStore
        {
            OnboardingCompleted = true,
            Language = "ru",
            Theme = ThemePreference.Dark
        };

        var session = new TrainingSession(store);
        await session.InitializeAsync();
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        // Full reset
        await session.ResetLearningProgressAsync();
        prefs.ResetAllPreferences();

        // Verify DB reset
        Assert.Equal(ArithmeticOperation.Addition, session.Progression.CurrentOperation);
        Assert.Equal(1, session.Progression.CurrentMaxOperand);
        Assert.Empty(session.ItemStates);

        // Verify Prefs reset
        Assert.False(prefs.GetOnboardingCompleted());
        Assert.Equal("system", prefs.GetLanguagePreference());
        Assert.Equal(ThemePreference.System, prefs.GetThemePreference());
    }
}
