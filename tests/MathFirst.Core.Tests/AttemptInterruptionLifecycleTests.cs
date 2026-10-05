namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
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

        private LearnerSnapshot _snapshot = new(
            LearnerProgression.CreateFresh(),
            new Dictionary<string, ItemLearningState>(StringComparer.Ordinal),
            new List<AttemptRecord>(),
            1,
            LearnerProgression.DefaultSchemaVersion);

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

    private static async Task<(TrainingSession Session, FakeClock Clock, InMemoryStore Store)> CreateSessionAsync(bool startTiming = true)
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock);
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
}
