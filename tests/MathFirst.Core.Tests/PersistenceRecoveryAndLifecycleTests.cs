namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using Xunit;

public sealed class PersistenceRecoveryAndLifecycleTests
{
    private sealed class FakeClock : IClock
    {
        private long _timestamp = 1_000_000;

        public long GetTimestamp() => _timestamp;

        public TimeSpan GetElapsedTime(long startTimestamp) =>
            TimeSpan.FromMilliseconds(Math.Max(0, _timestamp - startTimestamp));

        public void AdvanceMs(long milliseconds) => _timestamp += milliseconds;
    }

    private sealed class ControllableStore : ILearnerStore
    {
        private readonly TaskCompletionSource<PersistenceResult>? _pendingCommit;
        private readonly Queue<PersistenceResult> _results;

        public ControllableStore(
            IEnumerable<PersistenceResult>? results = null,
            TaskCompletionSource<PersistenceResult>? pendingCommit = null)
        {
            _results = new Queue<PersistenceResult>(results ?? []);
            _pendingCommit = pendingCommit;
        }

        public string StoragePath => "inmemory://persistence-recovery";
        public int LoadCount { get; private set; }
        public List<SubmissionChangeSet> Commits { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(new LearnerSnapshot(
                LearnerProgression.CreateFresh(),
                new Dictionary<string, ItemLearningState>(),
                new List<AttemptRecord>(),
                1,
                LearnerProgression.DefaultSchemaVersion));
        }

        public Task<PersistenceResult> CommitSubmissionAsync(
            SubmissionChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            Commits.Add(changeSet);
            if (_pendingCommit is not null)
            {
                return _pendingCommit.Task;
            }

            return Task.FromResult(_results.Dequeue());
        }

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task CorrectPersistenceFailure_EntersExplicitRecoveryState()
    {
        var store = new ControllableStore(
        [
            PersistenceResult.Unavailable("Synthetic transient failure."),
            PersistenceResult.Success(2)
        ]);
        var clock = new FakeClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var factBefore = session.CurrentFact;
        var orderBefore = session.SessionOrderCounter;
        clock.AdvanceMs(750);
        var evaluation = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var result = await session.CommitCurrentEvaluationAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(SessionInteractionState.PersistenceFailure, session.InteractionState);
        Assert.False(session.IsTimingActive);
        Assert.Equal(factBefore, session.CurrentFact);
        Assert.Equal(orderBefore, session.SessionOrderCounter);
        Assert.Equal(0, session.SessionCorrectCount);
        Assert.Equal(0, session.SessionTotalCount);
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.False(session.ItemStates.ContainsKey(factBefore.Id));
        Assert.Single(store.Commits);

        var recovered = await session.RecoverFromPersistenceFailureAsync();

        Assert.True(recovered);
        Assert.Equal(SessionInteractionState.CorrectFeedback, session.InteractionState);
        Assert.Equal(2, store.Commits.Count);
        Assert.Same(evaluation.ChangeSet, store.Commits[0]);
        Assert.Same(evaluation.ChangeSet, store.Commits[1]);
        Assert.Equal(store.Commits[0].SubmissionId, store.Commits[1].SubmissionId);
        Assert.Equal(1, session.SessionCorrectCount);
        Assert.Equal(1, session.SessionTotalCount);
        Assert.Equal(1, session.Progression.PracticePosition);
        Assert.Equal(1, session.ItemStates[factBefore.Id].TotalAttempts);
        Assert.True(session.FsrsStates.ContainsKey(factBefore.Id));

        Assert.True(session.AdvanceAfterCorrectAnswer());
        Assert.Equal(orderBefore + 1, session.SessionOrderCounter);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.True(session.IsTimingActive);
        Assert.Null(session.LastEvaluation);
    }

    [Fact]
    public async Task BackgroundDuringCorrectCommit_RequiresExplicitResumeForPreparedFact()
    {
        var completion = new TaskCompletionSource<PersistenceResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new FakeClock();
        var store = new ControllableStore(pendingCommit: completion);
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync();

        var evaluation = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var commit = session.CommitCurrentEvaluationAsync();
        Assert.Single(store.Commits);
        session.SetAppForeground(false);
        clock.AdvanceMs(600_000);
        completion.SetResult(PersistenceResult.Success(evaluation.ChangeSet.ExpectedRevision + 1));

        var result = await commit;
        Assert.True(result.IsSuccess);
        Assert.True(session.AdvanceAfterCorrectAnswer());
        var preparedFact = session.CurrentFact;
        var preparedOrder = session.SessionOrderCounter;
        var preparedDeadline = session.CurrentFactDeadlineMs;
        session.SetAppForeground(true);

        Assert.Single(store.Commits);
        Assert.Equal(1, session.SessionCorrectCount);
        Assert.Equal(1, session.SessionTotalCount);
        Assert.Equal(1, session.Progression.PracticePosition);
        Assert.Equal(2, preparedOrder);
        Assert.Equal(PracticeGateState.BackgroundResumeGate, session.PracticeGate);
        Assert.False(session.IsTimingActive);
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());

        clock.AdvanceMs(600_000);
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());
        Assert.Equal(preparedFact, session.CurrentFact);
        Assert.Equal(preparedOrder, session.SessionOrderCounter);

        session.StartOrResumePractice();
        Assert.True(session.IsTimingActive);
        Assert.Equal(preparedDeadline, session.CurrentFactDeadlineMs);
        Assert.Equal(0, session.GetCurrentActiveElapsedMs());

        clock.AdvanceMs(1_000);
        Assert.Equal(1_000, session.GetCurrentActiveElapsedMs());
    }

    [Fact]
    public async Task RevisionConflict_RecoversByReloadingAuthoritativeStateWithoutRetryingStaleChangeSet()
    {
        var store = new ControllableStore(
        [
            PersistenceResult.Conflict("Synthetic optimistic revision conflict.")
        ]);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        var evaluation = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var result = await session.CommitCurrentEvaluationAsync();

        Assert.Equal(PersistenceStatus.RevisionConflict, result.Status);
        Assert.Equal(SessionInteractionState.PersistenceFailure, session.InteractionState);
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Single(store.Commits);

        var recovered = await session.RecoverFromPersistenceFailureAsync();

        Assert.True(recovered);
        Assert.Equal(4, store.LoadCount);
        Assert.Single(store.Commits);
        Assert.Same(evaluation.ChangeSet, store.Commits[0]);
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.Equal(0, session.SessionCorrectCount);
        Assert.Equal(0, session.SessionTotalCount);
        Assert.Equal(SessionInteractionState.AwaitingAnswer, session.InteractionState);
        Assert.Null(session.LastEvaluation);
        Assert.True(session.IsTimingActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BackgroundDuringAcknowledgement_PreservesIncorrectAndTimeoutFeedback(bool timeout)
    {
        var store = new ControllableStore([PersistenceResult.Success(2)]);
        var session = new TrainingSession(store, new FakeClock());
        await session.InitializeAsync();

        if (timeout)
        {
            session.RecordTimeout();
        }
        else
        {
            session.SubmitAnswer(session.CurrentFact.CorrectResult + 1);
        }

        await session.CommitCurrentEvaluationAsync();
        var expectedState = timeout
            ? SessionInteractionState.TimeoutFeedback
            : SessionInteractionState.IncorrectFeedback;

        session.SetAppForeground(false);
        session.SetAppForeground(true);

        Assert.Equal(expectedState, session.InteractionState);
        Assert.Equal(PracticeGateState.Running, session.PracticeGate);
        Assert.False(session.IsTimingActive);
    }

    [Fact]
    public void PersistenceRecoveryDialog_IsBlockingLocalizedAndKeyboardReachable()
    {
        var home = File.ReadAllText(GetRepositoryPath(
            "src", "MathFirst.App", "Components", "Pages", "Home.razor"));

        Assert.Contains("SessionInteractionState.PersistenceFailure", home, StringComparison.Ordinal);
        Assert.Contains("class=\"practice-overlay persistence-error-dialog\"", home, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", home, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", home, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"persistence-error-title\"", home, StringComparison.Ordinal);
        Assert.Contains("@ref=\"_primaryOverlayButton\"", home, StringComparison.Ordinal);
        Assert.Contains("RecoverFromPersistenceFailureAsync", home, StringComparison.Ordinal);
        Assert.Contains("Training_PersistenceFailureTitle", home, StringComparison.Ordinal);
        Assert.Contains("Training_Retry", home, StringComparison.Ordinal);
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "MathFirst.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine([current!.FullName, .. segments]);
    }
}
