namespace MathFirst.Core.Tests;

using System.Globalization;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class NoTimePressureModeTests
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
        public string StoragePath => "inmemory_notimepressure.db";
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
    // 1. POLICY & NORMALIZATION TESTS
    // ============================================================

    [Theory]
    [InlineData(0, PracticeTimeSetting.Standard)]
    [InlineData(-1, PracticeTimeSetting.NoTimePressure)]
    [InlineData(30, PracticeTimeSetting.Seconds30)]
    [InlineData(45, PracticeTimeSetting.Seconds45)]
    [InlineData(60, PracticeTimeSetting.Seconds60)]
    [InlineData(-2, PracticeTimeSetting.Standard)]
    [InlineData(999, PracticeTimeSetting.Standard)]
    public void Policy_NormalizesRawValuesSafely(int rawValue, PracticeTimeSetting expected)
    {
        Assert.Equal(expected, PracticeTimePreferencePolicy.Normalize(rawValue));
        Assert.Equal(expected, PracticeTimePreferencePolicy.Normalize((PracticeTimeSetting)rawValue));
    }

    [Fact]
    public void Policy_DefaultIsStandard()
    {
        Assert.Equal(PracticeTimeSetting.Standard, PracticeTimePreferencePolicy.Default);
    }

    [Theory]
    [InlineData(PracticeTimeSetting.Standard, true, false)]
    [InlineData(PracticeTimeSetting.NoTimePressure, false, true)]
    [InlineData(PracticeTimeSetting.Seconds30, true, false)]
    [InlineData(PracticeTimeSetting.Seconds45, true, false)]
    [InlineData(PracticeTimeSetting.Seconds60, true, false)]
    public void Policy_SemanticProperties(
        PracticeTimeSetting setting,
        bool expectedHasEnforcedDeadline,
        bool expectedIsNoTimePressure)
    {
        Assert.Equal(expectedHasEnforcedDeadline, PracticeTimePreferencePolicy.HasEnforcedDeadline(setting));
        Assert.Equal(expectedIsNoTimePressure, PracticeTimePreferencePolicy.IsNoTimePressure(setting));
    }

    [Fact]
    public void Policy_DeadlineFloorIsZeroForNoTimePressure()
    {
        Assert.Equal(0L, PracticeTimePreferencePolicy.GetDeadlineFloorMs(PracticeTimeSetting.NoTimePressure));
    }

    [Fact]
    public void Policy_ResetPreferencesRestoresStandard()
    {
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.NoTimePressure);
        Assert.Equal(PracticeTimeSetting.NoTimePressure, prefStore.GetPracticeTimeSetting());

        prefStore.ResetPracticePreferences();
        Assert.Equal(PracticeTimeSetting.Standard, prefStore.GetPracticeTimeSetting());

        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.NoTimePressure);
        prefStore.ResetAllPreferences();
        Assert.Equal(PracticeTimeSetting.Standard, prefStore.GetPracticeTimeSetting());
    }

    // ============================================================
    // 2. TIMING & DEADLINE IMMUNITY TESTS
    // ============================================================

    [Fact]
    public async Task Timing_NoTimePressure_HasNoEnforcedDeadlineAndElapsedAccumulates()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.NoTimePressure);

        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        Assert.False(session.HasEnforcedDeadline);
        Assert.True(session.IsNoTimePressure);
        Assert.Equal(PracticeTimeSetting.NoTimePressure, session.CurrentPracticeTimeSetting);
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());

        // Advance 5 seconds
        clock.AdvanceSeconds(5.0);
        Assert.Equal(5000, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsCurrentItemTimedOut());

        // Advance past standard adaptive deadline (e.g. 15s / 30s)
        clock.AdvanceSeconds(25.0);
        Assert.Equal(30000, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsCurrentItemTimedOut());
        Assert.False(session.IsCurrentItemTimedOut(30.0));

        // Advance to 60s and beyond
        clock.AdvanceSeconds(30.0);
        Assert.Equal(60000, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsCurrentItemTimedOut());

        clock.AdvanceSeconds(60.0);
        Assert.Equal(120000, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsCurrentItemTimedOut());
    }

    [Fact]
    public async Task Timing_NoTimePressure_PauseSettingsAndBackgroundFreezeElapsed()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.NoTimePressure);

        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        clock.AdvanceSeconds(10.0);
        Assert.Equal(10000, session.GetCurrentActiveElapsedMs());

        // 1. Pause
        session.PausePractice();
        Assert.False(session.IsTimingActive);
        clock.AdvanceSeconds(20.0);
        Assert.Equal(10000, session.GetCurrentActiveElapsedMs());

        session.StartOrResumePractice();
        Assert.True(session.IsTimingActive);
        clock.AdvanceSeconds(5.0);
        Assert.Equal(15000, session.GetCurrentActiveElapsedMs());

        // 2. Settings navigation (PracticeSurfaceActive = false)
        session.PauseItemTiming();
        Assert.False(session.IsTimingActive);
        clock.AdvanceSeconds(45.0);
        Assert.Equal(15000, session.GetCurrentActiveElapsedMs());

        session.ResumeItemTiming();
        Assert.True(session.IsTimingActive);
        clock.AdvanceSeconds(7.0);
        Assert.Equal(22000, session.GetCurrentActiveElapsedMs());

        // 3. App Backgrounding
        session.SetAppForeground(false);
        Assert.False(session.IsTimingActive);
        clock.AdvanceSeconds(100.0);
        Assert.Equal(22000, session.GetCurrentActiveElapsedMs());

        session.SetAppForeground(true);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, session.PracticeGate);
        session.StartOrResumePractice();
        Assert.True(session.IsTimingActive);
        clock.AdvanceSeconds(3.0);
        Assert.Equal(25000, session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public async Task Timing_NoTimePressure_RecordTimeoutThrowsInvalidOperationException()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.NoTimePressure);

        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        clock.AdvanceSeconds(45.0);

        Assert.Throws<InvalidOperationException>(() => session.RecordTimeout());
        Assert.Throws<InvalidOperationException>(() => session.SubmitTimeout());
    }

    [Fact]
    public async Task Timing_NoTimePressure_LongLatencyAnswerEvaluatedNormallyAndPersisted()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.NoTimePressure);

        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        // Learner takes 47.8 seconds in No Time Pressure
        clock.AdvanceSeconds(47.8);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        Assert.Equal(AttemptOutcome.Correct, eval.Outcome);
        Assert.True(eval.IsCorrect);
        Assert.Equal(47800, eval.LatencyMs);
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);

        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(commitResult.IsSuccess);

        Assert.Single(store.CommittedChangeSets);
        var committedAttempt = store.CommittedChangeSets[0].Attempt;
        Assert.Equal(47800, committedAttempt.ResponseLatencyMs);
        Assert.Equal(AttemptOutcome.Correct, committedAttempt.Outcome);
        Assert.False(committedAttempt.IsFluent); // 47.8s > fluency threshold, so non-fluent
    }

    // ============================================================
    // 3. LEARNING EVIDENCE & FLUENCY TESTS
    // ============================================================

    [Fact]
    public async Task Learning_FastAndSlowResponsesClassifiedAccurately()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.NoTimePressure);

        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        // Fact 1: Fast answer (800 ms) -> Fluent
        clock.AdvanceMs(800);
        var fastEval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(fastEval.IsCorrect);
        Assert.Equal(800, fastEval.LatencyMs);
        var fastCommit = await session.CommitCurrentEvaluationAsync();
        Assert.True(fastCommit.IsSuccess);
        var fastAttempt = store.CommittedChangeSets[0].Attempt;
        Assert.True(fastAttempt.IsFluent);

        // Advance to Fact 2
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: true));

        // Fact 2: Slow answer (15,000 ms) in No Time Pressure -> Correct but NOT fluent
        clock.AdvanceMs(15000);
        var slowEval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(slowEval.IsCorrect);
        Assert.Equal(15000, slowEval.LatencyMs);
        var slowCommit = await session.CommitCurrentEvaluationAsync();
        Assert.True(slowCommit.IsSuccess);
        var slowAttempt = store.CommittedChangeSets[1].Attempt;
        Assert.False(slowAttempt.IsFluent);
    }

    [Fact]
    public async Task Learning_SwitchingBackToStandardRetainsPaceHistory()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.NoTimePressure);

        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        // Complete 3 facts in No Time Pressure
        for (var i = 0; i < 3; i++)
        {
            clock.AdvanceMs(2000);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            if (i < 2)
            {
                Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: true));
            }
        }

        // Switch to Standard
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.Standard);
        Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: true));

        // Standard mode is now active with enforced deadline
        Assert.True(session.HasEnforcedDeadline);
        Assert.False(session.IsNoTimePressure);
        Assert.Equal(PracticeTimeSetting.Standard, session.CurrentPracticeTimeSetting);
        Assert.InRange(session.CurrentFactDeadlineMs, AdaptivePacePolicy.MinimumDeadlineMs, AdaptivePacePolicy.MaximumDeadlineMs);
    }

    [Fact]
    public async Task Learning_ResetLearningProgress_PreservesNoTimePressureSetting()
    {
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.NoTimePressure);

        var session = new TrainingSession(store, preferenceStore: prefStore);
        await session.InitializeAsync();

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        await session.ResetLearningProgressAsync();

        Assert.Equal(PracticeTimeSetting.NoTimePressure, prefStore.GetPracticeTimeSetting());
        Assert.False(session.HasEnforcedDeadline);
        Assert.True(session.IsNoTimePressure);
    }

    // ============================================================
    // 4. UI / COMPONENT CONTRACT TESTS
    // ============================================================

    [Theory]
    [InlineData(0.0, "0.000 s")]
    [InlineData(-1.0, "0.000 s")]
    [InlineData(1.4, "1.400 s")]
    [InlineData(12.3, "12.300 s")]
    [InlineData(47.8, "47.800 s")]
    public void UI_FormatElapsedTimerDisplay_ProducesAccurateSeconds(double seconds, string expected)
    {
        Assert.Equal(expected, LearningPolicy.FormatElapsedTimerDisplay(seconds));
    }

    [Fact]
    public void UI_PracticeCountdownTimer_RendersCountUpWithoutProgressBarInNoTimePressure()
    {
        var timerSource = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Shared", "PracticeCountdownTimer.razor"));

        // HasEnforcedDeadline branch: progressbar role and fill bar
        Assert.Contains("@if (Session.HasEnforcedDeadline)", timerSource, StringComparison.Ordinal);
        Assert.Contains("role=\"progressbar\"", timerSource, StringComparison.Ordinal);
        Assert.Contains("timer-bar-fill", timerSource, StringComparison.Ordinal);

        // NoTimePressure branch: timer role, neutral track, no fill bar, no fake aria-valuemax
        Assert.Contains("timer-track-unlimited", timerSource, StringComparison.Ordinal);
        Assert.Contains("role=\"timer\"", timerSource, StringComparison.Ordinal);
        Assert.Contains("FormatElapsedTimerDisplay", timerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void UI_SettingsPage_IncludesNoTimePressureOption()
    {
        var settingsSource = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Settings.razor"));

        Assert.Contains("PracticeTimeSetting.NoTimePressure", settingsSource, StringComparison.Ordinal);
        Assert.Contains("PracticeTime_NoTimePressure", settingsSource, StringComparison.Ordinal);
        Assert.Contains("choice-grid-practice-time", settingsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_ParityAcrossLanguagesForNoTimePressure()
    {
        var localizer = new LocalizationService();

        foreach (var lang in new[] { "en", "de", "ru" })
        {
            localizer.ApplyLanguagePreference(lang);

            var noTimePressureStr = localizer["PracticeTime_NoTimePressure"];
            Assert.False(string.IsNullOrWhiteSpace(noTimePressureStr));
            Assert.NotEqual("PracticeTime_NoTimePressure", noTimePressureStr);

            var elapsedAriaLabel = localizer["Training_TimerElapsedAriaLabel"];
            Assert.False(string.IsNullOrWhiteSpace(elapsedAriaLabel));
            Assert.NotEqual("Training_TimerElapsedAriaLabel", elapsedAriaLabel);
        }
    }

    // ============================================================
    // 5. TIMED MODE NON-REGRESSION TESTS
    // ============================================================

    [Theory]
    [InlineData(PracticeTimeSetting.Standard)]
    [InlineData(PracticeTimeSetting.Seconds30)]
    [InlineData(PracticeTimeSetting.Seconds45)]
    [InlineData(PracticeTimeSetting.Seconds60)]
    public async Task TimedModeRegression_DeadlinesEnforcedAndTimeoutFunctions(PracticeTimeSetting setting)
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(setting);

        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        Assert.True(session.HasEnforcedDeadline);
        Assert.False(session.IsNoTimePressure);

        // Advance just past the deadline
        clock.AdvanceMs(session.CurrentFactDeadlineMs + 100);

        Assert.True(session.IsCurrentItemTimedOut());

        // Answer submission after deadline becomes timeout
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.Equal(AttemptOutcome.Timeout, eval.Outcome);
        Assert.False(eval.IsCorrect);
        Assert.Equal(SessionInteractionState.TimeoutFeedback, session.InteractionState);
    }
}
