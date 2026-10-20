namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>
/// MF-CYBER-007 Slice 7: Gameplay Store Schema V7 (Critical Strike / Overdrive persistence foundation).
/// Schema-only coverage: no purchase, combat-coordinator or receipt-processing integration is exercised here.
/// </summary>
public sealed class SqliteGameplayStoreSchemaV7MigrationTests : IDisposable
{
    private const int SqliteConstraintErrorCode = 19;
    private const long SeededResetEpoch = 2;

    private static readonly string[] ProgressionV7Columns = ["critical_strike_rank", "overdrive_rank"];
    private static readonly string[] RunStateV7Columns = ["critical_correct_attack_count", "overdrive_streak", "overdrive_remaining_bonus_attacks"];
    private static readonly string[] PendingIntentV7Columns = ["bound_critical_strike_rank", "bound_overdrive_rank"];
    private static readonly string[] ReceiptLedgerV7Columns = ["base_attack_damage", "bound_critical_strike_rank", "bound_overdrive_rank", "critical_bonus_percent", "overdrive_bonus_percent"];
    private static readonly string[] GameplayTables = ["gameplay_progression", "gameplay_run_state", "gameplay_pending_intent", "gameplay_receipt_ledger", "gameplay_reset_intent", "gameplay_schema_info"];

    private readonly string _testDirectory;

    public SqliteGameplayStoreSchemaV7MigrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_V7_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            // Stores clear their own pool on disposal; OpenRaw disables pooling.
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private string GetTempDbPath(string name)
    {
        return Path.Combine(_testDirectory, $"{name}_{Guid.NewGuid():N}.db");
    }

    // Verbatim copy of the production Gameplay Schema V6 DDL (SqliteGameplayStore.SchemaV6Ddl before V7).
    private const string RawSchemaV6Ddl = @"
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
            firewall_rank INTEGER NOT NULL DEFAULT 0 CHECK (firewall_rank >= 0 AND firewall_rank <= 3),
            consecutive_early_defeats INTEGER NOT NULL DEFAULT 0 CHECK (consecutive_early_defeats >= 0),
            is_beginner_assistance_active INTEGER NOT NULL DEFAULT 0 CHECK (is_beginner_assistance_active IN (0, 1)),
            activated_firewall_rank INTEGER NOT NULL DEFAULT 0 CHECK (activated_firewall_rank >= 0 AND activated_firewall_rank <= firewall_rank),
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
            shield_capacity INTEGER NOT NULL DEFAULT 0 CHECK (shield_capacity >= 0 AND shield_capacity <= 3),
            active_shields INTEGER NOT NULL DEFAULT 0 CHECK (active_shields >= 0 AND active_shields <= shield_capacity),
            shield_recharge_units INTEGER NOT NULL DEFAULT 0 CHECK (shield_recharge_units >= 0 AND shield_recharge_units < 30),
            reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
            updated_at TEXT NOT NULL,
            CHECK (
                (shield_capacity = 0 AND active_shields = 0 AND shield_recharge_units = 0)
                OR
                (shield_capacity > 0 AND active_shields = shield_capacity AND shield_recharge_units = 0)
                OR
                (shield_capacity > 0 AND active_shields < shield_capacity AND shield_recharge_units >= 0 AND shield_recharge_units < 30)
            )
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
            is_shield_absorbed INTEGER CHECK (is_shield_absorbed IS NULL OR is_shield_absorbed IN (0, 1)),
            absorbed_enemy_damage INTEGER CHECK (absorbed_enemy_damage IS NULL OR absorbed_enemy_damage >= 0),
            next_shield_capacity INTEGER CHECK (next_shield_capacity IS NULL OR (next_shield_capacity >= 0 AND next_shield_capacity <= 3)),
            next_active_shields INTEGER CHECK (next_active_shields IS NULL OR (next_active_shields >= 0 AND next_active_shields <= next_shield_capacity)),
            next_shield_recharge_units INTEGER CHECK (next_shield_recharge_units IS NULL OR (next_shield_recharge_units >= 0 AND next_shield_recharge_units < 30)),
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
                 defeated_opponent_encounter_provenance IS NULL AND
                 is_shield_absorbed IS NULL AND
                 absorbed_enemy_damage IS NULL AND
                 next_shield_capacity IS NULL AND
                 next_active_shields IS NULL AND
                 next_shield_recharge_units IS NULL)
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
                 (next_opponent_executed_attacks IS NOT NULL OR next_opponent_encounter_provenance IS NULL) AND
                 (
                     (is_shield_absorbed IS NULL AND absorbed_enemy_damage IS NULL AND next_shield_capacity IS NULL AND next_active_shields IS NULL AND next_shield_recharge_units IS NULL)
                     OR
                     (
                         is_shield_absorbed IS NOT NULL AND
                         absorbed_enemy_damage IS NOT NULL AND
                         next_shield_capacity IS NOT NULL AND
                         next_active_shields IS NOT NULL AND
                         next_shield_recharge_units IS NOT NULL AND
                         ((is_shield_absorbed = 0 AND absorbed_enemy_damage = 0) OR (is_shield_absorbed = 1 AND absorbed_enemy_damage > 0)) AND
                         (
                             (next_shield_capacity = 0 AND next_active_shields = 0 AND next_shield_recharge_units = 0)
                             OR
                             (next_shield_capacity > 0 AND next_active_shields = next_shield_capacity AND next_shield_recharge_units = 0)
                             OR
                             (next_shield_capacity > 0 AND next_active_shields < next_shield_capacity AND next_shield_recharge_units >= 0 AND next_shield_recharge_units < 30)
                         )
                     )
                 )
                )
            )
        );

        CREATE TABLE gameplay_pending_intent (
            submission_id TEXT PRIMARY KEY CHECK (length(submission_id) > 0),
            fact_id TEXT NOT NULL,
            is_correct INTEGER NOT NULL CHECK (is_correct IN (0, 1)),
            is_eligible INTEGER NOT NULL CHECK (is_eligible IN (0, 1)),
            response_latency_ms INTEGER NOT NULL CHECK (response_latency_ms >= 0),
            reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
            created_at TEXT NOT NULL,
            effective_attack_damage INTEGER NOT NULL DEFAULT 1 CHECK (effective_attack_damage >= 1)
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

    // Legacy V6 player: level 60 with a long history of correct answers (above the Critical Strike Rank 2
    // and Overdrive Rank 1 unlock levels), Attack rank 1, Firewall rank 1 activated, partially recharged shield.
    private const string SeedV6DataSql = @"
        INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '6');
        INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '41');
        INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '2');
        INSERT INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');

        INSERT INTO gameplay_progression (
            id, store_revision, reset_epoch,
            cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank,
            economy_version, updated_at,
            firewall_rank, consecutive_early_defeats, is_beginner_assistance_active,
            activated_firewall_rank
        ) VALUES (1, 41, 2, 98765, 60, 59, 4, 55, 1, 'ECONOMY_V1', '2026-10-19T08:00:00.0000000+00:00', 1, 2, 0, 1);

        INSERT INTO gameplay_run_state (
            id, sector, opponent_index, opponent_current_hp, player_current_hp,
            opponent_executed_attacks, opponent_encounter_provenance,
            shield_capacity, active_shields, shield_recharge_units,
            reset_epoch, updated_at
        ) VALUES (1, 1, 0, 2, 80, 1, 'Authoritative', 1, 0, 18, 2, '2026-10-19T08:00:00.0000000+00:00');

        INSERT INTO gameplay_receipt_ledger (
            submission_id, receipt_kind, fact_id, is_correct, is_eligible,
            response_latency_ms, reset_epoch, processed_at,
            requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
            incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
            potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
            is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
            next_player_current_hp, next_opponent_executed_attacks,
            awarded_xp, player_level_after, cumulative_xp_after,
            next_opponent_encounter_provenance
        ) VALUES (
            'sub-legacy-applied', 'Applied', '1+1', 1, 1,
            100, 2, '2026-10-19T07:00:00.0000000+00:00',
            1, 1, 0,
            0, 0, 0,
            0, 0, 0, 0,
            0, 1, 0, 1,
            100, 1,
            NULL, NULL, NULL,
            'Authoritative'
        );

        INSERT INTO gameplay_receipt_ledger (
            submission_id, receipt_kind, fact_id, is_correct, is_eligible,
            response_latency_ms, reset_epoch, processed_at
        ) VALUES ('sub-legacy-calm', 'CalmModeSuppressed', '2+3', 1, 0, 2400, 2, '2026-10-19T07:30:00.0000000+00:00');

        INSERT INTO gameplay_pending_intent (
            submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at, effective_attack_damage
        ) VALUES ('sub-pending-correct', '3+4', 1, 1, 1500, 2, '2026-10-19T07:58:00.0000000+00:00', 2);

        INSERT INTO gameplay_pending_intent (
            submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at, effective_attack_damage
        ) VALUES ('sub-pending-incorrect', '6+7', 0, 1, 3100, 2, '2026-10-19T07:59:00.0000000+00:00', 2);

        INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
        VALUES (1, 0, 2, 2, NULL, '2026-10-19T08:00:00.0000000+00:00');
    ";

    private const string LegacyAppliedReceiptInsertSql = @"
        INSERT INTO gameplay_receipt_ledger (
            submission_id, receipt_kind, fact_id, is_correct, is_eligible,
            response_latency_ms, reset_epoch, processed_at,
            requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
            incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
            potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
            is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
            next_player_current_hp, next_opponent_executed_attacks,
            next_opponent_encounter_provenance
        ) VALUES (
            'sub-applied', 'Applied', '1+1', 1, 1,
            100, 0, '2026-10-19T07:00:00.0000000+00:00',
            1, 1, 0,
            0, 0, 0,
            0, 0, 0, 0,
            0, 1, 0, 1,
            100, 1,
            'Authoritative'
        );

        INSERT INTO gameplay_receipt_ledger (
            submission_id, receipt_kind, fact_id, is_correct, is_eligible,
            response_latency_ms, reset_epoch, processed_at
        ) VALUES ('sub-calm', 'CalmModeSuppressed', '2+3', 1, 0, 2400, 0, '2026-10-19T07:30:00.0000000+00:00');

        INSERT INTO gameplay_pending_intent (
            submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at, effective_attack_damage
        ) VALUES ('sub-pending', '3+4', 1, 1, 1500, 0, '2026-10-19T07:58:00.0000000+00:00', 1);
    ";

    // =========================================================================
    // Helpers
    // =========================================================================

    private static SqliteConnection OpenRaw(string dbPath)
    {
        var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();
        return connection;
    }

    private static async Task ExecuteRawAsync(string dbPath, string sql)
    {
        using var connection = OpenRaw(dbPath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarRawAsync(string dbPath, string sql)
    {
        using var connection = OpenRaw(dbPath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task<string?> GetSchemaVersionAsync(string dbPath)
    {
        return (string?)await ScalarRawAsync(dbPath, "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';");
    }

    private static async Task SeedRawV6DatabaseAsync(string dbPath)
    {
        await ExecuteRawAsync(dbPath, RawSchemaV6Ddl + SeedV6DataSql);
    }

    private async Task<string> CreateFreshStoreAsync(string name)
    {
        var dbPath = GetTempDbPath(name);
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
        }

        return dbPath;
    }

    private sealed record ColumnInfo(int Cid, string Name, string Type, bool NotNull, string? DefaultValue, int PrimaryKey);

    private static async Task<List<ColumnInfo>> GetColumnsAsync(string dbPath, string table)
    {
        using var connection = OpenRaw(dbPath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = await cmd.ExecuteReaderAsync();
        var columns = new List<ColumnInfo>();
        while (await reader.ReadAsync())
        {
            columns.Add(new ColumnInfo(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) == 1,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5)));
        }

        return columns;
    }

    private static async Task<Dictionary<string, List<string>>> SnapshotTablesAsync(string dbPath, IReadOnlyDictionary<string, List<string>> columnsByTable)
    {
        var snapshot = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using var connection = OpenRaw(dbPath);
        foreach (var (table, columns) in columnsByTable)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {string.Join(", ", columns)} FROM {table} ORDER BY 1;";
            using var reader = await cmd.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                var values = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    values[i] = reader.IsDBNull(i)
                        ? "<NULL>"
                        : $"{reader.GetFieldType(i).Name}:{Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)}";
                }

                rows.Add(string.Join("|", values));
            }

            snapshot[table] = rows;
        }

        return snapshot;
    }

    private static async Task<Dictionary<string, List<string>>> GetLegacyColumnsAsync(string dbPath)
    {
        var columnsByTable = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var table in GameplayTables)
        {
            columnsByTable[table] = (await GetColumnsAsync(dbPath, table)).Select(c => c.Name).ToList();
        }

        return columnsByTable;
    }

    private static async Task<SqliteException> AssertConstraintViolationAsync(string dbPath, string sql)
    {
        var ex = await Assert.ThrowsAsync<SqliteException>(() => ExecuteRawAsync(dbPath, sql));
        Assert.Equal(SqliteConstraintErrorCode, ex.SqliteErrorCode);
        return ex;
    }

    private static async Task<long[]> ReadModifierRunStateAsync(string dbPath)
    {
        using var connection = OpenRaw(dbPath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT critical_correct_attack_count, overdrive_streak, overdrive_remaining_bonus_attacks FROM gameplay_run_state WHERE id = 1;";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return [reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)];
    }

    private static async Task<long[]> ReadModifierRanksAsync(string dbPath)
    {
        using var connection = OpenRaw(dbPath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT critical_strike_rank, overdrive_rank FROM gameplay_progression WHERE id = 1;";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return [reader.GetInt64(0), reader.GetInt64(1)];
    }

    private static async Task<object?[]> ReadReceiptMetadataAsync(string dbPath, string submissionId)
    {
        using var connection = OpenRaw(dbPath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT base_attack_damage, bound_critical_strike_rank, bound_overdrive_rank,
                   critical_bonus_percent, overdrive_bonus_percent
            FROM gameplay_receipt_ledger WHERE submission_id = @id;";
        cmd.Parameters.AddWithValue("@id", submissionId);
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var values = new object?[5];
        for (int i = 0; i < 5; i++)
        {
            values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return values;
    }

    private static string ReceiptMetadataUpdateSql(
        string submissionId,
        string baseAttackDamage = "2",
        string boundCriticalStrikeRank = "2",
        string boundOverdriveRank = "2",
        string criticalBonusPercent = "100",
        string overdriveBonusPercent = "50")
    {
        return $@"
            UPDATE gameplay_receipt_ledger
            SET base_attack_damage = {baseAttackDamage},
                bound_critical_strike_rank = {boundCriticalStrikeRank},
                bound_overdrive_rank = {boundOverdriveRank},
                critical_bonus_percent = {criticalBonusPercent},
                overdrive_bonus_percent = {overdriveBonusPercent}
            WHERE submission_id = '{submissionId}';";
    }

    // =========================================================================
    // 1. Schema version identity
    // =========================================================================

    [Fact]
    public void V01_CurrentGameplaySchema_IsV7_AndLearnerStoreSchemaRemainsV9()
    {
        Assert.Equal(7, SqliteGameplayStore.DefaultSchemaVersion);
        Assert.Equal(9, LearnerProgression.DefaultSchemaVersion);
    }

    // =========================================================================
    // 2. Fresh initialization
    // =========================================================================

    [Fact]
    public async Task V02_FreshStore_InitializesExactSchemaV7()
    {
        var dbPath = await CreateFreshStoreAsync("v02_fresh");

        Assert.Equal("7", await GetSchemaVersionAsync(dbPath));
    }

    [Fact]
    public async Task V03_FreshStore_HasAllV7ColumnsWithContractDefaultsAndNullability()
    {
        var dbPath = await CreateFreshStoreAsync("v03_columns");

        var progression = (await GetColumnsAsync(dbPath, "gameplay_progression")).ToDictionary(c => c.Name);
        var runState = (await GetColumnsAsync(dbPath, "gameplay_run_state")).ToDictionary(c => c.Name);
        var pending = (await GetColumnsAsync(dbPath, "gameplay_pending_intent")).ToDictionary(c => c.Name);
        var receipts = (await GetColumnsAsync(dbPath, "gameplay_receipt_ledger")).ToDictionary(c => c.Name);

        foreach (var (table, columns, names) in new[]
                 {
                     ("gameplay_progression", progression, ProgressionV7Columns),
                     ("gameplay_run_state", runState, RunStateV7Columns),
                     ("gameplay_pending_intent", pending, PendingIntentV7Columns)
                 })
        {
            foreach (var name in names)
            {
                Assert.True(columns.ContainsKey(name), $"Missing column {table}.{name}");
                Assert.Equal("INTEGER", columns[name].Type);
                Assert.True(columns[name].NotNull, $"{table}.{name} must be NOT NULL");
                Assert.Equal("0", columns[name].DefaultValue);
            }
        }

        foreach (var name in ReceiptLedgerV7Columns)
        {
            Assert.True(receipts.ContainsKey(name), $"Missing column gameplay_receipt_ledger.{name}");
            Assert.Equal("INTEGER", receipts[name].Type);
            Assert.False(receipts[name].NotNull, $"gameplay_receipt_ledger.{name} must be nullable");
            Assert.Null(receipts[name].DefaultValue);
        }

        // Existing base-attack-damage meaning of the pending intent column is retained unchanged.
        Assert.True(pending.ContainsKey("effective_attack_damage"));
        Assert.True(pending["effective_attack_damage"].NotNull);
        Assert.Equal("1", pending["effective_attack_damage"].DefaultValue);
    }

    [Fact]
    public async Task V04_FreshStore_SeedsZeroModifierRanksAndRunCounters()
    {
        var dbPath = await CreateFreshStoreAsync("v04_seed");

        Assert.Equal(new long[] { 0, 0 }, await ReadModifierRanksAsync(dbPath));
        Assert.Equal(new long[] { 0, 0, 0 }, await ReadModifierRunStateAsync(dbPath));
    }

    // =========================================================================
    // 3. Column-level CHECK constraints
    // =========================================================================

    [Theory]
    [InlineData("gameplay_progression", "critical_strike_rank", "-1")]
    [InlineData("gameplay_progression", "critical_strike_rank", "3")]
    [InlineData("gameplay_progression", "critical_strike_rank", "NULL")]
    [InlineData("gameplay_progression", "overdrive_rank", "-1")]
    [InlineData("gameplay_progression", "overdrive_rank", "3")]
    [InlineData("gameplay_progression", "overdrive_rank", "NULL")]
    [InlineData("gameplay_run_state", "critical_correct_attack_count", "-1")]
    [InlineData("gameplay_run_state", "critical_correct_attack_count", "2147483648")]
    [InlineData("gameplay_run_state", "critical_correct_attack_count", "NULL")]
    [InlineData("gameplay_run_state", "overdrive_streak", "-1")]
    [InlineData("gameplay_run_state", "overdrive_streak", "2147483648")]
    [InlineData("gameplay_run_state", "overdrive_streak", "NULL")]
    [InlineData("gameplay_run_state", "overdrive_remaining_bonus_attacks", "-1")]
    [InlineData("gameplay_run_state", "overdrive_remaining_bonus_attacks", "3")]
    [InlineData("gameplay_run_state", "overdrive_remaining_bonus_attacks", "NULL")]
    [InlineData("gameplay_pending_intent", "bound_critical_strike_rank", "-1")]
    [InlineData("gameplay_pending_intent", "bound_critical_strike_rank", "3")]
    [InlineData("gameplay_pending_intent", "bound_critical_strike_rank", "NULL")]
    [InlineData("gameplay_pending_intent", "bound_overdrive_rank", "-1")]
    [InlineData("gameplay_pending_intent", "bound_overdrive_rank", "3")]
    [InlineData("gameplay_pending_intent", "bound_overdrive_rank", "NULL")]
    public async Task V05_InvalidRankAndCounterValues_AreRejectedBySchema(string table, string column, string sqlValue)
    {
        var dbPath = await CreateFreshStoreAsync("v05_invalid");
        await ExecuteRawAsync(dbPath, LegacyAppliedReceiptInsertSql);

        await AssertConstraintViolationAsync(dbPath, $"UPDATE {table} SET {column} = {sqlValue};");

        Assert.Equal(new long[] { 0, 0 }, await ReadModifierRanksAsync(dbPath));
        Assert.Equal(new long[] { 0, 0, 0 }, await ReadModifierRunStateAsync(dbPath));
    }

    [Theory]
    [InlineData("0", "2", "2", "100", "50")]
    [InlineData("-1", "2", "2", "100", "50")]
    [InlineData("2", "-1", "2", "100", "50")]
    [InlineData("2", "3", "2", "100", "50")]
    [InlineData("2", "2", "-1", "100", "50")]
    [InlineData("2", "2", "3", "100", "50")]
    [InlineData("2", "2", "2", "-1", "50")]
    [InlineData("2", "2", "2", "101", "50")]
    [InlineData("2", "2", "2", "100", "-1")]
    [InlineData("2", "2", "2", "100", "51")]
    public async Task V06_InvalidReceiptMetadataValues_AreRejectedBySchema(
        string baseAttackDamage, string boundCritical, string boundOverdrive, string criticalPercent, string overdrivePercent)
    {
        var dbPath = await CreateFreshStoreAsync("v06_invalid_receipt");
        await ExecuteRawAsync(dbPath, LegacyAppliedReceiptInsertSql);

        await AssertConstraintViolationAsync(
            dbPath,
            ReceiptMetadataUpdateSql("sub-applied", baseAttackDamage, boundCritical, boundOverdrive, criticalPercent, overdrivePercent));

        Assert.Equal(new object?[] { null, null, null, null, null }, await ReadReceiptMetadataAsync(dbPath, "sub-applied"));
    }

    [Fact]
    public async Task V07_ValidBoundaryValues_AreAccepted()
    {
        var dbPath = await CreateFreshStoreAsync("v07_valid");
        await ExecuteRawAsync(dbPath, LegacyAppliedReceiptInsertSql);

        await ExecuteRawAsync(dbPath, @"
            UPDATE gameplay_progression SET critical_strike_rank = 2, overdrive_rank = 2;
            UPDATE gameplay_run_state SET critical_correct_attack_count = 2147483647, overdrive_streak = 2147483647, overdrive_remaining_bonus_attacks = 2;
            UPDATE gameplay_pending_intent SET bound_critical_strike_rank = 2, bound_overdrive_rank = 2;");
        await ExecuteRawAsync(dbPath, ReceiptMetadataUpdateSql("sub-applied"));

        Assert.Equal(new long[] { 2, 2 }, await ReadModifierRanksAsync(dbPath));
        Assert.Equal(new long[] { 2147483647, 2147483647, 2 }, await ReadModifierRunStateAsync(dbPath));
        Assert.Equal(new object?[] { 2L, 2L, 2L, 100L, 50L }, await ReadReceiptMetadataAsync(dbPath, "sub-applied"));

        // Rank-zero evidence without bonuses is also legitimate (e.g. a pre-purchase attack).
        await ExecuteRawAsync(dbPath, ReceiptMetadataUpdateSql("sub-applied", "1", "0", "0", "0", "0"));
        Assert.Equal(new object?[] { 1L, 0L, 0L, 0L, 0L }, await ReadReceiptMetadataAsync(dbPath, "sub-applied"));
    }

    [Fact]
    public async Task V08_ReceiptMetadata_RejectsPartialOrInconsistentRowEvidence()
    {
        var dbPath = await CreateFreshStoreAsync("v08_row_invariants");
        await ExecuteRawAsync(dbPath, LegacyAppliedReceiptInsertSql);

        // Partial evidence: the five metadata columns are written all-or-nothing.
        await AssertConstraintViolationAsync(dbPath, "UPDATE gameplay_receipt_ledger SET base_attack_damage = 2 WHERE submission_id = 'sub-applied';");
        await AssertConstraintViolationAsync(dbPath, ReceiptMetadataUpdateSql("sub-applied", overdriveBonusPercent: "NULL"));

        // Calm Mode suppressed receipts never carry combat damage evidence.
        await AssertConstraintViolationAsync(dbPath, ReceiptMetadataUpdateSql("sub-calm", "1", "0", "0", "0", "0"));

        // A bonus can never be evidenced for an unowned (rank 0) skill.
        await AssertConstraintViolationAsync(dbPath, ReceiptMetadataUpdateSql("sub-applied", "2", "0", "1", "50", "0"));
        await AssertConstraintViolationAsync(dbPath, ReceiptMetadataUpdateSql("sub-applied", "2", "1", "0", "0", "50"));

        // Incorrect answers never carry Critical Strike or Overdrive bonuses.
        await ExecuteRawAsync(dbPath, "UPDATE gameplay_receipt_ledger SET is_correct = 0 WHERE submission_id = 'sub-applied';");
        await AssertConstraintViolationAsync(dbPath, ReceiptMetadataUpdateSql("sub-applied", "2", "1", "1", "50", "0"));
        await AssertConstraintViolationAsync(dbPath, ReceiptMetadataUpdateSql("sub-applied", "2", "1", "1", "0", "50"));
        await ExecuteRawAsync(dbPath, ReceiptMetadataUpdateSql("sub-applied", "2", "1", "1", "0", "0"));

        Assert.Equal(new object?[] { 2L, 1L, 1L, 0L, 0L }, await ReadReceiptMetadataAsync(dbPath, "sub-applied"));
        Assert.Equal(new object?[] { null, null, null, null, null }, await ReadReceiptMetadataAsync(dbPath, "sub-calm"));
    }

    // =========================================================================
    // 4. V6 -> V7 migration
    // =========================================================================

    [Fact]
    public async Task V09_V6Database_MigratesToV7_PreservingEveryLegacyValue()
    {
        var dbPath = GetTempDbPath("v09_v6_migration");
        await SeedRawV6DatabaseAsync(dbPath);
        var legacyColumns = await GetLegacyColumnsAsync(dbPath);
        var before = await SnapshotTablesAsync(dbPath, legacyColumns);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
        }

        Assert.Equal("7", await GetSchemaVersionAsync(dbPath));
        var after = await SnapshotTablesAsync(dbPath, legacyColumns
            .Where(kv => kv.Key != "gameplay_schema_info")
            .ToDictionary(kv => kv.Key, kv => kv.Value));
        foreach (var (table, rows) in after)
        {
            Assert.Equal(before[table], rows);
        }

        // Only schema_version changes in the metadata table; store revision, reset epoch and economy are preserved.
        Assert.Equal("41", (string?)await ScalarRawAsync(dbPath, "SELECT value FROM gameplay_schema_info WHERE key = 'store_revision';"));
        Assert.Equal("2", (string?)await ScalarRawAsync(dbPath, "SELECT value FROM gameplay_schema_info WHERE key = 'reset_epoch';"));
        Assert.Equal("ECONOMY_V1", (string?)await ScalarRawAsync(dbPath, "SELECT value FROM gameplay_schema_info WHERE key = 'economy_version';"));
        Assert.Equal(4L, (long)(await ScalarRawAsync(dbPath, "SELECT COUNT(*) FROM gameplay_schema_info;"))!);
    }

    [Fact]
    public async Task V10_V6Migration_PreservesProgressionFirewallRunAndResetStateThroughStoreApi()
    {
        var dbPath = GetTempDbPath("v10_v6_api");
        await SeedRawV6DatabaseAsync(dbPath);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var progression = await store.GetProgressionAsync();
        Assert.Equal(41, progression.StoreRevision);
        Assert.Equal(SeededResetEpoch, progression.ResetEpoch);
        Assert.Equal(98765, progression.CumulativeXp);
        Assert.Equal(60, progression.PlayerLevel);
        Assert.Equal(59, progression.EarnedSkillPoints);
        Assert.Equal(4, progression.SpentSkillPoints);
        Assert.Equal(55, progression.AvailableSkillPoints);
        Assert.Equal(1, progression.AttackRank);
        Assert.Equal(1, progression.FirewallRank);
        Assert.Equal(1, progression.ActivatedFirewallRank);
        Assert.Equal("ECONOMY_V1", progression.EconomyVersion);

        var run = await store.GetRunStateAsync();
        Assert.Equal(1, run.Sector);
        Assert.Equal(0, run.OpponentIndex);
        Assert.Equal(2, run.CurrentOpponent.CurrentHp);
        Assert.Equal(80, run.PlayerCurrentHp);
        Assert.Equal(1, run.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(1, run.ShieldCapacity);
        Assert.Equal(0, run.ActiveShields);
        Assert.Equal(18, run.ShieldRechargeUnits);

        Assert.Equal(SeededResetEpoch, await store.GetResetEpochAsync());
        Assert.Equal(41, await store.GetStoreRevisionAsync());
        var resetIntent = await store.GetResetIntentAsync();
        Assert.False(resetIntent.IsPending);
        Assert.Equal(SeededResetEpoch, resetIntent.CurrentEpoch);
        Assert.Equal(SeededResetEpoch, resetIntent.TargetEpoch);
    }

    [Fact]
    public async Task V11_V6Migration_GrantsNoRetroactiveCriticalOrOverdriveEntitlementsOrProgress()
    {
        var dbPath = GetTempDbPath("v11_no_retroactive");
        await SeedRawV6DatabaseAsync(dbPath);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
        }

        // Level 60 history with many correct answers still yields unpurchased skills and zero run progress.
        Assert.Equal(new long[] { 0, 0 }, await ReadModifierRanksAsync(dbPath));
        Assert.Equal(new long[] { 0, 0, 0 }, await ReadModifierRunStateAsync(dbPath));

        // Existing pending intents bind to rank 0 for both skills.
        Assert.Equal(0L, (long)(await ScalarRawAsync(dbPath, "SELECT COUNT(*) FROM gameplay_pending_intent WHERE bound_critical_strike_rank <> 0 OR bound_overdrive_rank <> 0;"))!);
        Assert.Equal(2L, (long)(await ScalarRawAsync(dbPath, "SELECT COUNT(*) FROM gameplay_pending_intent WHERE bound_critical_strike_rank = 0 AND bound_overdrive_rank = 0;"))!);

        // Historical receipts keep NULL metadata: no fabricated damage or rank-binding evidence.
        Assert.Equal(new object?[] { null, null, null, null, null }, await ReadReceiptMetadataAsync(dbPath, "sub-legacy-applied"));
        Assert.Equal(new object?[] { null, null, null, null, null }, await ReadReceiptMetadataAsync(dbPath, "sub-legacy-calm"));
    }

    [Fact]
    public async Task V12_V6Migration_PreservesPendingIntentsForRecovery_AndExistingCombatBehavior()
    {
        var dbPath = GetTempDbPath("v12_pending");
        await SeedRawV6DatabaseAsync(dbPath);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();

            var pending = await store.GetPendingIntentsAsync(SeededResetEpoch);
            Assert.Equal(2, pending.Count);
            var correct = Assert.Single(pending, p => p.SubmissionId == "sub-pending-correct");
            Assert.Equal("3+4", correct.FactId);
            Assert.True(correct.IsCorrect);
            Assert.True(correct.IsEligible);
            Assert.Equal(1500, correct.ResponseLatencyMs);
            Assert.Equal(SeededResetEpoch, correct.ResetEpoch);
            Assert.Equal(DateTimeOffset.Parse("2026-10-19T07:58:00.0000000+00:00", CultureInfo.InvariantCulture), correct.CreatedAt);
            Assert.Equal(2, correct.EffectiveAttackDamage);
            var incorrect = Assert.Single(pending, p => p.SubmissionId == "sub-pending-incorrect");
            Assert.False(incorrect.IsCorrect);
            Assert.Equal(2, incorrect.EffectiveAttackDamage);

            // Recovery replays the migrated intent with its preserved base Attack damage only.
            var receipt = await store.ApplyAttemptTransactionAsync(correct);
            Assert.Equal(CyberDefenseReceiptKind.Applied, receipt.ReceiptKind);
            Assert.Equal(2, receipt.TransitionResult!.RequestedAttackDamage);
            Assert.Null(await store.GetPendingIntentAsync("sub-pending-correct"));
        }

        // Receipt insertion is unchanged in this slice: new receipts carry no V7 metadata yet.
        Assert.Equal(new object?[] { null, null, null, null, null }, await ReadReceiptMetadataAsync(dbPath, "sub-pending-correct"));
        Assert.Equal(new long[] { 0, 0 }, await ReadModifierRanksAsync(dbPath));
        Assert.Equal(new long[] { 0, 0, 0 }, await ReadModifierRunStateAsync(dbPath));
    }

    [Fact]
    public async Task V13_V6Migration_LegacyReceiptIdempotencyAndConflictDetection_AreUnchanged()
    {
        var dbPath = GetTempDbPath("v13_idempotency");
        await SeedRawV6DatabaseAsync(dbPath);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var legacy = await store.GetReceiptAsync("sub-legacy-applied");
        Assert.NotNull(legacy);
        Assert.Equal(CyberDefenseReceiptKind.Applied, legacy.ReceiptKind);
        Assert.Equal(1, legacy.TransitionResult!.RequestedAttackDamage);
        var calm = await store.GetReceiptAsync("sub-legacy-calm");
        Assert.NotNull(calm);
        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, calm.ReceiptKind);
        Assert.Null(calm.TransitionResult);

        var progressionBefore = await store.GetProgressionAsync();
        var runBefore = await store.GetRunStateAsync();

        var replayIntent = new CyberDefensePendingIntentRecord(
            "sub-legacy-applied", "1+1", isCorrect: true, isEligible: true, 100, SeededResetEpoch, DateTimeOffset.UtcNow, effectiveAttackDamage: 1);
        var replayed = await store.ApplyAttemptTransactionAsync(replayIntent);
        Assert.Equal(legacy, replayed);

        var conflictingIntent = new CyberDefensePendingIntentRecord(
            "sub-legacy-applied", "9+9", isCorrect: true, isEligible: true, 100, SeededResetEpoch, DateTimeOffset.UtcNow, effectiveAttackDamage: 1);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(conflictingIntent));
        Assert.Contains("Conflicting receipt", ex.Message, StringComparison.Ordinal);

        Assert.Equal(progressionBefore, await store.GetProgressionAsync());
        Assert.Equal(runBefore, await store.GetRunStateAsync());
        Assert.Equal(legacy, await store.GetReceiptAsync("sub-legacy-applied"));
    }

    [Fact]
    public async Task V14_MigratedV6Columns_MatchFreshV7Columns_Deterministically()
    {
        var freshPath = await CreateFreshStoreAsync("v14_fresh");
        var migratedPath = GetTempDbPath("v14_migrated");
        await SeedRawV6DatabaseAsync(migratedPath);
        using (var store = new SqliteGameplayStore(migratedPath))
        {
            await store.InitializeAsync();
        }

        foreach (var table in GameplayTables)
        {
            Assert.Equal(await GetColumnsAsync(freshPath, table), await GetColumnsAsync(migratedPath, table));
        }
    }

    [Fact]
    public async Task V15_ReopeningMigratedV7Database_IsRepeatSafe()
    {
        var dbPath = GetTempDbPath("v15_reopen");
        await SeedRawV6DatabaseAsync(dbPath);
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
        }

        var schemaSql = (string?)await ScalarRawAsync(dbPath, "SELECT group_concat(sql, ';') FROM (SELECT sql FROM sqlite_master WHERE sql IS NOT NULL ORDER BY name);");
        var allColumns = await GetLegacyColumnsAsync(dbPath);
        var snapshot = await SnapshotTablesAsync(dbPath, allColumns);

        for (int i = 0; i < 2; i++)
        {
            using var reopened = new SqliteGameplayStore(dbPath);
            await reopened.InitializeAsync();
            Assert.Equal(1, (await reopened.GetProgressionAsync()).AttackRank);
        }

        Assert.Equal("7", await GetSchemaVersionAsync(dbPath));
        Assert.Equal(schemaSql, (string?)await ScalarRawAsync(dbPath, "SELECT group_concat(sql, ';') FROM (SELECT sql FROM sqlite_master WHERE sql IS NOT NULL ORDER BY name);"));
        var reopenedSnapshot = await SnapshotTablesAsync(dbPath, allColumns);
        foreach (var (table, rows) in reopenedSnapshot)
        {
            Assert.Equal(snapshot[table], rows);
        }
    }

    // =========================================================================
    // 5. Earlier schema chains, future versions and failure rollback
    // =========================================================================

    [Fact]
    public async Task V16_V5Database_UpgradeChainReachesV7()
    {
        var dbPath = GetTempDbPath("v16_v5_chain");
        await SeedRawV6DatabaseAsync(dbPath);
        // Derive an unambiguous V5 store (Firewall rank 2 fully activated) from the V6 layout.
        await ExecuteRawAsync(dbPath, @"
            UPDATE gameplay_progression SET firewall_rank = 2, spent_sp = 12, available_sp = 47, activated_firewall_rank = 2;
            UPDATE gameplay_run_state SET shield_capacity = 2, active_shields = 1, shield_recharge_units = 18;
            ALTER TABLE gameplay_progression DROP COLUMN activated_firewall_rank;
            UPDATE gameplay_schema_info SET value = '5' WHERE key = 'schema_version';");

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var progression = await store.GetProgressionAsync();
            Assert.Equal(2, progression.FirewallRank);
            Assert.Equal(2, progression.ActivatedFirewallRank);
            Assert.Equal(98765, progression.CumulativeXp);
            Assert.Equal(1, progression.AttackRank);
            Assert.Equal(2, (await store.GetPendingIntentsAsync(SeededResetEpoch)).Count);
        }

        Assert.Equal("7", await GetSchemaVersionAsync(dbPath));
        Assert.Equal(new long[] { 0, 0 }, await ReadModifierRanksAsync(dbPath));
        Assert.Equal(new long[] { 0, 0, 0 }, await ReadModifierRunStateAsync(dbPath));
        Assert.Equal(new object?[] { null, null, null, null, null }, await ReadReceiptMetadataAsync(dbPath, "sub-legacy-applied"));
    }

    [Theory]
    [InlineData("8")]
    [InlineData("42")]
    public async Task V17_UnsupportedFutureSchemaVersion_IsRejectedWithoutMutation(string futureVersion)
    {
        var dbPath = GetTempDbPath("v17_future");
        await SeedRawV6DatabaseAsync(dbPath);
        await ExecuteRawAsync(dbPath, $"UPDATE gameplay_schema_info SET value = '{futureVersion}' WHERE key = 'schema_version';");

        using var store = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Contains($"Unsupported database schema version {futureVersion}", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Maximum supported version is 7", ex.Message, StringComparison.Ordinal);

        Assert.Equal(futureVersion, await GetSchemaVersionAsync(dbPath));
        Assert.DoesNotContain(await GetColumnsAsync(dbPath, "gameplay_progression"), c => c.Name == "critical_strike_rank");
    }

    [Fact]
    public async Task V18_MalformedV6WithConflictingColumn_RollsBackEntireV7Migration()
    {
        var dbPath = GetTempDbPath("v18_rollback");
        await SeedRawV6DatabaseAsync(dbPath);
        // A V6-labelled store that already contains one of the V7 receipt columns is malformed.
        await ExecuteRawAsync(dbPath, "ALTER TABLE gameplay_receipt_ledger ADD COLUMN overdrive_bonus_percent INTEGER;");
        var legacyColumns = await GetLegacyColumnsAsync(dbPath);
        var before = await SnapshotTablesAsync(dbPath, legacyColumns);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
            Assert.Contains("overdrive_bonus_percent", ex.Message, StringComparison.Ordinal);
        }

        Assert.Equal("6", await GetSchemaVersionAsync(dbPath));
        Assert.DoesNotContain(await GetColumnsAsync(dbPath, "gameplay_progression"), c => ProgressionV7Columns.Contains(c.Name));
        Assert.DoesNotContain(await GetColumnsAsync(dbPath, "gameplay_run_state"), c => RunStateV7Columns.Contains(c.Name));
        Assert.DoesNotContain(await GetColumnsAsync(dbPath, "gameplay_pending_intent"), c => PendingIntentV7Columns.Contains(c.Name));
        var after = await SnapshotTablesAsync(dbPath, legacyColumns);
        foreach (var (table, rows) in after)
        {
            Assert.Equal(before[table], rows);
        }
    }

    // =========================================================================
    // 6. Validation on reopen, reset safety and unchanged combat
    // =========================================================================

    [Theory]
    [InlineData("gameplay_progression", "critical_strike_rank", "3")]
    [InlineData("gameplay_progression", "overdrive_rank", "-1")]
    [InlineData("gameplay_run_state", "critical_correct_attack_count", "-1")]
    [InlineData("gameplay_run_state", "overdrive_streak", "-1")]
    [InlineData("gameplay_run_state", "overdrive_remaining_bonus_attacks", "3")]
    [InlineData("gameplay_pending_intent", "bound_critical_strike_rank", "3")]
    [InlineData("gameplay_pending_intent", "bound_overdrive_rank", "-1")]
    public async Task V19_Reopen_FailsClosedOnOutOfRangeModifierStateBypassingConstraints(string table, string column, string sqlValue)
    {
        var dbPath = await CreateFreshStoreAsync("v19_reopen_validation");
        await ExecuteRawAsync(dbPath, LegacyAppliedReceiptInsertSql);
        await ExecuteRawAsync(dbPath, $"PRAGMA ignore_check_constraints = ON; UPDATE {table} SET {column} = {sqlValue};");

        using var store = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Contains(column, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task V20_EpochReset_ClearsModifierRanksAndRunCounters()
    {
        var dbPath = await CreateFreshStoreAsync("v20_reset");
        await ExecuteRawAsync(dbPath, @"
            UPDATE gameplay_progression SET critical_strike_rank = 1, overdrive_rank = 2;
            UPDATE gameplay_run_state SET critical_correct_attack_count = 3, overdrive_streak = 2, overdrive_remaining_bonus_attacks = 1;");

        using (var store = new SqliteGameplayStore(dbPath))
        {
            var intent = await store.BeginOrGetResetIntentAsync();
            await store.ResetGameplayStateAsync(intent.TargetEpoch);
            await store.ClearResetIntentAsync(intent.TargetEpoch);
            Assert.Equal(intent.TargetEpoch, await store.GetResetEpochAsync());
        }

        Assert.Equal(new long[] { 0, 0 }, await ReadModifierRanksAsync(dbPath));
        Assert.Equal(new long[] { 0, 0, 0 }, await ReadModifierRunStateAsync(dbPath));
    }

    [Fact]
    public async Task V21_FreshV7_ExistingCombatAndStaging_AreUnchanged()
    {
        var dbPath = GetTempDbPath("v21_combat");
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-fresh", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);
            await store.StagePendingIntentAsync(intent);

            Assert.Equal(0L, (long)(await ScalarRawAsync(dbPath, "SELECT bound_critical_strike_rank + bound_overdrive_rank FROM gameplay_pending_intent WHERE submission_id = 'sub-fresh';"))!);
            Assert.Equal(1L, (long)(await ScalarRawAsync(dbPath, "SELECT effective_attack_damage FROM gameplay_pending_intent WHERE submission_id = 'sub-fresh';"))!);

            var receipt = await store.ApplyAttemptTransactionAsync(intent);
            Assert.Equal(1, receipt.TransitionResult!.RequestedAttackDamage);
            Assert.Equal(1, receipt.TransitionResult.AppliedOpponentDamage);
            Assert.Equal(6, receipt.AwardedXp);
            Assert.Equal(receipt, await store.ApplyAttemptTransactionAsync(intent));
        }

        Assert.Equal(new object?[] { null, null, null, null, null }, await ReadReceiptMetadataAsync(dbPath, "sub-fresh"));
        Assert.Equal(new long[] { 0, 0 }, await ReadModifierRanksAsync(dbPath));
        Assert.Equal(new long[] { 0, 0, 0 }, await ReadModifierRunStateAsync(dbPath));
    }
}
