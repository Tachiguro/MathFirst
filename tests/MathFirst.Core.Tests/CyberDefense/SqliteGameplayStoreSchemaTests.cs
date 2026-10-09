namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SqliteGameplayStoreSchemaTests : IDisposable
{
    private readonly string _testDbDir;

    public SqliteGameplayStoreSchemaTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstGameplayStoreTests_" + Guid.NewGuid().ToString("N"));
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

    private string GetTempDbPath(string prefix = "gameplay") =>
        Path.Combine(_testDbDir, $"{prefix}_{Guid.NewGuid():N}.db");

    // =========================================================================
    // 1. FRESH DATABASE INITIALIZATION & SCHEMA V1 CREATION
    // =========================================================================

    [Fact]
    public async Task InitializeAsync_FreshEmptyDatabase_CreatesAllSixSchemaV1Tables()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);

        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        var expectedTables = new[]
        {
            "gameplay_schema_info",
            "gameplay_progression",
            "gameplay_run_state",
            "gameplay_receipt_ledger",
            "gameplay_pending_intent",
            "gameplay_reset_intent"
        };

        Assert.Equal(6, tables.Count);
        foreach (var expected in expectedTables)
        {
            Assert.Contains(expected, tables);
        }
    }

    [Fact]
    public async Task InitializeAsync_FreshDatabase_CreatesInitialDomainRunState()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);

        await store.InitializeAsync();

        var runState = await store.GetRunStateAsync();
        var expectedInitial = CyberDefenseRunState.InitialRun();

        Assert.NotNull(runState);
        Assert.Equal(expectedInitial, runState);
        Assert.Equal(1, runState.Sector);
        Assert.Equal(0, runState.OpponentIndex);
        Assert.Equal(CyberDefenseCombatPolicy.PlayerMaxHp, runState.PlayerCurrentHp);
        Assert.Equal(OpponentKind.Normal, runState.CurrentOpponent.Kind);
        Assert.Equal(2, runState.CurrentOpponent.MaxHp);
        Assert.Equal(2, runState.CurrentOpponent.CurrentHp);

        var resetEpoch = await store.GetResetEpochAsync();
        Assert.Equal(0, resetEpoch);

        var revision = await store.GetStoreRevisionAsync();
        Assert.Equal(1, revision);
    }

    // =========================================================================
    // 2. EXISTING VALID DATABASE RE-OPENING & DATA PRESERVATION
    // =========================================================================

    [Fact]
    public async Task InitializeAsync_ExistingValidSchemaV1_PreservesExistingData()
    {
        var dbPath = GetTempDbPath();

        // 1. First initialization
        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
        }

        // 2. Mutate run state directly in SQLite to simulate active progression
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = @"
                UPDATE gameplay_run_state
                SET sector = 3,
                    opponent_index = 2,
                    opponent_current_hp = 1,
                    player_current_hp = 85,
                    reset_epoch = 1,
                    updated_at = '2026-10-09T12:00:00Z'
                WHERE id = 1;

                UPDATE gameplay_progression
                SET store_revision = 42,
                    reset_epoch = 1,
                    updated_at = '2026-10-09T12:00:00Z'
                WHERE id = 1;

                UPDATE gameplay_schema_info
                SET value = '42'
                WHERE key = 'store_revision';

                UPDATE gameplay_schema_info
                SET value = '1'
                WHERE key = 'reset_epoch';
            ";
            await updateCmd.ExecuteNonQueryAsync();
        }

        // 3. Re-open via a new store instance
        using var store2 = new SqliteGameplayStore(dbPath);
        await store2.InitializeAsync();

        var runState = await store2.GetRunStateAsync();
        Assert.Equal(3, runState.Sector);
        Assert.Equal(2, runState.OpponentIndex);
        Assert.Equal(85, runState.PlayerCurrentHp);
        Assert.Equal(1, runState.CurrentOpponent.CurrentHp);
        Assert.Equal(2, runState.CurrentOpponent.MaxHp); // isqrt(3+15)-2 = 4-2 = 2
        Assert.Equal(OpponentKind.Normal, runState.CurrentOpponent.Kind);

        Assert.Equal(1, await store2.GetResetEpochAsync());
        Assert.Equal(42, await store2.GetStoreRevisionAsync());
    }

    // =========================================================================
    // 3. FAIL-CLOSED VALIDATION: INCOMPLETE / MALFORMED / NEWER SCHEMAS
    // =========================================================================

    [Fact]
    public async Task InitializeAsync_ExistingIncompleteSchemaV1_FailsClosed()
    {
        var dbPath = GetTempDbPath();

        // Create a database with 5 of the 6 tables (missing gameplay_reset_intent)
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE gameplay_schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO gameplay_schema_info VALUES ('schema_version', '1');
                INSERT INTO gameplay_schema_info VALUES ('store_revision', '1');

                CREATE TABLE gameplay_progression (id INTEGER PRIMARY KEY CHECK (id = 1), store_revision INTEGER NOT NULL, reset_epoch INTEGER NOT NULL, updated_at TEXT NOT NULL);
                INSERT INTO gameplay_progression VALUES (1, 1, 0, '2026-10-09T10:00:00Z');

                CREATE TABLE gameplay_run_state (id INTEGER PRIMARY KEY CHECK (id = 1), sector INTEGER NOT NULL, opponent_index INTEGER NOT NULL, opponent_current_hp INTEGER NOT NULL, player_current_hp INTEGER NOT NULL, reset_epoch INTEGER NOT NULL, updated_at TEXT NOT NULL);
                INSERT INTO gameplay_run_state VALUES (1, 1, 0, 2, 100, 0, '2026-10-09T10:00:00Z');

                CREATE TABLE gameplay_receipt_ledger (submission_id TEXT PRIMARY KEY, receipt_kind TEXT NOT NULL, fact_id TEXT NOT NULL, is_correct INTEGER NOT NULL, is_eligible INTEGER NOT NULL, response_latency_ms INTEGER NOT NULL, reset_epoch INTEGER NOT NULL, processed_at TEXT NOT NULL);

                CREATE TABLE gameplay_pending_intent (submission_id TEXT PRIMARY KEY, fact_id TEXT NOT NULL, is_correct INTEGER NOT NULL, is_eligible INTEGER NOT NULL, response_latency_ms INTEGER NOT NULL, reset_epoch INTEGER NOT NULL, created_at TEXT NOT NULL);
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Contains("missing table", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeAsync_MissingSchemaMetadataInNonemptyDatabase_FailsClosed()
    {
        var dbPath = GetTempDbPath();

        // Create a database with an arbitrary table and no gameplay_schema_info
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE some_random_table (id INTEGER PRIMARY KEY, name TEXT);";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Contains("gameplay_schema_info", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeAsync_UnsupportedNewerSchemaVersion_FailsWithoutMutation()
    {
        var dbPath = GetTempDbPath();

        // Create a schema version 99 database
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE gameplay_schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO gameplay_schema_info VALUES ('schema_version', '99');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Contains("Unsupported", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Verify database was not mutated
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
            var version = (string?)await cmd.ExecuteScalarAsync();
            Assert.Equal("99", version);
        }
    }

    [Fact]
    public async Task InitializeAsync_InvalidSchemaVersion_FailsClosed()
    {
        var dbPath = GetTempDbPath();

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE gameplay_schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO gameplay_schema_info VALUES ('schema_version', 'not_a_number');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Contains("schema_version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeAsync_CorruptSqliteDatabase_FailsClosed()
    {
        var dbPath = GetTempDbPath();
        await File.WriteAllBytesAsync(dbPath, [0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE, 0xFD]);

        using var store = new SqliteGameplayStore(dbPath);
        await Assert.ThrowsAnyAsync<Exception>(() => store.InitializeAsync());
    }

    [Fact]
    public async Task InitializeAsync_InvalidPersistedRunState_FailsClosed()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
        }

        // Corrupt player HP to 0 (which is an invalid active run state in domain)
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            // Temporarily disable check constraint or set invalid values
            cmd.CommandText = "UPDATE gameplay_run_state SET sector = 0 WHERE id = 1;";
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqliteException)
            {
                // If SQL CHECK prevents it directly, update with valid SQL but invalid domain combination
                // e.g. opponent_current_hp > max HP: sector 1, opponent 0 has max HP 2.
                // If we set opponent_current_hp = 50:
                cmd.CommandText = "UPDATE gameplay_run_state SET opponent_current_hp = 50 WHERE id = 1;";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        using var storeCorrupt = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => storeCorrupt.InitializeAsync());
        Assert.Contains("domain", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 4. SCHEMA V1 TABLE CONSTRAINTS & BEHAVIORAL FIDELITY
    // =========================================================================

    [Fact]
    public async Task SchemaV1_ReceiptLedger_EnforcesUniqueNonemptySubmissionId()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        // 1. Rejects empty submission ID
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at
                ) VALUES (
                    '', 'CalmModeSuppressed', '2+2=4', 1, 1, 1000, 0, '2026-10-09T10:00:00Z'
                );";
            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }

        // 2. Accepts valid unique submission ID
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at
                ) VALUES (
                    'sub_123', 'CalmModeSuppressed', '2+2=4', 1, 1, 1000, 0, '2026-10-09T10:00:00Z'
                );";
            var rows = await cmd.ExecuteNonQueryAsync();
            Assert.Equal(1, rows);
        }

        // 3. Rejects duplicate submission ID
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at
                ) VALUES (
                    'sub_123', 'CalmModeSuppressed', '2+2=4', 1, 1, 1000, 0, '2026-10-09T10:00:00Z'
                );";
            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    public async Task SchemaV1_SuppressedReceipt_DoesNotRequireFabricatedCombatTransition()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        // Suppressed receipt must insert with pure submission metadata and null transition payloads
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO gameplay_receipt_ledger (
                submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                response_latency_ms, reset_epoch, processed_at
            ) VALUES (
                'sub_suppressed_1', 'CalmModeSuppressed', '3+3=6', 1, 1, 850, 0, '2026-10-09T10:00:00Z'
            );";
        var count = await cmd.ExecuteNonQueryAsync();
        Assert.Equal(1, count);

        // Attempting to attach applied fields to a CalmModeSuppressed receipt must fail CHECK constraint
        using var invalidCmd = connection.CreateCommand();
        invalidCmd.CommandText = @"
            INSERT INTO gameplay_receipt_ledger (
                submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                response_latency_ms, reset_epoch, processed_at, requested_attack_damage
            ) VALUES (
                'sub_suppressed_2', 'CalmModeSuppressed', '3+3=6', 1, 1, 850, 0, '2026-10-09T10:00:00Z', 1
            );";
        await Assert.ThrowsAsync<SqliteException>(() => invalidCmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task SchemaV1_AppliedReceipt_RejectsInvalidTerminalSnapshot()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        // 1. Non-game-over Applied receipt must have terminal fields as NULL
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                    incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                    potential_healing, applied_healing,
                    is_opponent_defeated, is_sector_completed, is_game_over,
                    next_sector, next_opponent_index, next_opponent_current_hp, next_player_current_hp,
                    terminal_sector, terminal_opponent_index, terminal_opponent_kind,
                    terminal_opponent_current_hp, terminal_opponent_max_hp, terminal_player_current_hp
                ) VALUES (
                    'sub_applied_valid', 'Applied', '5+5=10', 1, 1, 900, 0, '2026-10-09T10:00:00Z',
                    1, 1, 0,
                    0, 0, 0,
                    0, 0,
                    0, 0, 0,
                    1, 0, 1, 100,
                    NULL, NULL, NULL, NULL, NULL, NULL
                );";
            var rows = await cmd.ExecuteNonQueryAsync();
            Assert.Equal(1, rows);
        }

        // 2. Non-game-over Applied receipt WITH non-null terminal snapshot MUST FAIL CHECK constraint
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                    incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                    potential_healing, applied_healing,
                    is_opponent_defeated, is_sector_completed, is_game_over,
                    next_sector, next_opponent_index, next_opponent_current_hp, next_player_current_hp,
                    terminal_sector, terminal_opponent_index, terminal_opponent_kind,
                    terminal_opponent_current_hp, terminal_opponent_max_hp, terminal_player_current_hp
                ) VALUES (
                    'sub_applied_invalid_1', 'Applied', '5+5=10', 1, 1, 900, 0, '2026-10-09T10:00:00Z',
                    1, 1, 0,
                    0, 0, 0,
                    0, 0,
                    0, 0, 0,
                    1, 0, 1, 100,
                    1, 0, 'Normal', 2, 2, 0
                );";
            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }

        // 3. Game-over Applied receipt WITH terminal player HP != 0 MUST FAIL CHECK constraint
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                    incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                    potential_healing, applied_healing,
                    is_opponent_defeated, is_sector_completed, is_game_over,
                    next_sector, next_opponent_index, next_opponent_current_hp, next_player_current_hp,
                    terminal_sector, terminal_opponent_index, terminal_opponent_kind,
                    terminal_opponent_current_hp, terminal_opponent_max_hp, terminal_player_current_hp
                ) VALUES (
                    'sub_applied_invalid_2', 'Applied', '5+5=10', 0, 1, 900, 0, '2026-10-09T10:00:00Z',
                    0, 0, 0,
                    100, 100, 0,
                    0, 0,
                    0, 0, 1,
                    1, 0, 2, 100,
                    1, 0, 'Normal', 2, 2, 50
                );";
            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }

        // 4. Valid Game-over Applied receipt WITH terminal player HP == 0 MUST SUCCEED
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                    incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                    potential_healing, applied_healing,
                    is_opponent_defeated, is_sector_completed, is_game_over,
                    next_sector, next_opponent_index, next_opponent_current_hp, next_player_current_hp,
                    terminal_sector, terminal_opponent_index, terminal_opponent_kind,
                    terminal_opponent_current_hp, terminal_opponent_max_hp, terminal_player_current_hp
                ) VALUES (
                    'sub_applied_gameover_valid', 'Applied', '5+5=10', 0, 1, 900, 0, '2026-10-09T10:00:00Z',
                    0, 0, 0,
                    100, 100, 0,
                    0, 0,
                    0, 0, 1,
                    1, 0, 2, 100,
                    1, 0, 'Normal', 2, 2, 0
                );";
            var rows = await cmd.ExecuteNonQueryAsync();
            Assert.Equal(1, rows);
        }
    }

    [Fact]
    public async Task SchemaV1_PendingIntent_EnforcesSubmissionIdUniquenessAndEpoch()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        // 1. Insert valid pending intent
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO gameplay_pending_intent (
                    submission_id, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, created_at
                ) VALUES (
                    'intent_1', '7+7=14', 1, 1, 500, 0, '2026-10-09T10:00:00Z'
                );";
            var rows = await cmd.ExecuteNonQueryAsync();
            Assert.Equal(1, rows);
        }

        // 2. Rejects duplicate submission ID
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO gameplay_pending_intent (
                    submission_id, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, created_at
                ) VALUES (
                    'intent_1', '7+7=14', 1, 1, 500, 0, '2026-10-09T10:00:00Z'
                );";
            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }

        // 3. Rejects negative epoch
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO gameplay_pending_intent (
                    submission_id, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, created_at
                ) VALUES (
                    'intent_2', '7+7=14', 1, 1, 500, -1, '2026-10-09T10:00:00Z'
                );";
            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    public async Task SchemaV1_ResetIntent_SupportsPendingTargetEpoch()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        // Default state: is_pending = 0, current_epoch = 0, target_epoch = 0
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT is_pending, current_epoch, target_epoch FROM gameplay_reset_intent WHERE id = 1;";
            using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0, reader.GetInt32(0));
            Assert.Equal(0, reader.GetInt64(1));
            Assert.Equal(0, reader.GetInt64(2));
        }

        // Valid update to pending reset: is_pending = 1, current_epoch = 0, target_epoch = 1
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE gameplay_reset_intent
                SET is_pending = 1,
                    target_epoch = 1,
                    created_at = '2026-10-09T10:00:00Z',
                    updated_at = '2026-10-09T10:00:00Z'
                WHERE id = 1;";
            var rows = await cmd.ExecuteNonQueryAsync();
            Assert.Equal(1, rows);
        }

        // Invalid update: is_pending = 0 but target_epoch != current_epoch MUST FAIL CHECK
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE gameplay_reset_intent
                SET is_pending = 0,
                    current_epoch = 0,
                    target_epoch = 1
                WHERE id = 1;";
            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }

        // Invalid update: target_epoch < current_epoch MUST FAIL CHECK
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE gameplay_reset_intent
                SET is_pending = 1,
                    current_epoch = 5,
                    target_epoch = 4
                WHERE id = 1;";
            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    public async Task SchemaV1_ProgressionAndRunState_ExposeValidResetEpochBaseline()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT reset_epoch FROM gameplay_progression WHERE id = 1;";
            var epoch = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(0, epoch);
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT reset_epoch FROM gameplay_run_state WHERE id = 1;";
            var epoch = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(0, epoch);
        }
    }

    [Fact]
    public async Task SchemaV1_InitializationFailure_DoesNotLeavePartiallyVersionedSchema()
    {
        var dbPath = GetTempDbPath();

        // Triggering an initialization failure on a non-database corrupt file
        await File.WriteAllBytesAsync(dbPath, [0x42, 0x43, 0x44]);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => store.InitializeAsync());
        }

        SqliteConnection.ClearAllPools();

        // File remains what it was, not partially written SQLite
        var bytes = await File.ReadAllBytesAsync(dbPath);
        Assert.Equal([0x42, 0x43, 0x44], bytes);
    }

    // =========================================================================
    // 5. MATHEMATICAL NON-INTERFERENCE & ISOLATION CONTRACTS
    // =========================================================================

    [Fact]
    public async Task GameplayStore_CannotMutateLearnerStoreSchemaV9()
    {
        var learnerDbPath = GetTempDbPath("learner");
        var gameplayDbPath = GetTempDbPath("gameplay");

        // 1. Initialize learner store
        using (var learnerStore = new SqliteLearnerStore(learnerDbPath))
        {
            await learnerStore.InitializeAsync();
        }

        // 2. Initialize gameplay store
        using (var gameplayStore = new SqliteGameplayStore(gameplayDbPath))
        {
            await gameplayStore.InitializeAsync();
        }

        // 3. Verify learner store tables are untouched Schema V9
        using var connection = new SqliteConnection($"Data Source={learnerDbPath}");
        await connection.OpenAsync();

        var learnerTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            learnerTables.Add(reader.GetString(0));
        }

        var expectedLearnerTables = new[]
        {
            "schema_info",
            "learner_progression",
            "operation_progression",
            "item_learning_state",
            "attempt_history",
            "fsrs_card_state"
        };

        Assert.Equal(6, learnerTables.Count);
        foreach (var table in expectedLearnerTables)
        {
            Assert.Contains(table, learnerTables);
        }
        Assert.DoesNotContain("gameplay_run_state", learnerTables);
        Assert.DoesNotContain("gameplay_receipt_ledger", learnerTables);
    }

    [Fact]
    public async Task GameplayStore_StoragePathInjection_UsesOnlyTemporaryTestDirectory()
    {
        var tempDbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(tempDbPath);

        Assert.Equal(tempDbPath, store.StoragePath);
        Assert.StartsWith(_testDbDir, store.StoragePath, StringComparison.OrdinalIgnoreCase);

        await store.InitializeAsync();
        Assert.True(File.Exists(tempDbPath));
    }
}
