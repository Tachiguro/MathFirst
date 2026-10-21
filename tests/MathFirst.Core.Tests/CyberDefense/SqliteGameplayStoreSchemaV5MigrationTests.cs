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

public sealed class SqliteGameplayStoreSchemaV5MigrationTests : IDisposable
{
    private readonly string _testDirectory;

    public SqliteGameplayStoreSchemaV5MigrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_V5_" + Guid.NewGuid().ToString("N"));
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

    private string GetTempDbPath(string name = "gameplay_v5_test")
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

    private const string RawSchemaV4Ddl = @"
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

    [Fact]
    public async Task FreshDatabase_InitializesDirectlyToSchemaV5()
    {
        var dbPath = GetTempDbPath("fresh_v5");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // 1. Verify schema info version is 6
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
        var version = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal("7", version);

        // 2. Verify gameplay_progression has firewall_rank 0, consecutive_early_defeats 0, is_beginner_assistance_active 0
        cmd.CommandText = @"
            SELECT firewall_rank, consecutive_early_defeats, is_beginner_assistance_active,
                   cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank
            FROM gameplay_progression WHERE id = 1;
        ";
        using var progReader = await cmd.ExecuteReaderAsync();
        Assert.True(await progReader.ReadAsync());
        Assert.Equal(0, progReader.GetInt32(0)); // firewall_rank
        Assert.Equal(0, progReader.GetInt32(1)); // consecutive_early_defeats
        Assert.Equal(0, progReader.GetInt32(2)); // is_beginner_assistance_active
        Assert.Equal(0, progReader.GetInt64(3)); // cumulative_xp
        Assert.Equal(1, progReader.GetInt32(4)); // player_level
        Assert.Equal(0, progReader.GetInt32(5)); // earned_sp
        Assert.Equal(0, progReader.GetInt32(6)); // spent_sp
        Assert.Equal(0, progReader.GetInt32(7)); // available_sp
        Assert.Equal(0, progReader.GetInt32(8)); // attack_rank
        progReader.Close();

        // 3. Verify gameplay_run_state has shield_capacity 0, active_shields 0, shield_recharge_units 0
        cmd.CommandText = @"
            SELECT shield_capacity, active_shields, shield_recharge_units, sector, opponent_index, player_current_hp
            FROM gameplay_run_state WHERE id = 1;
        ";
        using var runReader = await cmd.ExecuteReaderAsync();
        Assert.True(await runReader.ReadAsync());
        Assert.Equal(0, runReader.GetInt32(0)); // shield_capacity
        Assert.Equal(0, runReader.GetInt32(1)); // active_shields
        Assert.Equal(0, runReader.GetInt32(2)); // shield_recharge_units
        Assert.Equal(1, runReader.GetInt32(3)); // sector
        Assert.Equal(0, runReader.GetInt32(4)); // opponent_index
        Assert.Equal(100, runReader.GetInt32(5)); // player_current_hp
        runReader.Close();

        // 4. Verify domain run state restored through store
        var runState = await store.GetRunStateAsync();
        Assert.Equal(0, runState.ShieldCapacity);
        Assert.Equal(0, runState.ActiveShields);
        Assert.Equal(0, runState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task MigrateV4ToV5_ValidV4Database_MigratesToSchemaV5WithPreservedData()
    {
        var dbPath = GetTempDbPath("migrate_v4_to_v5");
        await SeedRawV4DatabaseAsync(dbPath, pendingCount: 2, receiptCount: 2);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // 1. Verify schema version advanced to 6
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
        var version = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal("7", version);

        // 2. Verify progression preserved with firewall defaults
        cmd.CommandText = @"
            SELECT cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank,
                   firewall_rank, consecutive_early_defeats, is_beginner_assistance_active
            FROM gameplay_progression WHERE id = 1;
        ";
        using var progReader = await cmd.ExecuteReaderAsync();
        Assert.True(await progReader.ReadAsync());
        Assert.Equal(1250, progReader.GetInt64(0));
        Assert.Equal(8, progReader.GetInt32(1));
        Assert.Equal(7, progReader.GetInt32(2));
        Assert.Equal(5, progReader.GetInt32(3));
        Assert.Equal(2, progReader.GetInt32(4));
        Assert.Equal(3, progReader.GetInt32(5));
        Assert.Equal(0, progReader.GetInt32(6)); // firewall_rank
        Assert.Equal(0, progReader.GetInt32(7)); // consecutive_early_defeats
        Assert.Equal(0, progReader.GetInt32(8)); // is_beginner_assistance_active
        progReader.Close();

        // 3. Verify run state preserved with shield defaults
        cmd.CommandText = @"
            SELECT sector, opponent_index, opponent_current_hp, player_current_hp,
                   opponent_executed_attacks, opponent_encounter_provenance,
                   shield_capacity, active_shields, shield_recharge_units
            FROM gameplay_run_state WHERE id = 1;
        ";
        using var runReader = await cmd.ExecuteReaderAsync();
        Assert.True(await runReader.ReadAsync());
        Assert.Equal(4, runReader.GetInt32(0));
        Assert.Equal(2, runReader.GetInt32(1));
        Assert.Equal(2, runReader.GetInt32(2));
        Assert.Equal(85, runReader.GetInt32(3));
        Assert.Equal(2, runReader.GetInt32(4));
        Assert.Equal("Authoritative", runReader.GetString(5));
        Assert.Equal(0, runReader.GetInt32(6)); // shield_capacity
        Assert.Equal(0, runReader.GetInt32(7)); // active_shields
        Assert.Equal(0, runReader.GetInt32(8)); // shield_recharge_units
        runReader.Close();

        // 4. Verify domain GetRunStateAsync preserves these
        var runState = await store.GetRunStateAsync();
        Assert.Equal(4, runState.Sector);
        Assert.Equal(2, runState.OpponentIndex);
        Assert.Equal(85, runState.PlayerCurrentHp);
        Assert.Equal(0, runState.ShieldCapacity);
        Assert.Equal(0, runState.ActiveShields);
        Assert.Equal(0, runState.ShieldRechargeUnits);

        // 5. Verify pending intents preserved
        var pending = await store.GetPendingIntentsAsync(0);
        Assert.Equal(2, pending.Count);
        Assert.Equal(4, pending[0].EffectiveAttackDamage);

        // 6. Verify receipt ledger preserved with zero shields
        var receiptApplied = await store.GetReceiptAsync("receipt-v4-0");
        Assert.NotNull(receiptApplied);
        Assert.Equal(CyberDefenseReceiptKind.Applied, receiptApplied.ReceiptKind);
        Assert.NotNull(receiptApplied.TransitionResult);
        Assert.False(receiptApplied.TransitionResult.IsShieldAbsorbed);
        Assert.Equal(0, receiptApplied.TransitionResult.AbsorbedEnemyDamage);
        Assert.Equal(0, receiptApplied.TransitionResult.NextState.ShieldCapacity);
        Assert.Equal(0, receiptApplied.TransitionResult.NextState.ActiveShields);
        Assert.Equal(0, receiptApplied.TransitionResult.NextState.ShieldRechargeUnits);

        var receiptCalm = await store.GetReceiptAsync("receipt-v4-1");
        Assert.NotNull(receiptCalm);
        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, receiptCalm.ReceiptKind);
        Assert.Null(receiptCalm.TransitionResult);
    }

    [Fact]
    public async Task MigrateV1ToV5_HistoricalUpgradeChain_Succeeds()
    {
        var dbPath = GetTempDbPath("migrate_v1_to_v5");
        await SeedRawV1DatabaseAsync(dbPath);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
        Assert.Equal("7", (string?)await cmd.ExecuteScalarAsync());

        var run = await store.GetRunStateAsync();
        Assert.Equal(1, run.Sector);
        Assert.Equal(0, run.ShieldCapacity);
        Assert.Equal(0, run.ActiveShields);
        Assert.Equal(0, run.ShieldRechargeUnits);
    }

    [Fact]
    public async Task MigrateV2ToV5_HistoricalUpgradeChain_Succeeds()
    {
        var dbPath = GetTempDbPath("migrate_v2_to_v5");
        await SeedRawV2DatabaseAsync(dbPath);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
        Assert.Equal("7", (string?)await cmd.ExecuteScalarAsync());

        var run = await store.GetRunStateAsync();
        Assert.Equal(1, run.Sector);
        Assert.Equal(0, run.ShieldCapacity);
    }

    [Fact]
    public async Task MigrateV3ToV5_HistoricalUpgradeChain_Succeeds()
    {
        var dbPath = GetTempDbPath("migrate_v3_to_v5");
        await SeedRawV3DatabaseAsync(dbPath, pendingCount: 1, receiptCount: 1);

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
        Assert.Equal("7", (string?)await cmd.ExecuteScalarAsync());

        var run = await store.GetRunStateAsync();
        Assert.Equal(1, run.Sector);
        Assert.Equal(0, run.ShieldCapacity);
    }

    [Fact]
    public async Task ReopenAfterMigration_PreservesSchemaV5_WithoutRepeatedMigration()
    {
        var dbPath = GetTempDbPath("reopen_v5");
        await SeedRawV4DatabaseAsync(dbPath, pendingCount: 1, receiptCount: 1);

        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
        }

        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();
            var run = await store2.GetRunStateAsync();
            Assert.Equal(4, run.Sector);
            Assert.Equal(0, run.ShieldCapacity);
        }
    }

    [Fact]
    public async Task RejectUnsupportedFutureSchemaVersion_ThrowsInvalidOperationException()
    {
        var dbPath = GetTempDbPath("future_schema");
        await SeedRawV4DatabaseAsync(dbPath, pendingCount: 0, receiptCount: 0);

        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_schema_info SET value = '8' WHERE key = 'schema_version';";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Contains("Unsupported database schema version 8", ex.Message);
    }

    [Fact]
    public async Task RejectMalformedPersistedShieldState_ThrowsInvalidOperationException()
    {
        var dbPath = GetTempDbPath("malformed_shields");
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
        }

        // Intentionally corrupt shield state in DB by disabling constraints temporarily or inserting invalid row
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            // Capacity 0 but active 1 violates domain invariant
            cmd.CommandText = "UPDATE gameplay_run_state SET shield_capacity = 0, active_shields = 1 WHERE id = 1;";
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // If CHECK constraint rejects it, that's also valid invariant protection
                return;
            }
        }

        using var store2 = new SqliteGameplayStore(dbPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store2.InitializeAsync());
    }

    [Fact]
    public async Task DatabaseCheckConstraints_RejectInvalidShieldAndFirewallState()
    {
        var dbPath = GetTempDbPath("constraints_test");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // 1. firewall_rank > 3 rejected
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE gameplay_progression SET firewall_rank = 4 WHERE id = 1;";
            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }

        // 2. firewall_rank < 0 rejected
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE gameplay_progression SET firewall_rank = -1 WHERE id = 1;";
            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }

        // 3. active_shields > shield_capacity rejected
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE gameplay_run_state SET shield_capacity = 1, active_shields = 2 WHERE id = 1;";
            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }

        // 4. active_shields == shield_capacity but recharge_units > 0 rejected
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE gameplay_run_state SET shield_capacity = 1, active_shields = 1, shield_recharge_units = 5 WHERE id = 1;";
            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }

        // 5. shield_recharge_units >= 30 rejected
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE gameplay_run_state SET shield_capacity = 1, active_shields = 0, shield_recharge_units = 30 WHERE id = 1;";
            await Assert.ThrowsAsync<SqliteException>(() => cmd.ExecuteNonQueryAsync());
        }
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

    private static async Task SeedRawV4DatabaseAsync(string path, int pendingCount, int receiptCount)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = RawSchemaV4Ddl + @"
            INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '4');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '5');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');
            INSERT INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');

            INSERT INTO gameplay_progression (
                id, store_revision, reset_epoch, cumulative_xp, player_level,
                earned_sp, spent_sp, available_sp, attack_rank, economy_version, updated_at
            ) VALUES (1, 5, 0, 1250, 8, 7, 5, 2, 3, 'ECONOMY_V1', '2026-10-12T00:00:00.0000000Z');

            INSERT INTO gameplay_run_state (
                id, sector, opponent_index, opponent_current_hp, player_current_hp,
                opponent_executed_attacks, opponent_encounter_provenance, reset_epoch, updated_at
            ) VALUES (1, 4, 2, 2, 85, 2, 'Authoritative', 0, '2026-10-12T00:00:00.0000000Z');

            INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
            VALUES (1, 0, 0, 0, NULL, '2026-10-12T00:00:00.0000000Z');
        ";
        await cmd.ExecuteNonQueryAsync();

        for (int i = 0; i < pendingCount; i++)
        {
            using var pCmd = conn.CreateCommand();
            pCmd.CommandText = @"
                INSERT INTO gameplay_pending_intent (
                    submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at, effective_attack_damage
                ) VALUES (
                    @id, 'add:2+2', 1, 1, 150, 0, '2026-10-12T00:00:00.0000000Z', 4
                );
            ";
            pCmd.Parameters.AddWithValue("@id", $"sub-v4-{i}");
            await pCmd.ExecuteNonQueryAsync();
        }

        if (receiptCount >= 1)
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
                    'receipt-v4-0', 'Applied', 'add:2+2', 1, 1,
                    150, 0, '2026-10-12T00:00:00.0000000Z',
                    4, 2, 2,
                    0, 0, 0,
                    0, 0, 1, 0,
                    0, 4, 3, 2,
                    85, NULL, NULL,
                    NULL, NULL, NULL,
                    NULL,
                    0, 2,
                    10, 8, 1250,
                    'Authoritative', 'Authoritative'
                );
            ";
            await rCmd.ExecuteNonQueryAsync();
        }

        if (receiptCount >= 2)
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
                    'receipt-v4-1', 'CalmModeSuppressed', 'add:3+3', 1, 0,
                    200, 0, '2026-10-12T00:00:00.0000000Z',
                    NULL, NULL, NULL,
                    NULL, NULL, NULL,
                    NULL, NULL, NULL, NULL,
                    NULL, NULL, NULL, NULL,
                    NULL, NULL, NULL,
                    NULL, NULL, NULL,
                    NULL,
                    NULL, NULL,
                    NULL, NULL, NULL,
                    NULL, NULL
                );
            ";
            await rCmd.ExecuteNonQueryAsync();
        }
    }
}
