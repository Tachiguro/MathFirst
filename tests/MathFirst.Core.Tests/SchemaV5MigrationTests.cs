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
