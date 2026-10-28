namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

/// <summary>
/// MF-CYBER-009 Slice 4A: Gameplay Store Schema V8 and offline achievement persistence foundation.
/// Real SQLite integration tests for schema creation, transactional migrations, fail-closed constraints.
/// </summary>
public sealed class SqliteGameplayStoreSchemaV8MigrationTests : IDisposable
{
    private const int SqliteConstraintErrorCode = 19;
    private readonly string _testDirectory;

    public SqliteGameplayStoreSchemaV8MigrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_V8_" + Guid.NewGuid().ToString("N"));
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
            // Best effort cleanup in temp directory
        }
    }

    private string GetTempDbPath(string name) =>
        Path.Combine(_testDirectory, $"{name}_{Guid.NewGuid():N}.db");

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

    private static async Task<List<string>> GetTableNamesAsync(string dbPath)
    {
        using var connection = OpenRaw(dbPath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<string>();
        while (await reader.ReadAsync())
        {
            list.Add(reader.GetString(0));
        }

        return list;
    }

    private const string RawSchemaV7Ddl = @"
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
            critical_strike_rank INTEGER NOT NULL DEFAULT 0 CHECK (critical_strike_rank >= 0 AND critical_strike_rank <= 2),
            overdrive_rank INTEGER NOT NULL DEFAULT 0 CHECK (overdrive_rank >= 0 AND overdrive_rank <= 2),
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
            critical_correct_attack_count INTEGER NOT NULL DEFAULT 0 CHECK (critical_correct_attack_count >= 0 AND critical_correct_attack_count <= 2147483647),
            overdrive_streak INTEGER NOT NULL DEFAULT 0 CHECK (overdrive_streak >= 0 AND overdrive_streak <= 2147483647),
            overdrive_remaining_bonus_attacks INTEGER NOT NULL DEFAULT 0 CHECK (overdrive_remaining_bonus_attacks >= 0 AND overdrive_remaining_bonus_attacks <= 2),
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
            base_attack_damage INTEGER CHECK (base_attack_damage IS NULL OR base_attack_damage >= 1),
            bound_critical_strike_rank INTEGER CHECK (bound_critical_strike_rank IS NULL OR (bound_critical_strike_rank >= 0 AND bound_critical_strike_rank <= 2)),
            bound_overdrive_rank INTEGER CHECK (bound_overdrive_rank IS NULL OR (bound_overdrive_rank >= 0 AND bound_overdrive_rank <= 2)),
            critical_bonus_percent INTEGER CHECK (critical_bonus_percent IS NULL OR (critical_bonus_percent >= 0 AND critical_bonus_percent <= 100)),
            overdrive_bonus_percent INTEGER CHECK (overdrive_bonus_percent IS NULL OR (overdrive_bonus_percent >= 0 AND overdrive_bonus_percent <= 50))
        );

        CREATE TABLE gameplay_pending_intent (
            submission_id TEXT PRIMARY KEY CHECK (length(submission_id) > 0),
            fact_id TEXT NOT NULL,
            is_correct INTEGER NOT NULL CHECK (is_correct IN (0, 1)),
            is_eligible INTEGER NOT NULL CHECK (is_eligible IN (0, 1)),
            response_latency_ms INTEGER NOT NULL CHECK (response_latency_ms >= 0),
            reset_epoch INTEGER NOT NULL CHECK (reset_epoch >= 0),
            created_at TEXT NOT NULL,
            effective_attack_damage INTEGER NOT NULL DEFAULT 1 CHECK (effective_attack_damage >= 1),
            bound_critical_strike_rank INTEGER NOT NULL DEFAULT 0 CHECK (bound_critical_strike_rank >= 0 AND bound_critical_strike_rank <= 2),
            bound_overdrive_rank INTEGER NOT NULL DEFAULT 0 CHECK (bound_overdrive_rank >= 0 AND bound_overdrive_rank <= 2),
            practice_position INTEGER CHECK (practice_position IS NULL OR practice_position >= 1)
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

    private const string SeedV7DataSql = @"
        INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '7');
        INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '15');
        INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '1');
        INSERT INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');

        INSERT INTO gameplay_progression (
            id, store_revision, reset_epoch,
            cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank,
            economy_version, updated_at,
            firewall_rank, consecutive_early_defeats, is_beginner_assistance_active,
            activated_firewall_rank, critical_strike_rank, overdrive_rank
        ) VALUES (
            1, 15, 1,
            250, 4, 3, 2, 1, 1,
            'ECONOMY_V1', '2026-10-28T12:00:00.0000000+00:00',
            1, 0, 0,
            1, 1, 0
        );

        INSERT INTO gameplay_run_state (
            id, sector, opponent_index, opponent_current_hp, player_current_hp,
            opponent_executed_attacks, opponent_encounter_provenance,
            shield_capacity, active_shields, shield_recharge_units,
            reset_epoch, updated_at,
            critical_correct_attack_count, overdrive_streak, overdrive_remaining_bonus_attacks
        ) VALUES (
            1, 1, 0, 2, 90,
            1, 'Authoritative',
            1, 1, 0,
            1, '2026-10-28T12:00:00.0000000+00:00',
            1, 2, 0
        );

        INSERT INTO gameplay_receipt_ledger (
            submission_id, receipt_kind, fact_id, is_correct, is_eligible,
            response_latency_ms, reset_epoch, processed_at,
            requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
            incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
            potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
            is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
            next_player_current_hp, next_opponent_executed_attacks,
            awarded_xp, player_level_after, cumulative_xp_after,
            next_opponent_encounter_provenance, is_shield_absorbed, absorbed_enemy_damage,
            next_shield_capacity, next_active_shields, next_shield_recharge_units,
            base_attack_damage, bound_critical_strike_rank, bound_overdrive_rank,
            critical_bonus_percent, overdrive_bonus_percent
        ) VALUES (
            'sub-v7-1', 'Applied', '3+3', 1, 1,
            1200, 1, '2026-10-28T12:00:00.0000000+00:00',
            2, 2, 0,
            0, 0, 0,
            0, 0, 0, 0,
            0, 1, 0, 2,
            90, 1,
            10, 4, 250,
            'Authoritative', 0, 0,
            1, 1, 0,
            2, 1, 0,
            0, 0
        );

        INSERT INTO gameplay_pending_intent (
            submission_id, fact_id, is_correct, is_eligible, response_latency_ms,
            reset_epoch, created_at, effective_attack_damage,
            bound_critical_strike_rank, bound_overdrive_rank, practice_position
        ) VALUES (
            'sub-v7-pending', '4+4', 1, 1, 800,
            1, '2026-10-28T12:01:00.0000000+00:00', 2,
            1, 0, 3
        );

        INSERT INTO gameplay_reset_intent (
            id, is_pending, current_epoch, target_epoch, created_at, updated_at
        ) VALUES (
            1, 0, 1, 1, NULL, '2026-10-28T12:00:00.0000000+00:00'
        );
    ";

    // =========================================================================
    // 1. Schema version and fresh initialization
    // =========================================================================

    [Fact]
    public void V01_CurrentGameplaySchema_IsV8_AndLearnerStoreSchemaRemainsV9()
    {
        Assert.Equal(8, SqliteGameplayStore.DefaultSchemaVersion);
        Assert.Equal(9, LearnerProgression.DefaultSchemaVersion);
    }

    [Fact]
    public async Task V02_FreshStore_InitializesExactSchemaV8()
    {
        var dbPath = await CreateFreshStoreAsync("v02_fresh_v8");
        Assert.Equal("8", await GetSchemaVersionAsync(dbPath));
    }

    [Fact]
    public async Task V03_FreshStore_HasExactlySevenTables()
    {
        var dbPath = await CreateFreshStoreAsync("v03_tables_v8");
        var tables = await GetTableNamesAsync(dbPath);

        var expectedTables = new[]
        {
            "gameplay_schema_info",
            "gameplay_progression",
            "gameplay_run_state",
            "gameplay_receipt_ledger",
            "gameplay_pending_intent",
            "gameplay_reset_intent",
            "local_achievements"
        };

        Assert.Equal(7, tables.Count);
        foreach (var expected in expectedTables)
        {
            Assert.Contains(expected, tables);
        }
    }

    [Fact]
    public async Task V04_AchievementTable_HasExpectedColumnsAndConstraints()
    {
        var dbPath = await CreateFreshStoreAsync("v04_achievements_schema");
        var columns = (await GetColumnsAsync(dbPath, "local_achievements")).ToDictionary(c => c.Name);

        Assert.Equal(3, columns.Count);

        Assert.True(columns.ContainsKey("achievement_id"));
        Assert.Equal("TEXT", columns["achievement_id"].Type);
        Assert.True(columns["achievement_id"].NotNull);
        Assert.Equal(1, columns["achievement_id"].PrimaryKey);

        Assert.True(columns.ContainsKey("reset_epoch"));
        Assert.Equal("INTEGER", columns["reset_epoch"].Type);
        Assert.True(columns["reset_epoch"].NotNull);
        Assert.Equal(0, columns["reset_epoch"].PrimaryKey);

        Assert.True(columns.ContainsKey("unlocked_at_utc"));
        Assert.Equal("TEXT", columns["unlocked_at_utc"].Type);
        Assert.True(columns["unlocked_at_utc"].NotNull);
        Assert.Equal(0, columns["unlocked_at_utc"].PrimaryKey);
    }

    // =========================================================================
    // 2. Constraints and fail-closed validation
    // =========================================================================

    [Fact]
    public async Task V05_AchievementTable_EnforcesUniqueAchievementId()
    {
        var dbPath = await CreateFreshStoreAsync("v05_unique_id");
        var now = DateTimeOffset.UtcNow.ToString("O");

        await ExecuteRawAsync(dbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 0, '{now}');
        ");

        var ex = await Assert.ThrowsAsync<SqliteException>(() => ExecuteRawAsync(dbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('sector_10', 0, '{now}');
        "));
        Assert.Equal(SqliteConstraintErrorCode, ex.SqliteErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task V06_AchievementTable_RejectsEmptyOrWhitespaceAchievementId(string invalidId)
    {
        var dbPath = await CreateFreshStoreAsync("v06_invalid_id");
        var now = DateTimeOffset.UtcNow.ToString("O");

        var ex = await Assert.ThrowsAsync<SqliteException>(() => ExecuteRawAsync(dbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('{invalidId}', 0, '{now}');
        "));
        Assert.Equal(SqliteConstraintErrorCode, ex.SqliteErrorCode);
    }

    [Fact]
    public async Task V07_AchievementTable_RejectsNegativeResetEpoch()
    {
        var dbPath = await CreateFreshStoreAsync("v07_negative_epoch");
        var now = DateTimeOffset.UtcNow.ToString("O");

        var ex = await Assert.ThrowsAsync<SqliteException>(() => ExecuteRawAsync(dbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('boss_slayer', -1, '{now}');
        "));
        Assert.Equal(SqliteConstraintErrorCode, ex.SqliteErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task V08_AchievementTable_RejectsEmptyOrWhitespaceTimestamp(string invalidTimestamp)
    {
        var dbPath = await CreateFreshStoreAsync("v08_invalid_timestamp");

        var ex = await Assert.ThrowsAsync<SqliteException>(() => ExecuteRawAsync(dbPath, $@"
            INSERT INTO local_achievements (achievement_id, reset_epoch, unlocked_at_utc)
            VALUES ('boss_slayer', 0, '{invalidTimestamp}');
        "));
        Assert.Equal(SqliteConstraintErrorCode, ex.SqliteErrorCode);
    }

    // =========================================================================
    // 3. V7 to V8 Migration
    // =========================================================================

    [Fact]
    public async Task V09_MigrateV7ToV8_CreatesEmptyAchievementTable_AndUpdatesSchemaVersion()
    {
        var dbPath = GetTempDbPath("v09_migrate_v7_v8");
        await ExecuteRawAsync(dbPath, RawSchemaV7Ddl + SeedV7DataSql);

        Assert.Equal("7", await GetSchemaVersionAsync(dbPath));

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
        }

        Assert.Equal("8", await GetSchemaVersionAsync(dbPath));
        var tables = await GetTableNamesAsync(dbPath);
        Assert.Contains("local_achievements", tables);

        long achievementCount = (long)(await ScalarRawAsync(dbPath, "SELECT COUNT(*) FROM local_achievements;"))!;
        Assert.Equal(0L, achievementCount);
    }

    [Fact]
    public async Task V10_MigrateV7ToV8_PreservesAllExistingGameplayData()
    {
        var dbPath = GetTempDbPath("v10_preserve_v7_data");
        await ExecuteRawAsync(dbPath, RawSchemaV7Ddl + SeedV7DataSql);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();

            var prog = await store.GetProgressionAsync();
            Assert.Equal(15, prog.StoreRevision);
            Assert.Equal(1, prog.ResetEpoch);
            Assert.Equal(250, prog.CumulativeXp);
            Assert.Equal(4, prog.PlayerLevel);
            Assert.Equal(3, prog.EarnedSkillPoints);
            Assert.Equal(2, prog.SpentSkillPoints);
            Assert.Equal(1, prog.AvailableSkillPoints);
            Assert.Equal(1, prog.AttackRank);
            Assert.Equal(1, prog.FirewallRank);
            Assert.Equal(1, prog.ActivatedFirewallRank);

            var run = await store.GetRunStateAsync();
            Assert.Equal(1, run.Sector);
            Assert.Equal(0, run.OpponentIndex);
            Assert.Equal(2, run.CurrentOpponent.CurrentHp);
            Assert.Equal(90, run.PlayerCurrentHp);

            var receipt = await store.GetReceiptAsync("sub-v7-1");
            Assert.NotNull(receipt);
            Assert.Equal("sub-v7-1", receipt.SubmissionId);

            var pending = await store.GetPendingIntentAsync("sub-v7-pending");
            Assert.NotNull(pending);
            Assert.Equal("sub-v7-pending", pending.SubmissionId);
            Assert.Equal(3, pending.PracticePosition);

            var resetIntent = await store.GetResetIntentAsync();
            Assert.False(resetIntent.IsPending);
            Assert.Equal(1, resetIntent.CurrentEpoch);
        }
    }

    [Fact]
    public async Task V11_MigrateV7ToV8_PrematureAchievementTable_FailsClosedAndRollsBack()
    {
        var dbPath = GetTempDbPath("v11_premature_table");
        await ExecuteRawAsync(dbPath, RawSchemaV7Ddl + SeedV7DataSql);

        // Intentionally create a premature local_achievements table in V7 db
        await ExecuteRawAsync(dbPath, "CREATE TABLE local_achievements (id TEXT PRIMARY KEY);");

        using (var store = new SqliteGameplayStore(dbPath))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
            Assert.Contains("local_achievements", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Schema version must not have advanced to 8
        Assert.Equal("7", await GetSchemaVersionAsync(dbPath));
    }

    [Fact]
    public async Task V12_UnsupportedFutureSchemaVersion_ThrowsAndLeavesDatabaseIntact()
    {
        var dbPath = GetTempDbPath("v12_future_version");
        await ExecuteRawAsync(dbPath, RawSchemaV7Ddl + SeedV7DataSql);
        await ExecuteRawAsync(dbPath, "UPDATE gameplay_schema_info SET value = '99' WHERE key = 'schema_version';");

        using (var store = new SqliteGameplayStore(dbPath))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
            Assert.Contains("99", ex.Message);
        }

        Assert.Equal("99", await GetSchemaVersionAsync(dbPath));
    }

    [Fact]
    public async Task V13_NativeV8_MissingAchievementTable_FailsClosed()
    {
        var dbPath = GetTempDbPath("v13_missing_achievements");
        await ExecuteRawAsync(dbPath, RawSchemaV7Ddl + SeedV7DataSql);
        await ExecuteRawAsync(dbPath, "UPDATE gameplay_schema_info SET value = '8' WHERE key = 'schema_version';");

        using (var store = new SqliteGameplayStore(dbPath))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
            Assert.Contains("local_achievements", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task V14_NativeV8_MissingRequiredColumn_FailsClosed()
    {
        var dbPath = GetTempDbPath("v14_missing_col");
        await ExecuteRawAsync(dbPath, RawSchemaV7Ddl + SeedV7DataSql);
        await ExecuteRawAsync(dbPath, "UPDATE gameplay_schema_info SET value = '8' WHERE key = 'schema_version';");
        // Create table missing unlocked_at_utc
        await ExecuteRawAsync(dbPath, "CREATE TABLE local_achievements (achievement_id TEXT PRIMARY KEY NOT NULL, reset_epoch INTEGER NOT NULL);");

        using var store = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Contains("unlocked_at_utc", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task V15_NativeV8_MalformedRow_InvalidTimestamp_FailsClosed()
    {
        var dbPath = GetTempDbPath("v15_malformed_timestamp");
        await ExecuteRawAsync(dbPath, RawSchemaV7Ddl + SeedV7DataSql);
        await ExecuteRawAsync(dbPath, "UPDATE gameplay_schema_info SET value = '8' WHERE key = 'schema_version';");
        await ExecuteRawAsync(dbPath, @"
            CREATE TABLE local_achievements (
                achievement_id TEXT PRIMARY KEY NOT NULL,
                reset_epoch INTEGER NOT NULL,
                unlocked_at_utc TEXT NOT NULL
            );
            INSERT INTO local_achievements VALUES ('sector_10', 1, '2026-10-28 12:00:00+02:00');
        ");

        using var store = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Contains("timestamp", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task V16_ReopeningValidV8Database_IsRepeatSafe()
    {
        var dbPath = await CreateFreshStoreAsync("v16_reopen");

        for (int i = 0; i < 3; i++)
        {
            using var store = new SqliteGameplayStore(dbPath);
            await store.InitializeAsync();
            Assert.Equal("8", await GetSchemaVersionAsync(dbPath));
        }
    }
}
