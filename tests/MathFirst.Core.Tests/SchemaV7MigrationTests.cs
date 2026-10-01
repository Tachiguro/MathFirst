namespace MathFirst.Core.Tests;

using System.Data;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SchemaV7MigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstSchemaV7_" + Guid.NewGuid().ToString("N"));

    public SchemaV7MigrationTests() => Directory.CreateDirectory(_directory);

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
    public async Task FreshDatabase_InitializesAtSchemaV7_WithExactFiveContextColumns()
    {
        var path = Path.Combine(_directory, "fresh_v7.db");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var snapshot = await store.LoadSnapshotAsync();
            Assert.Equal(7, snapshot.SchemaVersion);
            Assert.Equal(7, LearnerProgression.DefaultSchemaVersion);
        }

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
            var version = await cmd.ExecuteScalarAsync();
            Assert.Equal("7", version);
        }

        var columns = await GetTableColumnsAsync(connection, "attempt_history");
        Assert.Equal(18, columns.Count);

        var expectedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
            "practice_position",
            "attempt_context_version",
            "presented_deadline_ms",
            "expected_pace_ms",
            "resolved_role",
            "operation_band_before"
        };

        var actualColumnNames = columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expectedColumns, actualColumnNames);
    }

    [Fact]
    public async Task SchemaV7_ContextColumns_HaveExactDeclaredTypesAndAreNullable()
    {
        var path = Path.Combine(_directory, "context_columns_types.db");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
        }

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        var columns = await GetTableColumnsAsync(connection, "attempt_history");
        var columnMap = columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        Assert.True(columnMap.TryGetValue("attempt_context_version", out var ctxVer));
        Assert.Equal("INTEGER", ctxVer.Type.ToUpperInvariant());
        Assert.False(ctxVer.NotNull);

        Assert.True(columnMap.TryGetValue("presented_deadline_ms", out var deadline));
        Assert.Equal("INTEGER", deadline.Type.ToUpperInvariant());
        Assert.False(deadline.NotNull);

        Assert.True(columnMap.TryGetValue("expected_pace_ms", out var pace));
        Assert.Equal("INTEGER", pace.Type.ToUpperInvariant());
        Assert.False(pace.NotNull);

        Assert.True(columnMap.TryGetValue("resolved_role", out var role));
        Assert.Equal("TEXT", role.Type.ToUpperInvariant());
        Assert.False(role.NotNull);

        Assert.True(columnMap.TryGetValue("operation_band_before", out var bandBefore));
        Assert.Equal("INTEGER", bandBefore.Type.ToUpperInvariant());
        Assert.False(bandBefore.NotNull);
    }

    [Fact]
    public async Task MigrateV6ToV7_AddsExactlyFiveNullableContextColumns()
    {
        var path = Path.Combine(_directory, "migrate_v6_to_v7.db");
        await CreateV6DatabaseAsync(path);

        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var snapshot = await store.LoadSnapshotAsync();
            Assert.Equal(7, snapshot.SchemaVersion);
        }

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
            Assert.Equal("7", await cmd.ExecuteScalarAsync());
        }

        var columns = await GetTableColumnsAsync(connection, "attempt_history");
        Assert.Equal(18, columns.Count);

        var expectedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
            "practice_position",
            "attempt_context_version",
            "presented_deadline_ms",
            "expected_pace_ms",
            "resolved_role",
            "operation_band_before"
        };

        var actualColumnNames = columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expectedColumns, actualColumnNames);
    }

    [Fact]
    public async Task MigrateV6ToV7_PreservesHistoricalRowsWithAllContextFieldsNull()
    {
        var path = Path.Combine(_directory, "historical_null_context.db");
        await CreateV6DatabaseAsync(path);

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position
                ) VALUES (
                    'v6-sub-1', 'add:2+3', 'Addition', 2, 3,
                    5, 5, 1, 1, 'Correct',
                    1200, '2026-09-15T10:00:00.0000000Z', 1
                );";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
        }

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position,
                    attempt_context_version, presented_deadline_ms, expected_pace_ms,
                    resolved_role, operation_band_before
                FROM attempt_history
                WHERE submission_id = 'v6-sub-1';";

            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            Assert.Equal("v6-sub-1", reader.GetString(0));
            Assert.Equal("add:2+3", reader.GetString(1));
            Assert.Equal("Addition", reader.GetString(2));
            Assert.Equal(2, reader.GetInt32(3));
            Assert.Equal(3, reader.GetInt32(4));
            Assert.Equal(5, reader.GetInt32(5));
            Assert.Equal(5, reader.GetInt32(6));
            Assert.Equal(1, reader.GetInt32(7));
            Assert.Equal(1, reader.GetInt32(8));
            Assert.Equal("Correct", reader.GetString(9));
            Assert.Equal(1200, reader.GetInt64(10));
            Assert.Equal("2026-09-15T10:00:00.0000000Z", reader.GetString(11));
            Assert.Equal(1, reader.GetInt64(12));

            // Context columns MUST all be NULL
            Assert.True(reader.IsDBNull(13), "attempt_context_version must be NULL for historical row");
            Assert.True(reader.IsDBNull(14), "presented_deadline_ms must be NULL for historical row");
            Assert.True(reader.IsDBNull(15), "expected_pace_ms must be NULL for historical row");
            Assert.True(reader.IsDBNull(16), "resolved_role must be NULL for historical row");
            Assert.True(reader.IsDBNull(17), "operation_band_before must be NULL for historical row");
        }
    }

    [Fact]
    public async Task MigrateV6ToV7_IsIdempotentWhenAllColumnsAlreadyExist()
    {
        var path = Path.Combine(_directory, "idempotent_migration.db");
        await CreateV6DatabaseAsync(path);

        using (var store1 = new SqliteLearnerStore(path))
        {
            await store1.InitializeAsync();
            await store1.CloseAsync();
        }

        // Second initialization on completed V7 store must be completely safe and idempotent
        using (var store2 = new SqliteLearnerStore(path))
        {
            await store2.InitializeAsync();
            var snapshot = await store2.LoadSnapshotAsync();
            Assert.Equal(7, snapshot.SchemaVersion);
            await store2.CloseAsync();
        }

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        var columns = await GetTableColumnsAsync(connection, "attempt_history");
        Assert.Equal(18, columns.Count);
    }

    [Fact]
    public async Task MigrateV6ToV7_RecoversFromPartiallyAppliedColumnSet()
    {
        var path = Path.Combine(_directory, "partial_v7_recovery.db");
        await CreateV6DatabaseAsync(path);

        // Manually simulate a crashed migration where only 2 of the 5 columns were added
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                ALTER TABLE attempt_history ADD COLUMN attempt_context_version INTEGER;
                ALTER TABLE attempt_history ADD COLUMN presented_deadline_ms INTEGER;
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position,
                    attempt_context_version, presented_deadline_ms
                ) VALUES (
                    'partial-sub-1', 'add:1+1', 'Addition', 1, 1,
                    2, 2, 1, 1, 'Correct',
                    1000, '2026-09-15T12:00:00.0000000Z', 1,
                    NULL, NULL
                );";
            await cmd.ExecuteNonQueryAsync();

            // Verify schema_version is still 6
            cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
            Assert.Equal("6", await cmd.ExecuteScalarAsync());
        }

        // Store initialization should recover by adding only the missing 3 columns and bumping version to 7
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var snapshot = await store.LoadSnapshotAsync();
            Assert.Equal(7, snapshot.SchemaVersion);
        }

        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
            Assert.Equal("7", await cmd.ExecuteScalarAsync());

            var columns = await GetTableColumnsAsync(connection, "attempt_history");
            Assert.Equal(18, columns.Count);

            cmd.CommandText = @"
                SELECT
                    attempt_context_version, presented_deadline_ms, expected_pace_ms,
                    resolved_role, operation_band_before
                FROM attempt_history
                WHERE submission_id = 'partial-sub-1';";
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.IsDBNull(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
            Assert.True(reader.IsDBNull(3));
            Assert.True(reader.IsDBNull(4));
        }
    }

    private static async Task<List<(string Name, string Type, bool NotNull)>> GetTableColumnsAsync(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = await cmd.ExecuteReaderAsync();
        var columns = new List<(string Name, string Type, bool NotNull)>();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            var type = reader.GetString(2);
            var notNull = reader.GetInt32(3) == 1;
            columns.Add((name, type, notNull));
        }
        return columns;
    }

    private static async Task CreateV6DatabaseAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE schema_info (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            INSERT INTO schema_info VALUES ('schema_version', '6');
            INSERT INTO schema_info VALUES ('store_revision', '1');

            CREATE TABLE learner_progression (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                practice_position INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL
            );
            INSERT INTO learner_progression VALUES (1, 0, '2026-09-10T00:00:00.0000000Z');

            CREATE TABLE operation_progression (
                operation TEXT PRIMARY KEY,
                band_index INTEGER NOT NULL CHECK (band_index >= 0),
                band_started_practice_position INTEGER NOT NULL CHECK (band_started_practice_position >= 0)
            );
            INSERT INTO operation_progression VALUES ('Addition', 0, 0);
            INSERT INTO operation_progression VALUES ('Subtraction', 0, 0);
            INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
            INSERT INTO operation_progression VALUES ('Division', 0, 0);

            CREATE TABLE item_learning_state (
                fact_id TEXT PRIMARY KEY,
                operation TEXT NOT NULL,
                left_operand INTEGER NOT NULL,
                right_operand INTEGER NOT NULL,
                total_attempts INTEGER NOT NULL,
                correct_attempts INTEGER NOT NULL,
                incorrect_attempts INTEGER NOT NULL,
                consecutive_correct INTEGER NOT NULL,
                last_latency_ms INTEGER NOT NULL,
                rolling_latency_ms INTEGER NOT NULL,
                fluent_streak INTEGER NOT NULL,
                is_mastered INTEGER NOT NULL,
                needs_remediation INTEGER NOT NULL,
                remediation_due_order INTEGER NOT NULL,
                last_practiced_order INTEGER NOT NULL,
                last_practiced_at TEXT
            );

            CREATE TABLE attempt_history (
                submission_id TEXT PRIMARY KEY,
                fact_id TEXT NOT NULL,
                operation TEXT NOT NULL,
                left_operand INTEGER NOT NULL,
                right_operand INTEGER NOT NULL,
                submitted_answer INTEGER,
                correct_answer INTEGER NOT NULL,
                is_correct INTEGER NOT NULL,
                is_fluent INTEGER NOT NULL CHECK (is_fluent IN (0, 1))
                    CHECK (is_fluent = 0 OR (is_correct = 1 AND outcome = 'Correct')),
                outcome TEXT NOT NULL DEFAULT 'Incorrect',
                response_latency_ms INTEGER NOT NULL,
                timestamp TEXT NOT NULL,
                practice_position INTEGER CHECK (practice_position IS NULL OR practice_position > 0)
            );

            CREATE TABLE fsrs_card_state (
                fact_id TEXT PRIMARY KEY,
                card_id TEXT NOT NULL,
                state INTEGER NOT NULL,
                step INTEGER,
                stability REAL,
                difficulty REAL,
                due_practice_position INTEGER NOT NULL,
                last_review_practice_position INTEGER,
                last_rating INTEGER
            );

            CREATE UNIQUE INDEX ux_attempt_history_practice_position
                ON attempt_history(practice_position) WHERE practice_position IS NOT NULL;
            CREATE INDEX ix_attempt_history_operation_practice_position
                ON attempt_history(operation, practice_position DESC) WHERE practice_position IS NOT NULL;
            CREATE INDEX ix_attempt_history_operation_fact_position
                ON attempt_history(operation, fact_id, practice_position DESC) WHERE practice_position IS NOT NULL;
        ";
        await cmd.ExecuteNonQueryAsync();
    }
}
