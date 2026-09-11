namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Domain;
using MathFirst.Infrastructure.Sqlite;

public sealed class BoundedSelectionIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstBoundedIntegration_" + Guid.NewGuid().ToString("N"));

    public BoundedSelectionIntegrationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task PeriodicRestart_ReplaysTheExactDurableSelectionSequence()
    {
        var continuous = await RunSequenceAsync(Path.Combine(_directory, "continuous.db"), 150, restartCadence: null);
        var restarted = await RunSequenceAsync(Path.Combine(_directory, "restarted.db"), 150, restartCadence: 25);

        Assert.Equal(continuous.Selections, restarted.Selections);
        Assert.Equal(continuous.PracticePosition, restarted.PracticePosition);
        Assert.Equal(continuous.Revision, restarted.Revision);
        Assert.Equal(continuous.Progressions, restarted.Progressions);
        Assert.Equal(continuous.NextSelection, restarted.NextSelection);
    }

    [Fact]
    public async Task MixedOutcomes_KeepWeakAdditionIndependentAndNeverStealOtherOperationSlots()
    {
        var path = Path.Combine(_directory, "mixed-weak.db");
        using var store = new SqliteLearnerStore(path);
        var clock = new ScriptedClock();
        var session = new TrainingSession(store, clock);
        await session.InitializeAsync(startTiming: false);
        var slots = Enum.GetValues<ArithmeticOperation>().ToDictionary(operation => operation, _ => 0);

        for (var position = 1; position <= 320; position++)
        {
            var fact = session.CurrentFact;
            Assert.Equal(AdaptivePracticeSelector.GetScheduledOperation(position), fact.Operation);
            slots[fact.Operation]++;
            clock.LatencyMs = fact.Operation == ArithmeticOperation.Addition
                ? 3_000
                : position % 17 == 0 ? 3_000 : 900;
            if (fact.Operation == ArithmeticOperation.Addition)
            {
                session.SubmitAnswer(fact.CorrectResult + 1);
            }
            else
            {
                session.SubmitAnswer(fact.CorrectResult);
            }

            Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
            Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: false) || session.InteractionState != SessionInteractionState.CorrectFeedback);
            if (session.InteractionState != SessionInteractionState.AwaitingAnswer)
            {
                session.AdvanceToNextFact(startTiming: false);
            }
        }

        Assert.All(slots.Values, count => Assert.Equal(80, count));
        Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
        Assert.Contains(
            session.Progression.OperationProgressions.Where(pair => pair.Key != ArithmeticOperation.Addition),
            pair => pair.Value.BandIndex > 0);
    }

    [Fact]
    public async Task UncommittedNewPresentation_DoesNotCreateDurableStateAcrossRestart()
    {
        var path = Path.Combine(_directory, "uncommitted-new.db");
        ArithmeticFact first;
        using (var store = new SqliteLearnerStore(path))
        {
            var session = new TrainingSession(store);
            await session.InitializeAsync(startTiming: false);
            first = session.CurrentFact;
            Assert.False(session.ItemStates.ContainsKey(first.Id));
        }

        using var reopened = new SqliteLearnerStore(path);
        await reopened.InitializeAsync();
        var snapshot = await reopened.LoadSnapshotAsync();
        Assert.Equal(0, snapshot.Progression.PracticePosition);
        Assert.Equal(1, snapshot.Revision);
        Assert.Empty(snapshot.RecentAttempts);
        Assert.Empty(snapshot.ItemStates);
        Assert.Empty(snapshot.FsrsStates);

        var reopenedSession = new TrainingSession(reopened);
        await reopenedSession.InitializeAsync(startTiming: false);
        Assert.Equal(first.Id, reopenedSession.CurrentFact.Id);
        reopenedSession.SubmitAnswer(reopenedSession.CurrentFact.CorrectResult);
        Assert.True((await reopenedSession.CommitCurrentEvaluationAsync()).IsSuccess);
        var committed = await reopened.LoadSnapshotAsync();
        Assert.Equal(1, committed.Progression.PracticePosition);
        Assert.True(committed.ItemStates.ContainsKey(first.Id));
        Assert.True(committed.FsrsStates.ContainsKey(first.Id));
    }

    [Fact]
    public async Task LongRunReplayAndStaleRevision_RemainIdempotentAndWriteFree()
    {
        var path = Path.Combine(_directory, "idempotency.db");
        using var primaryStore = new SqliteLearnerStore(path);
        var primary = new TrainingSession(primaryStore, new ScriptedClock());
        await primary.InitializeAsync(startTiming: false);
        SubmissionChangeSet? replay = null;
        for (var position = 1; position <= 80; position++)
        {
            primary.SubmitAnswer(primary.CurrentFact.CorrectResult);
            Assert.True((await primary.CommitCurrentEvaluationAsync()).IsSuccess);
            replay = primary.LastEvaluation!.ChangeSet;
            if (!primary.AdvanceAfterCorrectAnswer(startTiming: false))
            {
                Assert.Equal(SessionInteractionState.SessionCheckIn, primary.InteractionState);
                primary.ContinuePractice(startTiming: false);
            }
        }

        var beforeReplay = await primaryStore.LoadSnapshotAsync();
        Assert.True((await primaryStore.CommitSubmissionAsync(replay!)).IsSuccess);
        var afterReplay = await primaryStore.LoadSnapshotAsync();
        Assert.Equal(beforeReplay.Revision, afterReplay.Revision);
        Assert.Equal(beforeReplay.Progression.PracticePosition, afterReplay.Progression.PracticePosition);

        using var staleStore = new SqliteLearnerStore(path);
        var stale = new TrainingSession(staleStore, new ScriptedClock());
        await stale.InitializeAsync(startTiming: false);
        primary.SubmitAnswer(primary.CurrentFact.CorrectResult);
        Assert.True((await primary.CommitCurrentEvaluationAsync()).IsSuccess);
        var committedPosition = primary.Progression.PracticePosition;
        stale.SubmitAnswer(stale.CurrentFact.CorrectResult);
        var conflict = await stale.CommitCurrentEvaluationAsync();
        Assert.Equal(PersistenceStatus.RevisionConflict, conflict.Status);
        var afterConflict = await primaryStore.LoadSnapshotAsync();
        Assert.Equal(committedPosition, afterConflict.Progression.PracticePosition);
        Assert.Equal(primary.Progression.StoreRevision, afterConflict.Revision);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private static async Task<SequenceRun> RunSequenceAsync(string path, int length, int? restartCadence)
    {
        var selections = new List<string>();
        var clock = new ScriptedClock();
        SqliteLearnerStore? store = null;
        TrainingSession? session = null;
        try
        {
            async Task OpenAsync()
            {
                store = new SqliteLearnerStore(path);
                session = new TrainingSession(store, clock);
                await session.InitializeAsync(startTiming: false);
            }

            await OpenAsync();
            for (var position = 1; position <= length; position++)
            {
                var fact = session!.CurrentFact;
                selections.Add($"{fact.Operation}|{AdaptivePracticeSelector.GetRequestedRole(position)}|{fact.Id}");
                clock.LatencyMs = position % 19 == 0 ? 3_000 : 900;
                session.SubmitAnswer(fact.CorrectResult);
                Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
                if (!session.AdvanceAfterCorrectAnswer(startTiming: false))
                {
                    Assert.Equal(SessionInteractionState.SessionCheckIn, session.InteractionState);
                    session.ContinuePractice(startTiming: false);
                }

                if (restartCadence is not null && position % restartCadence.Value == 0 && position != length)
                {
                    store!.Dispose();
                    await OpenAsync();
                }
            }

            return new SequenceRun(
                selections,
                session!.Progression.PracticePosition,
                session.Progression.StoreRevision,
                session.Progression.OperationProgressions.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray(),
                $"{session.CurrentFact.Operation}|{AdaptivePracticeSelector.GetRequestedRole(session.Progression.PracticePosition + 1)}|{session.CurrentFact.Id}");
        }
        finally
        {
            store?.Dispose();
        }
    }

    private sealed record SequenceRun(
        IReadOnlyList<string> Selections,
        long PracticePosition,
        long Revision,
        IReadOnlyList<OperationProgression> Progressions,
        string NextSelection);

    private sealed class ScriptedClock : IClock
    {
        public long LatencyMs { get; set; } = 900;
        public long GetTimestamp() => 0;
        public TimeSpan GetElapsedTime(long startTimestamp) => TimeSpan.FromMilliseconds(LatencyMs);
    }
}
