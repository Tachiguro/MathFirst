namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using Xunit;

public sealed class AttemptInterruptionLifecycleTests
{
    private sealed class FakeClock : IClock
    {
        private long _timestamp = 1_000_000;

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, _timestamp - startTimestamp));

        public void AdvanceMs(long ms)
        {
            _timestamp += ms;
        }
    }

    private sealed class InMemoryStore : ILearnerStore
    {
        public string StoragePath => "inmemory://";

        private LearnerSnapshot _snapshot;

        public InMemoryStore(LearnerSnapshot? initialSnapshot = null)
        {
            _snapshot = initialSnapshot ?? new(
                LearnerProgression.CreateFresh(),
                new Dictionary<string, ItemLearningState>(StringComparer.Ordinal),
                new List<AttemptRecord>(),
                1,
                LearnerProgression.DefaultSchemaVersion);
        }

        public int CommitsCount { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken ct = default) =>
            Task.FromResult(_snapshot);

        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TestStoreEvidenceHelper.FilterLatestFrontierAttempts(
                _snapshot.RecentAttempts,
                operation,
                bandStartedPracticePosition,
                frontierFactIds));

        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken ct = default)
        {
            CommitsCount++;
            var nextItems = new Dictionary<string, ItemLearningState>(_snapshot.ItemStates, StringComparer.Ordinal)
            {
                [changeSet.UpdatedItemState.FactId] = changeSet.UpdatedItemState
            };
            var nextAttempts = _snapshot.RecentAttempts.Append(changeSet.Attempt).ToList();
            _snapshot = new LearnerSnapshot(
                changeSet.UpdatedProgression,
                nextItems,
                nextAttempts,
                changeSet.ExpectedRevision + 1,
                LearnerProgression.DefaultSchemaVersion);
            return Task.FromResult(PersistenceResult.Success(changeSet.ExpectedRevision + 1));
        }

        public Task ResetLearningProgressAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void Dispose() { }
    }

    private sealed class SingleOperationPreferenceStore(ArithmeticOperation operation) : IPreferenceStore
    {
        public bool GetOnboardingCompleted() => true;
        public void SetOnboardingCompleted(bool completed) { }
        public string GetLanguagePreference() => "system";
        public void SetLanguagePreference(string preference) { }
        public ThemePreference GetThemePreference() => ThemePreference.System;
        public void SetThemePreference(ThemePreference preference) { }
        public NumericKeypadLayout GetNumericKeypadLayout() => NumericKeypadLayout.Numpad;
        public void SetNumericKeypadLayout(NumericKeypadLayout layout) { }
        public bool GetHapticFeedbackEnabled() => true;
        public void SetHapticFeedbackEnabled(bool enabled) { }
        public bool GetOperationEnabled(ArithmeticOperation op) => op == operation;
        public void SetOperationEnabled(ArithmeticOperation op, bool enabled) { }
        public IReadOnlyList<ArithmeticOperation> GetEnabledOperations() => [operation];
        public PracticeTimeSetting GetPracticeTimeSetting() => PracticeTimeSetting.Standard;
        public void SetPracticeTimeSetting(PracticeTimeSetting setting) { }
        public void ResetPracticePreferences() { }
        public void ResetAllPreferences() { }
    }

    private static async Task<(TrainingSession Session, FakeClock Clock, InMemoryStore Store)> CreateSessionAsync(bool startTiming = true, int additionBandIndex = 0)
    {
        var clock = new FakeClock();
        LearnerSnapshot? snapshot = null;
        IPreferenceStore? preferenceStore = null;
        if (additionBandIndex > 0)
        {
            var progression = LearnerProgression.CreateFresh();
            progression.OperationProgressions[ArithmeticOperation.Addition] = new OperationProgression(ArithmeticOperation.Addition, additionBandIndex, 0);
            snapshot = new LearnerSnapshot(
                progression,
                new Dictionary<string, ItemLearningState>(StringComparer.Ordinal),
                new List<AttemptRecord>(),
                1,
                LearnerProgression.DefaultSchemaVersion);
            preferenceStore = new SingleOperationPreferenceStore(ArithmeticOperation.Addition);
        }
        var store = new InMemoryStore(snapshot);
        var session = new TrainingSession(store, clock, preferenceStore: preferenceStore);
        await session.InitializeAsync(startTiming: startTiming);
        return (session, clock, store);
    }

    [Fact]
    public void AttemptRecord_Constructor_SetsIsInterruptedExplicitly()
    {
        var defaultRecord = new AttemptRecord(
            submissionId: "sub-1",
            factId: "1+1=2",
            operation: ArithmeticOperation.Addition,
            leftOperand: 1,
            rightOperand: 1,
            submittedAnswer: 2,
            correctAnswer: 2,
            isCorrect: true,
            isFluent: true,
            responseLatencyMs: 800,
            timestamp: DateTimeOffset.UtcNow);

        Assert.False(defaultRecord.IsInterrupted);

        var interruptedRecord = new AttemptRecord(
            submissionId: "sub-2",
            factId: "1+1=2",
            operation: ArithmeticOperation.Addition,
            leftOperand: 1,
            rightOperand: 1,
            submittedAnswer: 2,
            correctAnswer: 2,
            isCorrect: true,
            isFluent: false,
            responseLatencyMs: 3500,
            timestamp: DateTimeOffset.UtcNow,
            isInterrupted: true);

        Assert.True(interruptedRecord.IsInterrupted);
        Assert.True(defaultRecord.IsTimingEligible);
        Assert.False(interruptedRecord.IsTimingEligible);
    }

    [Fact]
    public async Task Test01_NewAttempt_StartsNotInterrupted()
    {
        var (session, _, _) = await CreateSessionAsync(startTiming: true);

        Assert.NotNull(session.CurrentFact);
        Assert.True(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);
    }

    [Fact]
    public async Task Test02_ManualPause_MarksCurrentAttemptInterrupted()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        clock.AdvanceMs(1500);

        session.PausePractice();

        Assert.Equal(PracticeGateState.ManualPause, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.True(session.IsCurrentAttemptInterrupted);
    }

    [Fact]
    public async Task Test03_Resume_DoesNotClearInterruption()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        clock.AdvanceMs(1000);

        session.PausePractice();
        Assert.True(session.IsCurrentAttemptInterrupted);

        session.StartOrResumePractice();
        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.True(session.IsTimingActive);
        Assert.True(session.IsCurrentAttemptInterrupted);
    }

    [Fact]
    public async Task Test04_DuplicatePause_IsIdempotent()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        clock.AdvanceMs(1000);

        session.PausePractice();
        session.PausePractice();
        session.SetPracticeSurfaceActive(false);

        Assert.Equal(PracticeGateState.ManualPause, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.True(session.IsCurrentAttemptInterrupted);
    }

    [Fact]
    public async Task Test05_AppBackground_MarksActiveAttemptInterrupted()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        clock.AdvanceMs(1200);

        session.SetAppForeground(false);

        Assert.False(session.IsAppForeground);
        Assert.False(session.IsTimingActive);
        Assert.True(session.IsCurrentAttemptInterrupted);
    }

    [Fact]
    public async Task Test06_BackgroundBeforeTimingStarts_DoesNotMarkAttemptInterrupted()
    {
        var (session, _, _) = await CreateSessionAsync(startTiming: false);
        Assert.False(session.IsTimingActive);

        session.SetAppForeground(false);

        Assert.False(session.IsCurrentAttemptInterrupted);
    }

    [Fact]
    public async Task Test07_PracticeSurfaceDeactivation_MarksActiveAttemptInterrupted()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        clock.AdvanceMs(800);

        session.PauseItemTiming();

        Assert.False(session.IsPracticeSurfaceActive);
        Assert.False(session.IsTimingActive);
        Assert.True(session.IsCurrentAttemptInterrupted);
    }

    [Fact]
    public async Task Test08_NestedManualPauseBackgroundForeground_RemainsInterruptedAndPaused()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        clock.AdvanceMs(1000);

        session.PausePractice();
        Assert.Equal(PracticeGateState.ManualPause, session.PracticeGate);
        Assert.True(session.IsCurrentAttemptInterrupted);

        session.SetAppForeground(false);
        Assert.False(session.IsAppForeground);
        Assert.Equal(PracticeGateState.ManualPause, session.PracticeGate);
        Assert.True(session.IsCurrentAttemptInterrupted);

        session.SetAppForeground(true);
        Assert.True(session.IsAppForeground);
        Assert.Equal(PracticeGateState.ManualPause, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.True(session.IsCurrentAttemptInterrupted);
    }

    [Fact]
    public async Task Test09_UninterruptedSubmission_ProducesAttemptRecordIsInterruptedFalse()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        clock.AdvanceMs(1500);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        Assert.False(session.IsCurrentAttemptInterrupted);
        Assert.NotNull(eval.ChangeSet?.Attempt);
        Assert.False(eval.ChangeSet.Attempt.IsInterrupted);
    }

    [Fact]
    public async Task Test10_InterruptedCorrectSubmission()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        clock.AdvanceMs(2000);

        session.PausePractice();
        clock.AdvanceMs(30000);

        session.StartOrResumePractice();
        clock.AdvanceMs(1000);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        Assert.Equal(AttemptOutcome.Correct, eval.Outcome);
        Assert.True(eval.IsCorrect);
        Assert.Equal(3000, eval.LatencyMs);
        Assert.NotNull(eval.ChangeSet?.Attempt);
        Assert.Equal(3000, eval.ChangeSet.Attempt.ResponseLatencyMs);
        Assert.True(eval.ChangeSet.Attempt.IsInterrupted);
    }

    [Fact]
    public async Task Test11_InterruptedIncorrectSubmission()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        clock.AdvanceMs(1000);

        session.SetAppForeground(false);
        clock.AdvanceMs(5000);

        session.SetAppForeground(true);
        session.StartOrResumePractice();
        clock.AdvanceMs(500);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult + 99);

        Assert.Equal(AttemptOutcome.Incorrect, eval.Outcome);
        Assert.False(eval.IsCorrect);
        Assert.NotNull(eval.ChangeSet?.Attempt);
        Assert.True(eval.ChangeSet.Attempt.IsInterrupted);
    }

    [Fact]
    public async Task Test12_SubmissionItself_DoesNotMarkInterrupted()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        clock.AdvanceMs(1200);
        Assert.True(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        Assert.False(session.IsCurrentAttemptInterrupted);
        Assert.NotNull(eval.ChangeSet?.Attempt);
        Assert.False(eval.ChangeSet.Attempt.IsInterrupted);
    }

    [Fact]
    public async Task Test13_NextFact_ResetsInterruptionState()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        clock.AdvanceMs(1000);

        session.PausePractice();
        Assert.True(session.IsCurrentAttemptInterrupted);

        session.StartOrResumePractice();
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.ChangeSet.Attempt.IsInterrupted);

        await session.CommitCurrentEvaluationAsync();
        var advanced = session.AdvanceAfterCorrectAnswer();
        Assert.True(advanced);

        Assert.False(session.IsCurrentAttemptInterrupted);
        Assert.True(session.IsTimingActive);
    }

    [Fact]
    public async Task Test14_InterruptionAlone_DoesNotCreateAttemptOrAdvancePracticePosition()
    {
        var (session, clock, store) = await CreateSessionAsync(startTiming: true);
        var initialPosition = session.Progression.PracticePosition;
        clock.AdvanceMs(1000);

        session.PausePractice();
        clock.AdvanceMs(2000);
        session.SetAppForeground(false);
        clock.AdvanceMs(5000);
        session.SetAppForeground(true);
        session.SetPracticeSurfaceActive(false);

        Assert.Equal(initialPosition, session.Progression.PracticePosition);
        Assert.Equal(0, store.CommitsCount);
        Assert.Null(session.LastEvaluation);
    }

    [Fact]
    public async Task Test15_PausedDuration_IsExcludedFromLatency()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        clock.AdvanceMs(2000);

        session.PausePractice();
        clock.AdvanceMs(30000);

        session.StartOrResumePractice();
        clock.AdvanceMs(1000);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        Assert.Equal(3000, eval.LatencyMs);
        Assert.Equal(3000, session.LastResponseLatencyMs);
        Assert.Equal(3000, eval.ChangeSet.Attempt.ResponseLatencyMs);
    }

    [Fact]
    public async Task ShowInitialReadyGate_BeforeTimingStarts_DoesNotMarkAttemptInterrupted()
    {
        var (session, _, _) = await CreateSessionAsync(startTiming: false);
        session.ShowInitialReadyGate();

        Assert.Equal(PracticeGateState.InitialReadyGate, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);
    }

    [Fact]
    public async Task BackgroundDuringFeedback_DoesNotMarkSubsequentAttemptInterrupted()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        clock.AdvanceMs(1000);

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);

        session.SetAppForeground(false);
        session.SetAppForeground(true);
        var advanced = session.AdvanceAfterCorrectAnswer();
        Assert.True(advanced);

        Assert.Equal(PracticeGateState.BackgroundResumeGate, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);

        session.StartOrResumePractice();
        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.True(session.IsTimingActive);
        Assert.False(session.IsCurrentAttemptInterrupted);
    }

    [Fact]
    public async Task InterruptedCorrect_IncrementsCorrectAttempts_AndConsecutiveStreak_PreservesFluentStreakMasteryAndLatencies()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);
        var factId = session.CurrentFact.Id;

        // 1. Initial uninterrupted fast correct attempt
        clock.AdvanceMs(1200);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        session.AdvanceAfterCorrectAnswer();

        var state1 = session.ItemStates[factId];
        Assert.Equal(1, state1.TotalAttempts);
        Assert.Equal(1, state1.CorrectAttempts);
        Assert.Equal(1, state1.ConsecutiveCorrectStreak);
        Assert.Equal(1, state1.FluentStreak);
        Assert.False(state1.IsProvisionallyMastered);
        Assert.Equal(1200, state1.LastLatencyMs);
        Assert.Equal(1200, state1.RollingLatencyMs);

        // Interrupted correct attempt on current fact
        session.PausePractice();
        clock.AdvanceMs(5000);
        session.StartOrResumePractice();
        clock.AdvanceMs(800);

        var activeFactId = session.CurrentFact.Id;
        var preEvalState = session.ItemStates.TryGetValue(activeFactId, out var existingState)
            ? existingState
            : ItemLearningState.CreateNew(session.CurrentFact);
        var preFluentStreak = preEvalState.FluentStreak;
        var preMastered = preEvalState.IsProvisionallyMastered;
        var preLastLatency = preEvalState.LastLatencyMs;
        var preRollingLatency = preEvalState.RollingLatencyMs;
        var preCorrectAttempts = preEvalState.CorrectAttempts;
        var preConsecutiveStreak = preEvalState.ConsecutiveCorrectStreak;

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);
        Assert.True(eval.ChangeSet.Attempt.IsInterrupted);

        await session.CommitCurrentEvaluationAsync();
        var postState = session.ItemStates[activeFactId];

        Assert.Equal(preCorrectAttempts + 1, postState.CorrectAttempts);
        Assert.Equal(preConsecutiveStreak + 1, postState.ConsecutiveCorrectStreak);
        Assert.Equal(preFluentStreak, postState.FluentStreak);
        Assert.Equal(preMastered, postState.IsProvisionallyMastered);
        Assert.Equal(preLastLatency, postState.LastLatencyMs);
        Assert.Equal(preRollingLatency, postState.RollingLatencyMs);
    }

    [Fact]
    public async Task InterruptedCorrect_CannotNewlyGrantProvisionalMastery()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync(startTiming: true);

        var factId = session.CurrentFact.Id;
        if (!session.ItemStates.ContainsKey(factId))
        {
            session.ItemStates[factId] = ItemLearningState.CreateNew(session.CurrentFact);
        }
        session.ItemStates[factId].TotalAttempts = 2;
        session.ItemStates[factId].CorrectAttempts = 2;
        session.ItemStates[factId].ConsecutiveCorrectStreak = 2;
        session.ItemStates[factId].FluentStreak = 2;
        session.ItemStates[factId].IsProvisionallyMastered = false;

        session.PausePractice();
        session.StartOrResumePractice();
        clock.AdvanceMs(800);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);
        Assert.True(eval.ChangeSet.Attempt.IsInterrupted);

        await session.CommitCurrentEvaluationAsync();
        var state = session.ItemStates[factId];
        Assert.Equal(3, state.TotalAttempts);
        Assert.Equal(3, state.CorrectAttempts);
        Assert.Equal(3, state.ConsecutiveCorrectStreak);
        Assert.Equal(2, state.FluentStreak);
        Assert.False(state.IsProvisionallyMastered);
    }

    [Fact]
    public async Task InterruptedCorrect_DoesNotRevokeExistingProvisionalMastery()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync(startTiming: true);

        var factId = session.CurrentFact.Id;
        if (!session.ItemStates.ContainsKey(factId))
        {
            session.ItemStates[factId] = ItemLearningState.CreateNew(session.CurrentFact);
        }
        session.ItemStates[factId].TotalAttempts = 5;
        session.ItemStates[factId].CorrectAttempts = 5;
        session.ItemStates[factId].ConsecutiveCorrectStreak = 5;
        session.ItemStates[factId].FluentStreak = 5;
        session.ItemStates[factId].IsProvisionallyMastered = true;

        session.PausePractice();
        session.StartOrResumePractice();
        clock.AdvanceMs(800);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);
        Assert.True(eval.ChangeSet.Attempt.IsInterrupted);

        await session.CommitCurrentEvaluationAsync();
        var state = session.ItemStates[factId];
        Assert.Equal(6, state.TotalAttempts);
        Assert.Equal(6, state.CorrectAttempts);
        Assert.Equal(6, state.ConsecutiveCorrectStreak);
        Assert.Equal(5, state.FluentStreak);
        Assert.True(state.IsProvisionallyMastered);
    }

    [Fact]
    public async Task InterruptedIncorrect_AppliesErrorSemantics_ButPreservesLatencies()
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync(startTiming: true);

        var factId = session.CurrentFact.Id;
        if (!session.ItemStates.ContainsKey(factId))
        {
            session.ItemStates[factId] = ItemLearningState.CreateNew(session.CurrentFact);
        }
        session.ItemStates[factId].TotalAttempts = 5;
        session.ItemStates[factId].CorrectAttempts = 5;
        session.ItemStates[factId].ConsecutiveCorrectStreak = 5;
        session.ItemStates[factId].FluentStreak = 5;
        session.ItemStates[factId].IsProvisionallyMastered = true;
        session.ItemStates[factId].LastLatencyMs = 1200;
        session.ItemStates[factId].RollingLatencyMs = 1200;

        session.PausePractice();
        session.StartOrResumePractice();
        clock.AdvanceMs(5000);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult + 99);
        Assert.False(eval.IsCorrect);
        Assert.True(eval.ChangeSet.Attempt.IsInterrupted);

        await session.CommitCurrentEvaluationAsync();
        var state = session.ItemStates[factId];
        Assert.Equal(6, state.TotalAttempts);
        Assert.Equal(1, state.IncorrectAttempts);
        Assert.Equal(0, state.ConsecutiveCorrectStreak);
        Assert.Equal(0, state.FluentStreak);
        Assert.False(state.IsProvisionallyMastered);
        Assert.True(state.NeedsRemediation);
        Assert.Equal(1200, state.LastLatencyMs);
        Assert.Equal(1200, state.RollingLatencyMs);
    }

    [Fact]
    public async Task SessionMedian_ExcludesInterruptedCorrectLatency()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);

        // 1st attempt: uninterrupted correct 1000ms
        clock.AdvanceMs(1000);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        session.AdvanceAfterCorrectAnswer();

        // 2nd attempt: interrupted correct 12000ms active latency
        session.PausePractice();
        session.StartOrResumePractice();
        clock.AdvanceMs(12000);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();
        session.AdvanceAfterCorrectAnswer();

        // 3rd attempt: uninterrupted correct 3000ms
        clock.AdvanceMs(3000);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        var summary = session.GetSessionSummary();
        Assert.Equal(3, summary.CompletedCount);
        Assert.Equal(3, summary.CorrectCount);
        // Median of [1000, 3000] is 2000ms, excluding the 12000ms interrupted sample!
        Assert.Equal(2000, summary.MedianCorrectLatencyMs);
    }

    [Fact]
    public async Task SessionCheckInCadence_StillCountsInterruptedAttemptsNormally_ExcludingContaminatedMedian()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);

        // Perform 19 uninterrupted correct attempts (1000ms)
        for (int i = 0; i < 19; i++)
        {
            clock.AdvanceMs(1000);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceAfterCorrectAnswer();
        }
        Assert.Null(session.PendingCheckIn);

        // 20th attempt is interrupted correct (10000ms)
        session.PausePractice();
        session.StartOrResumePractice();
        clock.AdvanceMs(10000);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        await session.CommitCurrentEvaluationAsync();

        // Check-in triggers exactly at attempt 20!
        Assert.NotNull(session.PendingCheckIn);
        Assert.Equal(20, session.PendingCheckIn.TotalCount);
        Assert.Equal(20, session.PendingCheckIn.CorrectCount);
        // Median of the 19 clean 1000ms attempts is 1000ms, ignoring the 10000ms interrupted sample
        Assert.Equal(1000, session.PendingCheckIn.MedianCorrectLatencyMs);
    }

    [Fact]
    public async Task InterruptedCorrect_FsrsRatingStillUsesExistingActiveLatencyMapping()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);

        // Interrupted attempt with fast active latency (800ms)
        session.PausePractice();
        session.StartOrResumePractice();
        clock.AdvanceMs(800);

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);
        Assert.True(eval.ChangeSet.Attempt.IsInterrupted);

        var commit = await session.CommitCurrentEvaluationAsync();
        Assert.True(commit.IsSuccess);

        // FSRS rating for 800ms is Easy
        Assert.Equal(FsrsRating.Easy, session.FsrsStates[session.CurrentFact.Id].LastRating);
    }

    [Fact]
    public async Task BoundRecentAttempts_RetainsOlderEligibleAttempts_WhenRecentAttemptsAreInterrupted()
    {
        var (session, clock, _) = await CreateSessionAsync(startTiming: true);

        // Submit 10 clean correct attempts (1000ms each)
        for (int i = 0; i < 10; i++)
        {
            clock.AdvanceMs(1000);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceAfterCorrectAnswer();
        }

        // Submit 40 interrupted correct attempts (5000ms each)
        for (int i = 0; i < 40; i++)
        {
            session.PausePractice();
            session.StartOrResumePractice();
            clock.AdvanceMs(5000);
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            await session.CommitCurrentEvaluationAsync();
            session.AdvanceAfterCorrectAnswer();
        }

        // If older eligible attempts were dropped by BoundRecentAttempts (because 40 latest were kept),
        // there would be 0 eligible attempts in _recentAttempts and expected pace would be StaticPrior (3000ms).
        // Because BoundRecentAttempts retains the older 10 eligible attempts, pace is adapted from the 1000ms clean samples!
        Assert.True(session.CurrentFactExpectedPaceMs < 3000, $"Expected pace adapted < 3000, but was {session.CurrentFactExpectedPaceMs}");
        Assert.Equal(2527, session.CurrentFactExpectedPaceMs);
    }
}
