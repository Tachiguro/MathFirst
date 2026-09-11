namespace MathFirst.Core.Tests;

using System.Reflection;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

public sealed class LongRunIndependentProgressionTests
{
    [Fact]
    public async Task PersistedSession_LoadsBoundedSelectionStateInsteadOfAllHistoricalCards()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MathFirstBoundedSelectionRed_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "learner.db");
        try
        {
            using (var store = new SqliteLearnerStore(path))
            {
                var session = new TrainingSession(store);
                await session.InitializeAsync(startTiming: false);
                for (var position = 1; position <= 500; position++)
                {
                    var fact = session.CurrentFact;
                    session.SubmitAnswer(fact.CorrectResult);
                    Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
                    Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: false));
                }
            }

            await SeedHistoricalMaterializationAsync(path, 800);

            using var reopened = new SqliteLearnerStore(path);
            var reopenedSession = new TrainingSession(reopened);
            await reopenedSession.InitializeAsync(startTiming: false);

            Assert.True(
                reopenedSession.ItemStates.Count <= 512,
                $"Selection state was history-sized: {reopenedSession.ItemStates.Count} item states were loaded.");
            Assert.True(
                reopenedSession.FsrsStates.Count <= 512,
                $"Selection state was history-sized: {reopenedSession.FsrsStates.Count} FSRS states were loaded.");

            var prospectivePosition = reopenedSession.Progression.PracticePosition + 1;
            var operation = AdaptivePracticeSelector.GetScheduledOperation(prospectivePosition);
            var curriculum = new ArithmeticCurriculum().GetCurriculum(operation);
            var progression = reopenedSession.Progression.OperationProgressions[operation];
            var ownedFrontier = new AcquisitionOwnershipResolver(curriculum).GetOwnedFrontier(progression.BandIndex);
            Assert.True(curriculum.TryGetBand(progression.BandIndex, out var band));
            var introductionFrontier = band!.Kind == CurriculumBandKind.Structured
                ? DeterministicFactRanker.SelectStructuredSample(ownedFrontier, operation, band.Id)
                : ownedFrontier;
            var evidence = await reopened.LoadPracticeSelectionEvidenceAsync(new PracticeSelectionEvidenceRequest(
                operation,
                prospectivePosition,
                1,
                ownedFrontier,
                introductionFrontier));
            Assert.Equal(64, evidence.DueCandidates.Count);
            Assert.Equal(64, evidence.MaintenanceCandidates.Count);
            Assert.Equal(64, evidence.RemediationCandidates.Count);
            Assert.Equal(64, evidence.AnyMaterializedCandidates.Count);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

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
    public async Task StrongFreshLearner_AdvancesInitialMultiplicationAndNaturallyReceivesFactorTwo()
    {
        using var store = new InMemoryLearnerStore();
        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        for (var position = 1; position <= 50; position++)
        {
            var fact = session.CurrentFact;
            session.SubmitAnswer(fact.CorrectResult);
            var result = await session.CommitCurrentEvaluationAsync();
            Assert.True(result.IsSuccess);

            if (position == 47)
            {
                Assert.True(session.LastEvaluation!.OperationAdvanced);
                Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
                Assert.All(
                    new[]
                    {
                        ArithmeticOperation.Addition,
                        ArithmeticOperation.Subtraction,
                        ArithmeticOperation.Division
                    },
                    operation => Assert.Equal(0, session.Progression.OperationProgressions[operation].BandIndex));
            }

            Assert.True(session.AdvanceAfterCorrectAnswer(startTiming: false));
        }

        Assert.Equal(51, session.Progression.PracticePosition + 1);
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        Assert.True(session.CurrentFact.LeftOperand == 2 || session.CurrentFact.RightOperand == 2);
        Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
    }

    [Fact]
    public async Task RehydratedMultiplicationBandZeroLearner_AdvancesWithoutResettingPersistedFactsOrFsrs()
    {
        using var store = new InMemoryLearnerStore(CreatePersistedMultiplicationSnapshot(
            bandIndex: 0,
            historicalAttemptCount: 11,
            historicalCorrectCount: 10));
        var originalSnapshot = store.Snapshot;
        var originalFactIds = originalSnapshot.ItemStates.Keys.ToHashSet(StringComparer.Ordinal);
        var originalFsrs = originalSnapshot.FsrsStates;
        var session = new TrainingSession(store);

        await session.InitializeAsync(startTiming: false);

        Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);
        var submittedFactId = session.CurrentFact.Id;

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);

        Assert.True(session.LastEvaluation!.OperationAdvanced);
        Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
        Assert.Equal(originalFactIds, session.ItemStates.Keys.ToHashSet(StringComparer.Ordinal));
        Assert.All(
            originalFactIds.Where(factId => factId != submittedFactId),
            factId => Assert.Equal(originalFsrs[factId], session.FsrsStates[factId]));
        Assert.All(session.Progression.OperationProgressions.Values, progression => Assert.True(progression.BandIndex >= 0));
    }

    [Fact]
    public async Task RehydratedMultiplicationBandZeroLearner_MissingBootstrapCorrectness_DoesNotAdvanceOrReset()
    {
        using var store = new InMemoryLearnerStore(CreatePersistedMultiplicationSnapshot(
            bandIndex: 0,
            historicalAttemptCount: 11,
            historicalCorrectCount: 9));
        var originalFactIds = store.Snapshot.ItemStates.Keys.ToHashSet(StringComparer.Ordinal);
        var session = new TrainingSession(store);

        await session.InitializeAsync(startTiming: false);
        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);

        Assert.False(session.LastEvaluation!.OperationAdvanced);
        Assert.Equal(0, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
        Assert.Equal(originalFactIds, session.ItemStates.Keys.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RehydratedMultiplicationBandOneLearner_UsesTheStandardFortyAttemptProfile()
    {
        using var store = new InMemoryLearnerStore(CreatePersistedMultiplicationSnapshot(
            bandIndex: 1,
            historicalAttemptCount: 39,
            historicalCorrectCount: 39));
        var session = new TrainingSession(store);

        await session.InitializeAsync(startTiming: false);
        Assert.Equal(1, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
        Assert.Equal(ArithmeticOperation.Multiplication, session.CurrentFact.Operation);

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);

        Assert.True(session.LastEvaluation!.OperationAdvanced);
        Assert.Equal(2, session.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
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

        public InMemoryLearnerStore(LearnerSnapshot? snapshot = null)
        {
            if (snapshot is null)
            {
                return;
            }

            _progression = snapshot.Progression;
            _revision = snapshot.Revision;
            foreach (var (factId, itemState) in snapshot.ItemStates)
            {
                _items[factId] = itemState;
            }
            foreach (var (factId, fsrsState) in snapshot.FsrsStates)
            {
                _fsrs[factId] = fsrsState;
            }
            _attempts.AddRange(snapshot.RecentAttempts);
        }

        public LearnerSnapshot Snapshot => new(
            _progression,
            _items,
            _fsrs,
            _attempts,
            _revision,
            LearnerProgression.DefaultSchemaVersion,
            _progression.OperationProgressions);

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

    private static LearnerSnapshot CreatePersistedMultiplicationSnapshot(
        int bandIndex,
        int historicalAttemptCount,
        int historicalCorrectCount)
    {
        var curriculum = new ArithmeticCurriculum().Multiplication;
        Assert.True(curriculum.TryGetBand(bandIndex, out var band));
        var frontier = band!.Frontier;
        var practicePosition = checked((historicalAttemptCount * 4) + 2);
        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = practicePosition;
        progression.StoreRevision = 7;
        progression.OperationProgressions[ArithmeticOperation.Multiplication] =
            new OperationProgression(ArithmeticOperation.Multiplication, bandIndex, 0);

        var attempts = Enumerable.Range(0, historicalAttemptCount)
            .Select(index =>
            {
                var fact = frontier[index % frontier.Count];
                var isCorrect = index < historicalCorrectCount;
                return new AttemptRecord(
                    $"persisted-{bandIndex}-{index}",
                    fact.Id,
                    fact.Operation,
                    fact.LeftOperand,
                    fact.RightOperand,
                    isCorrect ? fact.CorrectResult : fact.CorrectResult + 1,
                    fact.CorrectResult,
                    isCorrect,
                    isCorrect,
                    isCorrect ? 800 : 3_000,
                    DateTimeOffset.UnixEpoch.AddMinutes(index),
                    practicePosition: 3 + (index * 4));
            })
            .ToArray();
        var itemStates = frontier.ToDictionary(
            fact => fact.Id,
            fact => new ItemLearningState
            {
                FactId = fact.Id,
                Operation = fact.Operation,
                LeftOperand = fact.LeftOperand,
                RightOperand = fact.RightOperand,
                TotalAttempts = attempts.Count(attempt => attempt.FactId == fact.Id),
                CorrectAttempts = attempts.Count(attempt => attempt.FactId == fact.Id && attempt.IsCorrect),
                IncorrectAttempts = attempts.Count(attempt => attempt.FactId == fact.Id && !attempt.IsCorrect),
                ConsecutiveCorrectStreak = 1,
                LastLatencyMs = 800,
                RollingLatencyMs = 800,
                FluentStreak = 1,
                LastPracticedOrder = checked((int)attempts.Where(attempt => attempt.FactId == fact.Id).Select(attempt => attempt.PracticePosition!.Value).DefaultIfEmpty().Max())
            },
            StringComparer.Ordinal);
        var fsrsStates = frontier.ToDictionary(
            fact => fact.Id,
            fact => new FsrsCardState(fact.Id, Guid.NewGuid(), 2, null, 1, 1, practicePosition + 10, practicePosition - 1, FsrsRating.Good),
            StringComparer.Ordinal);

        return new LearnerSnapshot(
            progression,
            itemStates,
            fsrsStates,
            attempts,
            progression.StoreRevision,
            LearnerProgression.DefaultSchemaVersion,
            progression.OperationProgressions);
    }

    private static async Task SeedHistoricalMaterializationAsync(string path, int count)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        for (var index = 0; index < count; index++)
        {
            var fact = new ArithmeticFact(ArithmeticOperation.Addition, 1_000_000 + index, 1);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO item_learning_state (
                    fact_id, operation, left_operand, right_operand,
                    total_attempts, correct_attempts, incorrect_attempts,
                    consecutive_correct, last_latency_ms, rolling_latency_ms,
                    fluent_streak, is_mastered, needs_remediation,
                    remediation_due_order, last_practiced_order, last_practiced_at)
                VALUES (@fact_id, 'Addition', @left, 1, 1, 1, 0, 1, 900, 900, 1, 0, @needs_remediation, @remediation_due_order, @order, NULL);
                INSERT INTO fsrs_card_state (
                    fact_id, card_id, state, step, stability, difficulty,
                    due_practice_position, last_review_practice_position, last_rating)
                VALUES (@fact_id, @card_id, 2, NULL, 1.0, 1.0, @due_position, @order, 3);";
            command.Parameters.AddWithValue("@fact_id", fact.Id);
            command.Parameters.AddWithValue("@left", fact.LeftOperand);
            command.Parameters.AddWithValue("@card_id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("@order", index + 1);
            command.Parameters.AddWithValue("@needs_remediation", index % 4 == 1 ? 1 : 0);
            command.Parameters.AddWithValue("@remediation_due_order", index % 4 == 1 ? 1 : 0);
            command.Parameters.AddWithValue("@due_position", index % 4 == 0 ? 1 : 1_000_000);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}
