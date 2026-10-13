namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SqliteGameplayStoreSchemaV6MigrationTests : IDisposable
{
    private readonly string _testDirectory;

    public SqliteGameplayStoreSchemaV6MigrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_V6_" + Guid.NewGuid().ToString("N"));
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

    private string GetTempDbPath(string name = "gameplay_v6_test")
    {
        return Path.Combine(_testDirectory, $"{name}_{Guid.NewGuid():N}.db");
    }

    private const string RawSchemaV5Ddl = @"
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
            next_shield_recharge_units INTEGER CHECK (next_shield_recharge_units IS NULL OR (next_shield_recharge_units >= 0 AND next_shield_recharge_units < 30))
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
            updated_at TEXT NOT NULL
        );
    ";

    private async Task SeedRawV5DatabaseAsync(
        string dbPath,
        int firewallRank = 0,
        int isAssistanceActive = 0,
        int shieldCapacity = 0,
        int activeShields = 0,
        int shieldRechargeUnits = 0,
        int playerLevel = 1,
        int earnedSp = 0,
        int spentSp = 0,
        int availableSp = 0)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = RawSchemaV5Ddl;
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = @"
            INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '5');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '1');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');

            INSERT INTO gameplay_progression (
                id, store_revision, reset_epoch,
                cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank,
                economy_version, updated_at,
                firewall_rank, consecutive_early_defeats, is_beginner_assistance_active
            ) VALUES (1, 1, 0, 0, @lvl, @earned, @spent, @avail, 0, 'ECONOMY_V1', '2026-10-13T12:00:00Z', @fw, 0, @assist);

            INSERT INTO gameplay_run_state (
                id, sector, opponent_index, opponent_current_hp, player_current_hp,
                opponent_executed_attacks, opponent_encounter_provenance,
                shield_capacity, active_shields, shield_recharge_units,
                reset_epoch, updated_at
            ) VALUES (1, 1, 0, 2, 100, 0, 'Authoritative', @cap, @active, @recharge, 0, '2026-10-13T12:00:00Z');

            INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
            VALUES (1, 0, 0, 0, NULL, '2026-10-13T12:00:00Z');
        ";
        cmd.Parameters.AddWithValue("@lvl", playerLevel);
        cmd.Parameters.AddWithValue("@earned", earnedSp);
        cmd.Parameters.AddWithValue("@spent", spentSp);
        cmd.Parameters.AddWithValue("@avail", availableSp);
        cmd.Parameters.AddWithValue("@fw", firewallRank);
        cmd.Parameters.AddWithValue("@assist", isAssistanceActive);
        cmd.Parameters.AddWithValue("@cap", shieldCapacity);
        cmd.Parameters.AddWithValue("@active", activeShields);
        cmd.Parameters.AddWithValue("@recharge", shieldRechargeUnits);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task T01_FreshGameplayStore_InitializesSchemaV6()
    {
        Assert.Equal(6, SqliteGameplayStore.DefaultSchemaVersion);

        var dbPath = GetTempDbPath("t01_fresh_v6");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
        var version = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal("6", version);

        cmd.CommandText = @"
            SELECT firewall_rank, activated_firewall_rank, consecutive_early_defeats, is_beginner_assistance_active
            FROM gameplay_progression WHERE id = 1;
        ";
        using var progReader = await cmd.ExecuteReaderAsync();
        Assert.True(await progReader.ReadAsync());
        Assert.Equal(0, progReader.GetInt32(0)); // firewall_rank
        Assert.Equal(0, progReader.GetInt32(1)); // activated_firewall_rank
        Assert.Equal(0, progReader.GetInt32(2)); // consecutive_early_defeats
        Assert.Equal(0, progReader.GetInt32(3)); // is_beginner_assistance_active
        progReader.Close();

        var prog = await store.GetProgressionAsync();
        Assert.Equal(0, prog.FirewallRank);
        Assert.Equal(0, prog.ActivatedFirewallRank);
    }

    [Fact]
    public async Task T02_ExistingV5Database_MigratesToSchemaV6_WhenUnambiguous()
    {
        // Unambiguous case A: firewall_rank 0, shield_capacity 0
        var dbPathA = GetTempDbPath("unambiguous_fw0_cap0");
        await SeedRawV5DatabaseAsync(dbPathA, firewallRank: 0, shieldCapacity: 0);

        using (var storeA = new SqliteGameplayStore(dbPathA))
        {
            await storeA.InitializeAsync();
            var prog = await storeA.GetProgressionAsync();
            Assert.Equal(0, prog.FirewallRank);
            Assert.Equal(0, prog.ActivatedFirewallRank);
        }

        using (var conn = new SqliteConnection($"Data Source={dbPathA}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
            Assert.Equal("6", (string?)await cmd.ExecuteScalarAsync());
        }

        // Unambiguous case B: firewall_rank 0, shield_capacity 1 (temporary emergency shield)
        var dbPathB = GetTempDbPath("unambiguous_fw0_cap1");
        await SeedRawV5DatabaseAsync(dbPathB, firewallRank: 0, isAssistanceActive: 1, shieldCapacity: 1, activeShields: 1, shieldRechargeUnits: 0);

        using (var storeB = new SqliteGameplayStore(dbPathB))
        {
            await storeB.InitializeAsync();
            var prog = await storeB.GetProgressionAsync();
            Assert.Equal(0, prog.FirewallRank);
            Assert.Equal(0, prog.ActivatedFirewallRank);
        }

        // Unambiguous case C: firewall_rank 2, shield_capacity 2 (fully activated rank 2)
        var dbPathC = GetTempDbPath("unambiguous_fw2_cap2");
        await SeedRawV5DatabaseAsync(dbPathC, firewallRank: 2, shieldCapacity: 2, activeShields: 1, shieldRechargeUnits: 15, playerLevel: 20, earnedSp: 20, spentSp: 8, availableSp: 12);

        using (var storeC = new SqliteGameplayStore(dbPathC))
        {
            await storeC.InitializeAsync();
            var prog = await storeC.GetProgressionAsync();
            Assert.Equal(2, prog.FirewallRank);
            Assert.Equal(2, prog.ActivatedFirewallRank);
        }

        // Unambiguous case D: firewall_rank 1, shield_capacity 0 (purchased, unactivated)
        var dbPathD = GetTempDbPath("unambiguous_fw1_cap0");
        await SeedRawV5DatabaseAsync(dbPathD, firewallRank: 1, shieldCapacity: 0, activeShields: 0, shieldRechargeUnits: 0, playerLevel: 5, earnedSp: 5, spentSp: 3, availableSp: 2);

        using (var storeD = new SqliteGameplayStore(dbPathD))
        {
            await storeD.InitializeAsync();
            var prog = await storeD.GetProgressionAsync();
            Assert.Equal(1, prog.FirewallRank);
            Assert.Equal(0, prog.ActivatedFirewallRank);
        }
    }

    [Fact]
    public async Task T03_ExistingV5Database_WithAmbiguousState_RollsBackMigration_AndPreservesV5()
    {
        // Canonical ambiguous example from Section 6 of prompt:
        // firewall_rank = 1, is_beginner_assistance_active = 0, shield_capacity = 1, active_shields = 0, shield_recharge_units = 18
        var dbPath = GetTempDbPath("ambiguous_v5");
        await SeedRawV5DatabaseAsync(
            dbPath,
            firewallRank: 1,
            isAssistanceActive: 0,
            shieldCapacity: 1,
            activeShields: 0,
            shieldRechargeUnits: 18,
            playerLevel: 5,
            earnedSp: 5,
            spentSp: 3,
            availableSp: 2);

        using var store = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Contains("Ambiguous", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Verify complete rollback: schema version remains 5
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
        Assert.Equal("5", (string?)await cmd.ExecuteScalarAsync());

        // Verify original data intact
        cmd.CommandText = "SELECT firewall_rank, is_beginner_assistance_active FROM gameplay_progression WHERE id = 1;";
        using var progReader = await cmd.ExecuteReaderAsync();
        Assert.True(await progReader.ReadAsync());
        Assert.Equal(1, progReader.GetInt32(0));
        Assert.Equal(0, progReader.GetInt32(1));
        progReader.Close();

        cmd.CommandText = "SELECT shield_capacity, active_shields, shield_recharge_units FROM gameplay_run_state WHERE id = 1;";
        using var runReader = await cmd.ExecuteReaderAsync();
        Assert.True(await runReader.ReadAsync());
        Assert.Equal(1, runReader.GetInt32(0));
        Assert.Equal(0, runReader.GetInt32(1));
        Assert.Equal(18, runReader.GetInt32(2));
    }

    [Fact]
    public async Task T04_RejectUnsupportedFutureSchemaVersion_ThrowsInvalidOperationException()
    {
        var dbPath = GetTempDbPath("unsupported_v7");
        await SeedRawV5DatabaseAsync(dbPath, firewallRank: 0, shieldCapacity: 0);

        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_schema_info SET value = '7' WHERE key = 'schema_version';";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Contains("Unsupported database schema version 7", ex.Message);
    }
}
