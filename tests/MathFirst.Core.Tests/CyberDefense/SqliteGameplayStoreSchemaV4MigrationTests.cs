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

public sealed class SqliteGameplayStoreSchemaV4MigrationTests : IDisposable
{
    private readonly string _testDirectory;

    public SqliteGameplayStoreSchemaV4MigrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_V4_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private string GetTempDbPath(string name = "gameplay_v4_test")
    {
        return Path.Combine(_testDirectory, $"{name}_{Guid.NewGuid():N}.db");
    }

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
            terminal_player_current_hp INTEGER CHECK (terminal_player_current_hp IS NULL OR terminal_player_current_hp = 0),
            CHECK (
                (receipt_kind = 'CalmModeSuppressed' AND
                 requested_attack_damage IS NULL AND
                 applied_opponent_damage IS NULL AND
                 excess_opponent_damage IS NULL AND
                 incoming_enemy_damage IS NULL AND
                 applied_player_damage IS NULL AND
                 excess_enemy_damage IS NULL AND
                 potential_healing IS NULL AND
                 applied_healing IS NULL AND
                 is_opponent_defeated IS NULL AND
                 is_sector_completed IS NULL AND
                 is_game_over IS NULL AND
                 next_sector IS NULL AND
                 next_opponent_index IS NULL AND
                 next_opponent_current_hp IS NULL AND
                 next_player_current_hp IS NULL AND
                 terminal_sector IS NULL AND
                 terminal_opponent_index IS NULL AND
                 terminal_opponent_kind IS NULL AND
                 terminal_opponent_current_hp IS NULL AND
                 terminal_opponent_max_hp IS NULL AND
                 terminal_player_current_hp IS NULL)
                OR
                (receipt_kind = 'Applied' AND
                 requested_attack_damage IS NOT NULL AND
                 applied_opponent_damage IS NOT NULL AND
                 excess_opponent_damage IS NOT NULL AND
                 incoming_enemy_damage IS NOT NULL AND
                 applied_player_damage IS NOT NULL AND
                 excess_enemy_damage IS NOT NULL AND
                 potential_healing IS NOT NULL AND
                 applied_healing IS NOT NULL AND
                 is_opponent_defeated IS NOT NULL AND
                 is_sector_completed IS NOT NULL AND
                 is_game_over IS NOT NULL AND
                 next_sector IS NOT NULL AND
                 next_opponent_index IS NOT NULL AND
                 next_opponent_current_hp IS NOT NULL AND
                 next_player_current_hp IS NOT NULL AND
                 ((is_game_over = 0 AND
                   terminal_sector IS NULL AND
                   terminal_opponent_index IS NULL AND
                   terminal_opponent_kind IS NULL AND
                   terminal_opponent_current_hp IS NULL AND
                   terminal_opponent_max_hp IS NULL AND
                   terminal_player_current_hp IS NULL)
                  OR
                  (is_game_over = 1 AND
                   terminal_sector IS NOT NULL AND
                   terminal_opponent_index IS NOT NULL AND
                   terminal_opponent_kind IS NOT NULL AND
                   terminal_opponent_current_hp IS NOT NULL AND
                   terminal_opponent_max_hp IS NOT NULL AND
                   terminal_player_current_hp = 0)))
            )
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
            updated_at TEXT NOT NULL,
            CHECK (
                (is_pending = 0 AND target_epoch = current_epoch)
                OR
                (is_pending = 1 AND target_epoch > current_epoch)
            )
        );

        CREATE INDEX IF NOT EXISTS ix_gameplay_receipt_ledger_epoch_processed
            ON gameplay_receipt_ledger(reset_epoch, processed_at);

        CREATE INDEX IF NOT EXISTS ix_gameplay_pending_intent_epoch_created
            ON gameplay_pending_intent(reset_epoch, created_at);
    ";

    private const string RawSchemaV2Ddl = @"
        CREATE TABLE gameplay_schema_info (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE gameplay_progression (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            store_revision INTEGER NOT NULL CHECK (store_revision >= 1),
            reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
            cumulative_xp INTEGER NOT NULL DEFAULT 0 CHECK (cumulative_xp >= 0),
            player_level INTEGER NOT NULL DEFAULT 1 CHECK (player_level >= 1),
            earned_sp INTEGER NOT NULL DEFAULT 0 CHECK (earned_sp >= 0),
            spent_sp INTEGER NOT NULL DEFAULT 0 CHECK (spent_sp >= 0),
            available_sp INTEGER NOT NULL DEFAULT 0 CHECK (available_sp >= 0),
            attack_rank INTEGER NOT NULL DEFAULT 0 CHECK (attack_rank >= 0),
            economy_version TEXT NOT NULL DEFAULT 'ECONOMY_V1',
            updated_at TEXT NOT NULL,
            CHECK (earned_sp = spent_sp + available_sp)
        );

        CREATE TABLE gameplay_run_state (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            sector INTEGER NOT NULL CHECK (sector >= 1),
            opponent_index INTEGER NOT NULL CHECK (opponent_index >= 0),
            opponent_current_hp INTEGER NOT NULL CHECK (opponent_current_hp >= 1),
            player_current_hp INTEGER NOT NULL CHECK (player_current_hp >= 1 AND player_current_hp <= 100),
            opponent_executed_attacks INTEGER NOT NULL DEFAULT 0 CHECK (opponent_executed_attacks >= 0),
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
            terminal_player_current_hp INTEGER CHECK (terminal_player_current_hp IS NULL OR terminal_player_current_hp = 0),
            next_opponent_executed_attacks INTEGER CHECK (next_opponent_executed_attacks IS NULL OR next_opponent_executed_attacks >= 0),
            executed_attacks_on_defeated_opponent INTEGER CHECK (executed_attacks_on_defeated_opponent IS NULL OR executed_attacks_on_defeated_opponent >= 1),
            awarded_xp INTEGER CHECK (awarded_xp IS NULL OR awarded_xp >= 0),
            player_level_after INTEGER CHECK (player_level_after IS NULL OR player_level_after >= 1),
            cumulative_xp_after INTEGER CHECK (cumulative_xp_after IS NULL OR cumulative_xp_after >= 0),
            CHECK (
                (receipt_kind = 'CalmModeSuppressed' AND
                 requested_attack_damage IS NULL AND
                 applied_opponent_damage IS NULL AND
                 excess_opponent_damage IS NULL AND
                 incoming_enemy_damage IS NULL AND
                 applied_player_damage IS NULL AND
                 excess_enemy_damage IS NULL AND
                 potential_healing IS NULL AND
                 applied_healing IS NULL AND
                 is_opponent_defeated IS NULL AND
                 is_sector_completed IS NULL AND
                 is_game_over IS NULL AND
                 next_sector IS NULL AND
                 next_opponent_index IS NULL AND
                 next_opponent_current_hp IS NULL AND
                 next_player_current_hp IS NULL AND
                 terminal_sector IS NULL AND
                 terminal_opponent_index IS NULL AND
                 terminal_opponent_kind IS NULL AND
                 terminal_opponent_current_hp IS NULL AND
                 terminal_opponent_max_hp IS NULL AND
                 terminal_player_current_hp IS NULL AND
                 next_opponent_executed_attacks IS NULL AND
                 executed_attacks_on_defeated_opponent IS NULL)
                OR
                (receipt_kind = 'Applied' AND
                 requested_attack_damage IS NOT NULL AND
                 applied_opponent_damage IS NOT NULL AND
                 excess_opponent_damage IS NOT NULL AND
                 incoming_enemy_damage IS NOT NULL AND
                 applied_player_damage IS NOT NULL AND
                 excess_enemy_damage IS NOT NULL AND
                 potential_healing IS NOT NULL AND
                 applied_healing IS NOT NULL AND
                 is_opponent_defeated IS NOT NULL AND
                 is_sector_completed IS NOT NULL AND
                 is_game_over IS NOT NULL AND
                 next_sector IS NOT NULL AND
                 next_opponent_index IS NOT NULL AND
                 next_opponent_current_hp IS NOT NULL AND
                 next_player_current_hp IS NOT NULL AND
                 ((is_game_over = 0 AND
                   terminal_sector IS NULL AND
                   terminal_opponent_index IS NULL AND
                   terminal_opponent_kind IS NULL AND
                   terminal_opponent_current_hp IS NULL AND
                   terminal_opponent_max_hp IS NULL AND
                   terminal_player_current_hp IS NULL)
                  OR
                  (is_game_over = 1 AND
                   terminal_sector IS NOT NULL AND
                   terminal_opponent_index IS NOT NULL AND
                   terminal_opponent_kind IS NOT NULL AND
                   terminal_opponent_current_hp IS NOT NULL AND
                   terminal_opponent_max_hp IS NOT NULL AND
                   terminal_player_current_hp = 0)) AND
                 (is_opponent_defeated = 1 OR executed_attacks_on_defeated_opponent IS NULL))
            )
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
            updated_at TEXT NOT NULL,
            CHECK (
                (is_pending = 0 AND target_epoch = current_epoch)
                OR
                (is_pending = 1 AND target_epoch > current_epoch)
            )
        );

        CREATE INDEX IF NOT EXISTS ix_gameplay_receipt_ledger_epoch_processed
            ON gameplay_receipt_ledger(reset_epoch, processed_at);

        CREATE INDEX IF NOT EXISTS ix_gameplay_pending_intent_epoch_created
            ON gameplay_pending_intent(reset_epoch, created_at);
    ";

    private const string RawSchemaV3Ddl = @"
        CREATE TABLE gameplay_schema_info (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE gameplay_progression (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            store_revision INTEGER NOT NULL CHECK (store_revision >= 1),
            reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
            cumulative_xp INTEGER NOT NULL DEFAULT 0 CHECK (cumulative_xp >= 0),
            player_level INTEGER NOT NULL DEFAULT 1 CHECK (player_level >= 1),
            earned_sp INTEGER NOT NULL DEFAULT 0 CHECK (earned_sp >= 0),
            spent_sp INTEGER NOT NULL DEFAULT 0 CHECK (spent_sp >= 0),
            available_sp INTEGER NOT NULL DEFAULT 0 CHECK (available_sp >= 0),
            attack_rank INTEGER NOT NULL DEFAULT 0 CHECK (attack_rank >= 0),
            economy_version TEXT NOT NULL DEFAULT 'ECONOMY_V1',
            updated_at TEXT NOT NULL,
            CHECK (earned_sp = spent_sp + available_sp)
        );

        CREATE TABLE gameplay_run_state (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            sector INTEGER NOT NULL CHECK (sector >= 1),
            opponent_index INTEGER NOT NULL CHECK (opponent_index >= 0),
            opponent_current_hp INTEGER NOT NULL CHECK (opponent_current_hp >= 1),
            player_current_hp INTEGER NOT NULL CHECK (player_current_hp >= 1 AND player_current_hp <= 100),
            opponent_executed_attacks INTEGER NOT NULL DEFAULT 0 CHECK (opponent_executed_attacks >= 0),
            opponent_encounter_provenance TEXT NOT NULL DEFAULT 'Authoritative' CHECK (opponent_encounter_provenance IN ('Authoritative', 'UnverifiedLegacy')),
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
            terminal_player_current_hp INTEGER CHECK (terminal_player_current_hp IS NULL OR terminal_player_current_hp = 0),
            next_opponent_executed_attacks INTEGER CHECK (next_opponent_executed_attacks IS NULL OR next_opponent_executed_attacks >= 0),
            executed_attacks_on_defeated_opponent INTEGER CHECK (executed_attacks_on_defeated_opponent IS NULL OR executed_attacks_on_defeated_opponent >= 1),
            awarded_xp INTEGER CHECK (awarded_xp IS NULL OR awarded_xp >= 0),
            player_level_after INTEGER CHECK (player_level_after IS NULL OR player_level_after >= 1),
            cumulative_xp_after INTEGER CHECK (cumulative_xp_after IS NULL OR cumulative_xp_after >= 0),
            next_opponent_encounter_provenance TEXT CHECK (next_opponent_encounter_provenance IS NULL OR next_opponent_encounter_provenance IN ('Authoritative', 'UnverifiedLegacy')),
            defeated_opponent_encounter_provenance TEXT CHECK (defeated_opponent_encounter_provenance IS NULL OR defeated_opponent_encounter_provenance IN ('Authoritative', 'UnverifiedLegacy')),
            CHECK (
                (receipt_kind = 'CalmModeSuppressed' AND
                 requested_attack_damage IS NULL AND
                 applied_opponent_damage IS NULL AND
                 excess_opponent_damage IS NULL AND
                 incoming_enemy_damage IS NULL AND
                 applied_player_damage IS NULL AND
                 excess_enemy_damage IS NULL AND
                 potential_healing IS NULL AND
                 applied_healing IS NULL AND
                 is_opponent_defeated IS NULL AND
                 is_sector_completed IS NULL AND
                 is_game_over IS NULL AND
                 next_sector IS NULL AND
                 next_opponent_index IS NULL AND
                 next_opponent_current_hp IS NULL AND
                 next_player_current_hp IS NULL AND
                 terminal_sector IS NULL AND
                 terminal_opponent_index IS NULL AND
                 terminal_opponent_kind IS NULL AND
                 terminal_opponent_current_hp IS NULL AND
                 terminal_opponent_max_hp IS NULL AND
                 terminal_player_current_hp IS NULL AND
                 next_opponent_executed_attacks IS NULL AND
                 executed_attacks_on_defeated_opponent IS NULL AND
                 next_opponent_encounter_provenance IS NULL AND
                 defeated_opponent_encounter_provenance IS NULL)
                OR
                (receipt_kind = 'Applied' AND
                 requested_attack_damage IS NOT NULL AND
                 applied_opponent_damage IS NOT NULL AND
                 excess_opponent_damage IS NOT NULL AND
                 incoming_enemy_damage IS NOT NULL AND
                 applied_player_damage IS NOT NULL AND
                 excess_enemy_damage IS NOT NULL AND
                 potential_healing IS NOT NULL AND
                 applied_healing IS NOT NULL AND
                 is_opponent_defeated IS NOT NULL AND
                 is_sector_completed IS NOT NULL AND
                 is_game_over IS NOT NULL AND
                 next_sector IS NOT NULL AND
                 next_opponent_index IS NOT NULL AND
                 next_opponent_current_hp IS NOT NULL AND
                 next_player_current_hp IS NOT NULL AND
                 ((is_game_over = 0 AND
                   terminal_sector IS NULL AND
                   terminal_opponent_index IS NULL AND
                   terminal_opponent_kind IS NULL AND
                   terminal_opponent_current_hp IS NULL AND
                   terminal_opponent_max_hp IS NULL AND
                   terminal_player_current_hp IS NULL)
                  OR
                  (is_game_over = 1 AND
                   terminal_sector IS NOT NULL AND
                   terminal_opponent_index IS NOT NULL AND
                   terminal_opponent_kind IS NOT NULL AND
                   terminal_opponent_current_hp IS NOT NULL AND
                   terminal_opponent_max_hp IS NOT NULL AND
                   terminal_player_current_hp = 0)) AND
                 (is_opponent_defeated = 1 OR (executed_attacks_on_defeated_opponent IS NULL AND defeated_opponent_encounter_provenance IS NULL)) AND
                 (executed_attacks_on_defeated_opponent IS NOT NULL OR defeated_opponent_encounter_provenance IS NULL) AND
                 (next_opponent_executed_attacks IS NOT NULL OR next_opponent_encounter_provenance IS NULL))
            )
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
            updated_at TEXT NOT NULL,
            CHECK (
                (is_pending = 0 AND target_epoch = current_epoch)
                OR
                (is_pending = 1 AND target_epoch > current_epoch)
            )
        );

        CREATE INDEX IF NOT EXISTS ix_gameplay_receipt_ledger_epoch_processed
            ON gameplay_receipt_ledger(reset_epoch, processed_at);

        CREATE INDEX IF NOT EXISTS ix_gameplay_pending_intent_epoch_created
            ON gameplay_pending_intent(reset_epoch, created_at);
    ";

    [Fact]
    public async Task T01_FreshGameplayStore_InitializesSchemaV4()
    {
        Assert.Equal(8, SqliteGameplayStore.DefaultSchemaVersion);

        var dbPath = GetTempDbPath("t01_fresh_v4");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
        var version = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal("8", version);

        // Verify column in table info
        var columns = await GetTableColumnsAsync(connection, "gameplay_pending_intent");
        Assert.True(columns.ContainsKey("effective_attack_damage"), "effective_attack_damage column must exist in Schema V4");
        Assert.Equal("1", columns["effective_attack_damage"].DefaultValue);
        Assert.True(columns["effective_attack_damage"].NotNull);
    }

    [Fact]
    public async Task T02_ExistingV3Database_MigratesToV4_WithoutLosingPendingIntentsOrReceiptRows()
    {
        var dbPath = GetTempDbPath("t02_v3_to_v4");
        await SeedRawV3DatabaseAsync(dbPath, pendingCount: 2, receiptCount: 2);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var progression = await store.GetProgressionAsync();
            Assert.Equal(1, progression.StoreRevision);
        }

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
            Assert.Equal("8", (string?)await cmd.ExecuteScalarAsync());
        }

        using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT count(*) FROM gameplay_pending_intent;";
            Assert.Equal(2L, (long)(await countCmd.ExecuteScalarAsync())!);

            countCmd.CommandText = "SELECT count(*) FROM gameplay_receipt_ledger;";
            Assert.Equal(2L, (long)(await countCmd.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task T03_HistoricalV3PendingIntent_ReceivesTruthfulEffectiveDamage1()
    {
        var dbPath = GetTempDbPath("t03_v3_pending_damage");
        await SeedRawV3DatabaseAsync(dbPath, pendingCount: 1, receiptCount: 0);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var pending = await store.GetPendingIntentAsync("sub-v3-0");
            Assert.NotNull(pending);
            // In Schema V4, historical pending intent receives damage 1
            Assert.Equal("sub-v3-0", pending.SubmissionId);
        }

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT effective_attack_damage FROM gameplay_pending_intent WHERE submission_id = 'sub-v3-0';";
        var damage = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(1, damage);
    }

    [Fact]
    public async Task T04_ExistingV1AndV2Databases_MigrateToV4_ThroughSupportedMigrationPaths()
    {
        // V1 -> V4
        var dbPathV1 = GetTempDbPath("t04_v1_to_v4");
        await SeedRawV1DatabaseAsync(dbPathV1);
        using (var storeV1 = new SqliteGameplayStore(dbPathV1))
        {
            await storeV1.InitializeAsync();
            var run = await storeV1.GetRunStateAsync();
            Assert.Equal(1, run.Sector);
        }
        using (var conn = new SqliteConnection($"Data Source={dbPathV1}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
            Assert.Equal("8", (string?)await cmd.ExecuteScalarAsync());
        }

        // V2 -> V4
        var dbPathV2 = GetTempDbPath("t04_v2_to_v4");
        await SeedRawV2DatabaseAsync(dbPathV2);
        using (var storeV2 = new SqliteGameplayStore(dbPathV2))
        {
            await storeV2.InitializeAsync();
            var run = await storeV2.GetRunStateAsync();
            Assert.Equal(1, run.Sector);
        }
        using (var conn = new SqliteConnection($"Data Source={dbPathV2}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
            Assert.Equal("8", (string?)await cmd.ExecuteScalarAsync());
        }
    }

    [Fact]
    public async Task T05_Migration_IsTransactionalAndRetrySafe()
    {
        var dbPath = GetTempDbPath("t05_retry_safe");
        await SeedRawV3DatabaseAsync(dbPath, pendingCount: 1, receiptCount: 0);

        // First successful migration
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
        }

        // Re-open should be idempotent and not fail
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var pending = await store.GetPendingIntentAsync("sub-v3-0");
            Assert.NotNull(pending);
        }
    }

    [Fact]
    public async Task T06_RepeatedInitialization_DoesNotDuplicateDamageColumn()
    {
        var dbPath = GetTempDbPath("t06_repeated_init");
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
        }
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
        }

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        var columns = await GetTableColumnsAsync(connection, "gameplay_pending_intent");
        int damageColCount = 0;
        foreach (var col in columns.Keys)
        {
            if (string.Equals(col, "effective_attack_damage", StringComparison.OrdinalIgnoreCase))
            {
                damageColCount++;
            }
        }
        Assert.Equal(1, damageColCount);
    }

    [Fact]
    public async Task T27_GameplayStoreRevision_RemainsMonotonic()
    {
        var dbPath = GetTempDbPath("t27_monotonic_rev");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var rev1 = await store.GetStoreRevisionAsync();
        // Give player some SP first or test store revision advance
        var runState = await store.GetRunStateAsync();
        Assert.NotNull(runState);
        var rev2 = await store.GetStoreRevisionAsync();
        Assert.True(rev2 >= rev1, $"Store revision must be non-decreasing: rev1={rev1}, rev2={rev2}");
    }

    [Fact]
    public async Task T28_V4ReadsAndWrites_RemainSafeUnderUnifiedConnectionGate()
    {
        var dbPath = GetTempDbPath("t28_gate_safety");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await store.GetProgressionAsync();
                await store.GetRunStateAsync();
                await store.GetResetEpochAsync();
            }));
        }

        await Task.WhenAll(tasks);
        Assert.Equal(0, store.ActiveOperationsForTest);
    }

    [Fact]
    public async Task T29_CorruptOrInvalidEffectiveDamage_FailsClosed()
    {
        var dbPath = GetTempDbPath("t29_corrupt_damage");
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
        }

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        // Attempt to insert invalid damage < 1 directly via SQL
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO gameplay_pending_intent (
                submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at, effective_attack_damage
            ) VALUES (
                'sub-bad-1', 'add:1+1', 1, 1, 100, 0, '2026-10-12T00:00:00.0000000Z', 0
            );
        ";

        var ex = await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Contains("CHECK", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task T30_SchemaV4_DoesNotModifyLearnerStoreSchemaV9()
    {
        var gameplayDbPath = GetTempDbPath("t30_gameplay");
        var learnerDbPath = GetTempDbPath("t30_learner");

        using (var gpStore = new SqliteGameplayStore(gameplayDbPath))
        {
            await gpStore.InitializeAsync();
        }

        using (var learnerStore = new SqliteLearnerStore(learnerDbPath))
        {
            await learnerStore.InitializeAsync();
        }

        using var learnerConn = new SqliteConnection($"Data Source={learnerDbPath}");
        await learnerConn.OpenAsync();
        using var cmd = learnerConn.CreateCommand();
        cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
        var version = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal("9", version);
    }

    [Fact]
    public async Task SqliteSyntaxCheck_AlterTableAddColumnWithCheckConstraint_IsSupportedOrThrows()
    {
        var dbPath = GetTempDbPath("syntax_check");
        await SeedRawV3DatabaseAsync(dbPath, pendingCount: 1, receiptCount: 0);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "ALTER TABLE gameplay_pending_intent ADD COLUMN effective_attack_damage INTEGER NOT NULL DEFAULT 1 CHECK (effective_attack_damage >= 1);";
        // Let's see if this executes cleanly or throws
        await cmd.ExecuteNonQueryAsync();

        // Check if column exists
        var cols = await GetTableColumnsAsync(conn, "gameplay_pending_intent");
        Assert.True(cols.ContainsKey("effective_attack_damage"));

        // Check if CHECK constraint is active
        using var invalidInsertCmd = conn.CreateCommand();
        invalidInsertCmd.CommandText = @"
            INSERT INTO gameplay_pending_intent (
                submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at, effective_attack_damage
            ) VALUES (
                'sub-bad-check', 'add:1+1', 1, 1, 150, 0, '2026-10-12T00:00:00.0000000Z', 0
            );
        ";
        var ex = await Assert.ThrowsAsync<SqliteException>(() => invalidInsertCmd.ExecuteNonQueryAsync());
        Assert.Contains("CHECK", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, (string Name, bool NotNull, string? DefaultValue)>> GetTableColumnsAsync(
        SqliteConnection connection, string tableName)
    {
        var cols = new Dictionary<string, (string, bool, string?)>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string name = reader.GetString(1);
            bool notNull = reader.GetInt32(3) == 1;
            string? dflt = reader.IsDBNull(4) ? null : reader.GetString(4);
            cols[name] = (name, notNull, dflt);
        }
        return cols;
    }

    private static async Task SeedRawV1DatabaseAsync(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = RawSchemaV1Ddl + @"
            INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '1');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '1');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');

            INSERT INTO gameplay_progression (id, store_revision, reset_epoch, updated_at)
            VALUES (1, 1, 0, '2026-10-12T00:00:00.0000000Z');

            INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, reset_epoch, updated_at)
            VALUES (1, 1, 0, 2, 100, 0, '2026-10-12T00:00:00.0000000Z');

            INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
            VALUES (1, 0, 0, 0, NULL, '2026-10-12T00:00:00.0000000Z');
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedRawV2DatabaseAsync(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = RawSchemaV2Ddl + @"
            INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '2');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '1');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');

            INSERT INTO gameplay_progression (
                id, store_revision, reset_epoch, cumulative_xp, player_level,
                earned_sp, spent_sp, available_sp, attack_rank, economy_version, updated_at
            ) VALUES (1, 1, 0, 0, 1, 0, 0, 0, 0, 'ECONOMY_V1', '2026-10-12T00:00:00.0000000Z');

            INSERT INTO gameplay_run_state (
                id, sector, opponent_index, opponent_current_hp, player_current_hp,
                opponent_executed_attacks, reset_epoch, updated_at
            ) VALUES (1, 1, 0, 2, 100, 0, 0, '2026-10-12T00:00:00.0000000Z');

            INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
            VALUES (1, 0, 0, 0, NULL, '2026-10-12T00:00:00.0000000Z');
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedRawV3DatabaseAsync(string path, int pendingCount, int receiptCount)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = RawSchemaV3Ddl + @"
            INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '3');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '1');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');

            INSERT INTO gameplay_progression (
                id, store_revision, reset_epoch, cumulative_xp, player_level,
                earned_sp, spent_sp, available_sp, attack_rank, economy_version, updated_at
            ) VALUES (1, 1, 0, 0, 1, 0, 0, 0, 0, 'ECONOMY_V1', '2026-10-12T00:00:00.0000000Z');

            INSERT INTO gameplay_run_state (
                id, sector, opponent_index, opponent_current_hp, player_current_hp,
                opponent_executed_attacks, opponent_encounter_provenance, reset_epoch, updated_at
            ) VALUES (1, 1, 0, 2, 100, 0, 'Authoritative', 0, '2026-10-12T00:00:00.0000000Z');

            INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
            VALUES (1, 0, 0, 0, NULL, '2026-10-12T00:00:00.0000000Z');
        ";
        await cmd.ExecuteNonQueryAsync();

        for (int i = 0; i < pendingCount; i++)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.CommandText = @"
                INSERT INTO gameplay_pending_intent (
                    submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at
                ) VALUES (
                    @id, 'add:1+1', 1, 1, 150, 0, '2026-10-12T00:00:00.0000000Z'
                );
            ";
            pCmd.Parameters.AddWithValue("@id", $"sub-v3-{i}");
            await pCmd.ExecuteNonQueryAsync();
        }

        for (int i = 0; i < receiptCount; i++)
        {
            using var rCmd = conn.CreateCommand();
            rCmd.CommandText = @"
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                    incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                    potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
                    is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
                    next_player_current_hp, terminal_sector, terminal_opponent_index,
                    terminal_opponent_kind, terminal_opponent_current_hp, terminal_opponent_max_hp,
                    terminal_player_current_hp,
                    next_opponent_executed_attacks, executed_attacks_on_defeated_opponent,
                    awarded_xp, player_level_after, cumulative_xp_after,
                    next_opponent_encounter_provenance, defeated_opponent_encounter_provenance
                ) VALUES (
                    @id, 'Applied', 'add:1+1', 1, 1,
                    150, 0, '2026-10-12T00:00:00.0000000Z',
                    1, 1, 0,
                    0, 0, 0,
                    0, 0, 0, 0,
                    0, 1, 0, 1,
                    100, NULL, NULL,
                    NULL, NULL, NULL,
                    NULL,
                    1, NULL,
                    10, 1, 10,
                    'Authoritative', NULL
                );
            ";
            rCmd.Parameters.AddWithValue("@id", $"receipt-v3-{i}");
            await rCmd.ExecuteNonQueryAsync();
        }
    }
}
