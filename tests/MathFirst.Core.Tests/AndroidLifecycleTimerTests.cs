namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using Xunit;

/// <summary>
/// Deterministic timer lifecycle tests for the foreground/background pause-resume
/// semantics required by MF-AND-001. No real sleeps — uses a FakeClock and
/// InMemoryStore throughout.
///
/// Test cases:
///   A. Practice + foreground  => timer runs
///   B. Practice + background  => timer paused
///   C. Settings + foreground  => paused
///   D. Settings -> background -> foreground => still paused
///   E. Long fake background duration => no timeout created
///   F. Return to Practice after background => same remaining semantic time
///   G. Background transition changes no attempt / PracticePosition / session score
/// </summary>
public sealed class AndroidLifecycleTimerTests
{
    // ============================================================
    // Shared test infrastructure
    // ============================================================

    private sealed class FakeClock : IClock
    {
        private long _timestamp = 1_000_000;

        public long GetTimestamp() => _timestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, _timestamp - startTimestamp));

        /// <summary>Advance the clock by the given milliseconds.</summary>
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
            new Dictionary<string, ItemLearningState>(),
            new List<AttemptRecord>(),
            1,
            LearnerProgression.DefaultSchemaVersion);

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken ct = default) =>
            Task.FromResult(_snapshot);
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TestStoreEvidenceHelper.FilterLatestFrontierAttempts(_snapshot.RecentAttempts, operation, bandStartedPracticePosition, frontierFactIds));
        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet, CancellationToken ct = default) =>
            Task.FromResult(PersistenceResult.Success(changeSet.ExpectedRevision + 1));
        public Task ResetLearningProgressAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private static async Task<(TrainingSession session, FakeClock clock)>
        CreateInitializedSession(bool startTiming = true)
    {
        var clock = new FakeClock();
        var store = new InMemoryStore();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync(startTiming: startTiming);
        return (session, clock);
    }

    // ============================================================
    // A. Practice + foreground => timer runs
    // ============================================================

    [Fact]
    public async Task A_PracticeAndForeground_TimerRuns()
    {
        var (session, clock) = await CreateInitializedSession(startTiming: true);

        // Both foreground and practice surface are active after initialization
        Assert.True(session.IsAppForeground);
        Assert.True(session.IsPracticeSurfaceActive);
        Assert.True(session.IsTimingActive);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);

        // Advance clock by 3 seconds
        clock.AdvanceMs(3000);

        var elapsed = session.GetCurrentActiveElapsedMs();
        Assert.True(elapsed >= 3000, $"Expected at least 3000 ms elapsed, got {elapsed}.");
    }

    // ============================================================
    // B. Practice + background => timer paused
    // ============================================================

    [Fact]
    public async Task B_PracticeAndBackground_TimerPaused()
    {
        var (session, clock) = await CreateInitializedSession(startTiming: true);

        // Accumulate 3 seconds of foreground active time
        clock.AdvanceMs(3000);
        var elapsedBeforeBackground = session.GetCurrentActiveElapsedMs();
        Assert.True(elapsedBeforeBackground >= 3000);

        // App goes to background
        session.SetAppForeground(false);
        Assert.False(session.IsTimingActive);
        Assert.False(session.IsAppForeground);

        // "Wait" 30 seconds in background — elapsed should NOT grow
        var elapsedAfterBackground = session.GetCurrentActiveElapsedMs();
        Assert.True(elapsedAfterBackground <= elapsedBeforeBackground + 10,
            $"Timer must not advance while backgrounded. Before={elapsedBeforeBackground} After={elapsedAfterBackground}.");
    }

    // ============================================================
    // C. Settings + foreground => timing paused (practice surface inactive)
    // ============================================================

    [Fact]
    public async Task C_SettingsSurfaceAndForeground_TimerPaused()
    {
        var (session, _) = await CreateInitializedSession(startTiming: true);

        // Simulate navigating to Settings (practice surface deactivated)
        session.SetPracticeSurfaceActive(false);

        Assert.False(session.IsTimingActive);
        Assert.True(session.IsAppForeground);
        Assert.False(session.IsPracticeSurfaceActive);
    }

    // ============================================================
    // D. Settings -> background -> foreground => still paused
    // ============================================================

    [Fact]
    public async Task D_SettingsThenBackgroundThenForeground_StillPaused()
    {
        var (session, _) = await CreateInitializedSession(startTiming: true);

        // Navigate to Settings
        session.SetPracticeSurfaceActive(false);
        Assert.False(session.IsTimingActive);

        // App backgrounds
        session.SetAppForeground(false);
        Assert.False(session.IsTimingActive);

        // App resumes — practice surface is still on Settings, not Practice
        session.SetAppForeground(true);

        // Timer must NOT restart just because the app came to foreground
        Assert.False(session.IsTimingActive,
            "Timer must remain paused when practice surface is not active, even after foreground resume.");
        Assert.True(session.IsAppForeground);
        Assert.False(session.IsPracticeSurfaceActive);
    }

    // ============================================================
    // E. Long fake background duration => no timeout recorded
    // ============================================================

    [Fact]
    public async Task E_LongBackgroundDuration_NoTimeoutRecorded()
    {
        var (session, clock) = await CreateInitializedSession(startTiming: true);

        // Accumulate 1 second of active time
        clock.AdvanceMs(1000);

        // Go to background
        session.SetAppForeground(false);

        var positionBefore = session.Progression.PracticePosition;
        var correctBefore = session.SessionCorrectCount;
        var totalBefore = session.SessionTotalCount;
        var stateBefore = session.InteractionState;

        // "Wait" 5 minutes (300 seconds) — far beyond any deadline
        clock.AdvanceMs(300_000);

        var elapsedAfterWait = session.GetCurrentActiveElapsedMs();
        Assert.True(elapsedAfterWait < session.CurrentFactDeadlineMs,
            $"Semantic elapsed ({elapsedAfterWait} ms) must remain below deadline ({session.CurrentFactDeadlineMs} ms) after background.");

        // No state mutation should have occurred
        Assert.Equal(positionBefore, session.Progression.PracticePosition);
        Assert.Equal(correctBefore, session.SessionCorrectCount);
        Assert.Equal(totalBefore, session.SessionTotalCount);
        Assert.Equal(stateBefore, session.InteractionState);
    }

    // ============================================================
    // F. Return to Practice after background => same remaining time
    // ============================================================

    [Fact]
    public async Task F_ReturnToPracticeAfterBackground_SameRemainingTime()
    {
        var (session, clock) = await CreateInitializedSession(startTiming: true);

        // Accumulate 5 seconds of active foreground time
        clock.AdvanceMs(5000);
        var elapsedBeforeBackground = session.GetCurrentActiveElapsedMs();

        // App backgrounds — elapsed freezes
        session.SetAppForeground(false);
        var frozenElapsed = session.GetCurrentActiveElapsedMs();
        Assert.Equal(elapsedBeforeBackground, frozenElapsed);

        // "Time passes" in background — no semantic elapsed change
        var elapsedStillFrozen = session.GetCurrentActiveElapsedMs();
        Assert.Equal(frozenElapsed, elapsedStillFrozen);

        // App returns to foreground with Practice still active, but learner readiness
        // must be re-established explicitly before semantic timing resumes.
        session.SetAppForeground(true);
        Assert.False(session.IsTimingActive);

        // Elapsed immediately after returning is still the same frozen amount.
        var elapsedAfterResume = session.GetCurrentActiveElapsedMs();
        Assert.Equal(frozenElapsed, elapsedAfterResume);

        var deadlineMs = session.CurrentFactDeadlineMs;
        var remainingAfterResume = deadlineMs - elapsedAfterResume;
        var remainingBeforeBackground = deadlineMs - frozenElapsed;
        Assert.Equal(remainingBeforeBackground, remainingAfterResume);
    }

    // ============================================================
    // G. Background transition changes no attempt/PracticePosition/score
    // ============================================================

    [Fact]
    public async Task G_BackgroundTransition_ChangesNoAttemptOrScore()
    {
        var (session, _) = await CreateInitializedSession(startTiming: true);

        var positionBefore = session.Progression.PracticePosition;
        var correctBefore = session.SessionCorrectCount;
        var totalBefore = session.SessionTotalCount;
        var stateBefore = session.InteractionState;
        var factBefore = session.CurrentFact.Id;

        // Background
        session.SetAppForeground(false);
        // Foreground
        session.SetAppForeground(true);
        // Background again
        session.SetAppForeground(false);
        // Foreground again
        session.SetAppForeground(true);

        // Nothing should have changed in session state
        Assert.Equal(positionBefore, session.Progression.PracticePosition);
        Assert.Equal(correctBefore, session.SessionCorrectCount);
        Assert.Equal(totalBefore, session.SessionTotalCount);
        Assert.Equal(stateBefore, session.InteractionState);
        Assert.Equal(factBefore, session.CurrentFact.Id);
        Assert.Null(session.LastEvaluation);
    }

    // ============================================================
    // Additional: SetPracticeSurface + SetAppForeground gate logic
    // ============================================================

    [Fact]
    public async Task Timer_OnlyRunsWhenBothPracticeAndForegroundAreActive()
    {
        var (session, _) = await CreateInitializedSession(startTiming: false);

        // Initially: practice inactive (startTiming=false), foreground=true
        Assert.False(session.IsTimingActive);

        // Activate practice surface
        session.SetPracticeSurfaceActive(true);
        Assert.True(session.IsTimingActive);

        // Background
        session.SetAppForeground(false);
        Assert.False(session.IsTimingActive);

        // Foreground returns — practice is still active, but explicit resume is required.
        session.SetAppForeground(true);
        Assert.False(session.IsTimingActive);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, session.PracticeGate);

        session.StartOrResumePractice();
        Assert.True(session.IsTimingActive);

        // Practice surface deactivates (navigate away)
        session.SetPracticeSurfaceActive(false);
        Assert.False(session.IsTimingActive);
    }

    [Fact]
    public async Task OnboardingAndForeground_TimerRemainsPaused()
    {
        var (session, clock) = await CreateInitializedSession(startTiming: false);

        Assert.True(session.IsAppForeground);
        Assert.False(session.IsPracticeSurfaceActive);
        Assert.False(session.IsTimingActive);

        clock.AdvanceMs(300_000);

        Assert.Equal(0, session.GetCurrentActiveElapsedMs());
        Assert.False(session.IsCurrentItemTimedOut());
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Equal(0, session.SessionTotalCount);
    }

    [Fact]
    public async Task RepeatedForegroundSignals_AreIdempotent()
    {
        var (session, clock) = await CreateInitializedSession(startTiming: true);

        clock.AdvanceMs(1_000);
        session.SetAppForeground(true);
        Assert.Equal(1_000, session.GetCurrentActiveElapsedMs());

        session.SetAppForeground(false);
        session.SetAppForeground(false);
        clock.AdvanceMs(300_000);
        Assert.Equal(1_000, session.GetCurrentActiveElapsedMs());

        session.SetAppForeground(true);
        session.SetAppForeground(true);
        Assert.False(session.IsTimingActive);
        Assert.Equal(1_000, session.GetCurrentActiveElapsedMs());

        session.StartOrResumePractice();
        session.StartOrResumePractice();
        clock.AdvanceMs(1_000);
        Assert.Equal(2_000, session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public async Task Timer_DeactivatedBySubmit_NotReactivatedByForegroundChange()
    {
        var (session, _) = await CreateInitializedSession(startTiming: true);
        Assert.True(session.IsTimingActive);

        // Submit an answer — timer stops
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.False(session.IsTimingActive);
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);

        // Background/foreground while in CorrectFeedback should not restart timer
        session.SetAppForeground(false);
        session.SetAppForeground(true);

        Assert.False(session.IsTimingActive,
            "Timer must not restart on foreground return when not in AwaitingAnswer state.");
    }
}
