namespace MathFirst.Core.Tests;

using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using Microsoft.Data.Sqlite;

public sealed class SchemaV5MigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstSchemaV5_" + Guid.NewGuid().ToString("N"));

    public SchemaV5MigrationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task Fresh_store_creates_schema_v6_operation_progression_rows()
    {
        var path = Path.Combine(_directory, "fresh.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(6, snapshot.SchemaVersion);
        Assert.Null(snapshot.LatestAcceptedPracticeAt);
        await store.CloseAsync();

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT name FROM sqlite_master
            WHERE type = 'index' AND name IN (
                'ix_item_learning_state_operation_fact',
                'ix_item_learning_state_operation_remediation_order_fact',
                'ix_item_learning_state_operation_maintenance_fact',
                'ix_fsrs_card_state_due_fact')
            ORDER BY name;";
        using var reader = await command.ExecuteReaderAsync();
        var indexes = new List<string>();
        while (await reader.ReadAsync()) indexes.Add(reader.GetString(0));
        Assert.Equal(4, indexes.Count);
    }

    [Fact]
    public async Task V4_store_migrates_through_v5_to_v6_without_assigning_historical_positions()
    {
        var path = Path.Combine(_directory, "v4.db");
        await CreateV4DatabaseAsync(path);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(6, snapshot.SchemaVersion);
        Assert.Equal(17, snapshot.Progression.PracticePosition);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero),
            snapshot.LatestAcceptedPracticeAt);
        await store.CloseAsync();

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT practice_position, is_fluent FROM attempt_history WHERE submission_id = 'legacy';";
        using (var attemptReader = await command.ExecuteReaderAsync())
        {
            Assert.True(await attemptReader.ReadAsync());
            Assert.True(attemptReader.IsDBNull(0));
            Assert.Equal(1L, attemptReader.GetInt64(1));
        }
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name LIKE 'ix_item_learning_state_operation_%';";
        Assert.Equal(3L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Accepted_submission_persists_positive_practice_position()
    {
        var path = Path.Combine(_directory, "runtime.db");
        using var store = new SqliteLearnerStore(path);
        var session = new TrainingSession(store);
        await session.InitializeAsync();

        session.SubmitAnswer(session.CurrentFact.CorrectResult);
        Assert.True((await session.CommitCurrentEvaluationAsync()).IsSuccess);
        await store.CloseAsync();

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT practice_position FROM attempt_history;";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Failed_incorrect_submission_does_not_advance_in_memory_state()
    {
        var store = new FailingStore();
        var session = new TrainingSession(store);
        await session.InitializeAsync();
        var fact = session.CurrentFact;

        session.SubmitAnswer(fact.CorrectResult + 1);
        var result = await session.CommitCurrentEvaluationAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(0, session.Progression.PracticePosition);
        Assert.False(session.ItemStates.ContainsKey(fact.Id));
    }

    [Fact]
    public async Task Fresh_store_creates_ix_attempt_history_operation_fact_position_index()
    {
        var path = Path.Combine(_directory, "fresh_v6_index.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        await store.CloseAsync();

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT sql FROM sqlite_master
            WHERE type = 'index' AND name = 'ix_attempt_history_operation_fact_position';";
        var sql = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.Contains("attempt_history", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("operation", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fact_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("practice_position DESC", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("practice_position IS NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Existing_v6_store_repairs_ix_attempt_history_operation_fact_position_without_incrementing_store_revision()
    {
        var path = Path.Combine(_directory, "existing_v6_repair.db");
        await CreateV6DatabaseWithoutFactIndexAsync(path);

        // Verify index does NOT exist before store initialization
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var probe = connection.CreateCommand();
            probe.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_attempt_history_operation_fact_position';";
            Assert.Equal(0L, (long)(await probe.ExecuteScalarAsync())!);
        }

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(6, snapshot.SchemaVersion);
        Assert.Equal(5, snapshot.Revision);
        await store.CloseAsync();

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_attempt_history_operation_fact_position';";
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);

            command.CommandText = "SELECT value FROM schema_info WHERE key = 'store_revision';";
            Assert.Equal("5", Assert.IsType<string>(await command.ExecuteScalarAsync()));

            command.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
            Assert.Equal("6", Assert.IsType<string>(await command.ExecuteScalarAsync()));
        }
    }

    [Fact]
    public async Task V5_store_migration_creates_ix_attempt_history_operation_fact_position_immediately()
    {
        var path = Path.Combine(_directory, "v5_migrate_index.db");
        await CreateV5DatabaseAsync(path);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(6, snapshot.SchemaVersion);
        await store.CloseAsync();

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_attempt_history_operation_fact_position';";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task V4_store_migration_creates_ix_attempt_history_operation_fact_position_immediately()
    {
        var path = Path.Combine(_directory, "v4_migrate_index.db");
        await CreateV4DatabaseAsync(path);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(6, snapshot.SchemaVersion);
        await store.CloseAsync();

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_attempt_history_operation_fact_position';";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Reopening_and_initializing_v6_store_is_idempotent()
    {
        var path = Path.Combine(_directory, "v6_idempotent.db");
        using (var store1 = new SqliteLearnerStore(path))
        {
            await store1.InitializeAsync();
            await store1.CloseAsync();
        }

        using (var store2 = new SqliteLearnerStore(path))
        {
            await store2.InitializeAsync();
            var snapshot = await store2.LoadSnapshotAsync();
            Assert.Equal(6, snapshot.SchemaVersion);
            await store2.CloseAsync();
        }

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_attempt_history_operation_fact_position';";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    private static async Task CreateV6DatabaseWithoutFactIndexAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO schema_info VALUES ('schema_version', '6');
            INSERT INTO schema_info VALUES ('store_revision', '5');
            CREATE TABLE learner_progression (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                practice_position INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL);
            INSERT INTO learner_progression VALUES (1, 10, '2026-09-11T00:00:00Z');
            CREATE TABLE operation_progression (
                operation TEXT PRIMARY KEY,
                band_index INTEGER NOT NULL CHECK (band_index >= 0),
                band_started_practice_position INTEGER NOT NULL CHECK (band_started_practice_position >= 0));
            INSERT INTO operation_progression VALUES ('Addition', 0, 0);
            INSERT INTO operation_progression VALUES ('Subtraction', 0, 0);
            INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
            INSERT INTO operation_progression VALUES ('Division', 0, 0);
            CREATE TABLE item_learning_state (
                fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL,
                right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL,
                correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL,
                consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL,
                rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL,
                is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL,
                remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL,
                last_practiced_at TEXT);
            CREATE TABLE attempt_history (
                submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL,
                left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL,
                submitted_answer INTEGER, correct_answer INTEGER NOT NULL,
                is_correct INTEGER NOT NULL, is_fluent INTEGER NOT NULL, outcome TEXT NOT NULL DEFAULT 'Incorrect',
                response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL,
                practice_position INTEGER CHECK (practice_position IS NULL OR practice_position > 0));
            INSERT INTO attempt_history VALUES
                ('sub-1', 'add:1+1', 'Addition', 1, 1, 2, 2, 1, 1, 'Correct', 1000, '2026-09-11T00:00:01Z', 1);
            CREATE TABLE fsrs_card_state (
                fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL,
                step INTEGER, stability REAL, difficulty REAL,
                due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER,
                last_rating INTEGER);
            CREATE UNIQUE INDEX ux_attempt_history_practice_position
                ON attempt_history(practice_position) WHERE practice_position IS NOT NULL;
            CREATE INDEX ix_attempt_history_operation_practice_position
                ON attempt_history(operation, practice_position DESC) WHERE practice_position IS NOT NULL;";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateV5DatabaseAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO schema_info VALUES ('schema_version', '5');
            INSERT INTO schema_info VALUES ('store_revision', '9');
            CREATE TABLE learner_progression (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                practice_position INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL);
            INSERT INTO learner_progression VALUES (1, 37, '2026-09-11T00:00:00Z');
            CREATE TABLE operation_progression (
                operation TEXT PRIMARY KEY,
                band_index INTEGER NOT NULL CHECK (band_index >= 0),
                band_started_practice_position INTEGER NOT NULL CHECK (band_started_practice_position >= 0));
            INSERT INTO operation_progression VALUES ('Addition', 0, 0);
            INSERT INTO operation_progression VALUES ('Subtraction', 0, 0);
            INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
            INSERT INTO operation_progression VALUES ('Division', 0, 0);
            CREATE TABLE item_learning_state (
                fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL,
                right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL,
                correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL,
                consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL,
                rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL,
                is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL,
                remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL,
                last_practiced_at TEXT);
            CREATE TABLE attempt_history (
                submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL,
                left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL,
                submitted_answer INTEGER, correct_answer INTEGER NOT NULL,
                is_correct INTEGER NOT NULL, outcome TEXT NOT NULL DEFAULT 'Incorrect',
                response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL,
                practice_position INTEGER CHECK (practice_position IS NULL OR practice_position > 0));
            INSERT INTO attempt_history VALUES
                ('correct-2500', 'add:1+1', 'Addition', 1, 1, 2, 2, 1, 'Correct', 2500, '2026-09-11T00:00:01Z', NULL);
            CREATE TABLE fsrs_card_state (
                fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL,
                step INTEGER, stability REAL, difficulty REAL,
                due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER,
                last_rating INTEGER);
            CREATE UNIQUE INDEX ux_attempt_history_practice_position
                ON attempt_history(practice_position) WHERE practice_position IS NOT NULL;
            CREATE INDEX ix_attempt_history_operation_practice_position
                ON attempt_history(operation, practice_position DESC) WHERE practice_position IS NOT NULL;";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateV4DatabaseAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO schema_info VALUES ('schema_version', '4');
            INSERT INTO schema_info VALUES ('store_revision', '7');
            CREATE TABLE learner_progression (
                id INTEGER PRIMARY KEY CHECK (id = 1), current_operation TEXT NOT NULL,
                current_max_operand INTEGER NOT NULL, operation_max_operands_json TEXT NOT NULL,
                practice_position INTEGER NOT NULL, completed_checkpoint_level INTEGER NOT NULL,
                active_checkpoint_level INTEGER, checkpoint_attempt_count INTEGER NOT NULL,
                checkpoint_correct_count INTEGER NOT NULL, updated_at TEXT NOT NULL);
            INSERT INTO learner_progression VALUES (1, 'Addition', 4,
                '{""Addition"":4,""Subtraction"":3,""Multiplication"":2,""Division"":1}', 17, 0, NULL, 0, 0, '2026-09-09T00:00:00Z');
            CREATE TABLE item_learning_state (fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, total_attempts INTEGER NOT NULL, correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL, consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL, rolling_latency_ms INTEGER NOT NULL, fluent_streak INTEGER NOT NULL, is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL, remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL, last_practiced_at TEXT);
            CREATE TABLE attempt_history (submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL, submitted_answer INTEGER, correct_answer INTEGER NOT NULL, is_correct INTEGER NOT NULL, outcome TEXT NOT NULL, response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL);
            INSERT INTO attempt_history VALUES ('legacy', 'add:1+1', 'Addition', 1, 1, 2, 2, 1, 'Correct', 1000, '2026-09-09T00:00:00Z');
            CREATE TABLE fsrs_card_state (fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL, step INTEGER, stability REAL, difficulty REAL, due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER, last_rating INTEGER);";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FailingStore : ILearnerStore
    {
        public string StoragePath => "inmemory://failure";
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LearnerSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(new LearnerSnapshot(LearnerProgression.CreateFresh(), new Dictionary<string, ItemLearningState>(), [], 1, LearnerProgression.DefaultSchemaVersion));
        public Task<PersistenceResult> CommitSubmissionAsync(SubmissionChangeSet changeSet, CancellationToken cancellationToken = default) => Task.FromResult(PersistenceResult.Unavailable("synthetic failure"));
        public Task ResetLearningProgressAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
