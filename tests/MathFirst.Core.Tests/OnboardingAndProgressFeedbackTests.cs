namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Xunit;

public sealed class OnboardingAndProgressFeedbackTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "MathFirstOnboardingProgress_" + Guid.NewGuid().ToString("N"));

    public OnboardingAndProgressFeedbackTests() => Directory.CreateDirectory(_testDirectory);

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
    public void OnboardingSelection_DefaultsToAllFourAndPersistsAnyNonEmptySubsetForSettings()
    {
        var preferences = new InMemoryPreferenceStore();
        var fresh = new PracticeOperationSelectionDraft(preferences.GetEnabledOperations());

        Assert.Equal(PracticeOperationPreferencePolicy.AllOperations, fresh.EnabledOperations);

        Assert.True(fresh.Toggle(ArithmeticOperation.Subtraction));
        Assert.True(fresh.Toggle(ArithmeticOperation.Multiplication));
        Assert.True(fresh.Toggle(ArithmeticOperation.Division));
        fresh.Save(preferences);

        Assert.Equal([ArithmeticOperation.Addition], preferences.GetEnabledOperations());
        Assert.False(fresh.Toggle(ArithmeticOperation.Addition));
        Assert.Equal([ArithmeticOperation.Addition], fresh.EnabledOperations);

        var twoOperations = new PracticeOperationSelectionDraft(PracticeOperationPreferencePolicy.AllOperations);
        Assert.True(twoOperations.Toggle(ArithmeticOperation.Subtraction));
        Assert.True(twoOperations.Toggle(ArithmeticOperation.Division));
        twoOperations.Save(preferences);

        Assert.Equal(
            [ArithmeticOperation.Addition, ArithmeticOperation.Multiplication],
            preferences.GetEnabledOperations());
    }

    [Fact]
    public void OnboardingSelection_ReconstructionPreservesExistingValidSelection()
    {
        var preferences = new InMemoryPreferenceStore();
        preferences.SetOperationEnabled(ArithmeticOperation.Addition, false);
        preferences.SetOperationEnabled(ArithmeticOperation.Subtraction, true);
        preferences.SetOperationEnabled(ArithmeticOperation.Multiplication, false);
        preferences.SetOperationEnabled(ArithmeticOperation.Division, true);

        var reconstructed = new PracticeOperationSelectionDraft(preferences.GetEnabledOperations());

        Assert.Equal(
            [ArithmeticOperation.Subtraction, ArithmeticOperation.Division],
            reconstructed.EnabledOperations);
    }

    [Fact]
    public void Onboarding_OffersNonEmptyOperationSelectionBackedBySharedPreferences()
    {
        var onboarding = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Onboarding", "OnboardingHost.razor"));

        Assert.Contains("Onboarding_OperationsTitle", onboarding, StringComparison.Ordinal);
        Assert.Contains("PracticeOperationSelectionDraft", onboarding, StringComparison.Ordinal);
        Assert.Contains("PreferenceStore.GetEnabledOperations()", onboarding, StringComparison.Ordinal);
        Assert.Contains("_operationSelection.Save(PreferenceStore)", onboarding, StringComparison.Ordinal);
        Assert.DoesNotContain("onboarding.operation", onboarding, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadyGate_ShowsAuthoritativeEnabledOnlyProgressForReturningLearners()
    {
        var home = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        Assert.Contains("HasCompletedPracticeHistory", home, StringComparison.Ordinal);
        Assert.Contains("ready-progress-overview", home, StringComparison.Ordinal);
        Assert.Contains("Training_CurrentProgress", home, StringComparison.Ordinal);
        Assert.Contains("OperationProgress", home, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionCheckIn_ShowsCompletedCountsAndActualProgressionChanges()
    {
        var summary = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.Application", "Practice", "PracticeCheckInSummary.cs"));
        var home = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        Assert.Contains("ProgressionChanges", summary, StringComparison.Ordinal);
        Assert.Contains("Training_CheckInCompleted", home, StringComparison.Ordinal);
        Assert.Contains("Training_CheckInProgressChange", home, StringComparison.Ordinal);
    }

    [Fact]
    public void NewProductCopy_HasEnglishGermanAndRussianParity()
    {
        var localizer = new LocalizationService();
        var keys = new[]
        {
            "Training_CurrentProgress",
            "Training_CheckInCompleted",
            "Training_CheckInProgressMade",
            "Training_CheckInProgressChange",
            "Onboarding_OperationsTitle",
            "Onboarding_OperationsDescription",
            "Onboarding_OperationsMinimum"
        };

        foreach (var language in new[] { "en", "de", "ru" })
        {
            localizer.ApplyLanguagePreference(language);
            foreach (var key in keys)
            {
                Assert.NotEqual(key, localizer[key]);
                Assert.False(string.IsNullOrWhiteSpace(localizer[key]));
            }
        }
    }

    [Fact]
    public void OperationChoicesAndHud_HaveAccessibleStateAndNarrowTwoColumnContracts()
    {
        var onboarding = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Onboarding", "OnboardingHost.razor"));
        var home = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Home.razor"));
        var styles = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "wwwroot", "app.css"));

        Assert.Contains("aria-pressed=\"@isSelected\"", onboarding, StringComparison.Ordinal);
        Assert.Contains("operation-choice-state", onboarding, StringComparison.Ordinal);
        Assert.Contains("onboarding-operation-grid", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("style=\"grid-template-columns: repeat(@Math.Max", home, StringComparison.Ordinal);
        Assert.Contains("data-operation-count=\"@OperationProgress.Count\"", home, StringComparison.Ordinal);
        Assert.Contains(".operation-progress-hud[data-operation-count=\"3\"]", styles, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr));", styles, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturningHistory_ComesFromAcceptedLearnerActivityAndSurvivesColdReload()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var firstSession = new TrainingSession(store, new FixedClock());
        await firstSession.InitializeAsync();

        Assert.False(firstSession.HasCompletedPracticeHistory);

        firstSession.SubmitAnswer(firstSession.CurrentFact.CorrectResult);
        Assert.True((await firstSession.CommitCurrentEvaluationAsync()).IsSuccess);
        Assert.True(firstSession.HasCompletedPracticeHistory);

        var coldSession = new TrainingSession(store, new FixedClock());
        await coldSession.InitializeAsync();

        Assert.True(coldSession.HasCompletedPracticeHistory);
    }

    [Fact]
    public void ReadyProgress_IsEmptyForFreshLearnerAndUsesEnabledCanonicalStagesWithoutMutation()
    {
        var progression = LearnerProgression.CreateFresh();
        progression.OperationProgressions = progression.OperationProgressions.ToDictionary(
            pair => pair.Key,
            pair => pair.Key == ArithmeticOperation.Multiplication
                ? new OperationProgression(pair.Key, 2, pair.Value.BandStartedPracticePosition)
                : pair.Value);
        var disabledDivision = progression.OperationProgressions[ArithmeticOperation.Division];
        var curriculum = new ArithmeticCurriculum();

        Assert.Empty(PracticeProgressOverview.Create(
            progression,
            curriculum,
            [ArithmeticOperation.Addition, ArithmeticOperation.Multiplication],
            hasCompletedPracticeHistory: false));

        var returning = PracticeProgressOverview.Create(
            progression,
            curriculum,
            [ArithmeticOperation.Addition, ArithmeticOperation.Multiplication],
            hasCompletedPracticeHistory: true);

        Assert.Equal(
            [ArithmeticOperation.Addition, ArithmeticOperation.Multiplication],
            returning.Select(progress => progress.Operation));
        Assert.Equal([1, 3], returning.Select(progress => progress.PresentationStage));
        Assert.Same(disabledDivision, progression.OperationProgressions[ArithmeticOperation.Division]);
    }

    [Fact]
    public async Task SessionSummary_CountsCorrectIncorrectAndTimeoutAttemptsExactlyOnce()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FixedClock());
        await session.InitializeAsync();

        await CompleteAttemptsAsync(session, 10, AttemptOutcome.Correct);
        await CompleteAttemptsAsync(session, 5, AttemptOutcome.Incorrect);
        await CompleteAttemptsAsync(session, 5, AttemptOutcome.Timeout);

        Assert.NotNull(session.PendingCheckIn);
        Assert.Equal(20, session.PendingCheckIn.TotalCount);
        Assert.Equal(10, session.PendingCheckIn.CorrectCount);
    }

    [Fact]
    public async Task SessionSummary_ReportsOnlyActualPracticedOperationAdvancement()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var preferences = InMemoryPreferenceStore.WithEnabled([ArithmeticOperation.Multiplication]);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync();

        await CompleteAttemptsAsync(session, 20, AttemptOutcome.Correct);

        var change = Assert.Single(session.PendingCheckIn!.ProgressionChanges);
        Assert.Equal(ArithmeticOperation.Multiplication, change.Operation);
        Assert.Equal(1, change.FromStage);
        Assert.True(change.ToStage > change.FromStage);
    }

    [Fact]
    public async Task ContinuePractice_ResetsProgressionBaselineForNextSummarySegment()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var preferences = InMemoryPreferenceStore.WithEnabled([ArithmeticOperation.Multiplication]);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync();
        await CompleteAttemptsAsync(session, 20, AttemptOutcome.Correct);
        Assert.NotEmpty(session.PendingCheckIn!.ProgressionChanges);

        await EnterCheckInAndContinueAsync(session);
        await CompleteAttemptsAsync(session, 20, AttemptOutcome.Incorrect);

        Assert.Empty(session.PendingCheckIn!.ProgressionChanges);
        Assert.Equal(20, session.PendingCheckIn.TotalCount);
        Assert.Equal(0, session.PendingCheckIn.CorrectCount);
    }

    [Fact]
    public async Task TakeBreak_ResetsProgressionBaselineBeforeLaterPractice()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var preferences = InMemoryPreferenceStore.WithEnabled([ArithmeticOperation.Multiplication]);
        var session = new TrainingSession(store, new FixedClock(), preferenceStore: preferences);
        await session.InitializeAsync();
        await CompleteAttemptsAsync(session, 20, AttemptOutcome.Correct);
        Assert.NotEmpty(session.PendingCheckIn!.ProgressionChanges);

        Assert.False(await session.AdvanceAfterCorrectAnswerAsync());
        await session.TakeBreakAsync();
        session.StartOrResumePractice();
        await CompleteAttemptsAsync(session, 20, AttemptOutcome.Incorrect);

        Assert.Empty(session.PendingCheckIn!.ProgressionChanges);
        Assert.Equal(20, session.PendingCheckIn.TotalCount);
    }

    [Fact]
    public async Task RepeatingACommittedSubmissionCannotDuplicateSessionOrPersistentCounts()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var session = new TrainingSession(store, new FixedClock());
        await session.InitializeAsync();
        session.SubmitAnswer(session.CurrentFact.CorrectResult);

        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(1, session.SessionTotalCount);
        Assert.Equal(1, session.SessionCorrectCount);
        Assert.Single(snapshot.RecentAttempts);
    }

    [Fact]
    public async Task ColdStart_BeginsFreshSummaryWindowInsteadOfRestoringNineteenAttempts()
    {
        using var store = new SqliteLearnerStore(GetTempDbPath());
        var firstSession = new TrainingSession(store, new FixedClock());
        await firstSession.InitializeAsync();
        await CompleteAttemptsAsync(firstSession, 19, AttemptOutcome.Correct);

        var coldSession = new TrainingSession(store, new FixedClock());
        await coldSession.InitializeAsync();
        await CompleteAttemptsAsync(coldSession, 1, AttemptOutcome.Correct);

        Assert.Null(coldSession.PendingCheckIn);
        Assert.Equal(1, coldSession.SessionTotalCount);
        Assert.Equal(1, coldSession.SessionCorrectCount);
    }

    private static async Task EnterCheckInAndContinueAsync(TrainingSession session)
    {
        Assert.False(await session.AdvanceAfterCorrectAnswerAsync());
        Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);
        await session.ContinuePracticeAsync();
    }

    private static async Task CompleteAttemptsAsync(
        TrainingSession session,
        int count,
        AttemptOutcome outcome)
    {
        for (var index = 0; index < count; index++)
        {
            if (outcome == AttemptOutcome.Timeout)
            {
                session.RecordTimeout();
            }
            else
            {
                var answer = outcome == AttemptOutcome.Correct
                    ? session.CurrentFact.CorrectResult
                    : session.CurrentFact.CorrectResult + 1;
                session.SubmitAnswer(answer);
            }

            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
            if (session.PendingCheckIn is not null)
            {
                continue;
            }

            if (session.InteractionState == SessionInteractionState.CorrectFeedback)
            {
                Assert.True(await session.AdvanceAfterCorrectAnswerAsync());
            }
            else if (session.InteractionState == SessionInteractionState.TeachingIntervention)
            {
                Assert.True(await session.AcknowledgeTeachingInterventionAsync());
            }
            else
            {
                Assert.True(await session.AcknowledgeFeedbackAsync());
            }
        }
    }

    private string GetTempDbPath() => Path.Combine(_testDirectory, $"test_{Guid.NewGuid():N}.db");

    private sealed class FixedClock : IClock
    {
        public long GetTimestamp() => 1_000_000;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(500);
    }

    private sealed class InMemoryPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<ArithmeticOperation, bool> _operations = [];

        public static InMemoryPreferenceStore WithEnabled(IEnumerable<ArithmeticOperation> enabled)
        {
            var store = new InMemoryPreferenceStore();
            var enabledSet = enabled.ToHashSet();
            foreach (var operation in PracticeOperationPreferencePolicy.AllOperations)
            {
                store.SetOperationEnabled(operation, enabledSet.Contains(operation));
            }
            return store;
        }

        public bool GetOnboardingCompleted() => false;
        public void SetOnboardingCompleted(bool completed) { }
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public string GetLanguagePreference() => LanguagePreferencePolicy.SystemPreferenceCode;
        public void SetLanguagePreference(string languageCode) { }
        public bool GetOperationEnabled(ArithmeticOperation operation) => _operations.GetValueOrDefault(operation, true);
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) => _operations[operation] = enabled;
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() =>
            PracticeOperationPreferencePolicy.NormalizeEnabledOperations(
                PracticeOperationPreferencePolicy.AllOperations.Where(GetOperationEnabled));
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public void ResetPracticePreferences() => _operations.Clear();
        public void ResetAllPreferences() => _operations.Clear();
    }

    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
