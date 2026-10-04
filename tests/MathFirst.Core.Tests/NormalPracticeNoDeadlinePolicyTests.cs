namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class NormalPracticeNoDeadlinePolicyTests
{
    private static string GetRepositoryPath(params string[] segments) =>
        Path.Combine([GetRepositoryRoot(), .. segments]);

    private static string GetRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private sealed class FakeClock : IClock
    {
        private long _timestamp = 10_000L;

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0L, _timestamp - startTimestamp));

        public void AdvanceMs(long milliseconds)
        {
            _timestamp += milliseconds;
        }

        public void AdvanceSeconds(double seconds)
        {
            _timestamp += (long)(seconds * 1000.0);
        }
    }

    private sealed class InMemoryPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<string, bool> _boolPrefs = new(StringComparer.Ordinal);
        private int _practiceTimeSetting = (int)PracticeTimeSetting.Standard;

        public bool OnboardingCompleted { get; set; }
        public string Language { get; set; } = "system";
        public ThemePreference Theme { get; set; } = ThemePreference.System;
        public NumericKeypadLayout KeypadLayout { get; set; } = NumericKeypadLayout.Numpad;
        public bool HapticFeedbackEnabled { get; set; } = true;

        public bool GetOnboardingCompleted() => OnboardingCompleted;
        public void SetOnboardingCompleted(bool completed) => OnboardingCompleted = completed;
        public string GetLanguagePreference() => Language;
        public void SetLanguagePreference(string preference) => Language = preference;
        public ThemePreference GetThemePreference() => Theme;
        public void SetThemePreference(ThemePreference preference) => Theme = preference;
        public NumericKeypadLayout GetNumericKeypadLayout() => KeypadLayout;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) => KeypadLayout = layout;
        public bool GetHapticFeedbackEnabled() => HapticFeedbackEnabled;
        public void SetHapticFeedbackEnabled(bool enabled) => HapticFeedbackEnabled = enabled;

        public bool GetOperationEnabled(ArithmeticOperation operation) =>
            _boolPrefs.GetValueOrDefault($"op_{operation}", true);

        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) =>
            _boolPrefs[$"op_{operation}"] = enabled;

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

        public PracticeTimeSetting GetPracticeTimeSetting() =>
            PracticeTimePreferencePolicy.Normalize(_practiceTimeSetting);

        public void SetPracticeTimeSetting(PracticeTimeSetting setting) =>
            _practiceTimeSetting = (int)PracticeTimePreferencePolicy.Normalize((int)setting);

        public void ResetPracticePreferences()
        {
            _boolPrefs.Clear();
            _practiceTimeSetting = (int)PracticeTimeSetting.Standard;
        }

        public void ResetAllPreferences()
        {
            OnboardingCompleted = false;
            Language = "system";
            Theme = ThemePreference.System;
            KeypadLayout = NumericKeypadLayout.Numpad;
            HapticFeedbackEnabled = true;
            ResetPracticePreferences();
        }
    }

    private sealed class InMemoryStore : ILearnerStore
    {
        public string StoragePath => "inmemory_slice2.db";
        public LearnerProgression Progression { get; set; } = LearnerProgression.CreateFresh();
        public Dictionary<string, ItemLearningState> Items { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, FsrsCardState> FsrsCards { get; } = new(StringComparer.Ordinal);
        public List<AttemptRecord> Attempts { get; } = [];
        public List<SubmissionChangeSet> CommittedChangeSets { get; } = [];
        public long Revision { get; set; } = 1;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LearnerSnapshot(
                Progression,
                Items,
                FsrsCards,
                Attempts,
                Revision,
                LearnerProgression.DefaultSchemaVersion,
                Progression.OperationProgressions));
        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken cancellationToken = default) =>
            LoadSnapshotAsync(cancellationToken);
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttemptRecord>>([]);
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default)
        {
            CommittedChangeSets.Add(changeSet);
            Revision = changeSet.ExpectedRevision + 1;
            Progression = changeSet.UpdatedProgression;
            Items[changeSet.UpdatedItemState.FactId] = changeSet.UpdatedItemState;
            if (changeSet.UpdatedFsrsState is not null)
            {
                FsrsCards[changeSet.UpdatedFsrsState.FactId] = changeSet.UpdatedFsrsState;
            }
            Attempts.Add(changeSet.Attempt);
            return Task.FromResult(PersistenceResult.Success(Revision));
        }
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default)
        {
            Progression = LearnerProgression.CreateFresh();
            Items.Clear();
            FsrsCards.Clear();
            Attempts.Clear();
            Revision++;
            return Task.CompletedTask;
        }
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    // ============================================================
    // RED-A: ALL NORMAL PRACTICE SETTINGS HAVE NO ENFORCED DEADLINE
    // ============================================================

    [Theory]
    [InlineData(PracticeTimeSetting.Standard)]
    [InlineData(PracticeTimeSetting.NoTimePressure)]
    [InlineData(PracticeTimeSetting.Seconds30)]
    [InlineData(PracticeTimeSetting.Seconds45)]
    [InlineData(PracticeTimeSetting.Seconds60)]
    public void RedA_AllNormalPracticeSettings_HaveNoEnforcedDeadline(PracticeTimeSetting setting)
    {
        Assert.False(PracticeTimePreferencePolicy.HasEnforcedDeadline(setting));
    }

    // ============================================================
    // RED-B: CLOCK PASSAGE ALONE DOES NOT TIME OUT NORMAL PRACTICE
    // ============================================================

    [Theory]
    [InlineData(PracticeTimeSetting.Standard)]
    [InlineData(PracticeTimeSetting.Seconds30)]
    [InlineData(PracticeTimeSetting.Seconds45)]
    [InlineData(PracticeTimeSetting.Seconds60)]
    public async Task RedB_ClockPassageAlone_DoesNotTimeOutNormalPractice(PracticeTimeSetting setting)
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(setting);

        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        var initialFact = session.CurrentFact;
        Assert.NotNull(initialFact);

        // Advance clock past the former deadline threshold
        clock.AdvanceMs(session.CurrentFactDeadlineMs + 1000);

        Assert.True(session.GetCurrentActiveElapsedMs() > session.CurrentFactDeadlineMs);
        Assert.False(session.IsCurrentItemTimedOut());
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.Same(initialFact, session.CurrentFact);
        Assert.Null(session.LastEvaluation);
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Equal(0, session.SessionTotalCount);
        Assert.Empty(store.CommittedChangeSets);
    }

    // ============================================================
    // RED-C: PARTIAL ANSWER SURVIVES FORMER DEADLINE
    // ============================================================

    [Fact]
    public async Task RedC_PartialAnswerInput_SurvivesFormerDeadline()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock: clock);
        await session.InitializeAsync(startTiming: true);

        session.SetCurrentAnswerInput("7");
        Assert.Equal("7", session.CurrentAnswerInput);

        // Advance far past the former deadline
        clock.AdvanceMs(session.CurrentFactDeadlineMs + 5000);

        // Input and state remain intact
        Assert.Equal("7", session.CurrentAnswerInput);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.Null(session.LastEvaluation);
        Assert.False(session.IsCurrentItemTimedOut());

        // Learner can submit correct answer later
        var eval = session.SubmitAnswer(session.CurrentFact!.CorrectResult);
        Assert.Equal(AttemptOutcome.Correct, eval.Outcome);
        Assert.True(eval.IsCorrect);
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);
    }

    // ============================================================
    // RED-D: EXPLICIT TIMEOUT COMPATIBILITY REMAINS
    // ============================================================

    [Fact]
    public async Task RedD_ExplicitTimeoutCompatibility_RemainsAvailableEvenWithNoEnforcedDeadline()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock: clock);
        await session.InitializeAsync(startTiming: true);

        // Explicit call to RecordTimeout for synthetic/compatibility purposes
        var eval = session.RecordTimeout();

        Assert.Equal(AttemptOutcome.Timeout, eval.Outcome);
        Assert.False(eval.IsCorrect);
        Assert.Null(eval.SubmittedAnswer);
        Assert.Equal(SessionInteractionState.TimeoutFeedback, session.InteractionState);

        var commit = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit.IsSuccess);
        Assert.Single(store.CommittedChangeSets);

        var attempt = store.CommittedChangeSets[0].Attempt;
        Assert.Equal(AttemptOutcome.Timeout, attempt.Outcome);
        Assert.False(attempt.IsCorrect);
        Assert.Null(attempt.SubmittedAnswer);
        Assert.False(attempt.IsFluent);
        Assert.Equal(FsrsRating.Again, session.FsrsStates[session.CurrentFact!.Id].LastRating);
    }

    // ============================================================
    // RED-E: PRESENTED DEADLINE MS TRUTHFULNESS
    // ============================================================

    [Fact]
    public async Task RedE_PresentedDeadlineMs_IsNullForNormalPracticeAttempt()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock: clock);
        await session.InitializeAsync(startTiming: true);

        var eval = session.SubmitAnswer(session.CurrentFact!.CorrectResult);

        Assert.NotNull(eval.ChangeSet);
        Assert.Null(eval.ChangeSet.Attempt.PresentedDeadlineMs);
    }

    // ============================================================
    // RED-F: UI AUTOMATIC TIMEOUT ROUTE DISABLED
    // ============================================================

    [Fact]
    public void RedF_HomeNormalPractice_DoesNotWireAutomaticTimeout()
    {
        var homeSource = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        Assert.DoesNotContain("OnTimeout=", homeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleTimeoutAsync", homeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordTimeout", homeSource, StringComparison.Ordinal);
    }
}
