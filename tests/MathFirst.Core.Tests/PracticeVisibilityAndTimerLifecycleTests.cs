namespace MathFirst.Core.Tests;

using System.Runtime.CompilerServices;
using MathFirst.Application;
using MathFirst.Application.Diagnostics;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using Xunit;

public sealed class PracticeVisibilityAndTimerLifecycleTests
{
    private sealed class FakeClock : IClock
    {
        private long _timestamp = 1_000_000;

        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, _timestamp - startTimestamp));

        public void AdvanceSeconds(double seconds)
        {
            _timestamp += (long)Math.Round(seconds * 1000.0);
        }

        public void AdvanceMs(long ms)
        {
            _timestamp += ms;
        }
    }

    private sealed class InMemoryPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<string, bool> _boolPrefs = new(StringComparer.Ordinal);
        private int _practiceTimeSetting = 0;
        public bool OnboardingCompleted { get; set; } = true;
        public string Language { get; set; } = "system";
        public ThemePreference Theme { get; set; } = ThemePreference.System;
        public NumericKeypadLayout KeypadLayout { get; set; } = NumericKeypadLayout.Numpad;

        public bool GetOnboardingCompleted() => OnboardingCompleted;
        public void SetOnboardingCompleted(bool completed) => OnboardingCompleted = completed;
        public string GetLanguagePreference() => Language;
        public void SetLanguagePreference(string preference) => Language = preference;
        public ThemePreference GetThemePreference() => Theme;
        public void SetThemePreference(ThemePreference preference) => Theme = preference;
        public NumericKeypadLayout GetNumericKeypadLayout() => KeypadLayout;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) => KeypadLayout = layout;

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
            _practiceTimeSetting = 0;
        }

        public void ResetAllPreferences()
        {
            OnboardingCompleted = false;
            Language = "system";
            Theme = ThemePreference.System;
            KeypadLayout = NumericKeypadLayout.Numpad;
            ResetPracticePreferences();
        }
    }

    private sealed class InMemoryStore : ILearnerStore
    {
        public string StoragePath => "inmemory://lifecycle-test";
        private LearnerSnapshot _snapshot = new(
            LearnerProgression.CreateFresh(),
            new Dictionary<string, ItemLearningState>(),
            new Dictionary<string, FsrsCardState>(),
            new List<AttemptRecord>(),
            1,
            LearnerProgression.DefaultSchemaVersion,
            LearnerProgression.CreateFresh().OperationProgressions);

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken ct = default) =>
            Task.FromResult(_snapshot);
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttemptRecord>>([]);
        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet, CancellationToken ct = default)
        {
            var rev = _snapshot.Revision + 1;
            var items = new Dictionary<string, ItemLearningState>(_snapshot.ItemStates)
            {
                [changeSet.UpdatedItemState.FactId] = changeSet.UpdatedItemState
            };
            var fsrs = new Dictionary<string, FsrsCardState>(_snapshot.FsrsStates);
            if (changeSet.UpdatedFsrsState is not null)
            {
                fsrs[changeSet.UpdatedFsrsState.FactId] = changeSet.UpdatedFsrsState;
            }
            var attempts = new List<AttemptRecord>(_snapshot.RecentAttempts) { changeSet.Attempt };

            _snapshot = new LearnerSnapshot(
                changeSet.UpdatedProgression,
                items,
                fsrs,
                attempts,
                rev,
                _snapshot.SchemaVersion,
                changeSet.UpdatedProgression.OperationProgressions);

            return Task.FromResult(PersistenceResult.Success(rev));
        }
        public Task ResetLearningProgressAsync(CancellationToken ct = default)
        {
            _snapshot = new LearnerSnapshot(
                LearnerProgression.CreateFresh(),
                new Dictionary<string, ItemLearningState>(),
                new Dictionary<string, FsrsCardState>(),
                new List<AttemptRecord>(),
                _snapshot.Revision + 1,
                LearnerProgression.DefaultSchemaVersion,
                LearnerProgression.CreateFresh().OperationProgressions);
            return Task.CompletedTask;
        }
        public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private static IReadOnlyList<OperationProgressDiagnostics> FilterOperationProgress(
        LearnerProgression progression,
        ArithmeticCurriculum curriculum,
        IPreferenceStore preferenceStore)
    {
        var enabledSet = new HashSet<ArithmeticOperation>(preferenceStore.GetEnabledOperations());
        var all = LearningProgressDiagnostics.Create(progression, curriculum).Operations;
        return all.Where(d => enabledSet.Contains(d.Operation)).ToList();
    }

    // ============================================================
    // 1. PRACTICE OPERATION VISIBILITY TESTS
    // ============================================================

    [Fact]
    public void PracticeVisibility_AllFourEnabled_ShowsAllFourOperations()
    {
        var prefStore = new InMemoryPreferenceStore();
        var progression = LearnerProgression.CreateFresh();
        var curriculum = new ArithmeticCurriculum();

        var visible = FilterOperationProgress(progression, curriculum, prefStore);

        Assert.Equal(4, visible.Count);
        Assert.Equal(ArithmeticOperation.Addition, visible[0].Operation);
        Assert.Equal(ArithmeticOperation.Subtraction, visible[1].Operation);
        Assert.Equal(ArithmeticOperation.Multiplication, visible[2].Operation);
        Assert.Equal(ArithmeticOperation.Division, visible[3].Operation);
    }

    [Fact]
    public void PracticeVisibility_AdditionOnly_ShowsAdditionOnly()
    {
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
        prefStore.SetOperationEnabled(ArithmeticOperation.Multiplication, false);
        prefStore.SetOperationEnabled(ArithmeticOperation.Division, false);

        var progression = LearnerProgression.CreateFresh();
        var curriculum = new ArithmeticCurriculum();

        var visible = FilterOperationProgress(progression, curriculum, prefStore);

        Assert.Single(visible);
        Assert.Equal(ArithmeticOperation.Addition, visible[0].Operation);
    }

    [Fact]
    public void PracticeVisibility_AdditionAndMultiplication_ShowsBothOperationsOnly()
    {
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetOperationEnabled(ArithmeticOperation.Subtraction, false);
        prefStore.SetOperationEnabled(ArithmeticOperation.Division, false);

        var progression = LearnerProgression.CreateFresh();
        var curriculum = new ArithmeticCurriculum();

        var visible = FilterOperationProgress(progression, curriculum, prefStore);

        Assert.Equal(2, visible.Count);
        Assert.Equal(ArithmeticOperation.Addition, visible[0].Operation);
        Assert.Equal(ArithmeticOperation.Multiplication, visible[1].Operation);
    }

    [Fact]
    public void PracticeVisibility_AdditionSubtractionDivision_ShowsThreeOperationsOnly()
    {
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetOperationEnabled(ArithmeticOperation.Multiplication, false);

        var progression = LearnerProgression.CreateFresh();
        var curriculum = new ArithmeticCurriculum();

        var visible = FilterOperationProgress(progression, curriculum, prefStore);

        Assert.Equal(3, visible.Count);
        Assert.Equal(ArithmeticOperation.Addition, visible[0].Operation);
        Assert.Equal(ArithmeticOperation.Subtraction, visible[1].Operation);
        Assert.Equal(ArithmeticOperation.Division, visible[2].Operation);
    }

    [Fact]
    public async Task PracticeVisibility_SettingsChangeDuringActiveQuestion_CurrentFactStable_PracticeHudUpdatesOnReturn_NextFactFollowsNewSet()
    {
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        var clock = new FakeClock();

        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        // Advance to a Multiplication question
        while (session.CurrentFact.Operation != ArithmeticOperation.Multiplication)
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceAfterCorrectAnswer(startTiming: true);
        }

        var activeMultFact = session.CurrentFact;
        Assert.Equal(ArithmeticOperation.Multiplication, activeMultFact.Operation);

        // User navigates to Settings and disables Multiplication
        session.PauseItemTiming();
        prefStore.SetOperationEnabled(ArithmeticOperation.Multiplication, false);
        Assert.Equal([ArithmeticOperation.Addition, ArithmeticOperation.Subtraction, ArithmeticOperation.Division], prefStore.GetEnabledOperations());

        // Return to Practice surface
        session.ResumeItemTiming();

        // 1. Current fact remains unchanged
        Assert.Same(activeMultFact, session.CurrentFact);
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);

        // 2. Practice HUD immediately reflects the updated enabled set
        var visibleHud = FilterOperationProgress(session.Progression, new ArithmeticCurriculum(), prefStore);
        Assert.Equal(3, visibleHud.Count);
        Assert.DoesNotContain(visibleHud, h => h.Operation == ArithmeticOperation.Multiplication);

        // 3. User submits answer and advances to next fact
        session.SubmitAnswer(activeMultFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        session.AdvanceAfterCorrectAnswer(startTiming: true);

        // 4. Next fact uses the new enabled set (never Multiplication)
        Assert.NotEqual(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        Assert.Contains(session.CurrentFact.Operation, prefStore.GetEnabledOperations());
    }

    // ============================================================
    // 2. SETTINGS UI CONTRACT & STATISTICS REMOVAL TESTS
    // ============================================================

    [Fact]
    public void Settings_DoesNotRenderStatisticsOrDiagnosticsSection_WhileRetainingAllOperationsAndPreferences()
    {
        var settingsSource = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Settings.razor"));

        // Must NOT contain diagnostics/statistics section or classes
        Assert.DoesNotContain("diagnostics-card", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_Title", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_Description", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_TotalAttempts", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_Accuracy", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_AnswerDeadline", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_LastLatency", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_DatabasePath", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_SchemaVersion", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_FsrsScheduler", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_FsrsModel", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_PracticePosition", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_FsrsCardsTracked", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_FsrsDueCards", settingsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Diagnostics_CurrentFactFsrs", settingsSource, StringComparison.Ordinal);

        // Must retain all 4 operations in Settings
        Assert.Contains("PracticeOperationPreferencePolicy.AllOperations", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Settings_OperationsTitle", settingsSource, StringComparison.Ordinal);

        // Must retain Practice Time settings
        Assert.Contains("Settings_PracticeTimeTitle", settingsSource, StringComparison.Ordinal);
        Assert.Contains("PracticeTime_Standard", settingsSource, StringComparison.Ordinal);
        Assert.Contains("PracticeTime_30s", settingsSource, StringComparison.Ordinal);
        Assert.Contains("PracticeTime_45s", settingsSource, StringComparison.Ordinal);
        Assert.Contains("PracticeTime_60s", settingsSource, StringComparison.Ordinal);

        // Must retain Reset options
        Assert.Contains("Reset_LearningProgress_Title", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Reset_UiPreferences_Title", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Reset_FullLocal_Title", settingsSource, StringComparison.Ordinal);

        // Must retain Language and Appearance
        Assert.Contains("Settings_UILanguage", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Settings_Appearance", settingsSource, StringComparison.Ordinal);
        Assert.Contains("Keypad_NumberKeypad", settingsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_OperationProgressHud_FiltersByEnabledOperations()
    {
        var homeSource = File.ReadAllText(GetRepositoryPath("src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        // Home.razor OperationProgress must filter by PreferenceStore.GetEnabledOperations
        Assert.Contains("PreferenceStore.GetEnabledOperations()", homeSource, StringComparison.Ordinal);
    }

    // ============================================================
    // 3. TIMER LIFECYCLE TESTS (Deterministic Clock)
    // ============================================================

    [Fact]
    public async Task TimerLifecycle_ManualPause_FreezesElapsedAndResumesWithRemainingTime()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock: clock);
        await session.InitializeAsync(startTiming: true);

        Assert.True(session.IsTimingActive);
        var initialDeadlineSeconds = session.CurrentFactDeadlineSeconds;

        // 1. Advance active time 10 seconds
        clock.AdvanceSeconds(10);
        Assert.Equal(10_000, session.GetCurrentActiveElapsedMs());

        // 2. Pause practice
        session.PausePractice();
        Assert.False(session.IsTimingActive);
        Assert.Equal(10_000, session.GetCurrentActiveElapsedMs());

        // 3. Advance real clock 30 seconds while paused
        clock.AdvanceSeconds(30);

        // Active elapsed MUST remain 10 seconds
        Assert.Equal(10_000, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsCurrentItemTimedOut());

        // 4. Resume practice
        session.StartOrResumePractice();
        Assert.True(session.IsTimingActive);

        // Active elapsed immediately after resume is still 10 seconds
        Assert.Equal(10_000, session.GetCurrentActiveElapsedMs());

        // 5. Advance another 5 active seconds
        clock.AdvanceSeconds(5);
        Assert.Equal(15_000, session.GetCurrentActiveElapsedMs());

        // Remaining time is deadline - 15s
        var remainingSeconds = initialDeadlineSeconds - session.GetCurrentItemElapsed().TotalSeconds;
        Assert.Equal(initialDeadlineSeconds - 15.0, remainingSeconds, precision: 1);
    }

    [Fact]
    public async Task TimerLifecycle_SettingsNavigation_FreezesElapsedAndPreservesCurrentFact()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock: clock);
        await session.InitializeAsync(startTiming: true);

        var activeFact = session.CurrentFact;
        Assert.NotNull(activeFact);

        // 1. Advance active time 10 seconds
        clock.AdvanceSeconds(10);
        Assert.Equal(10_000, session.GetCurrentActiveElapsedMs());

        // 2. Open Settings (deactivates practice surface)
        session.PauseItemTiming();
        Assert.False(session.IsTimingActive);
        Assert.Equal(10_000, session.GetCurrentActiveElapsedMs());

        // 3. Advance 30 seconds while in Settings
        clock.AdvanceSeconds(30);
        Assert.Equal(10_000, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsCurrentItemTimedOut());

        // 4. Return to practice
        session.ResumeItemTiming();
        Assert.True(session.IsTimingActive);
        Assert.Equal(10_000, session.GetCurrentActiveElapsedMs());

        // 5. Verify current fact is unchanged
        Assert.Same(activeFact, session.CurrentFact);

        // 6. Advance another 4 seconds
        clock.AdvanceSeconds(4);
        Assert.Equal(14_000, session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public async Task TimerLifecycle_BackgroundResumeVsColdStart_PreservesRemainingInProcess_ResetsOnColdStart()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.Seconds60);

        // 1. Initial process: start question with 60s deadline
        var session1 = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session1.InitializeAsync(startTiming: true);
        Assert.True(session1.CurrentFactDeadlineSeconds >= 60.0);

        // Advance 20 seconds
        clock.AdvanceSeconds(20);
        Assert.Equal(20_000, session1.GetCurrentActiveElapsedMs());

        // 2. Same process: Background app
        session1.SetAppForeground(false);
        Assert.False(session1.IsTimingActive);
        Assert.Equal(20_000, session1.GetCurrentActiveElapsedMs());

        // Advance 100 seconds while backgrounded
        clock.AdvanceSeconds(100);
        Assert.Equal(20_000, session1.GetCurrentActiveElapsedMs());
        Assert.False(session1.IsCurrentItemTimedOut());

        // 3. Same process: Return to foreground and resume
        session1.SetAppForeground(true);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, session1.PracticeGate);
        session1.StartOrResumePractice();
        Assert.True(session1.IsTimingActive);
        Assert.Equal(20_000, session1.GetCurrentActiveElapsedMs());

        // Remaining time in same process is preserved (~40 seconds remaining)
        var remainingSecondsInProcess = session1.CurrentFactDeadlineSeconds - session1.GetCurrentItemElapsed().TotalSeconds;
        Assert.Equal(40.0, remainingSecondsInProcess, precision: 1);

        // 4. True cold start / process restart: create a new TrainingSession
        var freshClock = new FakeClock();
        var session2 = new TrainingSession(store, clock: freshClock, preferenceStore: prefStore);
        await session2.InitializeAsync(startTiming: true);

        // Timer is fresh: 0 elapsed time, full 60-second deadline
        Assert.Equal(0, session2.GetCurrentActiveElapsedMs());
        Assert.True(session2.CurrentFactDeadlineSeconds >= 60.0);
        Assert.Equal(session2.CurrentFactDeadlineSeconds, session2.CurrentFactDeadlineSeconds - session2.GetCurrentItemElapsed().TotalSeconds);

        // Preferences preserved across cold start
        Assert.Equal(PracticeTimeSetting.Seconds60, prefStore.GetPracticeTimeSetting());
    }

    [Fact]
    public async Task TimerLifecycle_Explicit60Seconds_PauseAndColdRestart()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var prefStore = new InMemoryPreferenceStore();
        prefStore.SetPracticeTimeSetting(PracticeTimeSetting.Seconds60);

        var session = new TrainingSession(store, clock: clock, preferenceStore: prefStore);
        await session.InitializeAsync(startTiming: true);

        // Verify deadline is 60s
        Assert.Equal(60_000, session.CurrentFactDeadlineMs);

        // Advance 20 active seconds
        clock.AdvanceSeconds(20);
        Assert.Equal(20_000, session.GetCurrentActiveElapsedMs());

        // Pause or enter Settings
        session.PausePractice();
        Assert.False(session.IsTimingActive);

        // Advance 30 wall-clock seconds
        clock.AdvanceSeconds(30);

        // Resume
        session.StartOrResumePractice();
        Assert.True(session.IsTimingActive);

        // Expected active remaining time: approximately 40 seconds
        var remainingMs = session.CurrentFactDeadlineMs - session.GetCurrentActiveElapsedMs();
        Assert.Equal(40_000, remainingMs);

        // Simulate cold restart
        var coldClock = new FakeClock();
        var coldSession = new TrainingSession(store, clock: coldClock, preferenceStore: prefStore);
        await coldSession.InitializeAsync(startTiming: true);

        // Expected: fresh 60-second timer
        Assert.Equal(60_000, coldSession.CurrentFactDeadlineMs);
        Assert.Equal(0, coldSession.GetCurrentActiveElapsedMs());
        var coldRemainingMs = coldSession.CurrentFactDeadlineMs - coldSession.GetCurrentActiveElapsedMs();
        Assert.Equal(60_000, coldRemainingMs);
    }

    [Fact]
    public async Task TimerLifecycle_TimeoutCannotOccurDuringPauseSettingsOrBackground()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock: clock);
        await session.InitializeAsync(startTiming: true);

        // 1. Advance 5 active seconds
        clock.AdvanceSeconds(5);
        Assert.False(session.IsCurrentItemTimedOut());

        // 2. Pause and advance far beyond deadline
        session.PausePractice();
        clock.AdvanceSeconds(500);
        Assert.False(session.IsCurrentItemTimedOut());
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        // 3. Deactivate surface (Settings) and advance further
        session.SetPracticeSurfaceActive(false);
        clock.AdvanceSeconds(500);
        Assert.False(session.IsCurrentItemTimedOut());
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        // 4. Background and advance further
        session.SetAppForeground(false);
        clock.AdvanceSeconds(500);
        Assert.False(session.IsCurrentItemTimedOut());
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
    }

    [Fact]
    public async Task TimerLifecycle_RawLatency_ExcludesPauseSettingsAndBackgroundTime()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock: clock);
        await session.InitializeAsync(startTiming: true);

        // 1. Spend 2 active seconds
        clock.AdvanceSeconds(2);

        // 2. Pause for 30 wall-clock seconds
        session.PausePractice();
        clock.AdvanceSeconds(30);
        session.StartOrResumePractice();

        // 3. Open Settings for 20 wall-clock seconds
        session.PauseItemTiming();
        clock.AdvanceSeconds(20);
        session.ResumeItemTiming();

        // 4. Background for 40 wall-clock seconds
        session.SetAppForeground(false);
        clock.AdvanceSeconds(40);
        session.SetAppForeground(true);
        session.StartOrResumePractice();

        // 5. Spend 1 more active second and submit
        clock.AdvanceSeconds(1);
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        // Total active time = 2s + 1s = 3s = 3000ms
        // Excludes 30s + 20s + 40s = 90s of inactive time
        Assert.Equal(3_000, eval.LatencyMs);
        Assert.Equal(3_000, session.LastResponseLatencyMs);
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = GetRepositoryRoot();
        return Path.Combine([root, .. segments]);
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
