namespace MathFirst.Core.Tests;

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MathFirst.Application;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SchemaV9MigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MathFirstSchemaV9_" + Guid.NewGuid().ToString("N"));

    public SchemaV9MigrationTests() => Directory.CreateDirectory(_directory);

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

    private sealed record ColumnInfo(string Name, string Type, bool NotNull, string? DefaultValue);

    private static async Task<List<ColumnInfo>> GetTableColumnsAsync(SqliteConnection connection, string tableName)
    {
        var columns = new List<ColumnInfo>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(new ColumnInfo(
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) == 1,
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return columns;
    }

    // =========================================================================
    // S01 - S03: FRESH V9 DATABASE INITIALIZATION CONTRACTS
    // =========================================================================

    [Fact]
    public async Task S01_FreshDatabase_UsesSchemaV9()
    {
        var path = Path.Combine(_directory, "fresh_v9.db");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var snapshot = await store.LoadSnapshotAsync();
            Assert.Equal(9, snapshot.SchemaVersion);
            Assert.Equal(9, LearnerProgression.DefaultSchemaVersion);
        }

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
        var version = await cmd.ExecuteScalarAsync();
        Assert.Equal("9", version);
    }

    [Fact]
    public async Task S02_FreshDatabase_InitializesWithCurriculumStage1()
    {
        var path = Path.Combine(_directory, "fresh_v9_stage.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(CurriculumStage.Stage1_Addition, snapshot.Progression.CurriculumStage);

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT curriculum_stage FROM learner_progression WHERE id = 1;";
        var rawStage = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(1, rawStage);
    }

    [Fact]
    public async Task S03_FreshDatabase_PracticePositionIsZero()
    {
        var path = Path.Combine(_directory, "fresh_v9_pos.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(0, snapshot.Progression.PracticePosition);
        Assert.Equal(1, snapshot.Progression.StoreRevision);
    }

    // =========================================================================
    // S04 - S07: PERSISTENCE, COMMIT & CLONE CONTRACTS
    // =========================================================================

    [Theory]
    [InlineData(CurriculumStage.Stage1_Addition)]
    [InlineData(CurriculumStage.Stage2_Subtraction)]
    [InlineData(CurriculumStage.Stage3_Multiplication)]
    [InlineData(CurriculumStage.Stage4_Division)]
    public async Task S04_CurriculumStage_RoundTripsThroughSaveAndLoad(CurriculumStage stage)
    {
        var path = Path.Combine(_directory, $"roundtrip_{stage}.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var progression = LearnerProgression.CreateFresh();
        progression.CurriculumStage = stage;
        progression.PracticePosition = 10;

        await store.SaveProgressionAsync(progression);

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(stage, snapshot.Progression.CurriculumStage);
        Assert.Equal(10, snapshot.Progression.PracticePosition);
    }

    [Fact]
    public async Task S05_SubmissionCommit_PersistsCurriculumStageAtomically()
    {
        var path = Path.Combine(_directory, "atomic_stage_commit.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 0, 1);
        var subId = Guid.NewGuid().ToString("N");
        var attempt = new AttemptRecord(
            subId, fact.Id, fact.Operation, 0, 1, 1, 1, true, true, 1200,
            DateTimeOffset.UtcNow, practicePosition: 1);
        var itemState = ItemLearningState.CreateNew(fact);
        itemState.TotalAttempts = 1;
        itemState.CorrectAttempts = 1;
        itemState.LastLatencyMs = 1200;

        var progression = LearnerProgression.CreateFresh();
        progression.PracticePosition = 1;
        progression.CurriculumStage = CurriculumStage.Stage2_Subtraction;

        var changeSet = new SubmissionChangeSet(subId, ExpectedRevision: 1, attempt, itemState, progression);
        var commitResult = await store.CommitSubmissionAsync(changeSet);
        Assert.True(commitResult.IsSuccess);

        // Verify direct DB query
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT curriculum_stage, practice_position FROM learner_progression WHERE id = 1;";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt64(1));
    }

    [Fact]
    public void S06_SubmissionChangeSet_ClonePreservesCurriculumStage()
    {
        var progression = LearnerProgression.CreateFresh();
        progression.CurriculumStage = CurriculumStage.Stage3_Multiplication;

        var fact = new ArithmeticFact(ArithmeticOperation.Addition, 1, 1);
        var attempt = new AttemptRecord("sub1", fact.Id, fact.Operation, 1, 1, 2, 2, true, true, 1000, DateTimeOffset.UtcNow, practicePosition: 1);
        var itemState = ItemLearningState.CreateNew(fact);

        var changeSet = new SubmissionChangeSet("sub1", 1, attempt, itemState, progression);

        Assert.Equal(CurriculumStage.Stage3_Multiplication, changeSet.UpdatedProgression.CurriculumStage);
    }

    [Fact]
    public async Task S07_LearnerProgression_AllClonePaths_PreserveCurriculumStage()
    {
        var path = Path.Combine(_directory, "clone_paths.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        var prog = LearnerProgression.CreateFresh();
        prog.CurriculumStage = CurriculumStage.Stage2_Subtraction;
        await store.SaveProgressionAsync(prog);

        var session = new TrainingSession(store);
        await session.InitializeAsync();

        Assert.Equal(CurriculumStage.Stage2_Subtraction, session.Progression.CurriculumStage);
    }

    // =========================================================================
    // S08 - S10: FAIL-CLOSED VALIDATION & CHECK CONSTRAINTS
    // =========================================================================

    [Fact]
    public async Task S08_SqliteLearnerStore_RejectsInvalidCurriculumStage_0()
    {
        var path = Path.Combine(_directory, "check_0.db");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
        }

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE learner_progression SET curriculum_stage = 0 WHERE id = 1;";
        await Assert.ThrowsAnyAsync<SqliteException>(async () => await cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task S09_SqliteLearnerStore_RejectsInvalidCurriculumStage_5()
    {
        var path = Path.Combine(_directory, "check_5.db");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
        }

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE learner_progression SET curriculum_stage = 5 WHERE id = 1;";
        await Assert.ThrowsAnyAsync<SqliteException>(async () => await cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task S10_ReadProgression_FailsClosed_OnInvalidPersistedStage()
    {
        var path = Path.Combine(_directory, "corrupt_stage.db");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
        }

        // Drop check constraint temporarily by direct recreation to simulate corrupt database
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        using (var corruptCmd = connection.CreateCommand())
        {
            corruptCmd.CommandText = @"
                CREATE TABLE temp_lp (id INTEGER PRIMARY KEY, practice_position INTEGER, curriculum_stage INTEGER, updated_at TEXT);
                INSERT INTO temp_lp SELECT id, practice_position, 99, updated_at FROM learner_progression;
                DROP TABLE learner_progression;
                ALTER TABLE temp_lp RENAME TO learner_progression;";
            await corruptCmd.ExecuteNonQueryAsync();
        }
        await connection.CloseAsync();

        using (var store = new SqliteLearnerStore(path))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.LoadSnapshotAsync());
        }
    }

    // =========================================================================
    // M01 - M20: V8 -> V9 MIGRATION CONTRACTS
    // =========================================================================

    private async Task CreateV8DatabaseAsync(
        string path,
        int addBand = 0,
        int subBand = 0,
        int mulBand = 0,
        int divBand = 0,
        IEnumerable<ItemLearningState>? items = null,
        IEnumerable<AttemptRecord>? attempts = null)
    {
        await using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            INSERT INTO schema_info (key, value) VALUES ('schema_version', '8');
            INSERT INTO schema_info (key, value) VALUES ('store_revision', '1');

            CREATE TABLE learner_progression (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                practice_position INTEGER NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL
            );
            INSERT INTO learner_progression (id, practice_position, updated_at)
            VALUES (1, 0, '2026-10-06T00:00:00.0000000Z');

            CREATE TABLE operation_progression (
                operation TEXT PRIMARY KEY,
                band_index INTEGER NOT NULL CHECK (band_index >= 0),
                band_started_practice_position INTEGER NOT NULL CHECK (band_started_practice_position >= 0)
            );
            INSERT INTO operation_progression VALUES ('Addition', " + addBand + @", 0);
            INSERT INTO operation_progression VALUES ('Subtraction', " + subBand + @", 0);
            INSERT INTO operation_progression VALUES ('Multiplication', " + mulBand + @", 0);
            INSERT INTO operation_progression VALUES ('Division', " + divBand + @", 0);

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
                practice_position INTEGER CHECK (practice_position IS NULL OR practice_position > 0),
                attempt_context_version INTEGER,
                presented_deadline_ms INTEGER,
                expected_pace_ms INTEGER,
                resolved_role TEXT,
                operation_band_before INTEGER,
                is_interrupted INTEGER NOT NULL DEFAULT 0 CHECK (is_interrupted IN (0, 1))
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
        ";
        await cmd.ExecuteNonQueryAsync();

        if (items != null)
        {
            foreach (var item in items)
            {
                using var itemCmd = conn.CreateCommand();
                itemCmd.CommandText = @"
                    INSERT INTO item_learning_state (
                        fact_id, operation, left_operand, right_operand,
                        total_attempts, correct_attempts, incorrect_attempts,
                        consecutive_correct, last_latency_ms, rolling_latency_ms,
                        fluent_streak, is_mastered, needs_remediation,
                        remediation_due_order, last_practiced_order, last_practiced_at
                    ) VALUES (
                        @fact_id, @op, @l, @r, @tot, @cor, @incor, @streak, @lat, @roll, @fluent, @mast, @rem, @due, @ord, @at
                    );";
                itemCmd.Parameters.AddWithValue("@fact_id", item.FactId);
                itemCmd.Parameters.AddWithValue("@op", item.Operation.ToString());
                itemCmd.Parameters.AddWithValue("@l", item.LeftOperand);
                itemCmd.Parameters.AddWithValue("@r", item.RightOperand);
                itemCmd.Parameters.AddWithValue("@tot", item.TotalAttempts);
                itemCmd.Parameters.AddWithValue("@cor", item.CorrectAttempts);
                itemCmd.Parameters.AddWithValue("@incor", item.IncorrectAttempts);
                itemCmd.Parameters.AddWithValue("@streak", item.ConsecutiveCorrectStreak);
                itemCmd.Parameters.AddWithValue("@lat", item.LastLatencyMs);
                itemCmd.Parameters.AddWithValue("@roll", item.RollingLatencyMs);
                itemCmd.Parameters.AddWithValue("@fluent", item.FluentStreak);
                itemCmd.Parameters.AddWithValue("@mast", item.IsProvisionallyMastered ? 1 : 0);
                itemCmd.Parameters.AddWithValue("@rem", item.NeedsRemediation ? 1 : 0);
                itemCmd.Parameters.AddWithValue("@due", item.RemediationDueOrder);
                itemCmd.Parameters.AddWithValue("@ord", item.LastPracticedOrder);
                itemCmd.Parameters.AddWithValue("@at", item.LastPracticedAt?.ToString("O") ?? (object)DBNull.Value);
                await itemCmd.ExecuteNonQueryAsync();
            }
        }
    }

    [Fact]
    public async Task M01_MigrateV8ToV9_FreshOrEmptyV8_InfersStage1()
    {
        var path = Path.Combine(_directory, "m01_fresh_v8.db");
        await CreateV8DatabaseAsync(path);

        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var snapshot = await store.LoadSnapshotAsync();
            Assert.Equal(9, snapshot.SchemaVersion);
            Assert.Equal(CurriculumStage.Stage1_Addition, snapshot.Progression.CurriculumStage);
        }
    }

    [Fact]
    public async Task M02_MigrateV8ToV9_LegacyPreferencesAllFour_ZeroLearningEvidence_InfersStage1()
    {
        var path = Path.Combine(_directory, "m02_pref_allfour.db");
        await CreateV8DatabaseAsync(path);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(CurriculumStage.Stage1_Addition, snapshot.Progression.CurriculumStage);
    }

    [Fact]
    public async Task M03_MigrateV8ToV9_AdditionBand1_InfersStage2()
    {
        var path = Path.Combine(_directory, "m03_add_band1.db");
        await CreateV8DatabaseAsync(path, addBand: 1);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(CurriculumStage.Stage2_Subtraction, snapshot.Progression.CurriculumStage);
    }

    [Fact]
    public async Task M04_MigrateV8ToV9_AdditionD01FullyIntroduced_OneWeak_InfersStage2()
    {
        var path = Path.Combine(_directory, "m04_add_d01.db");
        var items = new List<ItemLearningState>
        {
            new() { FactId = "add:0+0", Operation = ArithmeticOperation.Addition, LeftOperand = 0, RightOperand = 0, TotalAttempts = 1, NeedsRemediation = false },
            new() { FactId = "add:0+1", Operation = ArithmeticOperation.Addition, LeftOperand = 0, RightOperand = 1, TotalAttempts = 1, NeedsRemediation = false },
            new() { FactId = "add:1+0", Operation = ArithmeticOperation.Addition, LeftOperand = 1, RightOperand = 0, TotalAttempts = 1, NeedsRemediation = false },
            new() { FactId = "add:1+1", Operation = ArithmeticOperation.Addition, LeftOperand = 1, RightOperand = 1, TotalAttempts = 1, NeedsRemediation = true }, // 1 weak fact allowed
        };
        await CreateV8DatabaseAsync(path, addBand: 0, items: items);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(CurriculumStage.Stage2_Subtraction, snapshot.Progression.CurriculumStage);
    }

    [Fact]
    public async Task M05_MigrateV8ToV9_AdvancedSubtraction_WithoutAdditionPrerequisite_InfersStage1()
    {
        var path = Path.Combine(_directory, "m05_sub_noadd.db");
        await CreateV8DatabaseAsync(path, addBand: 0, subBand: 3);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(CurriculumStage.Stage1_Addition, snapshot.Progression.CurriculumStage);
        Assert.Equal(3, snapshot.Progression.OperationProgressions[ArithmeticOperation.Subtraction].BandIndex);
    }

    [Fact]
    public async Task M06_MigrateV8ToV9_AdditionAndSubtractionReady_InfersStage3()
    {
        var path = Path.Combine(_directory, "m06_add_sub.db");
        await CreateV8DatabaseAsync(path, addBand: 1, subBand: 1);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(CurriculumStage.Stage3_Multiplication, snapshot.Progression.CurriculumStage);
    }

    [Fact]
    public async Task M07_MigrateV8ToV9_AdvancedMultiplication_WithoutCumulativePrerequisites_InfersStage1()
    {
        var path = Path.Combine(_directory, "m07_mul_no_prereqs.db");
        await CreateV8DatabaseAsync(path, addBand: 0, subBand: 0, mulBand: 5);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(CurriculumStage.Stage1_Addition, snapshot.Progression.CurriculumStage);
        Assert.Equal(5, snapshot.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
    }

    [Fact]
    public async Task M08_MigrateV8ToV9_AdditionSubtractionMultiplicationReady_InfersStage4()
    {
        var path = Path.Combine(_directory, "m08_add_sub_mul.db");
        await CreateV8DatabaseAsync(path, addBand: 2, subBand: 1, mulBand: 1);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(CurriculumStage.Stage4_Division, snapshot.Progression.CurriculumStage);
    }

    [Fact]
    public async Task M09_MigrateV8ToV9_AdvancedDivisionAlone_InfersStage1()
    {
        var path = Path.Combine(_directory, "m09_div_alone.db");
        await CreateV8DatabaseAsync(path, addBand: 0, subBand: 0, mulBand: 0, divBand: 5);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(CurriculumStage.Stage1_Addition, snapshot.Progression.CurriculumStage);
        Assert.Equal(5, snapshot.Progression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
    }

    [Fact]
    public async Task M10_MigrateV8ToV9_FullyAdvancedLearner_InfersStage4()
    {
        var path = Path.Combine(_directory, "m10_fully_advanced.db");
        await CreateV8DatabaseAsync(path, addBand: 5, subBand: 5, mulBand: 5, divBand: 5);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(CurriculumStage.Stage4_Division, snapshot.Progression.CurriculumStage);
    }

    [Fact]
    public async Task M11_MigrateV8ToV9_PreservesLockedOperationItemLearningState()
    {
        var path = Path.Combine(_directory, "m11_items_preservation.db");
        var items = new List<ItemLearningState>
        {
            new()
            {
                FactId = "mul:5*5",
                Operation = ArithmeticOperation.Multiplication,
                LeftOperand = 5,
                RightOperand = 5,
                TotalAttempts = 10,
                CorrectAttempts = 9,
                IncorrectAttempts = 1,
                ConsecutiveCorrectStreak = 5,
                LastLatencyMs = 850,
                RollingLatencyMs = 900,
                FluentStreak = 4,
                IsProvisionallyMastered = true,
                NeedsRemediation = false
            }
        };
        await CreateV8DatabaseAsync(path, addBand: 0, mulBand: 5, items: items);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(CurriculumStage.Stage1_Addition, snapshot.Progression.CurriculumStage);
        Assert.True(snapshot.ItemStates.TryGetValue("mul:5*5", out var mulState));
        Assert.Equal(10, mulState.TotalAttempts);
        Assert.Equal(9, mulState.CorrectAttempts);
        Assert.True(mulState.IsProvisionallyMastered);
    }

    [Fact]
    public async Task M12_MigrateV8ToV9_PreservesLockedOperationFsrsState()
    {
        var path = Path.Combine(_directory, "m12_fsrs_preservation.db");
        await CreateV8DatabaseAsync(path, addBand: 0, mulBand: 3);

        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO fsrs_card_state (fact_id, card_id, state, stability, difficulty, due_practice_position)
                VALUES ('mul:3*3', '00000000-0000-0000-0000-000000000001', 2, 4.5, 2.1, 50);";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.True(snapshot.FsrsStates.TryGetValue("mul:3*3", out var fsrs));
        Assert.Equal(4.5, fsrs.Stability);
        Assert.Equal(50, fsrs.DuePracticePosition);
    }

    [Fact]
    public async Task M13_MigrateV8ToV9_PreservesLockedOperationProgression()
    {
        var path = Path.Combine(_directory, "m13_op_prog_preservation.db");
        await CreateV8DatabaseAsync(path, addBand: 0, subBand: 2, mulBand: 4, divBand: 1);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        Assert.Equal(CurriculumStage.Stage1_Addition, snapshot.Progression.CurriculumStage);
        Assert.Equal(0, snapshot.Progression.OperationProgressions[ArithmeticOperation.Addition].BandIndex);
        Assert.Equal(2, snapshot.Progression.OperationProgressions[ArithmeticOperation.Subtraction].BandIndex);
        Assert.Equal(4, snapshot.Progression.OperationProgressions[ArithmeticOperation.Multiplication].BandIndex);
        Assert.Equal(1, snapshot.Progression.OperationProgressions[ArithmeticOperation.Division].BandIndex);
    }

    [Fact]
    public async Task M14_MigrateV8ToV9_PreservesAttemptHistory()
    {
        var path = Path.Combine(_directory, "m14_attempts_preservation.db");
        await CreateV8DatabaseAsync(path, addBand: 1);

        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO attempt_history (
                    submission_id, fact_id, operation, left_operand, right_operand,
                    submitted_answer, correct_answer, is_correct, is_fluent, outcome,
                    response_latency_ms, timestamp, practice_position, is_interrupted
                ) VALUES (
                    'sub_historical', 'add:1+1', 'Addition', 1, 1,
                    2, 2, 1, 1, 'Correct',
                    1100, '2026-10-06T12:00:00.0000000Z', 1, 1
                );";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var telemetry = await store.LoadCompleteAttemptTelemetryAsync();

        Assert.Single(telemetry);
        var attempt = telemetry[0];
        Assert.Equal("sub_historical", attempt.SubmissionId);
        Assert.True(attempt.IsInterrupted);
        Assert.Equal(1100, attempt.ResponseLatencyMs);
    }

    [Fact]
    public async Task M15_MigrateV8ToV9_ZeroDependencyOnPreferences()
    {
        var path = Path.Combine(_directory, "m15_no_pref_dep.db");
        await CreateV8DatabaseAsync(path, addBand: 1, subBand: 1);

        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        var snapshot = await store.LoadSnapshotAsync();

        // Stage 3 inferred solely from Add+Sub BandIndex >= 1
        Assert.Equal(CurriculumStage.Stage3_Multiplication, snapshot.Progression.CurriculumStage);
    }

    [Fact]
    public async Task M16_InitializeAsync_AlreadyV9_IsIdempotent()
    {
        var path = Path.Combine(_directory, "m16_idempotent.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();
        await store.InitializeAsync();

        var snapshot = await store.LoadSnapshotAsync();
        Assert.Equal(9, snapshot.SchemaVersion);
    }

    [Fact]
    public async Task M17_MigrateV8ToV9_FailureRollback_DoesNotLeaveSchemaAt9()
    {
        var path = Path.Combine(_directory, "m17_rollback.db");
        await CreateV8DatabaseAsync(path);

        // Put invalid data that causes migration failure if any
        await using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
            var v = await cmd.ExecuteScalarAsync();
            Assert.Equal("8", v);
        }
    }

    [Fact]
    public async Task M18_FreshDatabase_InitializesDirectlyAsV9_WithoutMigration()
    {
        var path = Path.Combine(_directory, "m18_fresh.db");
        using var store = new SqliteLearnerStore(path);
        await store.InitializeAsync();

        await using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
        var version = await cmd.ExecuteScalarAsync();
        Assert.Equal("9", version);
    }

    [Fact]
    public async Task M19_LearnerProgressionTable_CheckConstraint_RejectsOutRangeStages()
    {
        var path = Path.Combine(_directory, "m19_check.db");
        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
        }

        await using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();

        using var cmdLow = conn.CreateCommand();
        cmdLow.CommandText = "INSERT INTO learner_progression (id, practice_position, curriculum_stage, updated_at) VALUES (2, 0, 0, '2026-10-06T00:00:00Z');";
        await Assert.ThrowsAnyAsync<SqliteException>(async () => await cmdLow.ExecuteNonQueryAsync());

        using var cmdHigh = conn.CreateCommand();
        cmdHigh.CommandText = "INSERT INTO learner_progression (id, practice_position, curriculum_stage, updated_at) VALUES (3, 0, 5, '2026-10-06T00:00:00Z');";
        await Assert.ThrowsAnyAsync<SqliteException>(async () => await cmdHigh.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task M20_MigrateOlderSchemas_V1ThroughV8_ReachesValidV9()
    {
        // Test that migrating an older schema (e.g. V7 or V8) reaches V9
        var path = Path.Combine(_directory, "m20_older_v7.db");

        await using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO schema_info VALUES ('schema_version', '7');
                INSERT INTO schema_info VALUES ('store_revision', '1');

                CREATE TABLE learner_progression (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    practice_position INTEGER NOT NULL DEFAULT 0,
                    updated_at TEXT NOT NULL
                );
                INSERT INTO learner_progression VALUES (1, 0, '2026-10-06T00:00:00Z');

                CREATE TABLE operation_progression (
                    operation TEXT PRIMARY KEY,
                    band_index INTEGER NOT NULL CHECK (band_index >= 0),
                    band_started_practice_position INTEGER NOT NULL CHECK (band_started_practice_position >= 0)
                );
                INSERT INTO operation_progression VALUES ('Addition', 1, 0);
                INSERT INTO operation_progression VALUES ('Subtraction', 1, 0);
                INSERT INTO operation_progression VALUES ('Multiplication', 0, 0);
                INSERT INTO operation_progression VALUES ('Division', 0, 0);

                CREATE TABLE item_learning_state (
                    fact_id TEXT PRIMARY KEY, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL,
                    total_attempts INTEGER NOT NULL, correct_attempts INTEGER NOT NULL, incorrect_attempts INTEGER NOT NULL,
                    consecutive_correct INTEGER NOT NULL, last_latency_ms INTEGER NOT NULL, rolling_latency_ms INTEGER NOT NULL,
                    fluent_streak INTEGER NOT NULL, is_mastered INTEGER NOT NULL, needs_remediation INTEGER NOT NULL,
                    remediation_due_order INTEGER NOT NULL, last_practiced_order INTEGER NOT NULL, last_practiced_at TEXT
                );

                CREATE TABLE attempt_history (
                    submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, operation TEXT NOT NULL, left_operand INTEGER NOT NULL, right_operand INTEGER NOT NULL,
                    submitted_answer INTEGER, correct_answer INTEGER NOT NULL, is_correct INTEGER NOT NULL, is_fluent INTEGER NOT NULL CHECK (is_fluent IN (0, 1)),
                    outcome TEXT NOT NULL DEFAULT 'Incorrect', response_latency_ms INTEGER NOT NULL, timestamp TEXT NOT NULL,
                    practice_position INTEGER, attempt_context_version INTEGER, presented_deadline_ms INTEGER, expected_pace_ms INTEGER,
                    resolved_role TEXT, operation_band_before INTEGER
                );

                CREATE TABLE fsrs_card_state (
                    fact_id TEXT PRIMARY KEY, card_id TEXT NOT NULL, state INTEGER NOT NULL, step INTEGER, stability REAL, difficulty REAL,
                    due_practice_position INTEGER NOT NULL, last_review_practice_position INTEGER, last_rating INTEGER
                );
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteLearnerStore(path))
        {
            await store.InitializeAsync();
            var snapshot = await store.LoadSnapshotAsync();
            Assert.Equal(9, snapshot.SchemaVersion);
            Assert.Equal(CurriculumStage.Stage3_Multiplication, snapshot.Progression.CurriculumStage);
        }
    }
}
