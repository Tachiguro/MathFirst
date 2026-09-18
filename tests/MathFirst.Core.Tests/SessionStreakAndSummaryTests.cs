namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class SessionStreakAndSummaryTests
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

        public bool GetOperationEnabled(ArithmeticOperation operation) => true;
        public void SetOperationEnabled(ArithmeticOperation operation, bool enabled) { }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() =>
            PracticeOperationPreferencePolicy.AllOperations;

        public PracticeTimeSetting GetPracticeTimeSetting() =>
            PracticeTimePreferencePolicy.Normalize(_practiceTimeSetting);

        public void SetPracticeTimeSetting(PracticeTimeSetting setting) =>
            _practiceTimeSetting = (int)PracticeTimePreferencePolicy.Normalize((int)setting);

        public void ResetPracticePreferences()
        {
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
        public string StoragePath => "inmemory_streak.db";
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
    // 1. INITIAL SESSION STATE TESTS
    // ============================================================

    [Fact]
    public async Task SessionSummary_InitialState_IsCompletelyZeroAndEmpty()
    {
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        var session = new TrainingSession(store, preferenceStore: prefStore);
        await session.InitializeAsync();

        Assert.Equal(0, session.SessionTotalCount);
        Assert.Equal(0, session.SessionCorrectCount);
        Assert.Equal(0, session.CurrentCorrectStreak);

        var summary = session.GetSessionSummary();
        Assert.Equal(0, summary.CompletedCount);
        Assert.Equal(0, summary.CorrectCount);
        Assert.Equal(0, summary.CurrentCorrectStreak);
        Assert.Null(summary.MedianCorrectLatencyMs);
    }

    // ============================================================
    // 2. STREAK AND SUMMARY SEQUENCE TESTS
    // ============================================================

    [Fact]
    public async Task SessionSummary_CorrectIncorrectSequence_TracksCountsAndStreakCorrectly()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        // 1. First Correct answer (1000 ms)
        clock.AdvanceMs(1000);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        var summary1 = session.GetSessionSummary();
        Assert.Equal(1, summary1.CompletedCount);
        Assert.Equal(1, summary1.CorrectCount);
        Assert.Equal(1, summary1.CurrentCorrectStreak);
        Assert.Equal(1000, summary1.MedianCorrectLatencyMs);

        // Advance to Fact 2
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: true));

        // 2. Second Correct answer (3000 ms)
        clock.AdvanceMs(3000);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        var summary2 = session.GetSessionSummary();
        Assert.Equal(2, summary2.CompletedCount);
        Assert.Equal(2, summary2.CorrectCount);
        Assert.Equal(2, summary2.CurrentCorrectStreak);
        Assert.Equal(2000, summary2.MedianCorrectLatencyMs); // (1000 + 3000) / 2 = 2000

        // Advance to Fact 3
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: true));

        // 3. Third Correct answer (5000 ms)
        clock.AdvanceMs(5000);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        var summary3 = session.GetSessionSummary();
        Assert.Equal(3, summary3.CompletedCount);
        Assert.Equal(3, summary3.CorrectCount);
        Assert.Equal(3, summary3.CurrentCorrectStreak);
        Assert.Equal(3000, summary3.MedianCorrectLatencyMs); // median of [1000, 3000, 5000] = 3000

        // Advance to Fact 4
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: true));

        // 4. Fourth answer: INCORRECT (wrong answer submitted)
        clock.AdvanceMs(2000);
        session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
        await session.CommitCurrentEvaluationAsync();

        var summary4 = session.GetSessionSummary();
        Assert.Equal(4, summary4.CompletedCount);
        Assert.Equal(3, summary4.CorrectCount);
        Assert.Equal(0, summary4.CurrentCorrectStreak); // Reset on error!
        Assert.Equal(3000, summary4.MedianCorrectLatencyMs); // Median still reflects only Correct answers

        // Acknowledge incorrect feedback
        Assert.True(session.AcknowledgeFeedback(startTiming: true));

        // 5. Fifth answer: Correct again (9000 ms)
        clock.AdvanceMs(9000);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        var summary5 = session.GetSessionSummary();
        Assert.Equal(5, summary5.CompletedCount);
        Assert.Equal(4, summary5.CorrectCount);
        Assert.Equal(1, summary5.CurrentCorrectStreak); // Starts new streak at 1
        Assert.Equal(4000, summary5.MedianCorrectLatencyMs); // median of [1000, 3000, 5000, 9000] = 4000
    }

    [Fact]
    public async Task SessionSummary_TimeoutInTimedMode_ResetsStreak()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        // Fact 1: Correct
        clock.AdvanceMs(1500);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(1, session.CurrentCorrectStreak);

        // Advance to Fact 2
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: true));

        // Fact 2: Timeout
        clock.AdvanceMs(session.CurrentFactDeadlineMs + 100);
        session.RecordTimeout();
        await session.CommitCurrentEvaluationAsync();

        var summary = session.GetSessionSummary();
        Assert.Equal(2, summary.CompletedCount);
        Assert.Equal(1, summary.CorrectCount);
        Assert.Equal(0, summary.CurrentCorrectStreak); // Reset on timeout!
        Assert.Equal(1500, summary.MedianCorrectLatencyMs); // Timeout latency is excluded from median correct latency
    }

    [Fact]
    public async Task SessionSummary_NoTimePressure_LongLatencyIncrementsStreakAndParticipatesInMedian()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.NoTimePressure);

        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        // Fact 1: Fast (1200 ms)
        clock.AdvanceMs(1200);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: true));

        // Fact 2: Long No Time Pressure answer (47,800 ms)
        clock.AdvanceSeconds(47.8);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        var summary = session.GetSessionSummary();
        Assert.Equal(2, summary.CompletedCount);
        Assert.Equal(2, summary.CorrectCount);
        Assert.Equal(2, summary.CurrentCorrectStreak);
        Assert.Equal(24500, summary.MedianCorrectLatencyMs); // (1200 + 47800) / 2 = 24500 ms = 24.5 s
    }

    // ============================================================
    // 3. NON-OUTCOME INVARIANCE TESTS
    // ============================================================

    [Fact]
    public async Task SessionSummary_NonOutcomes_DoNotMutateStreakOrCounts()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        // 2 Correct answers to reach streak 2
        for (var i = 0; i < 2; i++)
        {
            clock.AdvanceMs(1500);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            if (i < 1)
            {
                Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: true));
            }
        }
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: true));

        Assert.Equal(2, session.CurrentCorrectStreak);
        Assert.Equal(2, session.SessionTotalCount);
        Assert.Equal(2, session.SessionCorrectCount);

        // 1. Partial/incomplete input
        session.SetCurrentAnswerInput("1");
        Assert.Equal("1", session.CurrentAnswerInput);
        Assert.Equal(2, session.CurrentCorrectStreak);
        Assert.Equal(2, session.SessionTotalCount);

        // 2. Clear input
        session.ClearCurrentAnswerInput();
        Assert.Equal(2, session.CurrentCorrectStreak);

        // 3. Pause and Resume
        session.PausePractice();
        Assert.Equal(PracticeGateState.ManualPause, session.PracticeGate);
        Assert.Equal(2, session.CurrentCorrectStreak);
        Assert.Equal(2, session.SessionTotalCount);

        session.StartOrResumePractice();
        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.Equal(2, session.CurrentCorrectStreak);

        // 4. Background and Foreground
        session.SetAppForeground(false);
        Assert.Equal(2, session.CurrentCorrectStreak);

        session.SetAppForeground(true);
        session.StartOrResumePractice();
        Assert.Equal(2, session.CurrentCorrectStreak);

        // 5. Settings navigation simulation (PauseItemTiming / ResumeItemTiming)
        session.PauseItemTiming();
        Assert.Equal(2, session.CurrentCorrectStreak);
        session.ResumeItemTiming();
        Assert.Equal(2, session.CurrentCorrectStreak);

        // Final summary check
        var summary = session.GetSessionSummary();
        Assert.Equal(2, summary.CompletedCount);
        Assert.Equal(2, summary.CorrectCount);
        Assert.Equal(2, summary.CurrentCorrectStreak);
    }

    [Fact]
    public async Task SessionSummary_TeachingInterventionAcknowledgement_DoesNotMutateStreak()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        // First answer is correct
        clock.AdvanceMs(1000);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(1, session.CurrentCorrectStreak);
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: true));

        // Submit wrong answers until a fact repeats and triggers TeachingIntervention
        var observedTeaching = false;
        for (var step = 0; step < 40; step++)
        {
            var fact = session.CurrentFact;
            var prevCount = session.GetConsecutiveErrorCount(fact.Id);

            session.SubmitAnswer(fact.CorrectResult + 1);
            var persist = await session.CommitCurrentEvaluationAsync();
            Assert.True(persist.IsSuccess);

            if (prevCount == 1)
            {
                Assert.Equal(SessionInteractionState.TeachingIntervention, session.InteractionState);
                observedTeaching = true;
                break;
            }

            Assert.Equal(1, session.GetConsecutiveErrorCount(fact.Id));
            session.AdvanceToNextFact();
        }

        Assert.True(observedTeaching, "Expected teaching intervention on second consecutive error.");

        var totalBeforeAck = session.SessionTotalCount;
        var correctBeforeAck = session.SessionCorrectCount;
        var streakBeforeAck = session.CurrentCorrectStreak;
        Assert.Equal(0, streakBeforeAck);

        // Acknowledge teaching intervention
        Assert.True(session.AcknowledgeTeachingIntervention(startTiming: true));

        // Acknowledging teaching MUST NOT mutate counts or streak
        var summary = session.GetSessionSummary();
        Assert.Equal(totalBeforeAck, summary.CompletedCount);
        Assert.Equal(correctBeforeAck, summary.CorrectCount);
        Assert.Equal(streakBeforeAck, summary.CurrentCorrectStreak);
    }

    // ============================================================
    // 4. MEDIAN & FORMATTING TESTS
    // ============================================================

    [Theory]
    [InlineData(1000.0, "1.0 s")]
    [InlineData(1800.0, "1.8 s")]
    [InlineData(3456.0, "3.5 s")]
    [InlineData(47800.0, "47.8 s")]
    [InlineData(0.0, "—")]
    [InlineData(-100.0, "—")]
    public void Formatting_FormatLatencySeconds_FormatsCorrectly(double ms, string expected)
    {
        var seconds = ms <= 0 ? 0.0 : ms / 1000.0;
        Assert.Equal(expected, LearningPolicy.FormatLatencySeconds(seconds));
    }

    [Fact]
    public void Formatting_FormatLatencySeconds_NullPlaceholder()
    {
        Assert.Equal("—", LearningPolicy.FormatLatencySeconds((long?)null));
        Assert.Equal("N/A", LearningPolicy.FormatLatencySeconds((long?)null, "N/A"));
        Assert.Equal("—", LearningPolicy.FormatLatencySeconds(0L));
    }

    // ============================================================
    // 5. RESET SEMANTICS TESTS
    // ============================================================

    [Fact]
    public async Task SessionSummary_ResetLearningProgress_ResetsStreakAndSummary()
    {
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        var session = new TrainingSession(store, preferenceStore: prefStore);
        await session.InitializeAsync();

        // 3 correct answers
        for (var i = 0; i < 3; i++)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceAfterCorrectAnswer();
        }

        Assert.Equal(3, session.SessionTotalCount);
        Assert.Equal(3, session.SessionCorrectCount);
        Assert.Equal(3, session.CurrentCorrectStreak);

        // Reset learning progress
        await session.ResetLearningProgressAsync();

        Assert.Equal(0, session.SessionTotalCount);
        Assert.Equal(0, session.SessionCorrectCount);
        Assert.Equal(0, session.CurrentCorrectStreak);

        var summary = session.GetSessionSummary();
        Assert.Equal(0, summary.CompletedCount);
        Assert.Equal(0, summary.CorrectCount);
        Assert.Equal(0, summary.CurrentCorrectStreak);
        Assert.Null(summary.MedianCorrectLatencyMs);
    }

    // ============================================================
    // 6. UI MARKUP & LOCALIZATION CONTRACT TESTS
    // ============================================================

    [Fact]
    public void UI_HomeRazor_ContainsRestrainedStreakAndPauseSummary()
    {
        var homeSource = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // Streak badge contract: only shown at streak >= 3
        Assert.Contains("Session.CurrentCorrectStreak >= 3", homeSource, StringComparison.Ordinal);
        Assert.Contains("practice-streak-badge", homeSource, StringComparison.Ordinal);

        // Pause summary contract: shown on ManualPause
        Assert.Contains("Session.PracticeGate == PracticeGateState.ManualPause", homeSource, StringComparison.Ordinal);
        Assert.Contains("pause-session-summary", homeSource, StringComparison.Ordinal);
        Assert.Contains("Training_PauseSummaryCompleted", homeSource, StringComparison.Ordinal);
        Assert.Contains("Training_PauseSummaryCorrect", homeSource, StringComparison.Ordinal);
        Assert.Contains("Training_PauseSummaryStreak", homeSource, StringComparison.Ordinal);
        Assert.Contains("Training_PauseSummaryMedianSpeed", homeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void UI_AppCss_ContainsStreakAndPauseSummaryStyles()
    {
        var cssSource = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "wwwroot", "app.css"));

        Assert.Contains(".practice-streak-badge", cssSource, StringComparison.Ordinal);
        Assert.Contains(".pause-session-summary", cssSource, StringComparison.Ordinal);
        Assert.Contains(".pause-summary-row", cssSource, StringComparison.Ordinal);
        Assert.Contains(".pause-summary-label", cssSource, StringComparison.Ordinal);
        Assert.Contains(".pause-summary-value", cssSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_ParityAcrossLanguagesForStreakAndPauseSummary()
    {
        var localizer = new LocalizationService();

        var requiredKeys = new[]
        {
            "Training_Streak",
            "Training_StreakAriaLabel",
            "Training_PauseSummaryTitle",
            "Training_PauseSummaryCompleted",
            "Training_PauseSummaryCorrect",
            "Training_PauseSummaryStreak",
            "Training_PauseSummaryMedianSpeed"
        };

        foreach (var lang in new[] { "en", "de", "ru" })
        {
            localizer.ApplyLanguagePreference(lang);

            foreach (var key in requiredKeys)
            {
                var value = localizer[key];
                Assert.False(string.IsNullOrWhiteSpace(value), $"Missing translation for {key} in language {lang}");
                Assert.NotEqual(key, value); // Must not return fallback key name
            }
        }
    }
}
