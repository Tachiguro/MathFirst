namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Application.Practice;
using MathFirst.Application.Scheduling;
using MathFirst.Domain;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class PaceCalibrationReadinessTests : IDisposable
{
    private readonly string _testDbDir;

    public PaceCalibrationReadinessTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstPaceCalibrationTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDbDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbDir))
            {
                Directory.Delete(_testDbDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup in temp directory
        }
    }

    private string GetTempDbPath() => Path.Combine(_testDbDir, $"test_{Guid.NewGuid():N}.db");

    private static async Task SeedAttemptHistoryAsync(
        string dbPath,
        IEnumerable<(string SubmissionId, string FactId, ArithmeticOperation Operation, int Left, int Right, int? Submitted, int Correct, bool IsCorrect, bool IsFluent, AttemptOutcome Outcome, long LatencyMs, long? PracticePosition)> attempts)
    {
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var transaction = conn.BeginTransaction();

        long maxPosition = 0;
        foreach (var att in attempts)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position
                ) VALUES (
                    @submission_id, @fact_id, @operation, @left_operand, @right_operand,
                    @submitted_answer, @correct_answer, @is_correct, @is_fluent, @outcome,
                    @response_latency_ms, @timestamp, @practice_position
                );";
            cmd.Parameters.AddWithValue("@submission_id", att.SubmissionId);
            cmd.Parameters.AddWithValue("@fact_id", att.FactId);
            cmd.Parameters.AddWithValue("@operation", att.Operation.ToString());
            cmd.Parameters.AddWithValue("@left_operand", att.Left);
            cmd.Parameters.AddWithValue("@right_operand", att.Right);
            cmd.Parameters.AddWithValue("@submitted_answer", (object?)att.Submitted ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@correct_answer", att.Correct);
            cmd.Parameters.AddWithValue("@is_correct", att.IsCorrect ? 1 : 0);
            cmd.Parameters.AddWithValue("@is_fluent", att.IsFluent ? 1 : 0);
            cmd.Parameters.AddWithValue("@outcome", att.Outcome.ToString());
            cmd.Parameters.AddWithValue("@response_latency_ms", att.LatencyMs);
            cmd.Parameters.AddWithValue("@timestamp", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@practice_position", (object?)att.PracticePosition ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();

            if (att.PracticePosition.HasValue && att.PracticePosition.Value > maxPosition)
            {
                maxPosition = att.PracticePosition.Value;
            }
        }

        if (maxPosition > 0)
        {
            using var progCmd = conn.CreateCommand();
            progCmd.Transaction = transaction;
            progCmd.CommandText = "UPDATE learner_progression SET practice_position = @pos WHERE id = 1;";
            progCmd.Parameters.AddWithValue("@pos", maxPosition);
            await progCmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static (string SubmissionId, string FactId, ArithmeticOperation Operation, int Left, int Right, int? Submitted, int Correct, bool IsCorrect, bool IsFluent, AttemptOutcome Outcome, long LatencyMs, long? PracticePosition)
        CreatePositionedCorrect(int position, long latencyMs = 1200) =>
        ($"sub-{position}", "add:0+1", ArithmeticOperation.Addition, 0, 1, 1, 1, true, latencyMs <= 2500, AttemptOutcome.Correct, latencyMs, position);

    private static (string SubmissionId, string FactId, ArithmeticOperation Operation, int Left, int Right, int? Submitted, int Correct, bool IsCorrect, bool IsFluent, AttemptOutcome Outcome, long LatencyMs, long? PracticePosition)
        CreatePositionedIncorrect(int position, long latencyMs = 3000) =>
        ($"sub-inc-{position}", "add:0+1", ArithmeticOperation.Addition, 0, 1, 99, 1, false, false, AttemptOutcome.Incorrect, latencyMs, position);

    private static (string SubmissionId, string FactId, ArithmeticOperation Operation, int Left, int Right, int? Submitted, int Correct, bool IsCorrect, bool IsFluent, AttemptOutcome Outcome, long LatencyMs, long? PracticePosition)
        CreatePositionedTimeout(int position, long latencyMs = 30000) =>
        ($"sub-to-{position}", "add:0+1", ArithmeticOperation.Addition, 0, 1, null, 1, false, false, AttemptOutcome.Timeout, latencyMs, position);

    private static (string SubmissionId, string FactId, ArithmeticOperation Operation, int Left, int Right, int? Submitted, int Correct, bool IsCorrect, bool IsFluent, AttemptOutcome Outcome, long LatencyMs, long? PracticePosition)
        CreateLegacyUnpositionedCorrect(string id, long latencyMs = 1200) =>
        ($"sub-legacy-{id}", "add:0+1", ArithmeticOperation.Addition, 0, 1, 1, 1, true, true, AttemptOutcome.Correct, latencyMs, null);

    private sealed class GatedFailingStore : ILearnerStore
    {
        private readonly SqliteLearnerStore _inner;
        public bool FailCommit { get; set; }

        public GatedFailingStore(string path)
        {
            _inner = new SqliteLearnerStore(path);
        }

        public string StoragePath => _inner.StoragePath;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            _inner.InitializeAsync(cancellationToken);

        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) =>
            _inner.LoadSnapshotAsync(cancellationToken);

        public Task<LearnerSnapshot> LoadRuntimeSnapshotAsync(CancellationToken cancellationToken = default) =>
            _inner.LoadRuntimeSnapshotAsync(cancellationToken);

        public Task<PracticeSelectionEvidence> LoadPracticeSelectionEvidenceAsync(
            PracticeSelectionEvidenceRequest request,
            CancellationToken cancellationToken = default) =>
            _inner.LoadPracticeSelectionEvidenceAsync(request, cancellationToken);

        public Task<IReadOnlyList<AttemptRecord>> LoadLatestFrontierAttemptsAsync(
            ArithmeticOperation operation,
            long bandStartedPracticePosition,
            IReadOnlyList<string> frontierFactIds,
            CancellationToken cancellationToken = default) =>
            _inner.LoadLatestFrontierAttemptsAsync(operation, bandStartedPracticePosition, frontierFactIds, cancellationToken);

        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default)
        {
            if (FailCommit)
            {
                return Task.FromResult(PersistenceResult.Unavailable("Simulated storage failure."));
            }
            return _inner.CommitSubmissionAsync(changeSet, cancellationToken);
        }

        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) =>
            _inner.ResetLearningProgressAsync(cancellationToken);

        public Task CloseAsync(CancellationToken cancellationToken = default) =>
            _inner.CloseAsync(cancellationToken);

        public void Dispose() => _inner.Dispose();
    }

    [Fact]
    public async Task PaceCalibration_23PositionedCorrectAttempts_IsNotReady()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            var attempts = Enumerable.Range(1, 23).Select(i => CreatePositionedCorrect(i));
            await SeedAttemptHistoryAsync(dbPath, attempts);

            var snapshot = await store.LoadRuntimeSnapshotAsync();
            Assert.Equal(23, snapshot.PositionedCorrectAttemptCount);
        }

        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store);
            await session.InitializeAsync(startTiming: false);

            Assert.Equal(23, session.PositionedCorrectAttemptCount);
            Assert.False(session.IsPaceCalibrationReady);
        }
    }

    [Fact]
    public async Task PaceCalibration_24PositionedCorrectAttempts_IsReady()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            var attempts = Enumerable.Range(1, 24).Select(i => CreatePositionedCorrect(i));
            await SeedAttemptHistoryAsync(dbPath, attempts);

            var snapshot = await store.LoadRuntimeSnapshotAsync();
            Assert.Equal(24, snapshot.PositionedCorrectAttemptCount);
        }

        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store);
            await session.InitializeAsync(startTiming: false);

            Assert.Equal(AdaptivePacePolicy.PaceCalibrationCorrectAttemptThreshold, session.PositionedCorrectAttemptCount);
            Assert.True(session.IsPaceCalibrationReady);
        }
    }

    [Fact]
    public async Task PaceCalibration_IncorrectAttempts_DoNotCount()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            var correctAttempts = Enumerable.Range(1, 23).Select(i => CreatePositionedCorrect(i));
            var incorrectAttempts = Enumerable.Range(24, 10).Select(i => CreatePositionedIncorrect(i));
            await SeedAttemptHistoryAsync(dbPath, correctAttempts.Concat(incorrectAttempts));

            var snapshot = await store.LoadRuntimeSnapshotAsync();
            Assert.Equal(23, snapshot.PositionedCorrectAttemptCount);
        }

        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store);
            await session.InitializeAsync(startTiming: false);

            Assert.Equal(23, session.PositionedCorrectAttemptCount);
            Assert.False(session.IsPaceCalibrationReady);
        }
    }

    [Fact]
    public async Task PaceCalibration_TimeoutAttempts_DoNotCount()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            var correctAttempts = Enumerable.Range(1, 23).Select(i => CreatePositionedCorrect(i));
            var timeoutAttempts = Enumerable.Range(24, 5).Select(i => CreatePositionedTimeout(i));
            await SeedAttemptHistoryAsync(dbPath, correctAttempts.Concat(timeoutAttempts));

            var snapshot = await store.LoadRuntimeSnapshotAsync();
            Assert.Equal(23, snapshot.PositionedCorrectAttemptCount);
        }

        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store);
            await session.InitializeAsync(startTiming: false);

            Assert.Equal(23, session.PositionedCorrectAttemptCount);
            Assert.False(session.IsPaceCalibrationReady);
        }
    }

    [Fact]
    public async Task PaceCalibration_UnpositionedCorrectAttempts_DoNotCount()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            var correctAttempts = Enumerable.Range(1, 23).Select(i => CreatePositionedCorrect(i));
            var legacyAttempts = Enumerable.Range(1, 10).Select(i => CreateLegacyUnpositionedCorrect(i.ToString()));
            await SeedAttemptHistoryAsync(dbPath, correctAttempts.Concat(legacyAttempts));

            var snapshot = await store.LoadRuntimeSnapshotAsync();
            Assert.Equal(23, snapshot.PositionedCorrectAttemptCount);
        }

        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store);
            await session.InitializeAsync(startTiming: false);

            Assert.Equal(23, session.PositionedCorrectAttemptCount);
            Assert.False(session.IsPaceCalibrationReady);
        }
    }

    [Fact]
    public async Task PaceCalibration_CorrectLatencyDoesNotAffectReadiness()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            // 12 fast (500ms) + 12 very slow (15000ms)
            var fastAttempts = Enumerable.Range(1, 12).Select(i => CreatePositionedCorrect(i, latencyMs: 500));
            var slowAttempts = Enumerable.Range(13, 12).Select(i => CreatePositionedCorrect(i, latencyMs: 15000));
            await SeedAttemptHistoryAsync(dbPath, fastAttempts.Concat(slowAttempts));

            var snapshot = await store.LoadRuntimeSnapshotAsync();
            Assert.Equal(24, snapshot.PositionedCorrectAttemptCount);
        }

        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store);
            await session.InitializeAsync(startTiming: false);

            Assert.Equal(24, session.PositionedCorrectAttemptCount);
            Assert.True(session.IsPaceCalibrationReady);
        }
    }

    [Fact]
    public async Task PaceCalibration_TwentyFourthSuccessfulCorrectCommit_EnablesReadinessImmediately()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();
        var attempts = Enumerable.Range(1, 23).Select(i => CreatePositionedCorrect(i));
        await SeedAttemptHistoryAsync(dbPath, attempts);

        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(23, session.PositionedCorrectAttemptCount);
        Assert.False(session.IsPaceCalibrationReady);

        // Submit the 24th answer (correct)
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);

        // Before commit: count remains 23 and readiness remains false
        Assert.Equal(23, session.PositionedCorrectAttemptCount);
        Assert.False(session.IsPaceCalibrationReady);

        // Commit persistence
        var persistResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(persistResult.IsSuccess);

        // Immediately after successful commit of #24: count becomes 24 and readiness is true
        Assert.Equal(24, session.PositionedCorrectAttemptCount);
        Assert.True(session.IsPaceCalibrationReady);
    }

    [Fact]
    public async Task PaceCalibration_FailedCorrectCommit_DoesNotAdvanceCalibration()
    {
        var dbPath = GetTempDbPath();
        using var store = new GatedFailingStore(dbPath);
        await store.InitializeAsync();
        var attempts = Enumerable.Range(1, 23).Select(i => CreatePositionedCorrect(i));
        await SeedAttemptHistoryAsync(dbPath, attempts);

        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        Assert.Equal(23, session.PositionedCorrectAttemptCount);
        Assert.False(session.IsPaceCalibrationReady);

        // Submit correct answer
        var eval = session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True(eval.IsCorrect);

        // Trigger failure on persistence
        store.FailCommit = true;
        var persistResult = await session.CommitCurrentEvaluationAsync();
        Assert.False(persistResult.IsSuccess);

        // Count must remain unchanged and readiness must remain false
        Assert.Equal(23, session.PositionedCorrectAttemptCount);
        Assert.False(session.IsPaceCalibrationReady);
    }

    [Fact]
    public async Task PaceCalibration_RetryAfterFailure_AdvancesExactlyOnce()
    {
        var dbPath = GetTempDbPath();
        using var store = new GatedFailingStore(dbPath);
        await store.InitializeAsync();
        var attempts = Enumerable.Range(1, 23).Select(i => CreatePositionedCorrect(i));
        await SeedAttemptHistoryAsync(dbPath, attempts);

        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        session.SubmitAnswer(session.CurrentFact.CorrectResult);

        // Fail first commit
        store.FailCommit = true;
        var failedResult = await session.CommitCurrentEvaluationAsync();
        Assert.False(failedResult.IsSuccess);
        Assert.Equal(23, session.PositionedCorrectAttemptCount);
        Assert.False(session.IsPaceCalibrationReady);

        // Unblock failure and recover/retry
        store.FailCommit = false;
        var recovered = await session.RecoverFromPersistenceFailureAsync();
        Assert.True(recovered);

        // Must increment exactly once to 24 and become ready
        Assert.Equal(24, session.PositionedCorrectAttemptCount);
        Assert.True(session.IsPaceCalibrationReady);
    }

    [Fact]
    public async Task PaceCalibration_AlreadyCommittedEvaluation_DoesNotIncrementTwice()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();
        var attempts = Enumerable.Range(1, 23).Select(i => CreatePositionedCorrect(i));
        await SeedAttemptHistoryAsync(dbPath, attempts);

        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        var commitResult = await session.CommitCurrentEvaluationAsync();
        Assert.True(commitResult.IsSuccess);
        Assert.Equal(24, session.PositionedCorrectAttemptCount);
        Assert.True(session.IsPaceCalibrationReady);

        // Repeated commit on already-committed submission
        var secondCommit = await session.CommitCurrentEvaluationAsync();
        Assert.True(secondCommit.IsSuccess);
        Assert.Equal(24, session.PositionedCorrectAttemptCount);
        Assert.True(session.IsPaceCalibrationReady);
    }

    [Fact]
    public async Task PaceCalibration_ReturningLearnerWith24CorrectAttempts_IsReadyImmediately()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            var attempts = Enumerable.Range(1, 24).Select(i => CreatePositionedCorrect(i));
            await SeedAttemptHistoryAsync(dbPath, attempts);
        }

        // Fresh store and fresh session (cold restart)
        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store);
            await session.InitializeAsync(startTiming: false);

            Assert.Equal(24, session.PositionedCorrectAttemptCount);
            Assert.True(session.IsPaceCalibrationReady);
        }
    }

    [Fact]
    public async Task PaceCalibration_ReturningLearnerBelowThreshold_IsNotReady()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            var attempts = Enumerable.Range(1, 23).Select(i => CreatePositionedCorrect(i));
            await SeedAttemptHistoryAsync(dbPath, attempts);
        }

        // Fresh store and fresh session (cold restart)
        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store);
            await session.InitializeAsync(startTiming: false);

            Assert.Equal(23, session.PositionedCorrectAttemptCount);
            Assert.False(session.IsPaceCalibrationReady);
        }
    }

    [Fact]
    public async Task PaceCalibration_BoundedCountSemantics_SaturatesAtThreshold()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteLearnerStore(dbPath))
        {
            await store.InitializeAsync();
            // Seed 50 qualifying attempts
            var attempts = Enumerable.Range(1, 50).Select(i => CreatePositionedCorrect(i));
            await SeedAttemptHistoryAsync(dbPath, attempts);

            var snapshot = await store.LoadRuntimeSnapshotAsync();
            // Saturated at threshold
            Assert.Equal(AdaptivePacePolicy.PaceCalibrationCorrectAttemptThreshold, snapshot.PositionedCorrectAttemptCount);
        }

        using (var store = new SqliteLearnerStore(dbPath))
        {
            var session = new TrainingSession(store);
            await session.InitializeAsync(startTiming: false);

            Assert.Equal(24, session.PositionedCorrectAttemptCount);
            Assert.True(session.IsPaceCalibrationReady);

            // Submitting and committing another correct attempt saturates and does not exceed threshold
            session.SubmitAnswer(session.CurrentFact.CorrectResult);
            var commitResult = await session.CommitCurrentEvaluationAsync();
            Assert.True(commitResult.IsSuccess);
            Assert.Equal(24, session.PositionedCorrectAttemptCount);
            Assert.True(session.IsPaceCalibrationReady);
        }
    }

    [Fact]
    public void PaceCalibration_LearnerSnapshot_ConstructorValidation()
    {
        var progression = LearnerProgression.CreateFresh();
        var itemStates = new Dictionary<string, ItemLearningState>();
        var fsrsStates = new Dictionary<string, FsrsCardState>();

        // Default count is 0
        var defaultSnapshot = new LearnerSnapshot(
            progression,
            itemStates,
            fsrsStates,
            [],
            1,
            LearnerProgression.DefaultSchemaVersion);
        Assert.Equal(0, defaultSnapshot.PositionedCorrectAttemptCount);

        // Explicit positive count
        var explicitSnapshot = new LearnerSnapshot(
            progression,
            itemStates,
            fsrsStates,
            [],
            1,
            LearnerProgression.DefaultSchemaVersion,
            positionedCorrectAttemptCount: 15);
        Assert.Equal(15, explicitSnapshot.PositionedCorrectAttemptCount);

        // Negative count throws ArgumentOutOfRangeException
        Assert.Throws<ArgumentOutOfRangeException>(() => new LearnerSnapshot(
            progression,
            itemStates,
            fsrsStates,
            [],
            1,
            LearnerProgression.DefaultSchemaVersion,
            positionedCorrectAttemptCount: -1));
    }

    [Fact]
    public async Task PaceCalibration_SchemaConformance_RemainsV6WithoutNewTablesOrColumns()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // 1. Schema version is exactly 6
        using (var versionCmd = conn.CreateCommand())
        {
            versionCmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
            var version = await versionCmd.ExecuteScalarAsync();
            Assert.Equal("6", version);
        }

        // 2. Expected tables only: schema_info, learner_progression, operation_progression, item_learning_state, attempt_history, fsrs_card_state
        var expectedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "schema_info",
            "learner_progression",
            "operation_progression",
            "item_learning_state",
            "attempt_history",
            "fsrs_card_state"
        };

        using (var tableCmd = conn.CreateCommand())
        {
            tableCmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            using var reader = await tableCmd.ExecuteReaderAsync();
            var actualTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync())
            {
                actualTables.Add(reader.GetString(0));
            }

            Assert.Equal(expectedTables, actualTables);
        }

        // 3. attempt_history columns check (no new calibration columns)
        var expectedAttemptColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "submission_id",
            "fact_id",
            "operation",
            "left_operand",
            "right_operand",
            "submitted_answer",
            "correct_answer",
            "is_correct",
            "is_fluent",
            "outcome",
            "response_latency_ms",
            "timestamp",
            "practice_position"
        };

        using (var colCmd = conn.CreateCommand())
        {
            colCmd.CommandText = "PRAGMA table_info(attempt_history);";
            using var reader = await colCmd.ExecuteReaderAsync();
            var actualColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync())
            {
                actualColumns.Add(reader.GetString(1));
            }

            Assert.Equal(expectedAttemptColumns, actualColumns);
        }
    }

    [Fact]
    public async Task PaceCalibration_ResetLearningProgress_ResetsReadiness()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteLearnerStore(dbPath);
        await store.InitializeAsync();
        var attempts = Enumerable.Range(1, 24).Select(i => CreatePositionedCorrect(i));
        await SeedAttemptHistoryAsync(dbPath, attempts);

        var session = new TrainingSession(store);
        await session.InitializeAsync(startTiming: false);
        Assert.True(session.IsPaceCalibrationReady);
        Assert.Equal(24, session.PositionedCorrectAttemptCount);

        // Reset progress
        await session.ResetLearningProgressAsync();

        Assert.Equal(0, session.PositionedCorrectAttemptCount);
        Assert.False(session.IsPaceCalibrationReady);
    }
}
