namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using Xunit;

public sealed class ClockAndTimingTests
{
    private sealed class FakeClock : IClock
    {
        public long CurrentTimestamp { get; set; }
        public TimeSpan Elapsed { get; set; }

        public long GetTimestamp() => CurrentTimestamp;
        public TimeSpan GetElapsedTime(long startTimestamp) => Elapsed;
    }

    private sealed class InMemoryStore : ILearnerStore
    {
        public string StoragePath => "inmemory.db";
        public LearnerSnapshot Snapshot { get; set; } = new(
            LearnerProgression.CreateFresh(),
            new Dictionary<string, ItemLearningState>(),
            new List<AttemptRecord>(),
            1,
            1);

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TestStoreEvidenceHelper.FilterLatestFrontierAttempts(Snapshot.RecentAttempts, operation, bandStartedPracticePosition, frontierFactIds));
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default) =>
            Task.FromResult(PersistenceResult.Success(changeSet.ExpectedRevision + 1));
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    [Fact]
    public void MonotonicClock_MeasuresPositiveElapsedTime()
    {
        var clock = MonotonicClock.Instance;
        var t1 = clock.GetTimestamp();
        Thread.Sleep(10);
        var elapsed = clock.GetElapsedTime(t1);

        Assert.True(elapsed.TotalMilliseconds >= 5);
    }

    [Fact]
    public async Task TrainingSession_CapturesPreciseLatencyBeforePersistence()
    {
        var fakeClock = new FakeClock
        {
            CurrentTimestamp = 1000,
            Elapsed = TimeSpan.FromMilliseconds(1450)
        };
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        Assert.Equal(1450, eval.LatencyMs);
        Assert.Equal(1450, session.LastResponseLatencyMs);
        Assert.Equal(1450, eval.ChangeSet.Attempt.ResponseLatencyMs);
    }

    [Fact]
    public async Task TrainingSession_ResetItemReadyTiming_ResetsMeasurementWindow()
    {
        var fakeClock = new FakeClock
        {
            CurrentTimestamp = 1000,
            Elapsed = TimeSpan.FromMilliseconds(50000) // Simulated long pause in Settings
        };
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        // Returning from Settings resets timing
        fakeClock.CurrentTimestamp = 51000;
        fakeClock.Elapsed = TimeSpan.FromMilliseconds(1200);
        session.ResetItemReadyTiming();

        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);

        // Latency is only the active response time (1200ms), NOT 50000ms
        Assert.Equal(1200, eval.LatencyMs);
        Assert.Equal(1200, session.LastResponseLatencyMs);
    }

    [Fact]
    public async Task TrainingSession_NextFactTransition_StartsFreshMeasurement()
    {
        var fakeClock = new FakeClock
        {
            CurrentTimestamp = 1000,
            Elapsed = TimeSpan.FromMilliseconds(1200)
        };
        var store = new InMemoryStore();
        var session = new TrainingSession(store, fakeClock);
        await session.InitializeAsync();

        session.SubmitAnswer(session.CurrentFact.CorrectResult);

        // Advance to next fact
        fakeClock.CurrentTimestamp = 3000;
        fakeClock.Elapsed = TimeSpan.FromMilliseconds(900);
        session.AdvanceToNextFact();

        var eval2 = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.Equal(900, eval2.LatencyMs);
        Assert.Equal(900, session.LastResponseLatencyMs);
    }
}
