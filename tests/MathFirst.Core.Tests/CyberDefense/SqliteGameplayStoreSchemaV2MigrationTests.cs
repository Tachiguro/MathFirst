namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MathFirst.Application.Gameplay;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SqliteGameplayStoreSchemaV2MigrationTests : IDisposable
{
    private readonly string _testDir;

    public SqliteGameplayStoreSchemaV2MigrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "MF_V2_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private string GetTempDbPath(string name = "gameplay") =>
        Path.Combine(_testDir, $"{name}_{Guid.NewGuid():N}.db");

    private const string RawSchemaV1Ddl = @"
        CREATE TABLE gameplay_schema_info (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE gameplay_progression (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            store_revision INTEGER NOT NULL CHECK (store_revision >= 1),
            reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
            updated_at TEXT NOT NULL
        );

        CREATE TABLE gameplay_run_state (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            sector INTEGER NOT NULL CHECK (sector >= 1),
            opponent_index INTEGER NOT NULL CHECK (opponent_index >= 0),
            opponent_current_hp INTEGER NOT NULL CHECK (opponent_current_hp >= 1),
            player_current_hp INTEGER NOT NULL CHECK (player_current_hp >= 1 AND player_current_hp <= 100),
            reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
            updated_at TEXT NOT NULL
        );

        CREATE TABLE gameplay_receipt_ledger (
            submission_id TEXT PRIMARY KEY CHECK (length(submission_id) > 0),
            receipt_kind TEXT NOT NULL CHECK (receipt_kind IN ('Applied', 'CalmModeSuppressed')),
            fact_id TEXT NOT NULL,
            is_correct INTEGER NOT NULL CHECK (is_correct IN (0, 1)),
            is_eligible INTEGER NOT NULL CHECK (is_eligible IN (0, 1)),
            response_latency_ms INTEGER NOT NULL CHECK (response_latency_ms >= 0),
            reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
            processed_at TEXT NOT NULL,
            requested_attack_damage INTEGER CHECK (requested_attack_damage IS NULL OR requested_attack_damage >= 0),
            applied_opponent_damage INTEGER CHECK (applied_opponent_damage IS NULL OR applied_opponent_damage >= 0),
            excess_opponent_damage INTEGER CHECK (excess_opponent_damage IS NULL OR excess_opponent_damage >= 0),
            incoming_enemy_damage INTEGER CHECK (incoming_enemy_damage IS NULL OR incoming_enemy_damage >= 0),
            applied_player_damage INTEGER CHECK (applied_player_damage IS NULL OR applied_player_damage >= 0),
            excess_enemy_damage INTEGER CHECK (excess_enemy_damage IS NULL OR excess_enemy_damage >= 0),
            potential_healing INTEGER CHECK (potential_healing IS NULL OR potential_healing >= 0),
            applied_healing INTEGER CHECK (applied_healing IS NULL OR applied_healing >= 0),
            is_opponent_defeated INTEGER CHECK (is_opponent_defeated IS NULL OR is_opponent_defeated IN (0, 1)),
            is_sector_completed INTEGER CHECK (is_sector_completed IS NULL OR is_sector_completed IN (0, 1)),
            is_game_over INTEGER CHECK (is_game_over IS NULL OR is_game_over IN (0, 1)),
            next_sector INTEGER CHECK (next_sector IS NULL OR next_sector >= 1),
            next_opponent_index INTEGER CHECK (next_opponent_index IS NULL OR next_opponent_index >= 0),
            next_opponent_current_hp INTEGER CHECK (next_opponent_current_hp IS NULL OR next_opponent_current_hp >= 1),
            next_player_current_hp INTEGER CHECK (next_player_current_hp IS NULL OR (next_player_current_hp >= 1 AND next_player_current_hp <= 100)),
            terminal_sector INTEGER CHECK (terminal_sector IS NULL OR terminal_sector >= 1),
            terminal_opponent_index INTEGER CHECK (terminal_opponent_index IS NULL OR terminal_opponent_index >= 0),
            terminal_opponent_kind TEXT CHECK (terminal_opponent_kind IS NULL OR terminal_opponent_kind IN ('Normal', 'Boss')),
            terminal_opponent_current_hp INTEGER CHECK (terminal_opponent_current_hp IS NULL OR terminal_opponent_current_hp >= 1),
            terminal_opponent_max_hp INTEGER CHECK (terminal_opponent_max_hp IS NULL OR terminal_opponent_max_hp >= 1),
            terminal_player_current_hp INTEGER CHECK (terminal_player_current_hp IS NULL OR terminal_player_current_hp = 0)
        );

        CREATE TABLE gameplay_pending_intent (
            submission_id TEXT PRIMARY KEY CHECK (length(submission_id) > 0),
            fact_id TEXT NOT NULL,
            is_correct INTEGER NOT NULL CHECK (is_correct IN (0, 1)),
            is_eligible INTEGER NOT NULL CHECK (is_eligible IN (0, 1)),
            response_latency_ms INTEGER NOT NULL CHECK (response_latency_ms >= 0),
            reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
            created_at TEXT NOT NULL
        );

        CREATE TABLE gameplay_reset_intent (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            is_pending INTEGER NOT NULL CHECK (is_pending IN (0, 1)),
            current_epoch INTEGER NOT NULL CHECK (current_epoch >= 0),
            target_epoch INTEGER NOT NULL CHECK (target_epoch >= current_epoch),
            created_at TEXT,
            updated_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_gameplay_receipt_ledger_epoch_processed
            ON gameplay_receipt_ledger(reset_epoch, processed_at);

        CREATE INDEX IF NOT EXISTS ix_gameplay_pending_intent_epoch_created
            ON gameplay_pending_intent(reset_epoch, created_at);
    ";

    // 1. Fresh V2 database initialization
    [Fact]
    public async Task FreshInitialization_CreatesSchemaV2_AndInitialProgression()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
            var version = (string?)await cmd.ExecuteScalarAsync();
            Assert.Equal("3", version);
        }

        var prog = await store.GetProgressionAsync();
        Assert.Equal(1, prog.StoreRevision);
        Assert.Equal(0, prog.ResetEpoch);
        Assert.Equal(0, prog.CumulativeXp);
        Assert.Equal(1, prog.PlayerLevel);
        Assert.Equal(0, prog.EarnedSkillPoints);
        Assert.Equal(0, prog.SpentSkillPoints);
        Assert.Equal(0, prog.AvailableSkillPoints);
        Assert.Equal(0, prog.AttackRank);
        Assert.Equal("ECONOMY_V1", prog.EconomyVersion);

        var run = await store.GetRunStateAsync();
        Assert.Equal(0, run.CurrentOpponent.ExecutedAttacks);
    }

    // 2. Existing V1 database migration
    [Fact]
    public async Task MigrateV1ToV2_PreservesAllExistingState_AndEstablishesV2Baseline()
    {
        var dbPath = GetTempDbPath();

        // Populate a genuine Schema V1 database
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RawSchemaV1Ddl;
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = @"
                INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '1');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '15');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '1');

                INSERT INTO gameplay_progression (id, store_revision, reset_epoch, updated_at)
                VALUES (1, 15, 1, '2026-10-09T12:00:00Z');

                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, reset_epoch, updated_at)
                VALUES (1, 4, 2, 2, 72, 1, '2026-10-09T12:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 1, 1, NULL, '2026-10-09T12:00:00Z');

                INSERT INTO gameplay_pending_intent (submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at)
                VALUES ('sub-v1-pending-1', '2+2', 1, 1, 350, 1, '2026-10-09T12:00:00Z');

                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                    incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                    potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
                    is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
                    next_player_current_hp, terminal_sector, terminal_opponent_index,
                    terminal_opponent_kind, terminal_opponent_current_hp, terminal_opponent_max_hp,
                    terminal_player_current_hp
                ) VALUES (
                    'sub-v1-hist-1', 'Applied', '3+3', 1, 1,
                    400, 1, '2026-10-09T12:00:00Z',
                    1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                    4, 2, 2, 72,
                    NULL, NULL, NULL, NULL, NULL, NULL
                );
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // Open with SqliteGameplayStore -> triggers migration
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Verify version upgraded to 2
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
            Assert.Equal("3", (string?)await cmd.ExecuteScalarAsync());
        }

        // 3. Preservation of active run state
        var run = await store.GetRunStateAsync();
        Assert.Equal(4, run.Sector);
        Assert.Equal(2, run.OpponentIndex);
        Assert.Equal(2, run.CurrentOpponent.CurrentHp);
        Assert.Equal(72, run.PlayerCurrentHp);
        Assert.Equal(0, run.CurrentOpponent.ExecutedAttacks); // safe baseline

        // 4. Preservation of progression revision and reset epoch
        var prog = await store.GetProgressionAsync();
        Assert.Equal(15, prog.StoreRevision);
        Assert.Equal(1, prog.ResetEpoch);
        Assert.Equal(0, prog.CumulativeXp);
        Assert.Equal(1, prog.PlayerLevel);
        Assert.Equal(0, prog.EarnedSkillPoints);
        Assert.Equal(0, prog.SpentSkillPoints);
        Assert.Equal(0, prog.AvailableSkillPoints);
        Assert.Equal(0, prog.AttackRank);
        Assert.Equal("ECONOMY_V1", prog.EconomyVersion);

        // 5. Preservation of historical receipts
        var histReceipt = await store.GetReceiptAsync("sub-v1-hist-1");
        Assert.NotNull(histReceipt);
        Assert.Equal("sub-v1-hist-1", histReceipt.SubmissionId);
        Assert.NotNull(histReceipt.TransitionResult);
        Assert.Equal(0, histReceipt.TransitionResult.NextState.CurrentOpponent.ExecutedAttacks);

        // 6. Preservation of pending intents
        var pending = await store.GetPendingIntentAsync("sub-v1-pending-1");
        Assert.NotNull(pending);
        Assert.Equal("2+2", pending.FactId);
        Assert.Equal(1, pending.ResetEpoch);
    }

    // 7. Transactional rollback during failed migration
    [Fact]
    public async Task TransactionalRollback_WhenMigrationFails()
    {
        var dbPath = GetTempDbPath();

        // Create V1 database with an invalid constraint violation that trips during migration
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RawSchemaV1Ddl;
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = @"
                INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '1');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '1');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');

                INSERT INTO gameplay_progression (id, store_revision, reset_epoch, updated_at)
                VALUES (1, 1, 0, '2026-10-09T10:00:00Z');

                -- Run state with illegal player HP 999 to cause domain rehydration to fail in migration validation
                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, reset_epoch, updated_at)
                VALUES (1, 1, 0, 2, 50, 0, '2026-10-09T10:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-09T10:00:00Z');
            ";
            await cmd.ExecuteNonQueryAsync();

            // Insert a corrupt receipt with invalid kind to trip migration copy
            cmd.CommandText = @"
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at
                ) VALUES (
                    'sub-corrupt', 'NonexistentKind', '1+1', 1, 1, 100, 0, '2026-10-09T10:00:00Z'
                );
            ";
            // Temporarily ignore check constraints for test setup
            using var pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA ignore_check_constraints = ON;";
            await pragmaCmd.ExecuteNonQueryAsync();
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await Assert.ThrowsAnyAsync<Exception>(() => store.InitializeAsync());

        // Verify version is still 1 (not 2)
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
            Assert.Equal("1", (string?)await cmd.ExecuteScalarAsync());
        }
    }

    // 8. Reopening an already migrated V2 database is idempotent
    [Fact]
    public async Task ReopeningMigratedDatabase_IsIdempotent()
    {
        var dbPath = GetTempDbPath();
        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
        }

        using var store2 = new SqliteGameplayStore(dbPath);
        await store2.InitializeAsync();

        var run = await store2.GetRunStateAsync();
        Assert.NotNull(run);
        Assert.Equal(1, run.Sector);
    }

    // 9. Unsupported future schema version rejection
    [Fact]
    public async Task UnsupportedFutureSchemaVersion_FailsClosed()
    {
        var dbPath = GetTempDbPath();
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE gameplay_schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO gameplay_schema_info VALUES ('schema_version', '4');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Contains("Unsupported database schema version 4", ex.Message);
    }

    // 10. Exact V2 receipt roundtrip equality & multi-hit tracking
    [Fact]
    public async Task V2Receipt_Roundtrip_PersistsAndRehydrates_ExecutedAttacks_Accurately()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var run = CyberDefenseRunState.InitialRun();
        // 1st attack (nonfatal, normal opponent has 2 HP)
        var transition = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        Assert.Equal(1, transition.NextState.CurrentOpponent.ExecutedAttacks);

        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            "sub-roundtrip-v2-1", "2+3", true, true, 420, 0, DateTimeOffset.UtcNow, transition);

        await store.RecordReceiptDirectAsync(receipt);

        var retrieved = await store.GetReceiptAsync("sub-roundtrip-v2-1");
        Assert.NotNull(retrieved);
        Assert.Equal(receipt, retrieved);
        Assert.NotNull(retrieved.TransitionResult);
        var transitionResult = retrieved.TransitionResult;
        Assert.Equal(transition, transitionResult);
        Assert.Equal(1, transitionResult.NextState.CurrentOpponent.ExecutedAttacks);
    }

    // 11. Multi-hit opponent tracking & defeat captures actual count
    [Fact]
    public async Task MultiHitOpponentTracking_PersistsAcrossTurns_AndDefeatCapturesTotal()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Turn 1
        var intent1 = new CyberDefensePendingIntentRecord("sub-mh-1", "1+1", true, true, 300, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent1);
        var receipt1 = await store.ApplyAttemptTransactionAsync(intent1);

        Assert.NotNull(receipt1.TransitionResult);
        var t1 = receipt1.TransitionResult;
        Assert.False(t1.IsOpponentDefeated);
        Assert.Equal(1, t1.NextState.CurrentOpponent.ExecutedAttacks);

        // Turn 2: Defeat (normal opponent 2 HP defeated in 2nd hit)
        var intent2 = new CyberDefensePendingIntentRecord("sub-mh-2", "2+2", true, true, 300, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent2);
        var receipt2 = await store.ApplyAttemptTransactionAsync(intent2);

        Assert.NotNull(receipt2.TransitionResult);
        var t2 = receipt2.TransitionResult;
        Assert.True(t2.IsOpponentDefeated);
        Assert.Equal(2, t2.ExecutedAttacksOnDefeatedOpponent);
        Assert.Equal(0, t2.NextState.CurrentOpponent.ExecutedAttacks); // Next opponent starts at 0
    }

    // 12. Mid-encounter cold restart preserves executed attacks
    [Fact]
    public async Task ColdRestart_MidEncounter_PreservesExecutedAttacks()
    {
        var dbPath = GetTempDbPath();
        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord("sub-cold-1", "1+1", true, true, 300, 0, DateTimeOffset.UtcNow);
            await store1.StagePendingIntentAsync(intent);
            await store1.ApplyAttemptTransactionAsync(intent);

            var run1 = await store1.GetRunStateAsync();
            Assert.Equal(1, run1.CurrentOpponent.ExecutedAttacks);
        }

        // Cold restart: Open brand new store instance
        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();
            var run2 = await store2.GetRunStateAsync();
            Assert.Equal(1, run2.CurrentOpponent.ExecutedAttacks);

            // Complete defeat after cold restart
            var intent2 = new CyberDefensePendingIntentRecord("sub-cold-2", "2+2", true, true, 300, 0, DateTimeOffset.UtcNow);
            await store2.StagePendingIntentAsync(intent2);
            var receipt2 = await store2.ApplyAttemptTransactionAsync(intent2);

            Assert.NotNull(receipt2.TransitionResult);
            var t2 = receipt2.TransitionResult;
            Assert.True(t2.IsOpponentDefeated);
            Assert.Equal(2, t2.ExecutedAttacksOnDefeatedOpponent);
        }
    }

    // 13. Full Local Reset resets attack rank, SP, and opponent attack count
    [Fact]
    public async Task FullLocalReset_ResetsAttackRank_SP_AndOpponentAttacks()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // 1. Advance combat so opponent has attacks
        var intent1 = new CyberDefensePendingIntentRecord("sub-reset-1", "1+1", true, true, 300, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent1);
        await store.ApplyAttemptTransactionAsync(intent1);

        var runBefore = await store.GetRunStateAsync();
        Assert.Equal(1, runBefore.CurrentOpponent.ExecutedAttacks);

        // 2. Perform two-phase reset
        var resetIntent = await store.BeginOrGetResetIntentAsync();
        Assert.True(resetIntent.IsPending);
        Assert.Equal(1, resetIntent.TargetEpoch);

        await store.ResetGameplayStateAsync(resetIntent.TargetEpoch);
        await store.ClearResetIntentAsync(resetIntent.TargetEpoch);

        // 3. Verify reset state
        var runAfter = await store.GetRunStateAsync();
        Assert.Equal(1, runAfter.Sector);
        Assert.Equal(0, runAfter.OpponentIndex);
        Assert.Equal(100, runAfter.PlayerCurrentHp);
        Assert.Equal(0, runAfter.CurrentOpponent.ExecutedAttacks);

        var progAfter = await store.GetProgressionAsync();
        Assert.Equal(1, progAfter.StoreRevision);
        Assert.Equal(1, progAfter.ResetEpoch);
        Assert.Equal(0, progAfter.CumulativeXp);
        Assert.Equal(1, progAfter.PlayerLevel);
        Assert.Equal(0, progAfter.EarnedSkillPoints);
        Assert.Equal(0, progAfter.SpentSkillPoints);
        Assert.Equal(0, progAfter.AvailableSkillPoints);
        Assert.Equal(0, progAfter.AttackRank);
        Assert.Equal("ECONOMY_V1", progAfter.EconomyVersion);

        // Receipts must be cleared
        var clearedReceipt = await store.GetReceiptAsync("sub-reset-1");
        Assert.Null(clearedReceipt);
    }

    // 14. Historical normal opponent defeat receipt preserves null executed attacks (MAJOR-01 regression target)
    [Fact]
    public async Task MigrateV1ToV2_PreservesHistoricalNormalOpponentDefeatReceipt_WithNullExecutedAttacks()
    {
        var dbPath = GetTempDbPath("v1_defeat_normal");

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RawSchemaV1Ddl;
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = @"
                INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '1');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '10');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');

                INSERT INTO gameplay_progression (id, store_revision, reset_epoch, updated_at)
                VALUES (1, 10, 0, '2026-10-09T12:00:00Z');

                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, reset_epoch, updated_at)
                VALUES (1, 1, 1, 2, 100, 0, '2026-10-09T12:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-09T12:00:00Z');

                -- Historical Schema V1 normal opponent defeat receipt (no attack count columns exist in V1)
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                    incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                    potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
                    is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
                    next_player_current_hp, terminal_sector, terminal_opponent_index,
                    terminal_opponent_kind, terminal_opponent_current_hp, terminal_opponent_max_hp,
                    terminal_player_current_hp
                ) VALUES (
                    'sub-v1-defeat-normal', 'Applied', '4+4', 1, 1,
                    250, 0, '2026-10-09T12:05:00Z',
                    2, 2, 0, 0, 0, 0, 2, 0, 1, 0, 0,
                    1, 1, 2, 100,
                    NULL, NULL, NULL, NULL, NULL, NULL
                );
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var receipt = await store.GetReceiptAsync("sub-v1-defeat-normal");
        Assert.NotNull(receipt);
        Assert.Equal("sub-v1-defeat-normal", receipt.SubmissionId);
        Assert.Equal("4+4", receipt.FactId);
        Assert.True(receipt.IsCorrect);
        Assert.True(receipt.IsEligible);
        Assert.Equal(250, receipt.ResponseLatencyMs);
        Assert.Equal(0, receipt.ResetEpoch);

        Assert.NotNull(receipt.TransitionResult);
        var transition = receipt.TransitionResult;
        Assert.True(transition.IsOpponentDefeated);
        Assert.False(transition.IsSectorCompleted);
        Assert.Equal(2, transition.RequestedAttackDamage);
        Assert.Equal(2, transition.AppliedOpponentDamage);
        Assert.Equal(1, transition.NextState.Sector);
        Assert.Equal(1, transition.NextState.OpponentIndex);
        Assert.Equal(2, transition.NextState.CurrentOpponent.CurrentHp);
        Assert.Equal(0, transition.NextState.CurrentOpponent.ExecutedAttacks);

        // Unknown historical count must remain null, NOT fabricated 1
        Assert.Null(transition.ExecutedAttacksOnDefeatedOpponent);
    }

    // 15. Historical sector boss defeat receipt preserves null executed attacks
    [Fact]
    public async Task MigrateV1ToV2_PreservesHistoricalBossDefeatReceipt_WithNullExecutedAttacks()
    {
        var dbPath = GetTempDbPath("v1_defeat_boss");

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RawSchemaV1Ddl;
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = @"
                INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '1');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '20');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');

                INSERT INTO gameplay_progression (id, store_revision, reset_epoch, updated_at)
                VALUES (1, 20, 0, '2026-10-09T13:00:00Z');

                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, reset_epoch, updated_at)
                VALUES (1, 2, 0, 2, 98, 0, '2026-10-09T13:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-09T13:00:00Z');

                -- Historical Schema V1 boss defeat receipt advancing sector 1 -> sector 2
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                    incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                    potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
                    is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
                    next_player_current_hp, terminal_sector, terminal_opponent_index,
                    terminal_opponent_kind, terminal_opponent_current_hp, terminal_opponent_max_hp,
                    terminal_player_current_hp
                ) VALUES (
                    'sub-v1-defeat-boss', 'Applied', '9+9', 1, 1,
                    320, 0, '2026-10-09T13:05:00Z',
                    2, 2, 0, 0, 0, 0, 6, 6, 1, 1, 0,
                    2, 0, 2, 98,
                    NULL, NULL, NULL, NULL, NULL, NULL
                );
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var receipt = await store.GetReceiptAsync("sub-v1-defeat-boss");
        Assert.NotNull(receipt);
        Assert.Equal("sub-v1-defeat-boss", receipt.SubmissionId);
        Assert.Equal("9+9", receipt.FactId);
        Assert.True(receipt.IsCorrect);
        Assert.True(receipt.IsEligible);

        Assert.NotNull(receipt.TransitionResult);
        var transition = receipt.TransitionResult;
        Assert.True(transition.IsOpponentDefeated);
        Assert.True(transition.IsSectorCompleted);
        Assert.Equal(2, transition.NextState.Sector);
        Assert.Equal(0, transition.NextState.OpponentIndex);
        Assert.Equal(0, transition.NextState.CurrentOpponent.ExecutedAttacks);

        // Unknown historical count must remain null, NOT fabricated 1
        Assert.Null(transition.ExecutedAttacksOnDefeatedOpponent);
    }

    // 16. Legacy count is never confused with a genuine V2 one-hit kill
    [Fact]
    public async Task LegacyDefeatReceipt_IsNeverConfusedWith_GenuineV2OneHitKill()
    {
        var dbPath = GetTempDbPath("legacy_vs_v2");

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RawSchemaV1Ddl;
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = @"
                INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '1');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '1');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');

                INSERT INTO gameplay_progression (id, store_revision, reset_epoch, updated_at)
                VALUES (1, 1, 0, '2026-10-09T10:00:00Z');

                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, reset_epoch, updated_at)
                VALUES (1, 1, 0, 2, 100, 0, '2026-10-09T10:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-09T10:00:00Z');

                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                    incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                    potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
                    is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
                    next_player_current_hp, terminal_sector, terminal_opponent_index,
                    terminal_opponent_kind, terminal_opponent_current_hp, terminal_opponent_max_hp,
                    terminal_player_current_hp
                ) VALUES (
                    'sub-legacy-defeat', 'Applied', '5+5', 1, 1,
                    200, 0, '2026-10-09T10:05:00Z',
                    2, 2, 0, 0, 0, 0, 2, 0, 1, 0, 0,
                    1, 1, 2, 100,
                    NULL, NULL, NULL, NULL, NULL, NULL
                );
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // 1. Rehydrate legacy receipt
        var legacyReceipt = await store.GetReceiptAsync("sub-legacy-defeat");
        Assert.NotNull(legacyReceipt);
        Assert.NotNull(legacyReceipt.TransitionResult);
        var legacyTransition = legacyReceipt.TransitionResult;
        Assert.True(legacyTransition.IsOpponentDefeated);
        Assert.Null(legacyTransition.ExecutedAttacksOnDefeatedOpponent);

        // 2. Perform fresh V2 one-hit defeat
        var run = await store.GetRunStateAsync();
        var v2Transition = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 2);
        Assert.True(v2Transition.IsOpponentDefeated);
        Assert.Equal(1, v2Transition.ExecutedAttacksOnDefeatedOpponent);

        var v2Receipt = CyberDefenseReceiptRecord.CreateApplied(
            "sub-v2-onehit", "6+6", true, true, 210, 0, DateTimeOffset.UtcNow, v2Transition);
        await store.RecordReceiptDirectAsync(v2Receipt);

        var retrievedV2Receipt = await store.GetReceiptAsync("sub-v2-onehit");
        Assert.NotNull(retrievedV2Receipt);
        Assert.NotNull(retrievedV2Receipt.TransitionResult);
        Assert.Equal(1, retrievedV2Receipt.TransitionResult.ExecutedAttacksOnDefeatedOpponent);

        // 3. Confirm legacy receipt count (null) is NOT equal to V2 one-hit count (1)
        Assert.NotEqual(
            legacyTransition.ExecutedAttacksOnDefeatedOpponent,
            retrievedV2Receipt.TransitionResult.ExecutedAttacksOnDefeatedOpponent);
        Assert.False(legacyTransition.Equals(retrievedV2Receipt.TransitionResult));
        Assert.False(legacyReceipt.Equals(retrievedV2Receipt));
    }

    // 17. New V2 one-hit defeat preserves count 1 across roundtrip and cold restart
    [Fact]
    public async Task V2OneHitDefeat_PreservesCountOne_AcrossRoundtripAndColdRestart()
    {
        var dbPath = GetTempDbPath("v2_onehit");
        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();

            var run = await store1.GetRunStateAsync();
            var transition = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 2);
            Assert.True(transition.IsOpponentDefeated);
            Assert.Equal(1, transition.ExecutedAttacksOnDefeatedOpponent);

            var receipt = CyberDefenseReceiptRecord.CreateApplied(
                "sub-v2-1hit", "7+7", true, true, 190, 0, DateTimeOffset.UtcNow, transition);
            await store1.RecordReceiptDirectAsync(receipt);

            var retrieved = await store1.GetReceiptAsync("sub-v2-1hit");
            Assert.NotNull(retrieved);
            Assert.NotNull(retrieved.TransitionResult);
            Assert.Equal(1, retrieved.TransitionResult.ExecutedAttacksOnDefeatedOpponent);
        }

        // Cold restart
        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();
            var coldRetrieved = await store2.GetReceiptAsync("sub-v2-1hit");
            Assert.NotNull(coldRetrieved);
            Assert.NotNull(coldRetrieved.TransitionResult);
            Assert.Equal(1, coldRetrieved.TransitionResult.ExecutedAttacksOnDefeatedOpponent);
        }
    }

    // 18. New V2 multi-hit defeat preserves actual count across roundtrip and cold restart
    [Fact]
    public async Task V2MultiHitDefeat_PreservesActualCount_AcrossRoundtripAndColdRestart()
    {
        var dbPath = GetTempDbPath("v2_multihit");
        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();

            // Hit 1: nonfatal (effective damage 1 on 2 HP normal opponent)
            var intent1 = new CyberDefensePendingIntentRecord("sub-mhit-1", "1+2", true, true, 300, 0, DateTimeOffset.UtcNow);
            await store1.StagePendingIntentAsync(intent1);
            var receipt1 = await store1.ApplyAttemptTransactionAsync(intent1);
            Assert.NotNull(receipt1.TransitionResult);
            Assert.False(receipt1.TransitionResult.IsOpponentDefeated);
            Assert.Null(receipt1.TransitionResult.ExecutedAttacksOnDefeatedOpponent);
            Assert.Equal(1, receipt1.TransitionResult.NextState.CurrentOpponent.ExecutedAttacks);

            // Hit 2: defeat (effective damage 1 on remaining 1 HP)
            var intent2 = new CyberDefensePendingIntentRecord("sub-mhit-2", "2+3", true, true, 280, 0, DateTimeOffset.UtcNow);
            await store1.StagePendingIntentAsync(intent2);
            var receipt2 = await store1.ApplyAttemptTransactionAsync(intent2);
            Assert.NotNull(receipt2.TransitionResult);
            Assert.True(receipt2.TransitionResult.IsOpponentDefeated);
            Assert.Equal(2, receipt2.TransitionResult.ExecutedAttacksOnDefeatedOpponent);
            Assert.Equal(0, receipt2.TransitionResult.NextState.CurrentOpponent.ExecutedAttacks);
        }

        // Cold restart
        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();

            var coldReceipt1 = await store2.GetReceiptAsync("sub-mhit-1");
            Assert.NotNull(coldReceipt1);
            Assert.NotNull(coldReceipt1.TransitionResult);
            Assert.Null(coldReceipt1.TransitionResult.ExecutedAttacksOnDefeatedOpponent);
            Assert.Equal(1, coldReceipt1.TransitionResult.NextState.CurrentOpponent.ExecutedAttacks);

            var coldReceipt2 = await store2.GetReceiptAsync("sub-mhit-2");
            Assert.NotNull(coldReceipt2);
            Assert.NotNull(coldReceipt2.TransitionResult);
            Assert.True(coldReceipt2.TransitionResult.IsOpponentDefeated);
            Assert.Equal(2, coldReceipt2.TransitionResult.ExecutedAttacksOnDefeatedOpponent);
            Assert.Equal(0, coldReceipt2.TransitionResult.NextState.CurrentOpponent.ExecutedAttacks);
        }
    }

    // 19. Duplicate submission replay does not produce additional rewards or attacks
    [Fact]
    public async Task DuplicateSubmissionReplay_DoesNotProduceAdditionalRewardsOrAttacks()
    {
        var dbPath = GetTempDbPath("v2_duplicate");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord("sub-dup-replay-1", "3+4", true, true, 250, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent);

        // First application
        var firstReceipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.NotNull(firstReceipt);
        Assert.NotNull(firstReceipt.TransitionResult);

        var runAfterFirst = await store.GetRunStateAsync();
        var progAfterFirst = await store.GetProgressionAsync();

        // Duplicate replay
        var secondReceipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.NotNull(secondReceipt);
        Assert.Equal(firstReceipt, secondReceipt);

        var runAfterSecond = await store.GetRunStateAsync();
        var progAfterSecond = await store.GetProgressionAsync();

        // Run state and progression must not advance or mutate on replay
        Assert.Equal(runAfterFirst.Sector, runAfterSecond.Sector);
        Assert.Equal(runAfterFirst.OpponentIndex, runAfterSecond.OpponentIndex);
        Assert.Equal(runAfterFirst.CurrentOpponent.CurrentHp, runAfterSecond.CurrentOpponent.CurrentHp);
        Assert.Equal(runAfterFirst.CurrentOpponent.ExecutedAttacks, runAfterSecond.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(progAfterFirst.StoreRevision, progAfterSecond.StoreRevision);

        // Verify receipt ledger contains exactly 1 row
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM gameplay_receipt_ledger WHERE submission_id = 'sub-dup-replay-1';";
        var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        Assert.Equal(1, count);
    }

    // 20. Existing receipt equality remains valid for complete V2 data and legacy data
    [Fact]
    public void ReceiptEquality_RemainsValid_ForCompleteV2Data_AndLegacyData()
    {
        var run = CyberDefenseRunState.InitialRun();
        var timestamp = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

        var transitionV2OneHit = new CyberDefenseCombatTransitionResult(
            true, 2, 2, 0, 0, 0, 0, 2, 0, true, false, false, run, null, executedAttacksOnDefeatedOpponent: 1);
        var transitionV2MultiHit = new CyberDefenseCombatTransitionResult(
            true, 2, 2, 0, 0, 0, 0, 2, 0, true, false, false, run, null, executedAttacksOnDefeatedOpponent: 3);
        var transitionLegacy = new CyberDefenseCombatTransitionResult(
            true, 2, 2, 0, 0, 0, 0, 2, 0, true, false, false, run, null, executedAttacksOnDefeatedOpponent: null);

        var rV2OneHit = CyberDefenseReceiptRecord.CreateApplied("sub-eq-1", "1+1", true, true, 100, 0, timestamp, transitionV2OneHit);
        var rV2OneHitClone = CyberDefenseReceiptRecord.CreateApplied("sub-eq-1", "1+1", true, true, 100, 0, timestamp, transitionV2OneHit);

        var rV2MultiHit = CyberDefenseReceiptRecord.CreateApplied("sub-eq-2", "1+1", true, true, 100, 0, timestamp, transitionV2MultiHit);
        var rLegacy = CyberDefenseReceiptRecord.CreateApplied("sub-eq-legacy", "1+1", true, true, 100, 0, timestamp, transitionLegacy);
        var rLegacyClone = CyberDefenseReceiptRecord.CreateApplied("sub-eq-legacy", "1+1", true, true, 100, 0, timestamp, transitionLegacy);

        // V2 one-hit equality
        Assert.Equal(rV2OneHit, rV2OneHitClone);
        Assert.True(rV2OneHit == rV2OneHitClone);
        Assert.Equal(rV2OneHit.GetHashCode(), rV2OneHitClone.GetHashCode());

        // Legacy equality
        Assert.Equal(rLegacy, rLegacyClone);
        Assert.True(rLegacy == rLegacyClone);
        Assert.Equal(rLegacy.GetHashCode(), rLegacyClone.GetHashCode());

        // Distinctness across counts
        Assert.NotEqual(rV2OneHit, rV2MultiHit);
        Assert.True(rV2OneHit != rV2MultiHit);

        // Distinctness between legacy (null) and V2 (1)
        Assert.NotEqual(rV2OneHit, rLegacy);
        Assert.True(rV2OneHit != rLegacy);
    }
}
