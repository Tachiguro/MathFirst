namespace MathFirst.Core.Tests;

using System.Reflection;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

public sealed class LongRunIndependentProgressionTests
{
    [Fact]
    public async Task StrongLearner_LongRun_UsesBoundedRuntimeEvidenceWithoutSelectorDeadEnds()
    {
        using var store = new InMemoryLearnerStore();
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        var operationSlots = Enum.GetValues<ArithmeticOperation>().ToDictionary(operation => operation, _ => 0);
        var advancements = new List<(ArithmeticOperation Operation, int Before, int After, long Position)>();

        for (var position = 1; position <= 2000; position++)
        {
            var fact = session.CurrentFact;
            var expectedOperation = AdaptivePracticeSelector.GetScheduledOperation(position);
            Assert.Equal(expectedOperation, fact.Operation);
            operationSlots[fact.Operation]++;

            var before = session.Progression.OperationProgressions[fact.Operation].BandIndex;
            session.SubmitAnswer(fact.CorrectResult);
            var result = await session.CommitCurrentEvaluationAsync();
            Assert.True(result.IsSuccess);
            Assert.Equal(position, session.Progression.PracticePosition);
            var after = session.Progression.OperationProgressions[fact.Operation].BandIndex;
            if (after > before)
            {
                advancements.Add((fact.Operation, before, after, position));
                Assert.Equal(before + 1, after);
                Assert.Equal(position, session.Progression.OperationProgressions[fact.Operation].BandStartedPracticePosition);
            }

            Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: false));
        }

        Assert.All(operationSlots.Values, slots => Assert.Equal(500, slots));
        Assert.NotEmpty(advancements);
        Assert.Contains(session.Progression.OperationProgressions.Values, progression => progression.BandIndex > 0);

        var recentAttempts = (IReadOnlyCollection<AttemptRecord>)typeof(TrainingSession)
            .GetField("_recentAttempts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(session)!;
        Assert.True(recentAttempts.Count <= 163, $"Runtime evidence was unbounded: {recentAttempts.Count} attempts retained.");
    }

    [Fact]
    public async Task PersistedLongRun_SnapshotIsBoundedWhileDurableHistoryIsPreserved()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstLongRun_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "learner.db");
        try
        {
            using (var store = new SqliteLearnerStore(path))
            {
                var session = new TrainingSession(store);
                await session.InitializeAsync(startTiming: false);
                for (var position = 1; position <= 200; position++)
                {
                    var fact = session.CurrentFact;
                    session.SubmitAnswer(fact.CorrectResult);
                    Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
                    Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: false));
                }
                await store.CloseAsync();
            }

            using (var reopened = new SqliteLearnerStore(path))
            {
                await reopened.InitializeAsync();
                var snapshot = await reopened.LoadSnapshotAsync();
                Assert.Equal(200, snapshot.Progression.PracticePosition);
                Assert.All(Enum.GetValues<ArithmeticOperation>(), operation =>
                    Assert.InRange(snapshot.RecentAttempts.Count(attempt => attempt.Operation == operation), 0, 40));
                await reopened.CloseAsync();
            }

            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM attempt_history WHERE practice_position IS NOT NULL;";
            Assert.Equal(200L, (long)(await count.ExecuteScalarAsync())!);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private sealed class InMemoryLearnerStore : ILearnerStore
    {
        private readonly Dictionary<string, ItemLearningState> _items = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FsrsCardState> _fsrs = new(StringComparer.Ordinal);
        private readonly List<AttemptRecord> _attempts = [];
        private readonly HashSet<string> _submissionIds = new(StringComparer.Ordinal);
        private LearnerProgression _progression = LearnerProgression.CreateFresh();
        private long _revision = 1;

        public string StoragePath => "inmemory://long-run";
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(
            new LearnerSnapshot(_progression, _items, _fsrs, _attempts, _revision, LearnerProgression.DefaultSchemaVersion, _progression.OperationProgressions));

        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default)
        {
            if (_submissionIds.Contains(changeSet.SubmissionId))
            {
                return Task.FromResult(PersistenceResult.Success(_revision));
            }
            if (changeSet.ExpectedRevision != _revision)
            {
                return Task.FromResult(PersistenceResult.Conflict("synthetic stale revision"));
            }

            _submissionIds.Add(changeSet.SubmissionId);
            _attempts.Add(changeSet.Attempt);
            _items[changeSet.UpdatedItemState.FactId] = changeSet.UpdatedItemState;
            if (changeSet.UpdatedFsrsState is not null)
            {
                _fsrs[changeSet.UpdatedFsrsState.FactId] = changeSet.UpdatedFsrsState;
            }
            _progression = changeSet.UpdatedProgression;
            _revision++;
            return Task.FromResult(PersistenceResult.Success(_revision));
        }
    }
}
