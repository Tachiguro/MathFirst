[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MathFirst.Core.Tests")]

namespace MathFirst.Infrastructure.Sqlite.Gameplay;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using Microsoft.Data.Sqlite;

public sealed class SqliteGameplayStore : IGameplayStore
{
    public const string DefaultDatabaseFileName = "mathfirst_gameplay.db";
    public const int DefaultSchemaVersion = 7;
    public const string DefaultEconomyVersion = "ECONOMY_V1";

    private readonly string _connectionString;
    private readonly string _storagePath;
    private SqliteConnection? _connection;
    private bool _isInitialized;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private bool _disposed;
    private int _activeOperations;
    private bool _resourcesCleanedUp;
    public event Action<CyberDefenseAttackUpgradeSnapshot>? AttackUpgradeSnapshotChanged;
    public event Action<CyberDefenseFirewallUpgradeSnapshot>? FirewallUpgradeSnapshotChanged;
    public event Action<CyberDefenseCombatModifierSnapshot>? CombatModifierSnapshotChanged;
    private readonly object _notificationLock = new();
    private long _lastPublishedEpoch = -1;
    private long _lastPublishedRevision = -1;
    private long _lastPublishedFirewallEpoch = -1;
    private long _lastPublishedFirewallRevision = -1;
    private long _lastPublishedModifierEpoch = -1;
    private long _lastPublishedModifierRevision = -1;

    private const string SchemaV6Ddl = @"
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

    // Gameplay Schema V7 (MF-CYBER-007): additive Critical Strike and Overdrive persistence foundation.
    // The same column definitions are appended to fresh databases (after SchemaV6Ddl) and to migrated V6
    // databases, so both paths produce identical tables. Order is significant and must never change.
    //
    // - gameplay_progression: permanent purchased ranks (0 = not purchased).
    // - gameplay_run_state: active-run modifier state (Critical phase counter, Overdrive streak, armed window).
    // - gameplay_pending_intent: skill ranks bound at the question boundary. effective_attack_damage keeps its
    //   existing meaning of BASE Attack damage and is never replaced by composed bonus damage.
    // - gameplay_receipt_ledger: nullable immutable damage and rank-binding evidence, written all-or-nothing.
    //   Legacy receipts keep NULL because their historical modifier context is unknown.
    //
    // Cross-table invariants are intentionally not expressed in SQL because they span the progression and
    // run-state rows; the later restore and transaction paths must reject violations fail-closed:
    // - critical_strike_rank = 0 requires critical_correct_attack_count = 0 (DEC-007-01).
    // - overdrive_rank = 0 requires overdrive_streak = 0 and overdrive_remaining_bonus_attacks = 0 (OD-1).
    // - overdrive_streak stays below CyberDefenseOverdrivePolicy.GetTriggerThreshold(overdrive_rank) (DEC-007-02).
    // - A Critical Strike rank change resets critical_correct_attack_count to 0 atomically with the purchase.
    // - Game-over and epoch reset clear all run modifier state; an incorrect answer clears the Overdrive
    //   streak and any armed window even when Firewall absorbs the damage (DEC-007-03).
    // - For a correct V7-evidenced receipt, requested_attack_damage equals
    //   CyberDefenseDamageCompositionPolicy.GetEffectiveDamage(base_attack_damage, critical_bonus_percent, overdrive_bonus_percent).
    private static readonly (string Table, string Column, string Definition)[] SchemaV7AddedColumns =
    [
        ("gameplay_progression", "critical_strike_rank",
            "INTEGER NOT NULL DEFAULT 0 CHECK (critical_strike_rank >= 0 AND critical_strike_rank <= 2)"),
        ("gameplay_progression", "overdrive_rank",
            "INTEGER NOT NULL DEFAULT 0 CHECK (overdrive_rank >= 0 AND overdrive_rank <= 2)"),
        ("gameplay_run_state", "critical_correct_attack_count",
            "INTEGER NOT NULL DEFAULT 0 CHECK (critical_correct_attack_count >= 0 AND critical_correct_attack_count <= 2147483647)"),
        ("gameplay_run_state", "overdrive_streak",
            "INTEGER NOT NULL DEFAULT 0 CHECK (overdrive_streak >= 0 AND overdrive_streak <= 2147483647)"),
        ("gameplay_run_state", "overdrive_remaining_bonus_attacks",
            "INTEGER NOT NULL DEFAULT 0 CHECK (overdrive_remaining_bonus_attacks >= 0 AND overdrive_remaining_bonus_attacks <= 2)"),
        ("gameplay_pending_intent", "bound_critical_strike_rank",
            "INTEGER NOT NULL DEFAULT 0 CHECK (bound_critical_strike_rank >= 0 AND bound_critical_strike_rank <= 2)"),
        ("gameplay_pending_intent", "bound_overdrive_rank",
            "INTEGER NOT NULL DEFAULT 0 CHECK (bound_overdrive_rank >= 0 AND bound_overdrive_rank <= 2)"),
        ("gameplay_pending_intent", "practice_position",
            "INTEGER CHECK (practice_position IS NULL OR practice_position >= 1)"),
        ("gameplay_receipt_ledger", "base_attack_damage",
            "INTEGER CHECK (base_attack_damage IS NULL OR base_attack_damage >= 1)"),
        ("gameplay_receipt_ledger", "bound_critical_strike_rank",
            "INTEGER CHECK (bound_critical_strike_rank IS NULL OR (bound_critical_strike_rank >= 0 AND bound_critical_strike_rank <= 2))"),
        ("gameplay_receipt_ledger", "bound_overdrive_rank",
            "INTEGER CHECK (bound_overdrive_rank IS NULL OR (bound_overdrive_rank >= 0 AND bound_overdrive_rank <= 2))"),
        ("gameplay_receipt_ledger", "critical_bonus_percent",
            "INTEGER CHECK (critical_bonus_percent IS NULL OR (critical_bonus_percent >= 0 AND critical_bonus_percent <= 100))"),
        ("gameplay_receipt_ledger", "overdrive_bonus_percent",
            @"INTEGER CHECK (overdrive_bonus_percent IS NULL OR (overdrive_bonus_percent >= 0 AND overdrive_bonus_percent <= 50))
            CHECK (
                (base_attack_damage IS NULL AND
                 bound_critical_strike_rank IS NULL AND
                 bound_overdrive_rank IS NULL AND
                 critical_bonus_percent IS NULL AND
                 overdrive_bonus_percent IS NULL)
                OR
                (receipt_kind = 'Applied' AND
                 base_attack_damage IS NOT NULL AND
                 bound_critical_strike_rank IS NOT NULL AND
                 bound_overdrive_rank IS NOT NULL AND
                 critical_bonus_percent IS NOT NULL AND
                 overdrive_bonus_percent IS NOT NULL AND
                 (critical_bonus_percent = 0 OR bound_critical_strike_rank >= 1) AND
                 (overdrive_bonus_percent = 0 OR bound_overdrive_rank >= 1) AND
                 (is_correct = 1 OR (critical_bonus_percent = 0 AND overdrive_bonus_percent = 0)))
            )")
    ];

    private const string SchemaV5Ddl = @"
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

    public SqliteGameplayStore(string storagePath)
    {
        _storagePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
        var dir = Path.GetDirectoryName(storagePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = storagePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            ForeignKeys = true
        };
        _connectionString = builder.ToString();
    }

    public string StoragePath => _storagePath;

    internal Task ExecuteUnderGateForTestAsync(Func<SqliteConnection, Task> action, CancellationToken cancellationToken = default)
    {
        return ExecuteWithConnectionGateAsync(async ct =>
        {
            if (_connection == null)
            {
                throw new InvalidOperationException("Connection not initialized.");
            }
            await action(_connection).ConfigureAwait(false);
        }, cancellationToken);
    }

    internal int ActiveOperationsForTest => Volatile.Read(ref _activeOperations);

    internal bool IsDisposedForTest
    {
        get
        {
            lock (_lock)
            {
                return _disposed;
            }
        }
    }

    internal SqliteConnection? ConnectionForTest
    {
        get
        {
            lock (_lock)
            {
                return _connection;
            }
        }
    }

    private void RegisterOperation()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SqliteGameplayStore));
            }
            _activeOperations++;
        }
    }

    private void UnregisterOperation()
    {
        bool shouldCleanup = false;
        lock (_lock)
        {
            if (_activeOperations > 0)
            {
                _activeOperations--;
            }
            if (_disposed && _activeOperations == 0)
            {
                shouldCleanup = true;
            }
        }

        if (shouldCleanup)
        {
            CleanupResources();
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SqliteGameplayStore));
            }
        }
    }

    private async Task<T> ExecuteWithConnectionGateAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken, bool initialize = true)
    {
        RegisterOperation();
        try
        {
            await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (initialize)
                {
                    await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (!_isInitialized || _connection == null)
                {
                    throw new InvalidOperationException("Initialize the Gameplay Store explicitly before reading a combat modifier snapshot.");
                }
                return await action(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _connectionGate.Release();
            }
        }
        finally
        {
            UnregisterOperation();
        }
    }

    private async Task ExecuteWithConnectionGateAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        RegisterOperation();
        try
        {
            await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await EnsureInitializedCoreAsync(cancellationToken).ConfigureAwait(false);
                await action(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _connectionGate.Release();
            }
        }
        finally
        {
            UnregisterOperation();
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithConnectionGateAsync(ct => Task.CompletedTask, cancellationToken);
    }

    private async Task EnsureInitializedCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            if (_isInitialized && _connection != null)
            {
                return;
            }
        }

        await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using (var pragmaCmd = connection.CreateCommand())
            {
                pragmaCmd.CommandText = @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA foreign_keys = ON;
                    PRAGMA busy_timeout = 5000;
                ";
                await pragmaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var probeCmd = connection.CreateCommand())
            {
                probeCmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
                using var reader = await probeCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    existingTables.Add(reader.GetString(0));
                }
            }

            if (existingTables.Count == 0)
            {
                await InitializeFreshDatabaseAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await ValidateExistingDatabaseAsync(connection, existingTables, cancellationToken).ConfigureAwait(false);
            }

            lock (_lock)
            {
                if (_disposed)
                {
                    connection.Dispose();
                    SqliteConnection.ClearPool(connection);
                    throw new ObjectDisposedException(nameof(SqliteGameplayStore));
                }
                _connection = connection;
                _isInitialized = true;
            }
        }
        catch
        {
            connection.Dispose();
            SqliteConnection.ClearPool(connection);
            throw;
        }
    }

    private static async Task InitializeFreshDatabaseAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var ddlCmd = connection.CreateCommand())
            {
                ddlCmd.Transaction = transaction;
                ddlCmd.CommandText = SchemaV6Ddl;
                await ddlCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await AddSchemaV7ColumnsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow.ToString("O");
            var initialRun = CyberDefenseRunState.InitialRun();

            using (var seedCmd = connection.CreateCommand())
            {
                seedCmd.Transaction = transaction;
                seedCmd.CommandText = @"
                    INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '7');
                    INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '1');
                    INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');
                    INSERT INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');

                    INSERT INTO gameplay_progression (
                        id, store_revision, reset_epoch,
                        cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank,
                        economy_version, updated_at,
                        firewall_rank, consecutive_early_defeats, is_beginner_assistance_active,
                        activated_firewall_rank
                    ) VALUES (1, 1, 0, 0, 1, 0, 0, 0, 0, 'ECONOMY_V1', @now, 0, 0, 0, 0);

                    INSERT INTO gameplay_run_state (
                        id, sector, opponent_index, opponent_current_hp, player_current_hp,
                        opponent_executed_attacks, opponent_encounter_provenance,
                        shield_capacity, active_shields, shield_recharge_units,
                        reset_epoch, updated_at
                    ) VALUES (1, @sector, @opponentIndex, @opponentHp, @playerHp, 0, 'Authoritative', 0, 0, 0, 0, @now);

                    INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                    VALUES (1, 0, 0, 0, NULL, @now);
                ";
                seedCmd.Parameters.AddWithValue("@now", now);
                seedCmd.Parameters.AddWithValue("@sector", initialRun.Sector);
                seedCmd.Parameters.AddWithValue("@opponentIndex", initialRun.OpponentIndex);
                seedCmd.Parameters.AddWithValue("@opponentHp", initialRun.CurrentOpponent.CurrentHp);
                seedCmd.Parameters.AddWithValue("@playerHp", initialRun.PlayerCurrentHp);
                await seedCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task ValidateExistingDatabaseAsync(SqliteConnection connection, HashSet<string> existingTables, CancellationToken cancellationToken)
    {
        if (!existingTables.Contains("gameplay_schema_info"))
        {
            throw new InvalidOperationException("Existing database is incomplete: missing table 'gameplay_schema_info'.");
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var metaCmd = connection.CreateCommand())
        {
            metaCmd.CommandText = "SELECT key, value FROM gameplay_schema_info;";
            using var reader = await metaCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                metadata[reader.GetString(0)] = reader.GetString(1);
            }
        }

        if (!metadata.TryGetValue("schema_version", out var versionStr)
            || !int.TryParse(versionStr, CultureInfo.InvariantCulture, out var version))
        {
            throw new InvalidOperationException(
                "Existing database has malformed or missing 'schema_version' in 'gameplay_schema_info'.");
        }

        if (version > DefaultSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported database schema version {version}. Maximum supported version is {DefaultSchemaVersion}.");
        }

        if (version < 1)
        {
            throw new InvalidOperationException(
                $"Invalid database schema version {version}. Supported versions are 1 to {DefaultSchemaVersion}.");
        }

        var requiredTables = new[]
        {
            "gameplay_schema_info",
            "gameplay_progression",
            "gameplay_run_state",
            "gameplay_receipt_ledger",
            "gameplay_pending_intent",
            "gameplay_reset_intent"
        };

        foreach (var table in requiredTables)
        {
            if (!existingTables.Contains(table))
            {
                throw new InvalidOperationException(
                    $"Existing database is incomplete: missing table '{table}'.");
            }
        }

        if (version == 1)
        {
            await MigrateV1ToV3Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV3ToV4Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV4ToV5Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV5ToV6Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV6ToV7Async(connection, cancellationToken).ConfigureAwait(false);
        }
        else if (version == 2)
        {
            await MigrateV2ToV3Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV3ToV4Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV4ToV5Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV5ToV6Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV6ToV7Async(connection, cancellationToken).ConfigureAwait(false);
        }
        else if (version == 3)
        {
            await MigrateV3ToV4Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV4ToV5Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV5ToV6Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV6ToV7Async(connection, cancellationToken).ConfigureAwait(false);
        }
        else if (version == 4)
        {
            await MigrateV4ToV5Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV5ToV6Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV6ToV7Async(connection, cancellationToken).ConfigureAwait(false);
        }
        else if (version == 5)
        {
            await MigrateV5ToV6Async(connection, cancellationToken).ConfigureAwait(false);
            await MigrateV6ToV7Async(connection, cancellationToken).ConfigureAwait(false);
        }
        else if (version == 6)
        {
            await MigrateV6ToV7Async(connection, cancellationToken).ConfigureAwait(false);
        }
        else if (version == 7)
        {
            await EnsureSchemaV7PendingIntentPracticePositionColumnAsync(connection, cancellationToken).ConfigureAwait(false);
        }

        using (var colCmd = connection.CreateCommand())
        {
            colCmd.CommandText = "SELECT effective_attack_damage FROM gameplay_pending_intent LIMIT 0;";
            await colCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        using (var runCmd = connection.CreateCommand())
        {
            runCmd.CommandText = @"
                SELECT sector, opponent_index, opponent_current_hp, player_current_hp,
                       opponent_executed_attacks, opponent_encounter_provenance,
                       shield_capacity, active_shields, shield_recharge_units
                FROM gameplay_run_state WHERE id = 1;
            ";
            using var reader = await runCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Existing database is missing active run state row in 'gameplay_run_state'.");
            }

            int sector = reader.GetInt32(0);
            int opponentIndex = reader.GetInt32(1);
            int opponentHp = reader.GetInt32(2);
            int playerHp = reader.GetInt32(3);
            int opponentExecutedAttacks = reader.GetInt32(4);
            string provStr = reader.GetString(5);
            int shieldCapacity = reader.GetInt32(6);
            int activeShields = reader.GetInt32(7);
            int shieldRechargeUnits = reader.GetInt32(8);

            if (!Enum.TryParse<OpponentEncounterProvenance>(provStr, ignoreCase: false, out var prov))
            {
                throw new InvalidOperationException($"Invalid opponent_encounter_provenance '{provStr}' in run state table.");
            }

            try
            {
                CyberDefenseRunState.Rehydrate(sector, opponentIndex, opponentHp, playerHp, opponentExecutedAttacks, prov, shieldCapacity, activeShields, shieldRechargeUnits);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException($"Invalid domain run state in database: {ex.Message}", ex);
            }
        }

        using (var progCmd = connection.CreateCommand())
        {
            progCmd.CommandText = @"
                SELECT store_revision, reset_epoch, cumulative_xp, player_level,
                       earned_sp, spent_sp, available_sp, attack_rank, economy_version,
                       firewall_rank, consecutive_early_defeats, is_beginner_assistance_active,
                       activated_firewall_rank
                FROM gameplay_progression WHERE id = 1;
            ";
            using var reader = await progCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Existing database is missing row in 'gameplay_progression'.");
            }

            long storeRevision = reader.GetInt64(0);
            long resetEpoch = reader.GetInt64(1);
            long cumulativeXp = reader.GetInt64(2);
            int playerLevel = reader.GetInt32(3);
            int earnedSp = reader.GetInt32(4);
            int spentSp = reader.GetInt32(5);
            int availableSp = reader.GetInt32(6);
            int attackRank = reader.GetInt32(7);
            string economyVersion = reader.GetString(8);
            int firewallRank = reader.GetInt32(9);
            int consecutiveEarlyDefeats = reader.GetInt32(10);
            int isBeginnerAssistanceActive = reader.GetInt32(11);
            int activatedFirewallRank = reader.GetInt32(12);

            if (storeRevision < 1 || resetEpoch < 0 || cumulativeXp < 0 || playerLevel < 1 ||
                earnedSp < 0 || spentSp < 0 || availableSp < 0 || attackRank < 0 ||
                string.IsNullOrEmpty(economyVersion) || earnedSp != checked(spentSp + availableSp) ||
                firewallRank < 0 || firewallRank > CyberDefenseFirewallPolicy.MaxRank ||
                consecutiveEarlyDefeats < 0 || (isBeginnerAssistanceActive != 0 && isBeginnerAssistanceActive != 1) ||
                activatedFirewallRank < 0 || activatedFirewallRank > firewallRank)
            {
                throw new InvalidOperationException("Existing database has invalid progression row.");
            }
        }

        using (var resetCmd = connection.CreateCommand())
        {
            resetCmd.CommandText = "SELECT is_pending, current_epoch, target_epoch FROM gameplay_reset_intent WHERE id = 1;";
            using var reader = await resetCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Existing database is missing row in 'gameplay_reset_intent'.");
            }
        }

        await ValidateSchemaV7ModifierStateAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    // Primitive-range validation of the V7 modifier columns on open. SQL CHECK constraints normally make these
    // unreachable; this fails closed if they were bypassed. Cross-table rank/modifier consistency is enforced by
    // the later restore path (see SchemaV7AddedColumns).
    private static async Task ValidateSchemaV7ModifierStateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using (var probeCmd = connection.CreateCommand())
        {
            probeCmd.CommandText = @"
                SELECT submission_id, receipt_kind, fact_id, is_correct, is_eligible,
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
                       next_opponent_encounter_provenance, defeated_opponent_encounter_provenance,
                       is_shield_absorbed, absorbed_enemy_damage,
                       next_shield_capacity, next_active_shields, next_shield_recharge_units,
                       base_attack_damage, bound_critical_strike_rank, bound_overdrive_rank,
                       critical_bonus_percent, overdrive_bonus_percent,
                       typeof(base_attack_damage) AS base_attack_damage_type,
                       typeof(bound_critical_strike_rank) AS bound_critical_strike_rank_type,
                       typeof(bound_overdrive_rank) AS bound_overdrive_rank_type,
                       typeof(critical_bonus_percent) AS critical_bonus_percent_type,
                       typeof(overdrive_bonus_percent) AS overdrive_bonus_percent_type
                FROM gameplay_receipt_ledger;";
            using var reader = await probeCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                _ = ReadReceiptRecord(reader);
            }
        }

        using (var progCmd = connection.CreateCommand())
        {
            progCmd.CommandText = "SELECT critical_strike_rank, overdrive_rank FROM gameplay_progression WHERE id = 1;";
            using var reader = await progCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Existing database is missing row in 'gameplay_progression'.");
            }

            ValidateSchemaV7Value("gameplay_progression", "critical_strike_rank", reader.GetInt64(0), 0, CyberDefenseCriticalStrikePolicy.MaxRank);
            ValidateSchemaV7Value("gameplay_progression", "overdrive_rank", reader.GetInt64(1), 0, CyberDefenseOverdrivePolicy.MaxRank);
        }

        using (var runCmd = connection.CreateCommand())
        {
            runCmd.CommandText = @"
                SELECT critical_correct_attack_count, overdrive_streak, overdrive_remaining_bonus_attacks
                FROM gameplay_run_state WHERE id = 1;";
            using var reader = await runCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Existing database is missing active run state row in 'gameplay_run_state'.");
            }

            ValidateSchemaV7Value("gameplay_run_state", "critical_correct_attack_count", reader.GetInt64(0), 0, int.MaxValue);
            ValidateSchemaV7Value("gameplay_run_state", "overdrive_streak", reader.GetInt64(1), 0, int.MaxValue);
            ValidateSchemaV7Value("gameplay_run_state", "overdrive_remaining_bonus_attacks", reader.GetInt64(2), 0, CyberDefenseOverdrivePolicy.Rank1BonusAttackCount);
        }

        using (var pendingCmd = connection.CreateCommand())
        {
            pendingCmd.CommandText = @"
                SELECT reset_epoch,
                       bound_critical_strike_rank, typeof(bound_critical_strike_rank) AS bound_critical_strike_rank_type,
                       bound_overdrive_rank, typeof(bound_overdrive_rank) AS bound_overdrive_rank_type,
                       practice_position, typeof(practice_position) AS practice_position_type
                FROM gameplay_pending_intent;";
            var seenPositionsByEpoch = new Dictionary<long, HashSet<long>>();
            using var reader = await pendingCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                long epoch = reader.GetInt64(0);
                _ = ReadPendingIntentRank(reader, 1, 2, "bound_critical_strike_rank", CyberDefenseCriticalStrikePolicy.MaxRank);
                _ = ReadPendingIntentRank(reader, 3, 4, "bound_overdrive_rank", CyberDefenseOverdrivePolicy.MaxRank);
                long? practicePosition = ReadPendingIntentPracticePosition(reader, 5, 6);
                if (practicePosition.HasValue)
                {
                    if (!seenPositionsByEpoch.TryGetValue(epoch, out var positions))
                    {
                        positions = new HashSet<long>();
                        seenPositionsByEpoch[epoch] = positions;
                    }

                    if (!positions.Add(practicePosition.Value))
                    {
                        throw new InvalidOperationException(
                            $"Existing database has conflicting duplicate practice_position {practicePosition.Value} in 'gameplay_pending_intent' for epoch {epoch}.");
                    }
                }
            }
        }
    }

    private static async Task EnsureSchemaV7PendingIntentPracticePositionColumnAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var probeCmd = connection.CreateCommand();
        probeCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('gameplay_pending_intent') WHERE name = 'practice_position';";
        long existing = Convert.ToInt64(await probeCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (existing == 0)
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE gameplay_pending_intent ADD COLUMN practice_position INTEGER CHECK (practice_position IS NULL OR practice_position >= 1);";
            await alterCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateSchemaV7Value(string table, string column, long value, long min, long max)
    {
        if (value < min || value > max)
        {
            throw new InvalidOperationException(
                $"Existing database has invalid value {value} in '{table}.{column}': expected {min} to {max}.");
        }
    }

    private static async Task MigrateV1ToV3Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            // 0. Classify active encounter provenance from V1 state
            int sector;
            int opponentIndex;
            int opponentHp;
            using (var checkRunCmd = connection.CreateCommand())
            {
                checkRunCmd.Transaction = transaction;
                checkRunCmd.CommandText = "SELECT sector, opponent_index, opponent_current_hp FROM gameplay_run_state WHERE id = 1;";
                using var r = await checkRunCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await r.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Missing run state row in V1 database.");
                }
                sector = r.GetInt32(0);
                opponentIndex = r.GetInt32(1);
                opponentHp = r.GetInt32(2);
            }

            int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, opponentIndex);
            bool isUntouched = (opponentHp == maxHp);
            string provenance = isUntouched ? "Authoritative" : "UnverifiedLegacy";

            // 1. Rebuild gameplay_progression table
            using (var progCmd = connection.CreateCommand())
            {
                progCmd.Transaction = transaction;
                progCmd.CommandText = @"
                    CREATE TABLE gameplay_progression_v3 (
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

                    INSERT INTO gameplay_progression_v3 (
                        id, store_revision, reset_epoch,
                        cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank,
                        economy_version, updated_at
                    )
                    SELECT
                        id, store_revision, reset_epoch,
                        0, 1, 0, 0, 0, 0,
                        'ECONOMY_V1', updated_at
                    FROM gameplay_progression;

                    DROP TABLE gameplay_progression;
                    ALTER TABLE gameplay_progression_v3 RENAME TO gameplay_progression;
                ";
                await progCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 2. Rebuild gameplay_run_state table
            using (var runCmd = connection.CreateCommand())
            {
                runCmd.Transaction = transaction;
                runCmd.CommandText = @"
                    CREATE TABLE gameplay_run_state_v3 (
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

                    INSERT INTO gameplay_run_state_v3 (
                        id, sector, opponent_index, opponent_current_hp, player_current_hp,
                        opponent_executed_attacks, opponent_encounter_provenance, reset_epoch, updated_at
                    )
                    SELECT
                        id, sector, opponent_index, opponent_current_hp, player_current_hp,
                        0, @provenance, reset_epoch, updated_at
                    FROM gameplay_run_state;

                    DROP TABLE gameplay_run_state;
                    ALTER TABLE gameplay_run_state_v3 RENAME TO gameplay_run_state;
                ";
                runCmd.Parameters.AddWithValue("@provenance", provenance);
                await runCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 3. Rebuild gameplay_receipt_ledger table
            using (var receiptCmd = connection.CreateCommand())
            {
                receiptCmd.Transaction = transaction;
                receiptCmd.CommandText = @"
                    CREATE TABLE gameplay_receipt_ledger_v3 (
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

                    INSERT INTO gameplay_receipt_ledger_v3 (
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
                    )
                    SELECT
                        submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                        response_latency_ms, reset_epoch, processed_at,
                        requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                        incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                        potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
                        is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
                        next_player_current_hp, terminal_sector, terminal_opponent_index,
                        terminal_opponent_kind, terminal_opponent_current_hp, terminal_opponent_max_hp,
                        terminal_player_current_hp,
                        NULL, NULL,
                        NULL, NULL, NULL,
                        NULL, NULL
                    FROM gameplay_receipt_ledger;

                    DROP TABLE gameplay_receipt_ledger;
                    ALTER TABLE gameplay_receipt_ledger_v3 RENAME TO gameplay_receipt_ledger;

                    CREATE INDEX IF NOT EXISTS ix_gameplay_receipt_ledger_epoch_processed
                        ON gameplay_receipt_ledger(reset_epoch, processed_at);
                ";
                await receiptCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 4. Update schema info to version 3
            using (var metaCmd = connection.CreateCommand())
            {
                metaCmd.Transaction = transaction;
                metaCmd.CommandText = @"
                    UPDATE gameplay_schema_info SET value = '3' WHERE key = 'schema_version';
                    INSERT OR REPLACE INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');
                ";
                await metaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task MigrateV2ToV3Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            // 0. Classify active encounter provenance from V2 state
            int sector;
            int opponentIndex;
            int opponentHp;
            int executedAttacks;
            using (var checkRunCmd = connection.CreateCommand())
            {
                checkRunCmd.Transaction = transaction;
                checkRunCmd.CommandText = "SELECT sector, opponent_index, opponent_current_hp, opponent_executed_attacks FROM gameplay_run_state WHERE id = 1;";
                using var r = await checkRunCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await r.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Missing run state row in V2 database.");
                }
                sector = r.GetInt32(0);
                opponentIndex = r.GetInt32(1);
                opponentHp = r.GetInt32(2);
                executedAttacks = r.GetInt32(3);
            }

            int maxHp = CyberDefenseScalingPolicy.GetOpponentMaxHp(sector, opponentIndex);
            // Conservative classification: Authoritative ONLY when verifiably fresh untouched
            bool isUntouched = (opponentHp == maxHp && executedAttacks == 0);
            string provenance = isUntouched ? "Authoritative" : "UnverifiedLegacy";

            // 1. Rebuild gameplay_run_state table with provenance
            using (var runCmd = connection.CreateCommand())
            {
                runCmd.Transaction = transaction;
                runCmd.CommandText = @"
                    CREATE TABLE gameplay_run_state_v3 (
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

                    INSERT INTO gameplay_run_state_v3 (
                        id, sector, opponent_index, opponent_current_hp, player_current_hp,
                        opponent_executed_attacks, opponent_encounter_provenance, reset_epoch, updated_at
                    )
                    SELECT
                        id, sector, opponent_index, opponent_current_hp, player_current_hp,
                        opponent_executed_attacks, @provenance, reset_epoch, updated_at
                    FROM gameplay_run_state;

                    DROP TABLE gameplay_run_state;
                    ALTER TABLE gameplay_run_state_v3 RENAME TO gameplay_run_state;
                ";
                runCmd.Parameters.AddWithValue("@provenance", provenance);
                await runCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 2. Rebuild gameplay_receipt_ledger table with receipt provenance
            using (var receiptCmd = connection.CreateCommand())
            {
                receiptCmd.Transaction = transaction;
                receiptCmd.CommandText = @"
                    CREATE TABLE gameplay_receipt_ledger_v3 (
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

                    INSERT INTO gameplay_receipt_ledger_v3 (
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
                    )
                    SELECT
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
                        CASE
                            WHEN receipt_kind = 'CalmModeSuppressed' OR next_opponent_executed_attacks IS NULL THEN NULL
                            ELSE 'UnverifiedLegacy'
                        END,
                        CASE
                            WHEN is_opponent_defeated = 1 AND executed_attacks_on_defeated_opponent IS NOT NULL THEN 'UnverifiedLegacy'
                            ELSE NULL
                        END
                    FROM gameplay_receipt_ledger;

                    DROP TABLE gameplay_receipt_ledger;
                    ALTER TABLE gameplay_receipt_ledger_v3 RENAME TO gameplay_receipt_ledger;

                    CREATE INDEX IF NOT EXISTS ix_gameplay_receipt_ledger_epoch_processed
                        ON gameplay_receipt_ledger(reset_epoch, processed_at);
                ";
                await receiptCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 3. Update schema info to version 3
            using (var metaCmd = connection.CreateCommand())
            {
                metaCmd.Transaction = transaction;
                metaCmd.CommandText = "UPDATE gameplay_schema_info SET value = '3' WHERE key = 'schema_version';";
                await metaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task MigrateV3ToV4Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            // 1. Add effective_attack_damage column to gameplay_pending_intent
            using (var alterCmd = connection.CreateCommand())
            {
                alterCmd.Transaction = transaction;
                alterCmd.CommandText = "ALTER TABLE gameplay_pending_intent ADD COLUMN effective_attack_damage INTEGER NOT NULL DEFAULT 1 CHECK (effective_attack_damage >= 1);";
                await alterCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 2. Update schema version to 4
            using (var metaCmd = connection.CreateCommand())
            {
                metaCmd.Transaction = transaction;
                metaCmd.CommandText = "UPDATE gameplay_schema_info SET value = '4' WHERE key = 'schema_version';";
                await metaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task MigrateV4ToV5Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            // 1. Rebuild gameplay_progression table with firewall fields
            int progCountBefore;
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.Transaction = transaction;
                countCmd.CommandText = "SELECT COUNT(*) FROM gameplay_progression;";
                progCountBefore = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            using (var progCmd = connection.CreateCommand())
            {
                progCmd.Transaction = transaction;
                progCmd.CommandText = @"
                    CREATE TABLE gameplay_progression_v5 (
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

                    INSERT INTO gameplay_progression_v5 (
                        id, store_revision, reset_epoch,
                        cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank,
                        economy_version, updated_at,
                        firewall_rank, consecutive_early_defeats, is_beginner_assistance_active
                    )
                    SELECT
                        id, store_revision, reset_epoch,
                        cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank,
                        economy_version, updated_at,
                        0, 0, 0
                    FROM gameplay_progression;

                    DROP TABLE gameplay_progression;
                    ALTER TABLE gameplay_progression_v5 RENAME TO gameplay_progression;
                ";
                await progCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            int progCountAfter;
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.Transaction = transaction;
                countCmd.CommandText = "SELECT COUNT(*) FROM gameplay_progression;";
                progCountAfter = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }
            if (progCountBefore != progCountAfter)
            {
                throw new InvalidOperationException($"Row count mismatch during progression migration: {progCountBefore} -> {progCountAfter}.");
            }

            // 2. Rebuild gameplay_run_state table with shield fields
            int runCountBefore;
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.Transaction = transaction;
                countCmd.CommandText = "SELECT COUNT(*) FROM gameplay_run_state;";
                runCountBefore = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            using (var runCmd = connection.CreateCommand())
            {
                runCmd.Transaction = transaction;
                runCmd.CommandText = @"
                    CREATE TABLE gameplay_run_state_v5 (
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

                    INSERT INTO gameplay_run_state_v5 (
                        id, sector, opponent_index, opponent_current_hp, player_current_hp,
                        opponent_executed_attacks, opponent_encounter_provenance,
                        shield_capacity, active_shields, shield_recharge_units,
                        reset_epoch, updated_at
                    )
                    SELECT
                        id, sector, opponent_index, opponent_current_hp, player_current_hp,
                        opponent_executed_attacks, opponent_encounter_provenance,
                        0, 0, 0,
                        reset_epoch, updated_at
                    FROM gameplay_run_state;

                    DROP TABLE gameplay_run_state;
                    ALTER TABLE gameplay_run_state_v5 RENAME TO gameplay_run_state;
                ";
                await runCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            int runCountAfter;
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.Transaction = transaction;
                countCmd.CommandText = "SELECT COUNT(*) FROM gameplay_run_state;";
                runCountAfter = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }
            if (runCountBefore != runCountAfter)
            {
                throw new InvalidOperationException($"Row count mismatch during run state migration: {runCountBefore} -> {runCountAfter}.");
            }

            // 3. Rebuild gameplay_receipt_ledger table with shield columns
            int receiptCountBefore;
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.Transaction = transaction;
                countCmd.CommandText = "SELECT COUNT(*) FROM gameplay_receipt_ledger;";
                receiptCountBefore = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            using (var receiptCmd = connection.CreateCommand())
            {
                receiptCmd.Transaction = transaction;
                receiptCmd.CommandText = @"
                    CREATE TABLE gameplay_receipt_ledger_v5 (
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

                    INSERT INTO gameplay_receipt_ledger_v5 (
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
                        next_opponent_encounter_provenance, defeated_opponent_encounter_provenance,
                        is_shield_absorbed, absorbed_enemy_damage, next_shield_capacity, next_active_shields, next_shield_recharge_units
                    )
                    SELECT
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
                        next_opponent_encounter_provenance, defeated_opponent_encounter_provenance,
                        CASE WHEN receipt_kind = 'Applied' THEN 0 ELSE NULL END,
                        CASE WHEN receipt_kind = 'Applied' THEN 0 ELSE NULL END,
                        CASE WHEN receipt_kind = 'Applied' THEN 0 ELSE NULL END,
                        CASE WHEN receipt_kind = 'Applied' THEN 0 ELSE NULL END,
                        CASE WHEN receipt_kind = 'Applied' THEN 0 ELSE NULL END
                    FROM gameplay_receipt_ledger;

                    DROP TABLE gameplay_receipt_ledger;
                    ALTER TABLE gameplay_receipt_ledger_v5 RENAME TO gameplay_receipt_ledger;

                    CREATE INDEX IF NOT EXISTS ix_gameplay_receipt_ledger_epoch_processed
                        ON gameplay_receipt_ledger(reset_epoch, processed_at);
                ";
                await receiptCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            int receiptCountAfter;
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.Transaction = transaction;
                countCmd.CommandText = "SELECT COUNT(*) FROM gameplay_receipt_ledger;";
                receiptCountAfter = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }
            if (receiptCountBefore != receiptCountAfter)
            {
                throw new InvalidOperationException($"Row count mismatch during receipt ledger migration: {receiptCountBefore} -> {receiptCountAfter}.");
            }

            // 4. Update schema version to 5
            using (var metaCmd = connection.CreateCommand())
            {
                metaCmd.Transaction = transaction;
                metaCmd.CommandText = "UPDATE gameplay_schema_info SET value = '5' WHERE key = 'schema_version';";
                await metaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task MigrateV5ToV6Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            // 0. Inspect existing V5 progression and run_state to detect ambiguity
            int firewallRank;
            int isBeginnerAssistanceActive;
            using (var progCheckCmd = connection.CreateCommand())
            {
                progCheckCmd.Transaction = transaction;
                progCheckCmd.CommandText = "SELECT firewall_rank, is_beginner_assistance_active FROM gameplay_progression WHERE id = 1;";
                using var r = await progCheckCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await r.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Missing progression row in V5 database.");
                }
                firewallRank = r.GetInt32(0);
                isBeginnerAssistanceActive = r.GetInt32(1);
            }

            int shieldCapacity;
            using (var runCheckCmd = connection.CreateCommand())
            {
                runCheckCmd.Transaction = transaction;
                runCheckCmd.CommandText = "SELECT shield_capacity FROM gameplay_run_state WHERE id = 1;";
                using var r = await runCheckCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await r.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Missing run state row in V5 database.");
                }
                shieldCapacity = r.GetInt32(0);
            }

            // Fail-closed ambiguity classification:
            // 1. If firewallRank == 0: activated_firewall_rank is 0 (unambiguous, no rank ever purchased).
            // 2. If shieldCapacity == 0: activated_firewall_rank is 0 (unambiguous, permanent capacity never activated).
            // 3. If firewallRank > 1 and shieldCapacity == firewallRank: activated_firewall_rank is firewallRank
            //    (unambiguous, capacity >= 2 could not have originated from beginner assistance).
            // 4. All other states (specifically firewallRank >= 1 and shieldCapacity == 1, or shieldCapacity != firewallRank when capacity > 0)
            //    are ambiguous because V5 cannot differentiate between a retired temporary emergency shield and an activated permanent rank.
            int determinedActivatedRank;
            if (firewallRank == 0)
            {
                determinedActivatedRank = 0;
            }
            else if (shieldCapacity == 0)
            {
                determinedActivatedRank = 0;
            }
            else if (firewallRank > 1 && shieldCapacity == firewallRank)
            {
                determinedActivatedRank = firewallRank;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Ambiguous legacy Schema V5 state detected (firewall_rank={firewallRank}, shield_capacity={shieldCapacity}, is_beginner_assistance_active={isBeginnerAssistanceActive}): " +
                    "cannot reliably determine whether shield capacity represents retired beginner assistance or activated permanent firewall rank. " +
                    "Migration aborted to prevent data corruption.");
            }

            // 1. Rebuild gameplay_progression table with activated_firewall_rank
            int progCountBefore;
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.Transaction = transaction;
                countCmd.CommandText = "SELECT COUNT(*) FROM gameplay_progression;";
                progCountBefore = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }

            using (var progCmd = connection.CreateCommand())
            {
                progCmd.Transaction = transaction;
                progCmd.CommandText = @"
                    CREATE TABLE gameplay_progression_v6 (
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

                    INSERT INTO gameplay_progression_v6 (
                        id, store_revision, reset_epoch,
                        cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank,
                        economy_version, updated_at,
                        firewall_rank, consecutive_early_defeats, is_beginner_assistance_active,
                        activated_firewall_rank
                    )
                    SELECT
                        id, store_revision, reset_epoch,
                        cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank,
                        economy_version, updated_at,
                        firewall_rank, consecutive_early_defeats, is_beginner_assistance_active,
                        @activatedRank
                    FROM gameplay_progression;

                    DROP TABLE gameplay_progression;
                    ALTER TABLE gameplay_progression_v6 RENAME TO gameplay_progression;
                ";
                progCmd.Parameters.AddWithValue("@activatedRank", determinedActivatedRank);
                await progCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            int progCountAfter;
            using (var countCmd = connection.CreateCommand())
            {
                countCmd.Transaction = transaction;
                countCmd.CommandText = "SELECT COUNT(*) FROM gameplay_progression;";
                progCountAfter = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }
            if (progCountBefore != progCountAfter)
            {
                throw new InvalidOperationException($"Row count mismatch during progression migration: {progCountBefore} -> {progCountAfter}.");
            }

            // 2. Update schema version to 6
            using (var metaCmd = connection.CreateCommand())
            {
                metaCmd.Transaction = transaction;
                metaCmd.CommandText = "UPDATE gameplay_schema_info SET value = '6' WHERE key = 'schema_version';";
                await metaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task MigrateV6ToV7Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            // 1. Purely additive: no table is rebuilt and no existing value is rewritten. New ranks and run
            //    counters take their zero defaults, pending intents bind to rank 0 and legacy receipts keep
            //    NULL modifier evidence, so no Critical Strike or Overdrive progress is fabricated.
            await AddSchemaV7ColumnsAsync(connection, transaction, cancellationToken).ConfigureAwait(false);

            // 2. Update schema version to 7
            using (var metaCmd = connection.CreateCommand())
            {
                metaCmd.Transaction = transaction;
                metaCmd.CommandText = "UPDATE gameplay_schema_info SET value = '7' WHERE key = 'schema_version';";
                await metaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task AddSchemaV7ColumnsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var (table, column, definition) in SchemaV7AddedColumns)
        {
            using (var probeCmd = connection.CreateCommand())
            {
                probeCmd.Transaction = transaction;
                probeCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info(@table) WHERE name = @column;";
                probeCmd.Parameters.AddWithValue("@table", table);
                probeCmd.Parameters.AddWithValue("@column", column);
                long existing = Convert.ToInt64(await probeCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
                if (existing != 0)
                {
                    throw new InvalidOperationException(
                        $"Malformed gameplay database: Schema V7 column '{table}.{column}' already exists before migration. " +
                        "Migration aborted to prevent data corruption.");
                }
            }

            using (var alterCmd = connection.CreateCommand())
            {
                alterCmd.Transaction = transaction;
                alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
                await alterCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task<CyberDefenseRunState> GetRunStateAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithConnectionGateAsync(async ct =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT sector, opponent_index, opponent_current_hp, player_current_hp,
                       opponent_executed_attacks, opponent_encounter_provenance,
                       shield_capacity, active_shields, shield_recharge_units
                FROM gameplay_run_state WHERE id = 1;
            ";
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                int sector = reader.GetInt32(0);
                int opponentIndex = reader.GetInt32(1);
                int opponentHp = reader.GetInt32(2);
                int playerHp = reader.GetInt32(3);
                int opponentExecutedAttacks = reader.GetInt32(4);
                string provenanceStr = reader.GetString(5);
                int shieldCapacity = reader.GetInt32(6);
                int activeShields = reader.GetInt32(7);
                int shieldRechargeUnits = reader.GetInt32(8);

                if (!Enum.TryParse<OpponentEncounterProvenance>(provenanceStr, ignoreCase: false, out var provenance))
                {
                    throw new InvalidOperationException($"Invalid opponent_encounter_provenance '{provenanceStr}' in gameplay_run_state.");
                }

                return CyberDefenseRunState.Rehydrate(
                    sector, opponentIndex, opponentHp, playerHp, opponentExecutedAttacks, provenance,
                    shieldCapacity, activeShields, shieldRechargeUnits);
            }

            throw new InvalidOperationException("No active run state row found in 'gameplay_run_state'.");
        }, cancellationToken);
    }

    public Task<long> GetResetEpochAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithConnectionGateAsync(async ct =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT reset_epoch FROM gameplay_progression WHERE id = 1;";
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (result is not null and not DBNull)
            {
                return Convert.ToInt64(result, CultureInfo.InvariantCulture);
            }

            throw new InvalidOperationException("No progression row found in 'gameplay_progression'.");
        }, cancellationToken);
    }

    public Task<long> GetStoreRevisionAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithConnectionGateAsync(async ct =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT store_revision FROM gameplay_progression WHERE id = 1;";
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (result is not null and not DBNull)
            {
                return Convert.ToInt64(result, CultureInfo.InvariantCulture);
            }

            throw new InvalidOperationException("No progression row found in 'gameplay_progression'.");
        }, cancellationToken);
    }

    public Task<CyberDefenseProgressionRecord> GetProgressionAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithConnectionGateAsync(async ct =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT store_revision, reset_epoch, cumulative_xp, player_level,
                       earned_sp, spent_sp, available_sp, attack_rank,
                       economy_version, updated_at, firewall_rank, activated_firewall_rank
                FROM gameplay_progression
                WHERE id = 1;
            ";
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("No progression row found in 'gameplay_progression'.");
            }

            long storeRevision = reader.GetInt64(0);
            long resetEpoch = reader.GetInt64(1);
            long cumulativeXp = reader.GetInt64(2);
            int playerLevel = reader.GetInt32(3);
            int earnedSp = reader.GetInt32(4);
            int spentSp = reader.GetInt32(5);
            int availableSp = reader.GetInt32(6);
            int attackRank = reader.GetInt32(7);
            string economyVersion = reader.GetString(8);
            string updatedAtStr = reader.GetString(9);
            int firewallRank = reader.GetInt32(10);
            int activatedFirewallRank = reader.GetInt32(11);

            if (!DateTimeOffset.TryParse(updatedAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt))
            {
                throw new InvalidOperationException($"Invalid updated_at timestamp '{updatedAtStr}' in progression table.");
            }

            return new CyberDefenseProgressionRecord(
                storeRevision,
                resetEpoch,
                cumulativeXp,
                playerLevel,
                earnedSp,
                spentSp,
                availableSp,
                attackRank,
                economyVersion,
                updatedAt,
                firewallRank,
                activatedFirewallRank);
        }, cancellationToken);
    }

    /// <summary>
    /// Restores purchased modifier ranks and run state from one consistent, validated V7 read.
    /// Requires explicit <see cref="InitializeAsync"/> first: reading cannot create/migrate a database.
    /// Throws <see cref="InvalidOperationException"/> for corrupt state or a pending reset; never
    /// repairs data or activates a rank. Pending-intent bindings and receipt evidence are not read.
    /// </summary>
    public Task<CyberDefenseCombatModifierSnapshot> GetCombatModifierSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithConnectionGateAsync(async ct =>
        {
            // Deferred BEGIN acquires no write reservation. The joined read establishes the SQLite
            // snapshot for progression, run state and reset context, including external connections.
            using var transaction = _connection!.BeginTransaction(deferred: true);
            var snapshot = await ReadCombatModifierSnapshotAsync(_connection, transaction, ct).ConfigureAwait(false);
            transaction.Commit();
            return snapshot;
        }, cancellationToken, initialize: false);
    }

    // Caller owns the gate and transaction. Purchases reuse exactly the restore validation without
    // re-entering the gate or taking a separate (potentially stale) snapshot before their writes.
    private static async Task<CyberDefenseCombatModifierSnapshot> ReadCombatModifierSnapshotAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                SELECT p.critical_strike_rank, typeof(p.critical_strike_rank) AS critical_strike_rank_type,
                       p.overdrive_rank, typeof(p.overdrive_rank) AS overdrive_rank_type,
                       p.store_revision, typeof(p.store_revision) AS store_revision_type,
                       p.reset_epoch, typeof(p.reset_epoch) AS reset_epoch_type,
                       rs.critical_correct_attack_count, typeof(rs.critical_correct_attack_count) AS critical_correct_attack_count_type,
                       rs.overdrive_streak, typeof(rs.overdrive_streak) AS overdrive_streak_type,
                       rs.overdrive_remaining_bonus_attacks, typeof(rs.overdrive_remaining_bonus_attacks) AS overdrive_remaining_bonus_attacks_type,
                       rs.reset_epoch AS run_reset_epoch, typeof(rs.reset_epoch) AS run_reset_epoch_type,
                       r.is_pending, typeof(r.is_pending) AS is_pending_type,
                       r.current_epoch, typeof(r.current_epoch) AS current_epoch_type,
                       r.target_epoch, typeof(r.target_epoch) AS target_epoch_type,
                       r.created_at, r.updated_at
                FROM gameplay_progression p
                JOIN gameplay_run_state rs ON rs.id = 1
                JOIN gameplay_reset_intent r ON r.id = 1
                WHERE p.id = 1;";
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Missing authoritative progression, run state or reset intent row for combat modifier snapshot.");
            }

            int criticalRank = (int)ReadModifierSnapshotInteger(reader, "critical_strike_rank", 0, CyberDefenseCriticalStrikePolicy.MaxRank);
            int overdriveRank = (int)ReadModifierSnapshotInteger(reader, "overdrive_rank", 0, CyberDefenseOverdrivePolicy.MaxRank);
            long revision = ReadModifierSnapshotInteger(reader, "store_revision", 1, long.MaxValue);
            long epoch = ReadModifierSnapshotInteger(reader, "reset_epoch", 0, long.MaxValue);
            int count = (int)ReadModifierSnapshotInteger(reader, "critical_correct_attack_count", 0, int.MaxValue);
            int streak = (int)ReadModifierSnapshotInteger(reader, "overdrive_streak", 0, int.MaxValue);
            int remaining = (int)ReadModifierSnapshotInteger(reader, "overdrive_remaining_bonus_attacks", 0, CyberDefenseOverdrivePolicy.Rank1BonusAttackCount);
            long runEpoch = ReadModifierSnapshotInteger(reader, "run_reset_epoch", 0, long.MaxValue);
            bool isPending = ReadModifierSnapshotInteger(reader, "is_pending", 0, 1) == 1;
            long currentEpoch = ReadModifierSnapshotInteger(reader, "current_epoch", 0, long.MaxValue);
            long targetEpoch = ReadModifierSnapshotInteger(reader, "target_epoch", 0, long.MaxValue);
            DateTimeOffset? createdAt = reader.IsDBNull(reader.GetOrdinal("created_at"))
                ? null : ReadModifierSnapshotTimestamp(reader, "created_at");
            DateTimeOffset updatedAt = ReadModifierSnapshotTimestamp(reader, "updated_at");

            if (isPending ? targetEpoch <= currentEpoch || createdAt == null || createdAt > updatedAt
                          : targetEpoch != currentEpoch || createdAt != null)
            {
                throw new InvalidOperationException("Invalid reset intent metadata for combat modifier snapshot.");
            }
            if (isPending)
            {
                // Both pre-reset and already-reset gameplay rows stay fenced until intent clear.
                throw new InvalidOperationException("Combat modifier snapshot is unavailable while a reset is pending.");
            }
            if (epoch != runEpoch || epoch != currentEpoch)
            {
                throw new InvalidOperationException("Inconsistent progression, run state and reset intent epochs for combat modifier snapshot.");
            }
            if (criticalRank == 0 && count != 0)
            {
                throw new InvalidOperationException("Critical Strike rank 0 cannot have a nonzero critical_correct_attack_count.");
            }
            if (overdriveRank == 0 && (streak != 0 || remaining != 0))
            {
                throw new InvalidOperationException("Overdrive rank 0 cannot have an overdrive_streak or armed window.");
            }

            // V7 cannot prove purchase provenance. Rank 1 streak 4 legitimately survives a
            // promotion to rank 2 and arms on the NEXT correct answer (Slice 6 / Issue #90).
            // Allow idle 0..4 at either enabled rank; atomic purchases must preserve provenance.
            if (remaining == 0 ? streak > 4
                               : streak != CyberDefenseOverdrivePolicy.Rank1BonusAttackCount - remaining)
            {
                throw new InvalidOperationException("Invalid Overdrive streak/window combination for combat modifier snapshot.");
            }

            return new CyberDefenseCombatModifierSnapshot(
                criticalRank, overdriveRank, new CyberDefenseCombatModifierState(count, streak, remaining),
                revision, epoch, new GameplayResetIntentRecord(false, currentEpoch, targetEpoch, createdAt, updatedAt));
        }
    }

    /// <summary>
    /// Atomically purchases one Critical Strike rank and restarts its phase. The returned snapshot
    /// belongs to the committed transaction; question-bound activation is the future caller's job.
    /// </summary>
    public Task<CyberDefenseCombatModifierSnapshot> PurchaseCriticalStrikeUpgradeAsync(
        int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default)
        => PurchaseCombatModifierUpgradeAsync(true, expectedRank, expectedResetEpoch, cancellationToken);

    /// <summary>
    /// Atomically purchases one Overdrive rank, preserving legitimate rank-1 progress on promotion.
    /// Does not arm/consume windows, compose damage or rewrite existing question/intent bindings.
    /// </summary>
    public Task<CyberDefenseCombatModifierSnapshot> PurchaseOverdriveUpgradeAsync(
        int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken = default)
        => PurchaseCombatModifierUpgradeAsync(false, expectedRank, expectedResetEpoch, cancellationToken);

    private Task<CyberDefenseCombatModifierSnapshot> PurchaseCombatModifierUpgradeAsync(
        bool criticalStrike, int expectedRank, long expectedResetEpoch, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRank);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(expectedRank,
            criticalStrike ? CyberDefenseCriticalStrikePolicy.MaxRank : CyberDefenseOverdrivePolicy.MaxRank);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedResetEpoch);

        var committedPurchase = ExecuteWithConnectionGateAsync(async ct =>
        {
            // Immediate BEGIN serializes validation and writes against other SQLite connections too.
            using var transaction = _connection!.BeginTransaction();
            try
            {
                var before = await ReadCombatModifierSnapshotAsync(_connection, transaction, ct).ConfigureAwait(false);
                if (expectedResetEpoch != before.ResetEpoch)
                    throw new InvalidOperationException("Purchase reset epoch does not match the authoritative reset epoch.");

                int currentRank = criticalStrike ? before.CriticalStrikeRank : before.OverdriveRank;
                if (expectedRank != currentRank)
                    throw new InvalidOperationException("Expected modifier rank does not match the authoritative purchased rank.");
                if (currentRank >= (criticalStrike ? CyberDefenseCriticalStrikePolicy.MaxRank : CyberDefenseOverdrivePolicy.MaxRank))
                    throw new InvalidOperationException("Combat modifier skill has already reached maximum rank.");

                int level, earned, spent, available;
                using (var command = _connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
                        SELECT player_level, typeof(player_level) AS player_level_type,
                               earned_sp, typeof(earned_sp) AS earned_sp_type,
                               spent_sp, typeof(spent_sp) AS spent_sp_type,
                               available_sp, typeof(available_sp) AS available_sp_type
                        FROM gameplay_progression WHERE id = 1;";
                    using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                        throw new InvalidOperationException("Missing authoritative progression row for modifier purchase.");
                    level = (int)ReadModifierSnapshotInteger(reader, "player_level", 1, int.MaxValue);
                    earned = (int)ReadModifierSnapshotInteger(reader, "earned_sp", 0, int.MaxValue);
                    spent = (int)ReadModifierSnapshotInteger(reader, "spent_sp", 0, int.MaxValue);
                    available = (int)ReadModifierSnapshotInteger(reader, "available_sp", 0, int.MaxValue);
                }
                if ((long)earned != (long)spent + available)
                    throw new InvalidOperationException("Progression skill-point conservation invariant violated.");

                int nextRank = checked(currentRank + 1);
                int requiredLevel = criticalStrike ? CyberDefenseCriticalStrikePolicy.GetRequiredLevel(nextRank)
                    : CyberDefenseOverdrivePolicy.GetRequiredLevel(nextRank);
                int cost = criticalStrike ? CyberDefenseCriticalStrikePolicy.GetUpgradeCost(nextRank)
                    : CyberDefenseOverdrivePolicy.GetUpgradeCost(nextRank);
                if (level < requiredLevel)
                    throw new InvalidOperationException("Player level is insufficient for the next combat modifier rank.");
                if (available < cost)
                    throw new InvalidOperationException("Insufficient available skill points for the next combat modifier rank.");

                int newSpent = checked(spent + cost);
                int newAvailable = checked(available - cost);
                long newRevision = checked(before.StoreRevision + 1);
                if ((long)earned != (long)newSpent + newAvailable)
                    throw new InvalidOperationException("Purchase skill-point conservation invariant violated.");

                // Only these two internally selected identifiers are interpolated; all values are bound.
                string rankColumn = criticalStrike ? "critical_strike_rank" : "overdrive_rank";
                string now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                using (var command = _connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = $@"
                        UPDATE gameplay_progression SET {rankColumn} = @rank,
                            spent_sp = @spent, available_sp = @available, store_revision = @revision, updated_at = @now
                        WHERE id = 1 AND reset_epoch = @epoch AND {rankColumn} = @expectedRank
                            AND store_revision = @expectedRevision;";
                    command.Parameters.AddWithValue("@rank", nextRank);
                    command.Parameters.AddWithValue("@spent", newSpent);
                    command.Parameters.AddWithValue("@available", newAvailable);
                    command.Parameters.AddWithValue("@revision", newRevision);
                    command.Parameters.AddWithValue("@now", now);
                    command.Parameters.AddWithValue("@epoch", expectedResetEpoch);
                    command.Parameters.AddWithValue("@expectedRank", expectedRank);
                    command.Parameters.AddWithValue("@expectedRevision", before.StoreRevision);
                    if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                        throw new InvalidOperationException("Modifier purchase progression update conflicted.");
                }

                var state = criticalStrike
                    ? CyberDefenseCombatModifierTransition.ResetCriticalPhase(before.ModifierState)
                    : currentRank == 0
                        ? new CyberDefenseCombatModifierState(before.ModifierState.CriticalCorrectAttackCount, 0, 0)
                        : before.ModifierState;
                using (var command = _connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
                        UPDATE gameplay_run_state SET critical_correct_attack_count = @count,
                            overdrive_streak = @streak, overdrive_remaining_bonus_attacks = @remaining, updated_at = @now
                        WHERE id = 1 AND reset_epoch = @epoch;";
                    command.Parameters.AddWithValue("@count", state.CriticalCorrectAttackCount);
                    command.Parameters.AddWithValue("@streak", state.OverdriveStreak);
                    command.Parameters.AddWithValue("@remaining", state.OverdriveRemainingBonusAttacks);
                    command.Parameters.AddWithValue("@now", now);
                    command.Parameters.AddWithValue("@epoch", expectedResetEpoch);
                    if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                        throw new InvalidOperationException("Modifier purchase run-state update conflicted.");
                }

                // Read the authoritative result before releasing this transaction/gate. A later
                // purchase may commit immediately afterward, but cannot change this immutable result.
                var result = await ReadCombatModifierSnapshotAsync(_connection, transaction, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                transaction.Commit();
                return result;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }, cancellationToken);

        return PublishCombatModifierSnapshotAfterCommitAsync(committedPurchase);
    }

    // The purchase task completes only after its transaction committed and the connection gate was
    // released, so subscribers run outside the gate and can never observe uncommitted state.
    private async Task<CyberDefenseCombatModifierSnapshot> PublishCombatModifierSnapshotAfterCommitAsync(
        Task<CyberDefenseCombatModifierSnapshot> committedPurchase)
    {
        var snapshot = await committedPurchase.ConfigureAwait(false);
        NotifyCombatModifierSnapshotChanged(snapshot);
        return snapshot;
    }

    private async Task PublishResetBaselineAfterClearAsync(
        Task clearTask, Func<CyberDefenseCombatModifierSnapshot?> getBaseline)
    {
        await clearTask.ConfigureAwait(false);
        var baseline = getBaseline();
        if (baseline != null)
        {
            NotifyCombatModifierSnapshotChanged(baseline);
        }
    }

    private void NotifyCombatModifierSnapshotChanged(CyberDefenseCombatModifierSnapshot snapshot)
    {
        var handlers = CombatModifierSnapshotChanged;
        if (handlers == null)
        {
            return;
        }

        lock (_notificationLock)
        {
            if (_lastPublishedModifierEpoch >= 0)
            {
                if (snapshot.ResetEpoch < _lastPublishedModifierEpoch)
                {
                    return;
                }

                if (snapshot.ResetEpoch == _lastPublishedModifierEpoch && snapshot.StoreRevision <= _lastPublishedModifierRevision)
                {
                    return;
                }
            }

            _lastPublishedModifierEpoch = snapshot.ResetEpoch;
            _lastPublishedModifierRevision = snapshot.StoreRevision;

            foreach (Action<CyberDefenseCombatModifierSnapshot> subscriber in handlers.GetInvocationList())
            {
                try
                {
                    subscriber(snapshot);
                }
                catch
                {
                    // Observer exception isolation: subscriber failures must never compromise a committed purchase.
                }
            }
        }
    }

    private static long ReadModifierSnapshotInteger(SqliteDataReader reader, string column, long min, long max)
    {
        // INTEGER affinity and range CHECKs do not guarantee the source storage class. In particular,
        // GetInt64 can truncate REAL values. Verify typeof BEFORE any numeric conversion or narrowing.
        if (reader.GetString(reader.GetOrdinal(column + "_type")) != "integer")
        {
            throw new InvalidOperationException($"Combat modifier snapshot requires INTEGER storage for '{column}'.");
        }
        long value = reader.GetInt64(reader.GetOrdinal(column));
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"Combat modifier snapshot has invalid '{column}': expected {min} to {max}.");
        }
        return value;
    }

    private static DateTimeOffset ReadModifierSnapshotTimestamp(SqliteDataReader reader, string column)
    {
        if (reader.GetValue(reader.GetOrdinal(column)) is not string text
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
        {
            throw new InvalidOperationException($"Invalid '{column}' timestamp in combat modifier snapshot reset context.");
        }
        return timestamp;
    }

    public async Task<CyberDefenseAttackUpgradeSnapshot> GetAttackUpgradeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var progression = await GetProgressionAsync(cancellationToken).ConfigureAwait(false);
        return CyberDefenseAttackUpgradeSnapshot.FromProgression(progression);
    }

    public async Task<CyberDefenseFirewallUpgradeSnapshot> GetFirewallUpgradeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var progression = await GetProgressionAsync(cancellationToken).ConfigureAwait(false);
        return CyberDefenseFirewallUpgradeSnapshot.FromProgression(progression);
    }

    public async Task<CyberDefenseProgressionRecord> PurchaseFirewallUpgradeAsync(
        int expectedRank,
        long expectedResetEpoch,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRank);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedResetEpoch);

        var newProgression = await ExecuteWithConnectionGateAsync(async ct =>
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                // 1. Check reset intent and read progression row within transaction
                bool isResetPending;
                long currentEpoch;
                long currentRevision;
                long currentCumulativeXp;
                int currentPlayerLevel;
                int currentEarnedSp;
                int currentSpentSp;
                int currentAvailableSp;
                int currentAttackRank;
                string economyVersion;
                int currentFirewallRank;
                int isBeginnerAssistanceActive;
                int currentActivatedFirewallRank;

                using (var checkCmd = _connection.CreateCommand())
                {
                    checkCmd.Transaction = transaction;
                    checkCmd.CommandText = @"
                        SELECT r.is_pending, p.reset_epoch, p.store_revision, p.cumulative_xp, p.player_level,
                               p.earned_sp, p.spent_sp, p.available_sp, p.attack_rank, p.economy_version,
                               p.firewall_rank, p.is_beginner_assistance_active, p.activated_firewall_rank
                        FROM gameplay_reset_intent r
                        JOIN gameplay_progression p ON p.id = 1
                        WHERE r.id = 1;
                    ";
                    using var reader = await checkCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("Missing reset intent or progression row.");
                    }

                    isResetPending = reader.GetInt32(0) == 1;
                    currentEpoch = reader.GetInt64(1);
                    currentRevision = reader.GetInt64(2);
                    currentCumulativeXp = reader.GetInt64(3);
                    currentPlayerLevel = reader.GetInt32(4);
                    currentEarnedSp = reader.GetInt32(5);
                    currentSpentSp = reader.GetInt32(6);
                    currentAvailableSp = reader.GetInt32(7);
                    currentAttackRank = reader.GetInt32(8);
                    economyVersion = reader.GetString(9);
                    currentFirewallRank = reader.GetInt32(10);
                    isBeginnerAssistanceActive = reader.GetInt32(11);
                    currentActivatedFirewallRank = reader.GetInt32(12);
                }

                // Step 1: Reject purchase if a reset is pending
                if (isResetPending)
                {
                    throw new InvalidOperationException("Cannot purchase firewall upgrade while a reset is pending.");
                }

                // Step 2: Validate expected reset epoch against current authoritative reset epoch
                if (expectedResetEpoch != currentEpoch)
                {
                    throw new InvalidOperationException(
                        $"Purchase reset epoch {expectedResetEpoch} does not match current store reset epoch {currentEpoch}.");
                }

                // Step 3: Validate SP conservation
                if (currentEarnedSp != checked(currentSpentSp + currentAvailableSp))
                {
                    throw new InvalidOperationException(
                        $"Progression invariant violated: earned SP ({currentEarnedSp}) != spent SP ({currentSpentSp}) + available SP ({currentAvailableSp}).");
                }

                // Step 4: Reject stale expectedRank
                if (expectedRank != currentFirewallRank)
                {
                    throw new InvalidOperationException(
                        $"Expected firewall rank {expectedRank} does not match current rank {currentFirewallRank}.");
                }

                // Step 5: Reject rank >= 3
                if (currentFirewallRank >= CyberDefenseFirewallPolicy.MaxRank)
                {
                    throw new InvalidOperationException(
                        $"Firewall rank {currentFirewallRank} has already reached maximum rank {CyberDefenseFirewallPolicy.MaxRank}.");
                }

                // Step 6: Compute exact next rank
                int nextRank = checked(currentFirewallRank + 1);

                // Step 7: Verify required player level
                int requiredLevel = CyberDefenseFirewallPolicy.GetRequiredLevel(nextRank);
                if (currentPlayerLevel < requiredLevel)
                {
                    throw new InvalidOperationException(
                        $"Player level {currentPlayerLevel} is insufficient for firewall rank {nextRank} (requires level {requiredLevel}).");
                }

                // Step 8: Derive exact purchase cost from CyberDefenseFirewallPolicy
                int cost = CyberDefenseFirewallPolicy.GetUpgradeCost(nextRank);

                // Step 9: Reject insufficient available SP
                if (currentAvailableSp < cost)
                {
                    throw new InvalidOperationException(
                        $"Insufficient available skill points ({currentAvailableSp}) to purchase firewall rank {nextRank} (cost {cost}).");
                }

                // Step 10: Perform checked arithmetic for new spent SP, new available SP, and new store revision
                int newSpentSp = checked(currentSpentSp + cost);
                int newAvailableSp = checked(currentAvailableSp - cost);
                long newRevision = checked(currentRevision + 1);

                // Step 11: Validate SP conservation post-calculation
                if (currentEarnedSp != checked(newSpentSp + newAvailableSp))
                {
                    throw new InvalidOperationException(
                        $"SP conservation invariant violated: earned SP ({currentEarnedSp}) != new spent SP ({newSpentSp}) + new available SP ({newAvailableSp}).");
                }

                var now = DateTimeOffset.UtcNow;

                // Step 12: On purchase of Firewall Rank 1, deactivate the persisted beginner-assistance eligibility flag
                int newAssistanceActive = nextRank == 1 ? 0 : isBeginnerAssistanceActive;

                // Step 13: Atomically update progression guarded against stale rank and reset epoch
                using (var updateCmd = _connection.CreateCommand())
                {
                    updateCmd.Transaction = transaction;
                    updateCmd.CommandText = @"
                        UPDATE gameplay_progression
                        SET store_revision = @newRevision,
                            spent_sp = @newSpentSp,
                            available_sp = @newAvailableSp,
                            firewall_rank = @nextRank,
                            is_beginner_assistance_active = @newAssistanceActive,
                            updated_at = @now
                        WHERE id = 1
                          AND reset_epoch = @expectedResetEpoch
                          AND firewall_rank = @expectedRank;
                    ";
                    updateCmd.Parameters.AddWithValue("@newRevision", newRevision);
                    updateCmd.Parameters.AddWithValue("@newSpentSp", newSpentSp);
                    updateCmd.Parameters.AddWithValue("@newAvailableSp", newAvailableSp);
                    updateCmd.Parameters.AddWithValue("@nextRank", nextRank);
                    updateCmd.Parameters.AddWithValue("@newAssistanceActive", newAssistanceActive);
                    updateCmd.Parameters.AddWithValue("@now", now.ToString("O"));
                    updateCmd.Parameters.AddWithValue("@expectedResetEpoch", expectedResetEpoch);
                    updateCmd.Parameters.AddWithValue("@expectedRank", expectedRank);

                    // Step 14: Verify exactly one progression row was updated
                    int rowsAffected = await updateCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    if (rowsAffected != 1)
                    {
                        throw new InvalidOperationException(
                            $"Failed to update progression row. Concurrency conflict or row modified concurrently (rows affected: {rowsAffected}).");
                    }
                }

                // Step 15: Commit transaction
                transaction.Commit();

                // Step 16: Return authoritative updated progression record
                return new CyberDefenseProgressionRecord(
                    newRevision,
                    currentEpoch,
                    currentCumulativeXp,
                    currentPlayerLevel,
                    currentEarnedSp,
                    newSpentSp,
                    newAvailableSp,
                    currentAttackRank,
                    economyVersion,
                    now,
                    nextRank,
                    currentActivatedFirewallRank);
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);

        NotifyFirewallUpgradeSnapshotChanged(CyberDefenseFirewallUpgradeSnapshot.FromProgression(newProgression));

        return newProgression;
    }

    public async Task<CyberDefenseFirewallActivationResult> ActivatePendingFirewallCapacityAsync(
        int questionBoundTargetCapacity,
        long expectedResetEpoch,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(questionBoundTargetCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedResetEpoch);

        return await ExecuteWithConnectionGateAsync(async ct =>
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                // 1. Check reset intent and progression row
                bool isResetPending;
                long currentEpoch;
                int firewallRank;
                int previouslyActivatedRank;

                using (var checkCmd = _connection.CreateCommand())
                {
                    checkCmd.Transaction = transaction;
                    checkCmd.CommandText = @"
                        SELECT r.is_pending, p.reset_epoch, p.firewall_rank, p.activated_firewall_rank
                        FROM gameplay_reset_intent r
                        JOIN gameplay_progression p ON p.id = 1
                        WHERE r.id = 1;
                    ";
                    using var reader = await checkCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("Missing reset intent or progression row.");
                    }

                    isResetPending = reader.GetInt32(0) == 1;
                    currentEpoch = reader.GetInt64(1);
                    firewallRank = reader.GetInt32(2);
                    previouslyActivatedRank = reader.GetInt32(3);
                }

                // 2. Reject if reset is pending
                if (isResetPending)
                {
                    transaction.Rollback();
                    return CyberDefenseFirewallActivationResult.RejectedResetPending("Cannot activate firewall capacity while a reset is pending.");
                }

                // 3. Verify expected reset epoch against current store reset epoch
                if (expectedResetEpoch != currentEpoch)
                {
                    transaction.Rollback();
                    return CyberDefenseFirewallActivationResult.RejectedEpochMismatch(
                        $"Activation reset epoch {expectedResetEpoch} does not match current store reset epoch {currentEpoch}.");
                }

                // 4. Read current gameplay_run_state
                int sector;
                int opponentIndex;
                int opponentHp;
                int playerHp;
                int opponentExecutedAttacks;
                string provenanceStr;
                int oldShieldCapacity;
                int oldActiveShields;
                int oldShieldRechargeUnits;

                using (var runCmd = _connection.CreateCommand())
                {
                    runCmd.Transaction = transaction;
                    runCmd.CommandText = @"
                        SELECT sector, opponent_index, opponent_current_hp, player_current_hp,
                               opponent_executed_attacks, opponent_encounter_provenance,
                               shield_capacity, active_shields, shield_recharge_units
                        FROM gameplay_run_state
                        WHERE id = 1;
                    ";
                    using var runReader = await runCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await runReader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("Missing gameplay_run_state row.");
                    }

                    sector = runReader.GetInt32(0);
                    opponentIndex = runReader.GetInt32(1);
                    opponentHp = runReader.GetInt32(2);
                    playerHp = runReader.GetInt32(3);
                    opponentExecutedAttacks = runReader.GetInt32(4);
                    provenanceStr = runReader.GetString(5);
                    oldShieldCapacity = runReader.GetInt32(6);
                    oldActiveShields = runReader.GetInt32(7);
                    oldShieldRechargeUnits = runReader.GetInt32(8);
                }

                if (!Enum.TryParse<OpponentEncounterProvenance>(provenanceStr, ignoreCase: false, out var provenance))
                {
                    throw new InvalidOperationException($"Invalid opponent_encounter_provenance '{provenanceStr}' in gameplay_run_state.");
                }

                // Validate existing state invariants
                var currentRunState = CyberDefenseRunState.Rehydrate(
                    sector, opponentIndex, opponentHp, playerHp, opponentExecutedAttacks, provenance,
                    oldShieldCapacity, oldActiveShields, oldShieldRechargeUnits);

                // 5. Safely defer activation while an older unsettled/recoverable gameplay intent exists
                using (var pendingCmd = _connection.CreateCommand())
                {
                    pendingCmd.Transaction = transaction;
                    pendingCmd.CommandText = @"
                        SELECT COUNT(*)
                        FROM gameplay_pending_intent
                        WHERE reset_epoch = @epoch;
                    ";
                    pendingCmd.Parameters.AddWithValue("@epoch", currentEpoch);
                    long pendingCount = Convert.ToInt64(await pendingCmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
                    if (pendingCount > 0)
                    {
                        transaction.Commit();
                        return CyberDefenseFirewallActivationResult.DeferredPendingIntent(
                            currentRunState,
                            "Cannot activate firewall capacity while unsettled combat intents exist.");
                    }
                }

                // 6. Capacity and recharge semantics with question-bound target clamp
                int effectiveTarget = Math.Min(firewallRank, questionBoundTargetCapacity);
                int targetCapacity = Math.Clamp(effectiveTarget, 0, CyberDefenseFirewallPolicy.MaxShieldCapacity);

                // Deterministic no-op if purchased capacity has already been activated or target capacity <= previously activated rank
                if (targetCapacity <= previouslyActivatedRank)
                {
                    transaction.Commit();
                    return CyberDefenseFirewallActivationResult.AlreadyCurrent(currentRunState);
                }

                int newCapacity = targetCapacity;
                int newActiveShields;
                int newRechargeUnits;

                if (previouslyActivatedRank == 0)
                {
                    // First activation of permanent firewall: replace former temporary emergency slot (if any).
                    // Start fully charged with 0 recharge units.
                    newActiveShields = targetCapacity;
                    newRechargeUnits = 0;
                }
                else
                {
                    // Incremental upgrade (e.g. Rank 1 -> 2 or Rank 2 -> 3):
                    // Charge only newly added slot(s), preserve existing active shields and recharge unless now full.
                    int rankDelta = targetCapacity - previouslyActivatedRank;
                    newActiveShields = oldActiveShields + rankDelta;
                    newRechargeUnits = newActiveShields == newCapacity ? 0 : oldShieldRechargeUnits;
                }

                // Validate new shield invariants
                var newRunState = CyberDefenseRunState.Rehydrate(
                    sector, opponentIndex, opponentHp, playerHp, opponentExecutedAttacks, provenance,
                    newCapacity, newActiveShields, newRechargeUnits);

                var now = DateTimeOffset.UtcNow;

                // 7. Update gameplay_run_state atomically with optimistic concurrency check
                using (var updateRunCmd = _connection.CreateCommand())
                {
                    updateRunCmd.Transaction = transaction;
                    updateRunCmd.CommandText = @"
                        UPDATE gameplay_run_state
                        SET shield_capacity = @newCapacity,
                            active_shields = @newActiveShields,
                            shield_recharge_units = @newRechargeUnits,
                            updated_at = @now
                        WHERE id = 1
                          AND reset_epoch = @expectedResetEpoch
                          AND shield_capacity = @oldCapacity;
                    ";
                    updateRunCmd.Parameters.AddWithValue("@newCapacity", newCapacity);
                    updateRunCmd.Parameters.AddWithValue("@newActiveShields", newActiveShields);
                    updateRunCmd.Parameters.AddWithValue("@newRechargeUnits", newRechargeUnits);
                    updateRunCmd.Parameters.AddWithValue("@now", now.ToString("O"));
                    updateRunCmd.Parameters.AddWithValue("@expectedResetEpoch", expectedResetEpoch);
                    updateRunCmd.Parameters.AddWithValue("@oldCapacity", oldShieldCapacity);

                    int rows = await updateRunCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    if (rows != 1)
                    {
                        throw new InvalidOperationException(
                            $"Failed to update gameplay_run_state during firewall activation. Concurrency conflict (rows affected: {rows}).");
                    }
                }

                // 8. Update gameplay_progression.activated_firewall_rank atomically
                using (var updateProgCmd = _connection.CreateCommand())
                {
                    updateProgCmd.Transaction = transaction;
                    updateProgCmd.CommandText = @"
                        UPDATE gameplay_progression
                        SET activated_firewall_rank = @targetRank,
                            updated_at = @now
                        WHERE id = 1
                          AND reset_epoch = @expectedResetEpoch
                          AND activated_firewall_rank = @previouslyActivatedRank;
                    ";
                    updateProgCmd.Parameters.AddWithValue("@targetRank", targetCapacity);
                    updateProgCmd.Parameters.AddWithValue("@now", now.ToString("O"));
                    updateProgCmd.Parameters.AddWithValue("@expectedResetEpoch", expectedResetEpoch);
                    updateProgCmd.Parameters.AddWithValue("@previouslyActivatedRank", previouslyActivatedRank);

                    int rows = await updateProgCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    if (rows != 1)
                    {
                        throw new InvalidOperationException(
                            $"Failed to update gameplay_progression during firewall activation. Concurrency conflict (rows affected: {rows}).");
                    }
                }

                transaction.Commit();
                return CyberDefenseFirewallActivationResult.Applied(newRunState);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                try { transaction.Rollback(); } catch { }
                return CyberDefenseFirewallActivationResult.Failed(ex.Message);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CyberDefenseProgressionRecord> PurchaseAttackUpgradeAsync(
        int expectedRank,
        long expectedResetEpoch,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRank);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedResetEpoch);

        var newProgression = await ExecuteWithConnectionGateAsync(async ct =>
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                // 1. Check reset intent and read progression row within transaction
                bool isResetPending;
                long currentEpoch;
                long currentRevision;
                long currentCumulativeXp;
                int currentPlayerLevel;
                int currentEarnedSp;
                int currentSpentSp;
                int currentAvailableSp;
                int currentAttackRank;
                string economyVersion;
                int currentFirewallRank;
                int currentActivatedFirewallRank;

                using (var checkCmd = _connection.CreateCommand())
                {
                    checkCmd.Transaction = transaction;
                    checkCmd.CommandText = @"
                        SELECT r.is_pending, p.reset_epoch, p.store_revision, p.cumulative_xp, p.player_level,
                               p.earned_sp, p.spent_sp, p.available_sp, p.attack_rank, p.economy_version,
                               p.firewall_rank, p.activated_firewall_rank
                        FROM gameplay_reset_intent r
                        JOIN gameplay_progression p ON p.id = 1
                        WHERE r.id = 1;
                    ";
                    using var reader = await checkCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("Missing reset intent or progression row.");
                    }

                    isResetPending = reader.GetInt32(0) == 1;
                    currentEpoch = reader.GetInt64(1);
                    currentRevision = reader.GetInt64(2);
                    currentCumulativeXp = reader.GetInt64(3);
                    currentPlayerLevel = reader.GetInt32(4);
                    currentEarnedSp = reader.GetInt32(5);
                    currentSpentSp = reader.GetInt32(6);
                    currentAvailableSp = reader.GetInt32(7);
                    currentAttackRank = reader.GetInt32(8);
                    economyVersion = reader.GetString(9);
                    currentFirewallRank = reader.GetInt32(10);
                    currentActivatedFirewallRank = reader.GetInt32(11);
                }

                // Step 1: Check reset intent state
                if (isResetPending)
                {
                    throw new InvalidOperationException("Cannot purchase attack upgrade while a reset is pending.");
                }

                // Step 2: Validate expected reset epoch
                if (expectedResetEpoch != currentEpoch)
                {
                    throw new InvalidOperationException(
                        $"Purchase reset epoch {expectedResetEpoch} does not match current store reset epoch {currentEpoch}.");
                }

                // Step 4: Validate progression invariants
                if (currentEarnedSp != checked(currentSpentSp + currentAvailableSp))
                {
                    throw new InvalidOperationException(
                        $"Progression invariant violated: earned SP ({currentEarnedSp}) != spent SP ({currentSpentSp}) + available SP ({currentAvailableSp}).");
                }

                // Step 5: Check expected current rank
                if (expectedRank != currentAttackRank)
                {
                    throw new InvalidOperationException(
                        $"Expected attack rank {expectedRank} does not match current rank {currentAttackRank}.");
                }

                // Step 6: Calculate exact next rank (fail closed on integer overflow or non-representable damage)
                if (currentAttackRank >= CyberDefenseAttackUpgradeSnapshot.MaxRepresentableAttackRank)
                {
                    throw new InvalidOperationException(
                        $"Attack rank overflow: current rank {currentAttackRank} has reached maximum representation limit.");
                }

                int nextRank;
                try
                {
                    nextRank = checked(currentAttackRank + 1);
                    _ = CyberDefenseAttackUpgradePolicy.GetAttackDamage(nextRank);
                }
                catch (OverflowException ex)
                {
                    throw new InvalidOperationException(
                        $"Attack rank upgrade rejected: next rank damage exceeds maximum representable attack damage.", ex);
                }

                // Step 7: Calculate exact cost
                int cost = CyberDefenseAttackUpgradePolicy.GetUpgradeCost(nextRank);

                // Step 8: Verify available SP
                if (currentAvailableSp < cost)
                {
                    throw new InvalidOperationException(
                        $"Insufficient available skill points ({currentAvailableSp}) to purchase attack rank {nextRank} (cost {cost}).");
                }

                // Step 9: Calculate new spent and available SP using checked arithmetic
                int newSpentSp = checked(currentSpentSp + cost);
                int newAvailableSp = checked(currentAvailableSp - cost);
                long newRevision = checked(currentRevision + 1);

                // Step 10: Verify SP conservation
                if (currentEarnedSp != checked(newSpentSp + newAvailableSp))
                {
                    throw new InvalidOperationException(
                        $"SP conservation invariant violated: earned SP ({currentEarnedSp}) != new spent SP ({newSpentSp}) + new available SP ({newAvailableSp}).");
                }

                var now = DateTimeOffset.UtcNow;

                // Step 11: Update progression with guarded predicate
                using (var updateCmd = _connection.CreateCommand())
                {
                    updateCmd.Transaction = transaction;
                    updateCmd.CommandText = @"
                        UPDATE gameplay_progression
                        SET store_revision = @newRevision,
                            spent_sp = @newSpentSp,
                            available_sp = @newAvailableSp,
                            attack_rank = @nextRank,
                            updated_at = @now
                        WHERE id = 1
                          AND reset_epoch = @expectedResetEpoch
                          AND attack_rank = @expectedRank;
                    ";
                    updateCmd.Parameters.AddWithValue("@newRevision", newRevision);
                    updateCmd.Parameters.AddWithValue("@newSpentSp", newSpentSp);
                    updateCmd.Parameters.AddWithValue("@newAvailableSp", newAvailableSp);
                    updateCmd.Parameters.AddWithValue("@nextRank", nextRank);
                    updateCmd.Parameters.AddWithValue("@now", now.ToString("O"));
                    updateCmd.Parameters.AddWithValue("@expectedResetEpoch", expectedResetEpoch);
                    updateCmd.Parameters.AddWithValue("@expectedRank", expectedRank);

                    // Step 12: Verify exactly one progression row was updated
                    int rowsAffected = await updateCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    if (rowsAffected != 1)
                    {
                        throw new InvalidOperationException(
                            $"Failed to update progression row. Concurrency conflict or row modified concurrently (rows affected: {rowsAffected}).");
                    }
                }

                // Step 13: Commit
                transaction.Commit();

                // Step 14: Return resulting immutable progression snapshot
                var newProgression = new CyberDefenseProgressionRecord(
                    newRevision,
                    currentEpoch,
                    currentCumulativeXp,
                    currentPlayerLevel,
                    currentEarnedSp,
                    newSpentSp,
                    newAvailableSp,
                    nextRank,
                    economyVersion,
                    now,
                    currentFirewallRank,
                    currentActivatedFirewallRank);

                return newProgression;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);

        NotifyAttackUpgradeSnapshotChanged(CyberDefenseAttackUpgradeSnapshot.FromProgression(newProgression));

        return newProgression;
    }

    private void NotifyAttackUpgradeSnapshotChanged(CyberDefenseAttackUpgradeSnapshot snapshot)
    {
        var handlers = AttackUpgradeSnapshotChanged;
        if (handlers == null)
        {
            return;
        }

        lock (_notificationLock)
        {
            if (_lastPublishedEpoch >= 0)
            {
                if (snapshot.ResetEpoch < _lastPublishedEpoch)
                {
                    return;
                }

                if (snapshot.ResetEpoch == _lastPublishedEpoch && snapshot.StoreRevision <= _lastPublishedRevision)
                {
                    return;
                }
            }

            _lastPublishedEpoch = snapshot.ResetEpoch;
            _lastPublishedRevision = snapshot.StoreRevision;

            foreach (Action<CyberDefenseAttackUpgradeSnapshot> subscriber in handlers.GetInvocationList())
            {
                try
                {
                    subscriber(snapshot);
                }
                catch
                {
                    // Observer exception isolation: subscriber failures must never compromise store operations.
                }
            }
        }
    }

    private void NotifyFirewallUpgradeSnapshotChanged(CyberDefenseFirewallUpgradeSnapshot snapshot)
    {
        var handlers = FirewallUpgradeSnapshotChanged;
        if (handlers == null)
        {
            return;
        }

        lock (_notificationLock)
        {
            if (_lastPublishedFirewallEpoch >= 0)
            {
                if (snapshot.ResetEpoch < _lastPublishedFirewallEpoch)
                {
                    return;
                }

                if (snapshot.ResetEpoch == _lastPublishedFirewallEpoch && snapshot.StoreRevision <= _lastPublishedFirewallRevision)
                {
                    return;
                }
            }

            _lastPublishedFirewallEpoch = snapshot.ResetEpoch;
            _lastPublishedFirewallRevision = snapshot.StoreRevision;

            foreach (Action<CyberDefenseFirewallUpgradeSnapshot> subscriber in handlers.GetInvocationList())
            {
                try
                {
                    subscriber(snapshot);
                }
                catch
                {
                    // Observer exception isolation: subscriber failures must never compromise store operations.
                }
            }
        }
    }

    /// <summary>
    /// Retrieves a stored receipt by its unique submission ID.
    /// Returns null if no receipt exists with the specified submission ID.
    /// </summary>
    public Task<CyberDefenseReceiptRecord?> GetReceiptAsync(string submissionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(submissionId))
        {
            throw new ArgumentException("Submission ID cannot be null or whitespace.", nameof(submissionId));
        }

        return ExecuteWithConnectionGateAsync(async ct =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT submission_id, receipt_kind, fact_id, is_correct, is_eligible,
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
                       next_opponent_encounter_provenance, defeated_opponent_encounter_provenance,
                       is_shield_absorbed, absorbed_enemy_damage,
                       next_shield_capacity, next_active_shields, next_shield_recharge_units,
                       base_attack_damage, bound_critical_strike_rank, bound_overdrive_rank,
                       critical_bonus_percent, overdrive_bonus_percent,
                       typeof(base_attack_damage) AS base_attack_damage_type,
                       typeof(bound_critical_strike_rank) AS bound_critical_strike_rank_type,
                       typeof(bound_overdrive_rank) AS bound_overdrive_rank_type,
                       typeof(critical_bonus_percent) AS critical_bonus_percent_type,
                       typeof(overdrive_bonus_percent) AS overdrive_bonus_percent_type
                FROM gameplay_receipt_ledger
                WHERE submission_id = @submissionId;
            ";
            cmd.Parameters.AddWithValue("@submissionId", submissionId);

            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return null;
            }

            return ReadReceiptRecord(reader);
        }, cancellationToken);
    }

    /// <summary>
    /// Narrowly scoped persistence operation for recording a validated receipt.
    /// Note: The atomic consumer in Slice 3 will perform multi-table atomic commits
    /// combining run state, receipt ledger, and progression revision.
    /// </summary>
    public Task RecordReceiptDirectAsync(CyberDefenseReceiptRecord receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return ExecuteWithConnectionGateAsync(async ct =>
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = transaction;
                InsertReceiptRecord(cmd, receipt);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Binds an INSERT statement for the specified receipt to the provided command.
    /// Used by multi-statement atomic transactions.
    /// </summary>
    public static void InsertReceiptRecord(SqliteCommand cmd, CyberDefenseReceiptRecord receipt)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentNullException.ThrowIfNull(receipt);

        cmd.CommandText = @"
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
                next_opponent_encounter_provenance, defeated_opponent_encounter_provenance,
                is_shield_absorbed, absorbed_enemy_damage,
                next_shield_capacity, next_active_shields, next_shield_recharge_units,
                base_attack_damage, bound_critical_strike_rank, bound_overdrive_rank,
                critical_bonus_percent, overdrive_bonus_percent
            ) VALUES (
                @submission_id, @receipt_kind, @fact_id, @is_correct, @is_eligible,
                @response_latency_ms, @reset_epoch, @processed_at,
                @requested_attack_damage, @applied_opponent_damage, @excess_opponent_damage,
                @incoming_enemy_damage, @applied_player_damage, @excess_enemy_damage,
                @potential_healing, @applied_healing, @is_opponent_defeated, @is_sector_completed,
                @is_game_over, @next_sector, @next_opponent_index, @next_opponent_current_hp,
                @next_player_current_hp, @terminal_sector, @terminal_opponent_index,
                @terminal_opponent_kind, @terminal_opponent_current_hp, @terminal_opponent_max_hp,
                @terminal_player_current_hp,
                @next_opponent_executed_attacks, @executed_attacks_on_defeated_opponent,
                @awarded_xp, @player_level_after, @cumulative_xp_after,
                @next_opponent_encounter_provenance, @defeated_opponent_encounter_provenance,
                @is_shield_absorbed, @absorbed_enemy_damage,
                @next_shield_capacity, @next_active_shields, @next_shield_recharge_units,
                @base_attack_damage, @bound_critical_strike_rank, @bound_overdrive_rank,
                @critical_bonus_percent, @overdrive_bonus_percent
            );
        ";

        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@submission_id", receipt.SubmissionId);
        cmd.Parameters.AddWithValue("@receipt_kind", receipt.ReceiptKind.ToString());
        cmd.Parameters.AddWithValue("@fact_id", receipt.FactId);
        cmd.Parameters.AddWithValue("@is_correct", receipt.IsCorrect ? 1 : 0);
        cmd.Parameters.AddWithValue("@is_eligible", receipt.IsEligible ? 1 : 0);
        cmd.Parameters.AddWithValue("@response_latency_ms", receipt.ResponseLatencyMs);
        cmd.Parameters.AddWithValue("@reset_epoch", receipt.ResetEpoch);
        cmd.Parameters.AddWithValue("@processed_at", receipt.ProcessedAt.ToString("O"));

        if (receipt.ReceiptKind == CyberDefenseReceiptKind.Applied)
        {
            var t = receipt.TransitionResult ?? throw new InvalidOperationException("Applied receipt requires non-null transition result.");
            cmd.Parameters.AddWithValue("@requested_attack_damage", t.RequestedAttackDamage);
            cmd.Parameters.AddWithValue("@applied_opponent_damage", t.AppliedOpponentDamage);
            cmd.Parameters.AddWithValue("@excess_opponent_damage", t.ExcessOpponentDamage);
            cmd.Parameters.AddWithValue("@incoming_enemy_damage", t.IncomingEnemyDamage);
            cmd.Parameters.AddWithValue("@applied_player_damage", t.AppliedPlayerDamage);
            cmd.Parameters.AddWithValue("@excess_enemy_damage", t.ExcessEnemyDamage);
            cmd.Parameters.AddWithValue("@potential_healing", t.PotentialHealing);
            cmd.Parameters.AddWithValue("@applied_healing", t.AppliedHealing);
            cmd.Parameters.AddWithValue("@is_opponent_defeated", t.IsOpponentDefeated ? 1 : 0);
            cmd.Parameters.AddWithValue("@is_sector_completed", t.IsSectorCompleted ? 1 : 0);
            cmd.Parameters.AddWithValue("@is_game_over", t.IsGameOver ? 1 : 0);
            cmd.Parameters.AddWithValue("@next_sector", t.NextState.Sector);
            cmd.Parameters.AddWithValue("@next_opponent_index", t.NextState.OpponentIndex);
            cmd.Parameters.AddWithValue("@next_opponent_current_hp", t.NextState.CurrentOpponent.CurrentHp);
            cmd.Parameters.AddWithValue("@next_player_current_hp", t.NextState.PlayerCurrentHp);

            if (t.IsGameOver && t.TerminalSnapshot != null)
            {
                cmd.Parameters.AddWithValue("@terminal_sector", t.TerminalSnapshot.Sector);
                cmd.Parameters.AddWithValue("@terminal_opponent_index", t.TerminalSnapshot.OpponentIndex);
                cmd.Parameters.AddWithValue("@terminal_opponent_kind", t.TerminalSnapshot.Kind.ToString());
                cmd.Parameters.AddWithValue("@terminal_opponent_current_hp", t.TerminalSnapshot.OpponentCurrentHp);
                cmd.Parameters.AddWithValue("@terminal_opponent_max_hp", t.TerminalSnapshot.OpponentMaxHp);
                cmd.Parameters.AddWithValue("@terminal_player_current_hp", t.TerminalSnapshot.PlayerCurrentHp);
            }
            else
            {
                cmd.Parameters.AddWithValue("@terminal_sector", DBNull.Value);
                cmd.Parameters.AddWithValue("@terminal_opponent_index", DBNull.Value);
                cmd.Parameters.AddWithValue("@terminal_opponent_kind", DBNull.Value);
                cmd.Parameters.AddWithValue("@terminal_opponent_current_hp", DBNull.Value);
                cmd.Parameters.AddWithValue("@terminal_opponent_max_hp", DBNull.Value);
                cmd.Parameters.AddWithValue("@terminal_player_current_hp", DBNull.Value);
            }

            cmd.Parameters.AddWithValue("@next_opponent_executed_attacks", t.NextState.CurrentOpponent.ExecutedAttacks);
            cmd.Parameters.AddWithValue("@executed_attacks_on_defeated_opponent", (object?)t.ExecutedAttacksOnDefeatedOpponent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@awarded_xp", (object?)receipt.AwardedXp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@player_level_after", (object?)receipt.PlayerLevelAfter ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cumulative_xp_after", (object?)receipt.CumulativeXpAfter ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@next_opponent_encounter_provenance", t.NextState.CurrentOpponent.Provenance.ToString());
            cmd.Parameters.AddWithValue("@defeated_opponent_encounter_provenance", (object?)t.DefeatedOpponentProvenance?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@is_shield_absorbed", t.IsShieldAbsorbed ? 1 : 0);
            cmd.Parameters.AddWithValue("@absorbed_enemy_damage", t.AbsorbedEnemyDamage);
            cmd.Parameters.AddWithValue("@next_shield_capacity", t.NextState.ShieldCapacity);
            cmd.Parameters.AddWithValue("@next_active_shields", t.NextState.ActiveShields);
            cmd.Parameters.AddWithValue("@next_shield_recharge_units", t.NextState.ShieldRechargeUnits);
            cmd.Parameters.AddWithValue("@base_attack_damage", (object?)receipt.BaseAttackDamage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bound_critical_strike_rank", (object?)receipt.BoundCriticalStrikeRank ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bound_overdrive_rank", (object?)receipt.BoundOverdriveRank ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@critical_bonus_percent", (object?)receipt.CriticalBonusPercent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@overdrive_bonus_percent", (object?)receipt.OverdriveBonusPercent ?? DBNull.Value);
        }
        else
        {
            cmd.Parameters.AddWithValue("@requested_attack_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@applied_opponent_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@excess_opponent_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@incoming_enemy_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@applied_player_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@excess_enemy_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@potential_healing", DBNull.Value);
            cmd.Parameters.AddWithValue("@applied_healing", DBNull.Value);
            cmd.Parameters.AddWithValue("@is_opponent_defeated", DBNull.Value);
            cmd.Parameters.AddWithValue("@is_sector_completed", DBNull.Value);
            cmd.Parameters.AddWithValue("@is_game_over", DBNull.Value);
            cmd.Parameters.AddWithValue("@next_sector", DBNull.Value);
            cmd.Parameters.AddWithValue("@next_opponent_index", DBNull.Value);
            cmd.Parameters.AddWithValue("@next_opponent_current_hp", DBNull.Value);
            cmd.Parameters.AddWithValue("@next_player_current_hp", DBNull.Value);
            cmd.Parameters.AddWithValue("@terminal_sector", DBNull.Value);
            cmd.Parameters.AddWithValue("@terminal_opponent_index", DBNull.Value);
            cmd.Parameters.AddWithValue("@terminal_opponent_kind", DBNull.Value);
            cmd.Parameters.AddWithValue("@terminal_opponent_current_hp", DBNull.Value);
            cmd.Parameters.AddWithValue("@terminal_opponent_max_hp", DBNull.Value);
            cmd.Parameters.AddWithValue("@terminal_player_current_hp", DBNull.Value);
            cmd.Parameters.AddWithValue("@next_opponent_executed_attacks", DBNull.Value);
            cmd.Parameters.AddWithValue("@executed_attacks_on_defeated_opponent", DBNull.Value);
            cmd.Parameters.AddWithValue("@awarded_xp", DBNull.Value);
            cmd.Parameters.AddWithValue("@player_level_after", DBNull.Value);
            cmd.Parameters.AddWithValue("@cumulative_xp_after", DBNull.Value);
            cmd.Parameters.AddWithValue("@next_opponent_encounter_provenance", DBNull.Value);
            cmd.Parameters.AddWithValue("@defeated_opponent_encounter_provenance", DBNull.Value);
            cmd.Parameters.AddWithValue("@is_shield_absorbed", DBNull.Value);
            cmd.Parameters.AddWithValue("@absorbed_enemy_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@next_shield_capacity", DBNull.Value);
            cmd.Parameters.AddWithValue("@next_active_shields", DBNull.Value);
            cmd.Parameters.AddWithValue("@next_shield_recharge_units", DBNull.Value);
            cmd.Parameters.AddWithValue("@base_attack_damage", DBNull.Value);
            cmd.Parameters.AddWithValue("@bound_critical_strike_rank", DBNull.Value);
            cmd.Parameters.AddWithValue("@bound_overdrive_rank", DBNull.Value);
            cmd.Parameters.AddWithValue("@critical_bonus_percent", DBNull.Value);
            cmd.Parameters.AddWithValue("@overdrive_bonus_percent", DBNull.Value);
        }
    }

    /// <summary>
    /// Rehydrates an immutable receipt record from a reader query on gameplay_receipt_ledger.
    /// </summary>
    public static CyberDefenseReceiptRecord ReadReceiptRecord(SqliteDataReader reader)
    {
        string subId = reader.GetString(0);
        string kindStr = reader.GetString(1);
        string factId = reader.GetString(2);
        bool isCorrect = reader.GetInt32(3) == 1;
        bool isEligible = reader.GetInt32(4) == 1;
        long responseLatencyMs = reader.GetInt64(5);
        long resetEpoch = reader.GetInt64(6);
        string processedAtStr = reader.GetString(7);

        if (!DateTimeOffset.TryParse(processedAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var processedAt))
        {
            throw new InvalidOperationException($"Invalid processed_at timestamp '{processedAtStr}' in receipt ledger.");
        }

        if (!Enum.TryParse<CyberDefenseReceiptKind>(kindStr, ignoreCase: false, out var kind))
        {
            throw new InvalidOperationException($"Invalid receipt_kind '{kindStr}' in receipt ledger.");
        }

        if (kind == CyberDefenseReceiptKind.CalmModeSuppressed)
        {
            int maxNullOrdinal = reader.FieldCount >= 46 ? 45 : 40;
            for (int i = 8; i <= maxNullOrdinal; i++)
            {
                if (!reader.IsDBNull(i))
                {
                    throw new InvalidOperationException($"CalmModeSuppressed receipt row contains non-null combat transition column at index {i}.");
                }
            }

            if (reader.FieldCount >= 51)
            {
                for (int i = 46; i <= 50; i++)
                {
                    if (reader.IsDBNull(i) || reader.GetString(i) != "null")
                    {
                        throw new InvalidOperationException($"CalmModeSuppressed receipt row contains non-null V7 combat modifier column at index {i - 5}.");
                    }
                }
            }

            return CyberDefenseReceiptRecord.CreateCalmModeSuppressed(
                subId, factId, isCorrect, isEligible, responseLatencyMs, resetEpoch, processedAt);
        }

        if (kind == CyberDefenseReceiptKind.Applied)
        {
            for (int i = 8; i <= 22; i++)
            {
                if (reader.IsDBNull(i))
                {
                    throw new InvalidOperationException($"Applied receipt row contains null combat transition column at index {i}.");
                }
            }


            int requestedAttackDamage = reader.GetInt32(8);
            int appliedOpponentDamage = reader.GetInt32(9);
            int excessOpponentDamage = reader.GetInt32(10);
            int incomingEnemyDamage = reader.GetInt32(11);
            int appliedPlayerDamage = reader.GetInt32(12);
            int excessEnemyDamage = reader.GetInt32(13);
            int potentialHealing = reader.GetInt32(14);
            int appliedHealing = reader.GetInt32(15);
            bool isOpponentDefeated = reader.GetInt32(16) == 1;
            bool isSectorCompleted = reader.GetInt32(17) == 1;
            bool isGameOver = reader.GetInt32(18) == 1;
            int nextSector = reader.GetInt32(19);
            int nextOpponentIndex = reader.GetInt32(20);
            int nextOpponentCurrentHp = reader.GetInt32(21);
            int nextPlayerCurrentHp = reader.GetInt32(22);

            int nextOpponentExecutedAttacks = reader.IsDBNull(29) ? 0 : reader.GetInt32(29);
            OpponentEncounterProvenance nextOpponentProvenance = OpponentEncounterProvenance.Authoritative;
            if (!reader.IsDBNull(34))
            {
                string nextProvStr = reader.GetString(34);
                if (!Enum.TryParse<OpponentEncounterProvenance>(nextProvStr, ignoreCase: false, out nextOpponentProvenance))
                {
                    throw new InvalidOperationException($"Invalid next_opponent_encounter_provenance '{nextProvStr}' in receipt ledger.");
                }
            }

            int? executedAttacksOnDefeated = null;
            OpponentEncounterProvenance? defeatedOpponentProvenance = null;
            if (isOpponentDefeated)
            {
                executedAttacksOnDefeated = reader.IsDBNull(30) ? null : reader.GetInt32(30);
                if (!reader.IsDBNull(35))
                {
                    string defProvStr = reader.GetString(35);
                    if (!Enum.TryParse<OpponentEncounterProvenance>(defProvStr, ignoreCase: false, out var parsedDefProv))
                    {
                        throw new InvalidOperationException($"Invalid defeated_opponent_encounter_provenance '{defProvStr}' in receipt ledger.");
                    }
                    defeatedOpponentProvenance = parsedDefProv;
                }
            }
            else
            {
                if (!reader.IsDBNull(30))
                {
                    throw new InvalidOperationException("Non-defeated opponent receipt row contains non-null executed_attacks_on_defeated_opponent.");
                }

                if (!reader.IsDBNull(35))
                {
                    throw new InvalidOperationException("Non-defeated opponent receipt row contains non-null defeated_opponent_encounter_provenance.");
                }
            }

            bool isShieldAbsorbed = false;
            int absorbedEnemyDamage = 0;
            int nextShieldCapacity = 0;
            int nextActiveShields = 0;
            int nextShieldRechargeUnits = 0;

            if (reader.FieldCount > 36 && !reader.IsDBNull(36))
            {
                for (int i = 36; i <= 40; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        throw new InvalidOperationException($"Applied receipt row contains inconsistent shield columns at index {i}.");
                    }
                }
                isShieldAbsorbed = reader.GetInt32(36) == 1;
                absorbedEnemyDamage = reader.GetInt32(37);
                nextShieldCapacity = reader.GetInt32(38);
                nextActiveShields = reader.GetInt32(39);
                nextShieldRechargeUnits = reader.GetInt32(40);
            }
            else if (reader.FieldCount > 36)
            {
                for (int i = 37; i <= 40; i++)
                {
                    if (!reader.IsDBNull(i))
                    {
                        throw new InvalidOperationException($"Applied receipt row contains inconsistent null shield columns at index {i}.");
                    }
                }
            }

            CyberDefenseRunState nextState;
            try
            {
                nextState = CyberDefenseRunState.Rehydrate(
                    nextSector,
                    nextOpponentIndex,
                    nextOpponentCurrentHp,
                    nextPlayerCurrentHp,
                    nextOpponentExecutedAttacks,
                    nextOpponentProvenance,
                    nextShieldCapacity,
                    nextActiveShields,
                    nextShieldRechargeUnits);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException($"Invalid domain next state in receipt ledger: {ex.Message}", ex);
            }

            CyberDefenseTerminalRunSnapshot? terminalSnapshot = null;
            if (isGameOver)
            {
                for (int i = 23; i <= 28; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        throw new InvalidOperationException($"Game-over receipt row contains null terminal column at index {i}.");
                    }
                }

                int termSector = reader.GetInt32(23);
                int termOppIndex = reader.GetInt32(24);
                string termKindStr = reader.GetString(25);
                int termOppCurrentHp = reader.GetInt32(26);
                int termOppMaxHp = reader.GetInt32(27);
                int termPlayerHp = reader.GetInt32(28);

                if (!Enum.TryParse<OpponentKind>(termKindStr, ignoreCase: false, out var termKind))
                {
                    throw new InvalidOperationException($"Invalid terminal_opponent_kind '{termKindStr}' in receipt ledger.");
                }

                try
                {
                    terminalSnapshot = new CyberDefenseTerminalRunSnapshot(
                        termSector, termOppIndex, termKind, termOppCurrentHp, termOppMaxHp, termPlayerHp);
                }
                catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
                {
                    throw new InvalidOperationException($"Invalid terminal snapshot in receipt ledger: {ex.Message}", ex);
                }
            }
            else
            {
                for (int i = 23; i <= 28; i++)
                {
                    if (!reader.IsDBNull(i))
                    {
                        throw new InvalidOperationException($"Non-game-over receipt row contains non-null terminal column at index {i}.");
                    }
                }
            }

            int? awardedXp = reader.IsDBNull(31) ? null : reader.GetInt32(31);
            int? playerLevelAfter = reader.IsDBNull(32) ? null : reader.GetInt32(32);
            long? cumulativeXpAfter = reader.IsDBNull(33) ? null : reader.GetInt64(33);

            if (awardedXp.HasValue || playerLevelAfter.HasValue || cumulativeXpAfter.HasValue)
            {
                if (!awardedXp.HasValue || !playerLevelAfter.HasValue || !cumulativeXpAfter.HasValue)
                {
                    throw new InvalidOperationException("Inconsistent reward metadata in receipt ledger row: awarded_xp, player_level_after, and cumulative_xp_after must all be non-null if any is present.");
                }
            }

            CyberDefenseCombatTransitionResult transitionResult;
            try
            {
                transitionResult = new CyberDefenseCombatTransitionResult(
                    isCorrect,
                    requestedAttackDamage,
                    appliedOpponentDamage,
                    excessOpponentDamage,
                    incomingEnemyDamage,
                    appliedPlayerDamage,
                    excessEnemyDamage,
                    potentialHealing,
                    appliedHealing,
                    isOpponentDefeated,
                    isSectorCompleted,
                    isGameOver,
                    nextState,
                    terminalSnapshot,
                    executedAttacksOnDefeated,
                    defeatedOpponentProvenance,
                    isShieldAbsorbed: isShieldAbsorbed,
                    absorbedEnemyDamage: absorbedEnemyDamage);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException($"Invalid combat transition result in receipt ledger: {ex.Message}", ex);
            }

            int? baseAttackDamage = null;
            int? boundCriticalStrikeRank = null;
            int? boundOverdriveRank = null;
            int? criticalBonusPercent = null;
            int? overdriveBonusPercent = null;

            if (reader.FieldCount >= 46)
            {
                bool anyModifierPresent = !reader.IsDBNull(41) ||
                                          !reader.IsDBNull(42) ||
                                          !reader.IsDBNull(43) ||
                                          !reader.IsDBNull(44) ||
                                          !reader.IsDBNull(45);
                bool allModifierPresent = !reader.IsDBNull(41) &&
                                          !reader.IsDBNull(42) &&
                                          !reader.IsDBNull(43) &&
                                          !reader.IsDBNull(44) &&
                                          !reader.IsDBNull(45);

                if (anyModifierPresent && !allModifierPresent)
                {
                    throw new InvalidOperationException(
                        "Inconsistent V7 combat modifier metadata in receipt ledger row: base_attack_damage, bound ranks, and bonus percentages must all be non-null if any is present.");
                }

                if (!anyModifierPresent && reader.FieldCount >= 51)
                {
                    for (int i = 46; i <= 50; i++)
                    {
                        if (reader.IsDBNull(i) || reader.GetString(i) != "null")
                        {
                            throw new InvalidOperationException(
                                $"Inconsistent V7 combat modifier storage class at index {i} in receipt ledger.");
                        }
                    }
                }
                else if (allModifierPresent)
                {
                    baseAttackDamage = ReadReceiptModifierInteger(
                        reader,
                        valueOrdinal: 41,
                        typeOrdinal: reader.FieldCount > 46 ? 46 : null,
                        column: "base_attack_damage",
                        min: 1,
                        max: int.MaxValue);
                    boundCriticalStrikeRank = ReadReceiptModifierInteger(
                        reader,
                        valueOrdinal: 42,
                        typeOrdinal: reader.FieldCount > 47 ? 47 : null,
                        column: "bound_critical_strike_rank",
                        min: 0,
                        max: CyberDefenseCriticalStrikePolicy.MaxRank);
                    boundOverdriveRank = ReadReceiptModifierInteger(
                        reader,
                        valueOrdinal: 43,
                        typeOrdinal: reader.FieldCount > 48 ? 48 : null,
                        column: "bound_overdrive_rank",
                        min: 0,
                        max: CyberDefenseOverdrivePolicy.MaxRank);
                    criticalBonusPercent = ReadReceiptModifierInteger(
                        reader,
                        valueOrdinal: 44,
                        typeOrdinal: reader.FieldCount > 49 ? 49 : null,
                        column: "critical_bonus_percent",
                        min: 0,
                        max: CyberDefenseCriticalStrikePolicy.GetBonusPercent(CyberDefenseCriticalStrikePolicy.MaxRank));
                    overdriveBonusPercent = ReadReceiptModifierInteger(
                        reader,
                        valueOrdinal: 45,
                        typeOrdinal: reader.FieldCount > 50 ? 50 : null,
                        column: "overdrive_bonus_percent",
                        min: 0,
                        max: CyberDefenseOverdrivePolicy.Rank1BonusPercent);
                }
            }

            try
            {
                return CyberDefenseReceiptRecord.CreateApplied(
                    subId,
                    factId,
                    isCorrect,
                    isEligible,
                    responseLatencyMs,
                    resetEpoch,
                    processedAt,
                    transitionResult,
                    awardedXp,
                    playerLevelAfter,
                    cumulativeXpAfter,
                    baseAttackDamage,
                    boundCriticalStrikeRank,
                    boundOverdriveRank,
                    criticalBonusPercent,
                    overdriveBonusPercent);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException($"Invalid receipt record in receipt ledger: {ex.Message}", ex);
            }
        }

        throw new InvalidOperationException($"Unsupported receipt kind '{kind}'.");
    }

    private static int ReadReceiptModifierInteger(
        SqliteDataReader reader,
        int valueOrdinal,
        int? typeOrdinal,
        string column,
        int min,
        int max)
    {
        if (typeOrdinal is int explicitTypeOrdinal)
        {
            if (reader.IsDBNull(explicitTypeOrdinal) || reader.GetString(explicitTypeOrdinal) != "integer")
            {
                throw new InvalidOperationException(
                    $"Receipt ledger table requires INTEGER storage for '{column}'.");
            }
        }

        if (reader.IsDBNull(valueOrdinal) || reader.GetFieldType(valueOrdinal) != typeof(long))
        {
            throw new InvalidOperationException(
                $"Receipt ledger table requires INTEGER storage for '{column}'.");
        }

        long value = reader.GetInt64(valueOrdinal);
        if (value < min || value > max)
        {
            throw new InvalidOperationException(
                $"Existing database has invalid value {value} in 'gameplay_receipt_ledger.{column}': expected {min} to {max}.");
        }

        return (int)value;
    }

    public Task StagePendingIntentAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        return ExecuteWithConnectionGateAsync(async ct =>
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                using (var guardCmd = _connection.CreateCommand())
                {
                    guardCmd.Transaction = transaction;
                    guardCmd.CommandText = @"
                        SELECT r.is_pending, p.reset_epoch, p.attack_rank,
                               p.critical_strike_rank, typeof(p.critical_strike_rank) AS critical_strike_rank_type,
                               p.overdrive_rank, typeof(p.overdrive_rank) AS overdrive_rank_type
                        FROM gameplay_reset_intent r
                        JOIN gameplay_progression p ON p.id = 1
                        WHERE r.id = 1;
                    ";
                    using var guardReader = await guardCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await guardReader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("Missing gameplay_reset_intent or gameplay_progression row.");
                    }
                    bool isResetPending = guardReader.GetInt32(0) == 1;
                    long currentEpoch = guardReader.GetInt64(1);
                    int currentAttackRank = guardReader.GetInt32(2);

                    if (isResetPending)
                    {
                        throw new InvalidOperationException("Cannot stage pending intent while a reset is pending.");
                    }

                    if (intent.ResetEpoch != currentEpoch)
                    {
                        throw new InvalidOperationException(
                            $"Pending intent reset epoch {intent.ResetEpoch} does not match current store reset epoch {currentEpoch}.");
                    }

                    int currentCriticalStrikeRank = (int)ReadModifierSnapshotInteger(
                        guardReader, "critical_strike_rank", 0, CyberDefenseCriticalStrikePolicy.MaxRank);
                    int currentOverdriveRank = (int)ReadModifierSnapshotInteger(
                        guardReader, "overdrive_rank", 0, CyberDefenseOverdrivePolicy.MaxRank);
                    guardReader.Close();

                    int maxEntitledDamage = CyberDefenseAttackUpgradePolicy.GetAttackDamage(currentAttackRank);
                    if (intent.EffectiveAttackDamage > maxEntitledDamage)
                    {
                        throw new InvalidOperationException(
                            $"Pending intent effective attack damage {intent.EffectiveAttackDamage} exceeds authoritative entitlement {maxEntitledDamage} for attack rank {currentAttackRank}.");
                    }

                    if (intent.BoundCriticalStrikeRank > currentCriticalStrikeRank)
                    {
                        throw new InvalidOperationException(
                            $"Pending intent bound Critical Strike rank {intent.BoundCriticalStrikeRank} exceeds authoritative owned rank {currentCriticalStrikeRank}.");
                    }

                    if (intent.BoundOverdriveRank > currentOverdriveRank)
                    {
                        throw new InvalidOperationException(
                            $"Pending intent bound Overdrive rank {intent.BoundOverdriveRank} exceeds authoritative owned rank {currentOverdriveRank}.");
                    }
                }

                using (var selectCmd = _connection.CreateCommand())
                {
                    selectCmd.Transaction = transaction;
                    selectCmd.CommandText = @"
                        SELECT submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at, effective_attack_damage,
                               bound_critical_strike_rank, bound_overdrive_rank,
                               typeof(bound_critical_strike_rank) AS bound_critical_strike_rank_type,
                               typeof(bound_overdrive_rank) AS bound_overdrive_rank_type,
                               practice_position,
                               typeof(practice_position) AS practice_position_type
                        FROM gameplay_pending_intent
                        WHERE submission_id = @id;
                    ";
                    selectCmd.Parameters.AddWithValue("@id", intent.SubmissionId);
                    using var reader = await selectCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var existingIntent = ReadPendingIntent(reader);
                        reader.Close();

                        if (!HasMatchingPendingIntentCorePayload(existingIntent, intent))
                        {
                            throw new InvalidOperationException(
                                $"Conflicting pending intent already staged for submission '{intent.SubmissionId}'.");
                        }

                        if (existingIntent.PracticePosition.HasValue)
                        {
                            if (!intent.PracticePosition.HasValue || intent.PracticePosition.Value != existingIntent.PracticePosition.Value)
                            {
                                throw new InvalidOperationException(
                                    $"Conflicting pending intent practice position already staged for submission '{intent.SubmissionId}'.");
                            }

                            transaction.Commit();
                            return;
                        }

                        if (!intent.PracticePosition.HasValue)
                        {
                            transaction.Commit();
                            return;
                        }

                        await EnsureNoConflictingPracticePositionInEpochAsync(
                            _connection,
                            transaction,
                            intent.ResetEpoch,
                            intent.PracticePosition.Value,
                            intent.SubmissionId,
                            ct).ConfigureAwait(false);

                        using (var reconcileCmd = _connection.CreateCommand())
                        {
                            reconcileCmd.Transaction = transaction;
                            reconcileCmd.CommandText = @"
                                UPDATE gameplay_pending_intent
                                SET practice_position = @practice_position
                                WHERE submission_id = @submission_id;
                            ";
                            reconcileCmd.Parameters.AddWithValue("@practice_position", intent.PracticePosition.Value);
                            reconcileCmd.Parameters.AddWithValue("@submission_id", intent.SubmissionId);
                            await reconcileCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }

                        transaction.Commit();
                        return;
                    }
                }

                if (intent.PracticePosition.HasValue)
                {
                    await EnsureNoConflictingPracticePositionInEpochAsync(
                        _connection,
                        transaction,
                        intent.ResetEpoch,
                        intent.PracticePosition.Value,
                        intent.SubmissionId,
                        ct).ConfigureAwait(false);
                }

                using (var insertCmd = _connection.CreateCommand())
                {
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText = @"
                        INSERT INTO gameplay_pending_intent (
                            submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at,
                            effective_attack_damage, bound_critical_strike_rank, bound_overdrive_rank, practice_position
                        ) VALUES (
                            @submission_id, @fact_id, @is_correct, @is_eligible, @response_latency_ms, @reset_epoch, @created_at,
                            @effective_attack_damage, @bound_critical_strike_rank, @bound_overdrive_rank, @practice_position
                        );
                    ";
                    insertCmd.Parameters.AddWithValue("@submission_id", intent.SubmissionId);
                    insertCmd.Parameters.AddWithValue("@fact_id", intent.FactId);
                    insertCmd.Parameters.AddWithValue("@is_correct", intent.IsCorrect ? 1 : 0);
                    insertCmd.Parameters.AddWithValue("@is_eligible", intent.IsEligible ? 1 : 0);
                    insertCmd.Parameters.AddWithValue("@response_latency_ms", intent.ResponseLatencyMs);
                    insertCmd.Parameters.AddWithValue("@reset_epoch", intent.ResetEpoch);
                    insertCmd.Parameters.AddWithValue("@created_at", intent.CreatedAt.ToString("O"));
                    insertCmd.Parameters.AddWithValue("@effective_attack_damage", intent.EffectiveAttackDamage);
                    insertCmd.Parameters.AddWithValue("@bound_critical_strike_rank", intent.BoundCriticalStrikeRank);
                    insertCmd.Parameters.AddWithValue("@bound_overdrive_rank", intent.BoundOverdriveRank);
                    insertCmd.Parameters.AddWithValue("@practice_position", (object?)intent.PracticePosition ?? DBNull.Value);
                    await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                transaction.Commit();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }, cancellationToken);
    }

    public Task<CyberDefensePendingIntentRecord?> GetPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(submissionId))
        {
            throw new ArgumentException("Submission ID cannot be null or whitespace.", nameof(submissionId));
        }

        return ExecuteWithConnectionGateAsync(async ct =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at, effective_attack_damage,
                       bound_critical_strike_rank, bound_overdrive_rank,
                       typeof(bound_critical_strike_rank) AS bound_critical_strike_rank_type,
                       typeof(bound_overdrive_rank) AS bound_overdrive_rank_type,
                       practice_position,
                       typeof(practice_position) AS practice_position_type
                FROM gameplay_pending_intent
                WHERE submission_id = @id;
            ";
            cmd.Parameters.AddWithValue("@id", submissionId);

            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return null;
            }

            return ReadPendingIntent(reader);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<CyberDefensePendingIntentRecord>> GetPendingIntentsAsync(long resetEpoch, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(resetEpoch);

        return ExecuteWithConnectionGateAsync(async ct =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at, effective_attack_damage,
                       bound_critical_strike_rank, bound_overdrive_rank,
                       typeof(bound_critical_strike_rank) AS bound_critical_strike_rank_type,
                       typeof(bound_overdrive_rank) AS bound_overdrive_rank_type,
                       practice_position,
                       typeof(practice_position) AS practice_position_type
                FROM gameplay_pending_intent
                WHERE reset_epoch = @epoch
                ORDER BY created_at ASC, submission_id ASC;
            ";
            cmd.Parameters.AddWithValue("@epoch", resetEpoch);

            var list = new List<CyberDefensePendingIntentRecord>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(ReadPendingIntent(reader));
            }

            ValidateAndSortPendingIntents(list);
            return (IReadOnlyList<CyberDefensePendingIntentRecord>)list;
        }, cancellationToken);
    }

    public Task ClearPendingIntentAsync(string submissionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(submissionId))
        {
            throw new ArgumentException("Submission ID cannot be null or whitespace.", nameof(submissionId));
        }

        return ExecuteWithConnectionGateAsync(async ct =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "DELETE FROM gameplay_pending_intent WHERE submission_id = @id;";
            cmd.Parameters.AddWithValue("@id", submissionId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<CyberDefenseReceiptRecord> ApplyAttemptTransactionAsync(CyberDefensePendingIntentRecord intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        return ExecuteWithConnectionGateAsync(async ct =>
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                // 0. Check reset intent
                using (var guardCmd = _connection.CreateCommand())
                {
                    guardCmd.Transaction = transaction;
                    guardCmd.CommandText = "SELECT is_pending FROM gameplay_reset_intent WHERE id = 1;";
                    using var guardReader = await guardCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await guardReader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("Missing gameplay_reset_intent row.");
                    }
                    bool isResetPending = guardReader.GetInt32(0) == 1;
                    guardReader.Close();

                    if (isResetPending)
                    {
                        throw new InvalidOperationException("Cannot apply attempt transaction while a reset is pending.");
                    }
                }

                // 1. Check existing receipt in ledger
                using (var checkCmd = _connection.CreateCommand())
                {
                    checkCmd.Transaction = transaction;

                    checkCmd.CommandText = @"
                        SELECT submission_id, receipt_kind, fact_id, is_correct, is_eligible,
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
                               next_opponent_encounter_provenance, defeated_opponent_encounter_provenance,
                               is_shield_absorbed, absorbed_enemy_damage,
                               next_shield_capacity, next_active_shields, next_shield_recharge_units,
                               base_attack_damage, bound_critical_strike_rank, bound_overdrive_rank,
                               critical_bonus_percent, overdrive_bonus_percent,
                               typeof(base_attack_damage) AS base_attack_damage_type,
                               typeof(bound_critical_strike_rank) AS bound_critical_strike_rank_type,
                               typeof(bound_overdrive_rank) AS bound_overdrive_rank_type,
                               typeof(critical_bonus_percent) AS critical_bonus_percent_type,
                               typeof(overdrive_bonus_percent) AS overdrive_bonus_percent_type
                        FROM gameplay_receipt_ledger
                        WHERE submission_id = @id;
                    ";
                    checkCmd.Parameters.AddWithValue("@id", intent.SubmissionId);
                    using var receiptReader = await checkCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (await receiptReader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var existingReceipt = ReadReceiptRecord(receiptReader);
                        receiptReader.Close();

                        CyberDefensePendingIntentRecord? leftoverPending = null;
                        using (var pendingCheckCmd = _connection.CreateCommand())
                        {
                            pendingCheckCmd.Transaction = transaction;
                            pendingCheckCmd.CommandText = @"
                                SELECT submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at, effective_attack_damage,
                                       bound_critical_strike_rank, bound_overdrive_rank,
                                       typeof(bound_critical_strike_rank) AS bound_critical_strike_rank_type,
                                       typeof(bound_overdrive_rank) AS bound_overdrive_rank_type,
                                       practice_position,
                                       typeof(practice_position) AS practice_position_type
                                FROM gameplay_pending_intent
                                WHERE submission_id = @id;
                            ";
                            pendingCheckCmd.Parameters.AddWithValue("@id", intent.SubmissionId);
                            using var pendingCheckReader = await pendingCheckCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                            if (await pendingCheckReader.ReadAsync(ct).ConfigureAwait(false))
                            {
                                leftoverPending = ReadPendingIntent(pendingCheckReader);
                            }
                        }

                        if (leftoverPending is not null)
                        {
                            if (!HasMatchingPendingIntentCorePayload(leftoverPending, intent) ||
                                (leftoverPending.PracticePosition.HasValue &&
                                 intent.PracticePosition.HasValue &&
                                 leftoverPending.PracticePosition.Value != intent.PracticePosition.Value))
                            {
                                throw new InvalidOperationException(
                                    $"Conflicting pending intent already staged for submission '{intent.SubmissionId}'.");
                            }
                        }

                        bool isConflictingReceipt =
                            !string.Equals(existingReceipt.FactId, intent.FactId, StringComparison.Ordinal) ||
                            existingReceipt.IsCorrect != intent.IsCorrect ||
                            existingReceipt.IsEligible != intent.IsEligible ||
                            existingReceipt.ResponseLatencyMs != intent.ResponseLatencyMs ||
                            existingReceipt.ResetEpoch != intent.ResetEpoch;

                        if (!isConflictingReceipt && existingReceipt.ReceiptKind == CyberDefenseReceiptKind.Applied)
                        {
                            if (existingReceipt.BaseAttackDamage.HasValue)
                            {
                                isConflictingReceipt =
                                    existingReceipt.BaseAttackDamage.Value != intent.EffectiveAttackDamage ||
                                    existingReceipt.BoundCriticalStrikeRank != intent.BoundCriticalStrikeRank ||
                                    existingReceipt.BoundOverdriveRank != intent.BoundOverdriveRank;
                            }
                            else
                            {
                                isConflictingReceipt =
                                    intent.BoundCriticalStrikeRank != 0 ||
                                    intent.BoundOverdriveRank != 0 ||
                                    (existingReceipt.IsCorrect &&
                                     existingReceipt.TransitionResult?.RequestedAttackDamage != intent.EffectiveAttackDamage);
                            }
                        }

                        if (isConflictingReceipt)
                        {
                            throw new InvalidOperationException(
                                $"Conflicting receipt already exists for submission '{intent.SubmissionId}'.");
                        }

                        using (var cleanPendingCmd = _connection.CreateCommand())
                        {
                            cleanPendingCmd.Transaction = transaction;
                            cleanPendingCmd.CommandText = "DELETE FROM gameplay_pending_intent WHERE submission_id = @id;";
                            cleanPendingCmd.Parameters.AddWithValue("@id", intent.SubmissionId);
                            await cleanPendingCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }

                        transaction.Commit();
                        return existingReceipt;
                    }
                }

                // 2. Validate progression reset epoch and load progression state
                long currentEpoch;
                long currentRevision;
                long currentCumulativeXp;
                int currentPlayerLevel;
                int currentEarnedSp;
                int currentSpentSp;
                int currentAvailableSp;
                int currentAttackRank;
                int currentFirewallRank;
                int currentConsecutiveEarlyDefeats;
                bool isBeginnerAssistanceActive;
                int currentActivatedFirewallRank;
                int currentCriticalStrikeRank;
                int currentOverdriveRank;
                using (var progCmd = _connection.CreateCommand())
                {
                    progCmd.Transaction = transaction;
                    progCmd.CommandText = @"
                        SELECT reset_epoch, store_revision, cumulative_xp, player_level,
                               earned_sp, spent_sp, available_sp, attack_rank,
                               firewall_rank, consecutive_early_defeats, is_beginner_assistance_active,
                               activated_firewall_rank,
                               critical_strike_rank, typeof(critical_strike_rank) AS critical_strike_rank_type,
                               overdrive_rank, typeof(overdrive_rank) AS overdrive_rank_type
                        FROM gameplay_progression
                        WHERE id = 1;
                    ";
                    using var progReader = await progCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await progReader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("Missing progression row in 'gameplay_progression'.");
                    }
                    currentEpoch = progReader.GetInt64(0);
                    currentRevision = progReader.GetInt64(1);
                    currentCumulativeXp = progReader.GetInt64(2);
                    currentPlayerLevel = progReader.GetInt32(3);
                    currentEarnedSp = progReader.GetInt32(4);
                    currentSpentSp = progReader.GetInt32(5);
                    currentAvailableSp = progReader.GetInt32(6);
                    currentAttackRank = progReader.GetInt32(7);
                    currentFirewallRank = progReader.GetInt32(8);
                    currentConsecutiveEarlyDefeats = progReader.GetInt32(9);
                    isBeginnerAssistanceActive = progReader.GetInt32(10) == 1;
                    currentActivatedFirewallRank = progReader.GetInt32(11);
                    currentCriticalStrikeRank = (int)ReadModifierSnapshotInteger(
                        progReader,
                        "critical_strike_rank",
                        0,
                        CyberDefenseCriticalStrikePolicy.MaxRank);
                    currentOverdriveRank = (int)ReadModifierSnapshotInteger(
                        progReader,
                        "overdrive_rank",
                        0,
                        CyberDefenseOverdrivePolicy.MaxRank);
                }

                if (intent.ResetEpoch != currentEpoch)
                {
                    throw new InvalidOperationException(
                        $"Pending intent reset epoch {intent.ResetEpoch} does not match current store reset epoch {currentEpoch}.");
                }

                int maxEntitledDamage = CyberDefenseAttackUpgradePolicy.GetAttackDamage(currentAttackRank);
                if (intent.EffectiveAttackDamage < 1 || intent.EffectiveAttackDamage > maxEntitledDamage)
                {
                    throw new InvalidOperationException(
                        $"Attempt effective attack damage {intent.EffectiveAttackDamage} exceeds authoritative entitlement {maxEntitledDamage} for attack rank {currentAttackRank}.");
                }

                var pendingForEpoch = new List<CyberDefensePendingIntentRecord>();
                using (var pendingCmd = _connection.CreateCommand())
                {
                    pendingCmd.Transaction = transaction;
                    pendingCmd.CommandText = @"
                        SELECT submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at, effective_attack_damage,
                               bound_critical_strike_rank, bound_overdrive_rank,
                               typeof(bound_critical_strike_rank) AS bound_critical_strike_rank_type,
                               typeof(bound_overdrive_rank) AS bound_overdrive_rank_type,
                               practice_position,
                               typeof(practice_position) AS practice_position_type
                        FROM gameplay_pending_intent
                        WHERE reset_epoch = @epoch
                        ORDER BY created_at ASC, submission_id ASC;
                    ";
                    pendingCmd.Parameters.AddWithValue("@epoch", currentEpoch);
                    using var pendingReader = await pendingCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    while (await pendingReader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        pendingForEpoch.Add(ReadPendingIntent(pendingReader));
                    }
                }

                ValidateAndSortPendingIntents(pendingForEpoch);

                int persistedIndex = -1;
                CyberDefensePendingIntentRecord? persistedIntent = null;
                for (int i = 0; i < pendingForEpoch.Count; i++)
                {
                    if (string.Equals(pendingForEpoch[i].SubmissionId, intent.SubmissionId, StringComparison.Ordinal))
                    {
                        persistedIndex = i;
                        persistedIntent = pendingForEpoch[i];
                        break;
                    }
                }

                if (persistedIntent is not null)
                {
                    if (!HasMatchingPendingIntentCorePayload(persistedIntent, intent))
                    {
                        throw new InvalidOperationException(
                            $"Conflicting pending intent already staged for submission '{intent.SubmissionId}'.");
                    }

                    if (persistedIntent.PracticePosition.HasValue)
                    {
                        if (!intent.PracticePosition.HasValue || intent.PracticePosition.Value != persistedIntent.PracticePosition.Value)
                        {
                            throw new InvalidOperationException(
                                $"Pending intent practice position for submission '{intent.SubmissionId}' does not match the authoritative staged intent.");
                        }
                    }
                    else if (intent.PracticePosition.HasValue)
                    {
                        persistedIntent = persistedIntent.WithPracticePosition(intent.PracticePosition.Value);
                        pendingForEpoch[persistedIndex] = persistedIntent;
                        ValidateAndSortPendingIntents(pendingForEpoch);
                    }
                    else if (pendingForEpoch.Exists(p => p.PracticePosition.HasValue))
                    {
                        throw new InvalidOperationException(
                            $"Cannot apply legacy unpositioned pending intent '{intent.SubmissionId}' while positioned pending intents exist without reconciling its authoritative PracticePosition.");
                    }

                    if (!string.Equals(pendingForEpoch[0].SubmissionId, intent.SubmissionId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Cannot apply pending intent '{intent.SubmissionId}' out of order while earlier pending intent '{pendingForEpoch[0].SubmissionId}' remains staged.");
                    }
                }
                else
                {
                    if (intent.BoundCriticalStrikeRank != 0 || intent.BoundOverdriveRank != 0)
                    {
                        throw new InvalidOperationException(
                            $"Cannot apply unstaged attempt '{intent.SubmissionId}' with nonzero question-bound modifier ranks " +
                            $"(CriticalStrike={intent.BoundCriticalStrikeRank}, Overdrive={intent.BoundOverdriveRank}) without a durably staged pending intent.");
                    }

                    if (pendingForEpoch.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"Cannot apply unstaged attempt '{intent.SubmissionId}' while earlier pending intent '{pendingForEpoch[0].SubmissionId}' remains staged.");
                    }
                }

                int boundCriticalStrikeRank = persistedIntent?.BoundCriticalStrikeRank ?? intent.BoundCriticalStrikeRank;
                int boundOverdriveRank = persistedIntent?.BoundOverdriveRank ?? intent.BoundOverdriveRank;
                if (boundCriticalStrikeRank > currentCriticalStrikeRank)
                {
                    throw new InvalidOperationException(
                        $"Attempt question-bound Critical Strike rank {boundCriticalStrikeRank} exceeds owned Critical Strike rank {currentCriticalStrikeRank}.");
                }

                if (boundOverdriveRank > currentOverdriveRank)
                {
                    throw new InvalidOperationException(
                        $"Attempt question-bound Overdrive rank {boundOverdriveRank} exceeds owned Overdrive rank {currentOverdriveRank}.");
                }

                // 3. Load and validate active run state and modifier counters
                CyberDefenseRunState currentRunState;
                CyberDefenseCombatModifierState currentModifierState;
                using (var runCmd = _connection.CreateCommand())
                {
                    runCmd.Transaction = transaction;
                    runCmd.CommandText = @"
                        SELECT sector, opponent_index, opponent_current_hp, player_current_hp,
                               opponent_executed_attacks, opponent_encounter_provenance,
                               shield_capacity, active_shields, shield_recharge_units,
                               critical_correct_attack_count, typeof(critical_correct_attack_count) AS critical_correct_attack_count_type,
                               overdrive_streak, typeof(overdrive_streak) AS overdrive_streak_type,
                               overdrive_remaining_bonus_attacks, typeof(overdrive_remaining_bonus_attacks) AS overdrive_remaining_bonus_attacks_type
                        FROM gameplay_run_state WHERE id = 1;
                    ";
                    using var runReader = await runCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await runReader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("Missing active run state row in 'gameplay_run_state'.");
                    }
                    int sector = runReader.GetInt32(0);
                    int oppIndex = runReader.GetInt32(1);
                    int oppHp = runReader.GetInt32(2);
                    int playerHp = runReader.GetInt32(3);
                    int oppExecutedAttacks = runReader.GetInt32(4);
                    string oppProvStr = runReader.GetString(5);
                    int shieldCapacity = runReader.GetInt32(6);
                    int activeShields = runReader.GetInt32(7);
                    int shieldRechargeUnits = runReader.GetInt32(8);

                    int criticalCorrectAttackCount = (int)ReadModifierSnapshotInteger(
                        runReader,
                        "critical_correct_attack_count",
                        0,
                        int.MaxValue);
                    int overdriveStreak = (int)ReadModifierSnapshotInteger(
                        runReader,
                        "overdrive_streak",
                        0,
                        int.MaxValue);
                    int overdriveRemainingBonusAttacks = (int)ReadModifierSnapshotInteger(
                        runReader,
                        "overdrive_remaining_bonus_attacks",
                        0,
                        CyberDefenseOverdrivePolicy.Rank1BonusAttackCount);

                    if (currentCriticalStrikeRank == 0 && criticalCorrectAttackCount != 0)
                    {
                        throw new InvalidOperationException(
                            "Existing database has non-zero critical_correct_attack_count while critical_strike_rank is 0.");
                    }

                    if (currentOverdriveRank == 0 && (overdriveStreak != 0 || overdriveRemainingBonusAttacks != 0))
                    {
                        throw new InvalidOperationException(
                            "Existing database has non-zero Overdrive counters while overdrive_rank is 0.");
                    }

                    if (overdriveRemainingBonusAttacks == 0
                        ? overdriveStreak > 4
                        : overdriveStreak != CyberDefenseOverdrivePolicy.Rank1BonusAttackCount - overdriveRemainingBonusAttacks)
                    {
                        throw new InvalidOperationException(
                            $"Existing database has incompatible Overdrive state: overdrive_streak={overdriveStreak}, overdrive_remaining_bonus_attacks={overdriveRemainingBonusAttacks}.");
                    }

                    if (!Enum.TryParse<OpponentEncounterProvenance>(oppProvStr, ignoreCase: false, out var oppProv))
                    {
                        throw new InvalidOperationException($"Invalid opponent_encounter_provenance '{oppProvStr}' in gameplay_run_state.");
                    }
                    currentRunState = CyberDefenseRunState.Rehydrate(
                        sector, oppIndex, oppHp, playerHp, oppExecutedAttacks, oppProv,
                        shieldCapacity, activeShields, shieldRechargeUnits);
                    currentModifierState = new CyberDefenseCombatModifierState(
                        criticalCorrectAttackCount,
                        overdriveStreak,
                        overdriveRemainingBonusAttacks);
                }

                var now = DateTimeOffset.UtcNow;
                CyberDefenseReceiptRecord receipt;

                if (!intent.IsEligible)
                {
                    receipt = CyberDefenseReceiptRecord.CreateCalmModeSuppressed(
                        intent.SubmissionId,
                        intent.FactId,
                        intent.IsCorrect,
                        intent.IsEligible,
                        intent.ResponseLatencyMs,
                        intent.ResetEpoch,
                        now);

                    using (var insertReceiptCmd = _connection.CreateCommand())
                    {
                        insertReceiptCmd.Transaction = transaction;
                        InsertReceiptRecord(insertReceiptCmd, receipt);
                        await insertReceiptCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    using (var deletePendingCmd = _connection.CreateCommand())
                    {
                        deletePendingCmd.Transaction = transaction;
                        deletePendingCmd.CommandText = "DELETE FROM gameplay_pending_intent WHERE submission_id = @id;";
                        deletePendingCmd.Parameters.AddWithValue("@id", intent.SubmissionId);
                        await deletePendingCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    transaction.Commit();
                    return receipt;
                }

                // 4. Applied Combat Attempt
                int baseAttackDamage = persistedIntent?.EffectiveAttackDamage ?? intent.EffectiveAttackDamage;
                var modifierOutcome = CyberDefenseCombatModifierTransition.ResolveAttempt(
                    currentModifierState,
                    intent.IsCorrect,
                    boundCriticalStrikeRank,
                    boundOverdriveRank);
                int composedAttackDamage = CyberDefenseDamageCompositionPolicy.GetEffectiveDamage(
                    baseAttackDamage,
                    modifierOutcome.CriticalBonusPercent,
                    modifierOutcome.OverdriveBonusPercent);

                var transition = CyberDefenseStateMachine.ApplyAttempt(
                    currentRunState,
                    intent.IsCorrect,
                    effectiveAttackDamage: composedAttackDamage);
                var nextModifierState = transition.IsGameOver
                    ? CyberDefenseCombatModifierTransition.ResetRunModifiers()
                    : modifierOutcome.NextState;

                int nextConsecutiveEarlyDefeats = currentConsecutiveEarlyDefeats;
                bool nextAssistanceActive = isBeginnerAssistanceActive;

                if (transition.IsGameOver)
                {
                    bool sector1BossReachedAtGameOver = currentRunState.Sector == 1 &&
                        currentRunState.CurrentOpponent.Kind == OpponentKind.Boss;

                    nextConsecutiveEarlyDefeats = CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterGameOver(
                        currentConsecutiveEarlyDefeats,
                        currentRunState.Sector,
                        sector1BossReachedAtGameOver);

                    bool shouldActivate = CyberDefenseBeginnerAssistancePolicy.IsEligibleForActivation(
                        nextConsecutiveEarlyDefeats,
                        sector1BossReachedAtGameOver,
                        currentFirewallRank,
                        isBeginnerAssistanceActive);

                    if (shouldActivate)
                    {
                        nextAssistanceActive = true;

                        var assistedRebootState = CyberDefenseRunState.InitialRun(
                            shieldCapacity: CyberDefenseBeginnerAssistancePolicy.EmergencyShieldCount);

                        transition = new CyberDefenseCombatTransitionResult(
                            transition.IsCorrect,
                            transition.RequestedAttackDamage,
                            transition.AppliedOpponentDamage,
                            transition.ExcessOpponentDamage,
                            transition.IncomingEnemyDamage,
                            transition.AppliedPlayerDamage,
                            transition.ExcessEnemyDamage,
                            transition.PotentialHealing,
                            transition.AppliedHealing,
                            transition.IsOpponentDefeated,
                            transition.IsSectorCompleted,
                            transition.IsGameOver,
                            assistedRebootState,
                            transition.TerminalSnapshot,
                            transition.ExecutedAttacksOnDefeatedOpponent,
                            transition.DefeatedOpponentProvenance,
                            transition.IsShieldAbsorbed,
                            transition.AbsorbedEnemyDamage);
                    }
                }
                else
                {
                    bool sector1Cleared = transition.IsSectorCompleted && currentRunState.Sector == 1;
                    if (sector1Cleared)
                    {
                        if (CyberDefenseBeginnerAssistancePolicy.ShouldDeactivate(isBeginnerAssistanceActive, sector1Cleared: true, currentFirewallRank) || isBeginnerAssistanceActive)
                        {
                            nextAssistanceActive = false;
                        }

                        nextConsecutiveEarlyDefeats = 0;

                        if (currentActivatedFirewallRank == 0)
                        {
                            var nextStateWithoutEmergencyShield = CyberDefenseRunState.CreateActive(
                                transition.NextState.Sector,
                                transition.NextState.OpponentIndex,
                                transition.NextState.PlayerCurrentHp,
                                transition.NextState.CurrentOpponent,
                                shieldCapacity: 0,
                                activeShields: 0,
                                shieldRechargeUnits: 0);

                            transition = new CyberDefenseCombatTransitionResult(
                                transition.IsCorrect,
                                transition.RequestedAttackDamage,
                                transition.AppliedOpponentDamage,
                                transition.ExcessOpponentDamage,
                                transition.IncomingEnemyDamage,
                                transition.AppliedPlayerDamage,
                                transition.ExcessEnemyDamage,
                                transition.PotentialHealing,
                                transition.AppliedHealing,
                                transition.IsOpponentDefeated,
                                transition.IsSectorCompleted,
                                transition.IsGameOver,
                                nextStateWithoutEmergencyShield,
                                transition.TerminalSnapshot,
                                transition.ExecutedAttacksOnDefeatedOpponent,
                                transition.DefeatedOpponentProvenance,
                                transition.IsShieldAbsorbed,
                                transition.AbsorbedEnemyDamage);
                        }
                    }
                    else
                    {
                        bool reachedOrAtSector1Boss = (currentRunState.Sector == 1 && currentRunState.CurrentOpponent.Kind == OpponentKind.Boss) ||
                            (currentRunState.Sector == 1 && transition.NextState.CurrentOpponent.Kind == OpponentKind.Boss);

                        if (reachedOrAtSector1Boss)
                        {
                            nextConsecutiveEarlyDefeats = CyberDefenseBeginnerAssistancePolicy.GetConsecutiveEarlyDefeatsAfterSector1BossReached(currentConsecutiveEarlyDefeats);
                        }
                    }
                }

                if (CyberDefenseBeginnerAssistancePolicy.ShouldDeactivate(nextAssistanceActive, sector1Cleared: false, currentFirewallRank))
                {
                    nextAssistanceActive = false;
                }

                int awardedXp;
                if (transition.IsOpponentDefeated)
                {
                    int attackCount = transition.ExecutedAttacksOnDefeatedOpponent
                        ?? throw new InvalidOperationException("Opponent defeated but executed attacks count is null.");
                    awardedXp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
                        intent.IsCorrect,
                        opponentDefeated: true,
                        currentRunState.CurrentOpponent.Kind,
                        attackCount);
                }
                else
                {
                    awardedXp = CyberDefenseEconomyPolicy.CalculateAttemptTotalXp(
                        intent.IsCorrect,
                        opponentDefeated: false,
                        currentRunState.CurrentOpponent.Kind,
                        1);
                }

                long newCumulativeXp = checked(currentCumulativeXp + awardedXp);
                int newPlayerLevel = CyberDefenseLevelPolicy.GetLevel(newCumulativeXp);
                if (newPlayerLevel < currentPlayerLevel)
                {
                    throw new InvalidOperationException(
                        $"Player level cannot decrease from {currentPlayerLevel} to {newPlayerLevel}.");
                }

                int gainedLevels = checked(newPlayerLevel - currentPlayerLevel);
                int newEarnedSp = checked(currentEarnedSp + gainedLevels);
                int newAvailableSp = checked(currentAvailableSp + gainedLevels);
                int newSpentSp = currentSpentSp;
                int newAttackRank = currentAttackRank;

                if (newEarnedSp != checked(newSpentSp + newAvailableSp))
                {
                    throw new InvalidOperationException(
                        $"Earned SP invariant violated: {newEarnedSp} != {newSpentSp} + {newAvailableSp}.");
                }

                using (var updateRunCmd = _connection.CreateCommand())
                {
                    updateRunCmd.Transaction = transaction;
                    updateRunCmd.CommandText = @"
                        UPDATE gameplay_run_state
                        SET sector = @sector,
                            opponent_index = @opponentIndex,
                            opponent_current_hp = @opponentHp,
                            player_current_hp = @playerHp,
                            opponent_executed_attacks = @opponentExecutedAttacks,
                            opponent_encounter_provenance = @opponentEncounterProvenance,
                            shield_capacity = @shieldCapacity,
                            active_shields = @activeShields,
                            shield_recharge_units = @shieldRechargeUnits,
                            critical_correct_attack_count = @criticalCorrectAttackCount,
                            overdrive_streak = @overdriveStreak,
                            overdrive_remaining_bonus_attacks = @overdriveRemainingBonusAttacks,
                            updated_at = @now
                        WHERE id = 1;
                    ";
                    updateRunCmd.Parameters.AddWithValue("@sector", transition.NextState.Sector);
                    updateRunCmd.Parameters.AddWithValue("@opponentIndex", transition.NextState.OpponentIndex);
                    updateRunCmd.Parameters.AddWithValue("@opponentHp", transition.NextState.CurrentOpponent.CurrentHp);
                    updateRunCmd.Parameters.AddWithValue("@playerHp", transition.NextState.PlayerCurrentHp);
                    updateRunCmd.Parameters.AddWithValue("@opponentExecutedAttacks", transition.NextState.CurrentOpponent.ExecutedAttacks);
                    updateRunCmd.Parameters.AddWithValue("@opponentEncounterProvenance", transition.NextState.CurrentOpponent.Provenance.ToString());
                    updateRunCmd.Parameters.AddWithValue("@shieldCapacity", transition.NextState.ShieldCapacity);
                    updateRunCmd.Parameters.AddWithValue("@activeShields", transition.NextState.ActiveShields);
                    updateRunCmd.Parameters.AddWithValue("@shieldRechargeUnits", transition.NextState.ShieldRechargeUnits);
                    updateRunCmd.Parameters.AddWithValue("@criticalCorrectAttackCount", nextModifierState.CriticalCorrectAttackCount);
                    updateRunCmd.Parameters.AddWithValue("@overdriveStreak", nextModifierState.OverdriveStreak);
                    updateRunCmd.Parameters.AddWithValue("@overdriveRemainingBonusAttacks", nextModifierState.OverdriveRemainingBonusAttacks);
                    updateRunCmd.Parameters.AddWithValue("@now", now.ToString("O"));
                    await updateRunCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                var newRevision = checked(currentRevision + 1);
                using (var updateProgCmd = _connection.CreateCommand())
                {
                    updateProgCmd.Transaction = transaction;
                    updateProgCmd.CommandText = @"
                        UPDATE gameplay_progression
                        SET store_revision = @rev,
                            cumulative_xp = @cumulativeXp,
                            player_level = @playerLevel,
                            earned_sp = @earnedSp,
                            spent_sp = @spentSp,
                            available_sp = @availableSp,
                            attack_rank = @attackRank,
                            consecutive_early_defeats = @consecutiveEarlyDefeats,
                            is_beginner_assistance_active = @isBeginnerAssistanceActive,
                            updated_at = @now
                        WHERE id = 1;
                    ";
                    updateProgCmd.Parameters.AddWithValue("@rev", newRevision);
                    updateProgCmd.Parameters.AddWithValue("@cumulativeXp", newCumulativeXp);
                    updateProgCmd.Parameters.AddWithValue("@playerLevel", newPlayerLevel);
                    updateProgCmd.Parameters.AddWithValue("@earnedSp", newEarnedSp);
                    updateProgCmd.Parameters.AddWithValue("@spentSp", newSpentSp);
                    updateProgCmd.Parameters.AddWithValue("@availableSp", newAvailableSp);
                    updateProgCmd.Parameters.AddWithValue("@attackRank", newAttackRank);
                    updateProgCmd.Parameters.AddWithValue("@consecutiveEarlyDefeats", nextConsecutiveEarlyDefeats);
                    updateProgCmd.Parameters.AddWithValue("@isBeginnerAssistanceActive", nextAssistanceActive ? 1 : 0);
                    updateProgCmd.Parameters.AddWithValue("@now", now.ToString("O"));
                    await updateProgCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                receipt = CyberDefenseReceiptRecord.CreateApplied(
                    intent.SubmissionId,
                    intent.FactId,
                    intent.IsCorrect,
                    intent.IsEligible,
                    intent.ResponseLatencyMs,
                    intent.ResetEpoch,
                    now,
                    transition,
                    awardedXp: awardedXp,
                    playerLevelAfter: newPlayerLevel,
                    cumulativeXpAfter: newCumulativeXp,
                    baseAttackDamage: baseAttackDamage,
                    boundCriticalStrikeRank: boundCriticalStrikeRank,
                    boundOverdriveRank: boundOverdriveRank,
                    criticalBonusPercent: modifierOutcome.CriticalBonusPercent,
                    overdriveBonusPercent: modifierOutcome.OverdriveBonusPercent);

                using (var insertReceiptCmd = _connection.CreateCommand())
                {
                    insertReceiptCmd.Transaction = transaction;
                    InsertReceiptRecord(insertReceiptCmd, receipt);
                    await insertReceiptCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                using (var deletePendingCmd = _connection.CreateCommand())
                {
                    deletePendingCmd.Transaction = transaction;
                    deletePendingCmd.CommandText = "DELETE FROM gameplay_pending_intent WHERE submission_id = @id;";
                    deletePendingCmd.Parameters.AddWithValue("@id", intent.SubmissionId);
                    await deletePendingCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                transaction.Commit();
                return receipt;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }, cancellationToken);
    }

    private static async Task EnsureNoConflictingPracticePositionInEpochAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long resetEpoch,
        long practicePosition,
        string submissionId,
        CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            SELECT submission_id, practice_position, typeof(practice_position) AS practice_position_type
            FROM gameplay_pending_intent
            WHERE reset_epoch = @epoch;
        ";
        cmd.Parameters.AddWithValue("@epoch", resetEpoch);
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            string otherSubId = reader.GetString(0);
            long? existingPos = ReadPendingIntentPracticePosition(reader, 1, 2);
            if (!string.Equals(otherSubId, submissionId, StringComparison.Ordinal) &&
                existingPos.HasValue &&
                existingPos.Value == practicePosition)
            {
                throw new InvalidOperationException(
                    $"Conflicting duplicate practice_position {practicePosition} already staged in 'gameplay_pending_intent' for epoch {resetEpoch} (submission '{otherSubId}').");
            }
        }
    }

    private static void ValidateAndSortPendingIntents(List<CyberDefensePendingIntentRecord> intents)
    {
        HashSet<long>? seenPositions = null;
        for (int i = 0; i < intents.Count; i++)
        {
            if (intents[i].PracticePosition is long pos)
            {
                seenPositions ??= new HashSet<long>();
                if (!seenPositions.Add(pos))
                {
                    throw new InvalidOperationException(
                        $"Conflicting duplicate practice_position {pos} in 'gameplay_pending_intent' for epoch {intents[i].ResetEpoch}.");
                }
            }
        }

        intents.Sort(ComparePendingIntentsDeterministically);
    }

    internal static int ComparePendingIntentsDeterministically(
        CyberDefensePendingIntentRecord left,
        CyberDefensePendingIntentRecord right)
    {
        if (left.PracticePosition.HasValue && right.PracticePosition.HasValue)
        {
            int positionComparison = left.PracticePosition.Value.CompareTo(right.PracticePosition.Value);
            if (positionComparison != 0)
            {
                return positionComparison;
            }

            return StringComparer.Ordinal.Compare(left.SubmissionId, right.SubmissionId);
        }

        // Unknown-position legacy rows must never be placed after newer positioned rows.
        if (!left.PracticePosition.HasValue && right.PracticePosition.HasValue)
        {
            return -1;
        }

        if (left.PracticePosition.HasValue && !right.PracticePosition.HasValue)
        {
            return 1;
        }

        int timeComparison = left.CreatedAt.CompareTo(right.CreatedAt);
        if (timeComparison != 0)
        {
            return timeComparison;
        }

        return StringComparer.Ordinal.Compare(left.SubmissionId, right.SubmissionId);
    }

    public static CyberDefensePendingIntentRecord ReadPendingIntent(SqliteDataReader reader)
    {
        string subId = reader.GetString(0);
        string factId = reader.GetString(1);
        bool isCorrect = reader.GetInt32(2) == 1;
        bool isEligible = reader.GetInt32(3) == 1;
        long latency = reader.GetInt64(4);
        long epoch = reader.GetInt64(5);
        string createdAtStr = reader.GetString(6);

        if (!DateTimeOffset.TryParse(createdAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            throw new InvalidOperationException($"Invalid created_at timestamp '{createdAtStr}' in pending intent table.");
        }

        int effectiveAttackDamage = 1;
        if (reader.FieldCount > 7 && !reader.IsDBNull(7))
        {
            effectiveAttackDamage = reader.GetInt32(7);
            if (effectiveAttackDamage < 1)
            {
                throw new InvalidOperationException($"Invalid effective_attack_damage '{effectiveAttackDamage}' in pending intent table: must be >= 1.");
            }
        }

        int boundCriticalStrikeRank = 0;
        if (reader.FieldCount > 8)
        {
            int? criticalTypeOrdinal = reader.FieldCount > 10 ? 10 : null;
            boundCriticalStrikeRank = ReadPendingIntentRank(
                reader,
                valueOrdinal: 8,
                typeOrdinal: criticalTypeOrdinal,
                column: "bound_critical_strike_rank",
                maxRank: CyberDefenseCriticalStrikePolicy.MaxRank);
        }

        int boundOverdriveRank = 0;
        if (reader.FieldCount > 9)
        {
            int? overdriveTypeOrdinal = reader.FieldCount > 11 ? 11 : null;
            boundOverdriveRank = ReadPendingIntentRank(
                reader,
                valueOrdinal: 9,
                typeOrdinal: overdriveTypeOrdinal,
                column: "bound_overdrive_rank",
                maxRank: CyberDefenseOverdrivePolicy.MaxRank);
        }

        long? practicePosition = null;
        if (reader.FieldCount > 12)
        {
            int? positionTypeOrdinal = reader.FieldCount > 13 ? 13 : null;
            practicePosition = ReadPendingIntentPracticePosition(
                reader,
                valueOrdinal: 12,
                typeOrdinal: positionTypeOrdinal);
        }

        return new CyberDefensePendingIntentRecord(
            subId,
            factId,
            isCorrect,
            isEligible,
            latency,
            epoch,
            createdAt,
            effectiveAttackDamage,
            boundCriticalStrikeRank,
            boundOverdriveRank,
            practicePosition);
    }

    private static long? ReadPendingIntentPracticePosition(
        SqliteDataReader reader,
        int valueOrdinal,
        int? typeOrdinal)
    {
        if (typeOrdinal is int explicitTypeOrdinal)
        {
            if (reader.IsDBNull(explicitTypeOrdinal))
            {
                return null;
            }

            string storageType = reader.GetString(explicitTypeOrdinal);
            if (string.Equals(storageType, "null", StringComparison.Ordinal))
            {
                return null;
            }

            if (!string.Equals(storageType, "integer", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Pending intent table requires NULL or INTEGER storage for 'practice_position'.");
            }
        }

        if (reader.IsDBNull(valueOrdinal))
        {
            return null;
        }

        if (reader.GetFieldType(valueOrdinal) != typeof(long))
        {
            throw new InvalidOperationException(
                "Pending intent table requires NULL or INTEGER storage for 'practice_position'.");
        }

        long value = reader.GetInt64(valueOrdinal);
        if (value < 1)
        {
            throw new InvalidOperationException(
                $"Existing database has invalid value {value} in 'gameplay_pending_intent.practice_position': expected >= 1.");
        }

        return value;
    }

    private static int ReadPendingIntentRank(
        SqliteDataReader reader,
        int valueOrdinal,
        int? typeOrdinal,
        string column,
        int maxRank)
    {
        if (typeOrdinal is int explicitTypeOrdinal)
        {
            if (reader.IsDBNull(explicitTypeOrdinal) || reader.GetString(explicitTypeOrdinal) != "integer")
            {
                throw new InvalidOperationException(
                    $"Pending intent table requires INTEGER storage for '{column}'.");
            }
        }

        if (reader.IsDBNull(valueOrdinal) || reader.GetFieldType(valueOrdinal) != typeof(long))
        {
            throw new InvalidOperationException(
                $"Pending intent table requires INTEGER storage for '{column}'.");
        }

        long value = reader.GetInt64(valueOrdinal);
        if (value < 0 || value > maxRank)
        {
            throw new InvalidOperationException(
                $"Existing database has invalid value {value} in 'gameplay_pending_intent.{column}': expected 0 to {maxRank}.");
        }

        return (int)value;
    }

    private static bool HasMatchingPendingIntentCorePayload(
        CyberDefensePendingIntentRecord existing,
        CyberDefensePendingIntentRecord candidate)
    {
        return string.Equals(existing.FactId, candidate.FactId, StringComparison.Ordinal) &&
               existing.IsCorrect == candidate.IsCorrect &&
               existing.IsEligible == candidate.IsEligible &&
               existing.ResponseLatencyMs == candidate.ResponseLatencyMs &&
               existing.ResetEpoch == candidate.ResetEpoch &&
               existing.EffectiveAttackDamage == candidate.EffectiveAttackDamage &&
               existing.BoundCriticalStrikeRank == candidate.BoundCriticalStrikeRank &&
               existing.BoundOverdriveRank == candidate.BoundOverdriveRank;
    }

    public Task<GameplayResetIntentRecord> GetResetIntentAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithConnectionGateAsync(async ct =>
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT is_pending, current_epoch, target_epoch, created_at, updated_at FROM gameplay_reset_intent WHERE id = 1;";
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return ReadResetIntent(reader);
            }

            throw new InvalidOperationException("No reset intent row found in 'gameplay_reset_intent'.");
        }, cancellationToken);
    }

    public Task<GameplayResetIntentRecord> BeginOrGetResetIntentAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteWithConnectionGateAsync(async ct =>
        {
            using (var pragmaCmd = _connection!.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA synchronous = FULL;";
                await pragmaCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            using var transaction = _connection.BeginTransaction();
            try
            {
                long currentEpoch;
                long targetEpoch;
                bool isPending;
                DateTimeOffset? createdAt = null;

                using (var selectCmd = _connection.CreateCommand())
                {
                    selectCmd.Transaction = transaction;
                    selectCmd.CommandText = "SELECT is_pending, current_epoch, target_epoch, created_at, updated_at FROM gameplay_reset_intent WHERE id = 1;";
                    using var reader = await selectCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("Missing reset intent row in 'gameplay_reset_intent'.");
                    }
                    isPending = reader.GetInt32(0) == 1;
                    currentEpoch = reader.GetInt64(1);
                    targetEpoch = reader.GetInt64(2);
                    var createdStr = reader.IsDBNull(3) ? null : reader.GetString(3);
                    if (!string.IsNullOrEmpty(createdStr) && DateTimeOffset.TryParse(createdStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedCreated))
                    {
                        createdAt = parsedCreated;
                    }
                }

                if (isPending)
                {
                    transaction.Commit();
                    using (var restoreCmd = _connection.CreateCommand())
                    {
                        restoreCmd.CommandText = "PRAGMA synchronous = NORMAL;";
                        await restoreCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }
                    return new GameplayResetIntentRecord(true, currentEpoch, targetEpoch, createdAt, DateTimeOffset.UtcNow);
                }

                var now = DateTimeOffset.UtcNow;
                targetEpoch = currentEpoch + 1;

                using (var updateCmd = _connection.CreateCommand())
                {
                    updateCmd.Transaction = transaction;
                    updateCmd.CommandText = @"
                        UPDATE gameplay_reset_intent
                        SET is_pending = 1,
                            target_epoch = @targetEpoch,
                            created_at = @now,
                            updated_at = @now
                        WHERE id = 1;
                    ";
                    updateCmd.Parameters.AddWithValue("@targetEpoch", targetEpoch);
                    updateCmd.Parameters.AddWithValue("@now", now.ToString("O"));
                    await updateCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                transaction.Commit();

                using (var restoreCmd = _connection.CreateCommand())
                {
                    restoreCmd.CommandText = "PRAGMA synchronous = NORMAL;";
                    await restoreCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                return new GameplayResetIntentRecord(true, currentEpoch, targetEpoch, now, now);
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                try
                {
                    using var restoreCmd = _connection.CreateCommand();
                    restoreCmd.CommandText = "PRAGMA synchronous = NORMAL;";
                    restoreCmd.ExecuteNonQuery();
                }
                catch { }
                throw;
            }
        }, cancellationToken);
    }

    public async Task ResetGameplayStateAsync(long targetEpoch, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetEpoch);

        CyberDefenseAttackUpgradeSnapshot? resetSnapshot = null;
        CyberDefenseFirewallUpgradeSnapshot? resetFirewallSnapshot = null;

        await ExecuteWithConnectionGateAsync(async ct =>
        {
            using var transaction = _connection!.BeginTransaction();
            try
            {
                using (var checkCmd = _connection.CreateCommand())
                {
                    checkCmd.Transaction = transaction;
                    checkCmd.CommandText = "SELECT is_pending, target_epoch FROM gameplay_reset_intent WHERE id = 1;";
                    using var reader = await checkCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("Missing reset intent row in 'gameplay_reset_intent'.");
                    }
                    bool isPending = reader.GetInt32(0) == 1;
                    long storedTargetEpoch = reader.GetInt64(1);
                    if (!isPending || storedTargetEpoch != targetEpoch)
                    {
                        throw new InvalidOperationException(
                            $"Cannot reset gameplay state to epoch {targetEpoch}. Active pending reset intent target is {storedTargetEpoch} (is_pending={isPending}).");
                    }
                }

                var now = DateTimeOffset.UtcNow.ToString("O");
                var initialRun = CyberDefenseRunState.InitialRun();

                using (var resetCmd = _connection.CreateCommand())
                {
                    resetCmd.Transaction = transaction;
                    resetCmd.CommandText = @"
                        DELETE FROM gameplay_pending_intent;
                        DELETE FROM gameplay_receipt_ledger;

                        UPDATE gameplay_run_state
                        SET sector = @sector,
                            opponent_index = @opponentIndex,
                            opponent_current_hp = @opponentHp,
                            player_current_hp = @playerHp,
                            opponent_executed_attacks = 0,
                            opponent_encounter_provenance = 'Authoritative',
                            shield_capacity = 0,
                            active_shields = 0,
                            shield_recharge_units = 0,
                            critical_correct_attack_count = 0,
                            overdrive_streak = 0,
                            overdrive_remaining_bonus_attacks = 0,
                            reset_epoch = @targetEpoch,
                            updated_at = @now
                        WHERE id = 1;

                        UPDATE gameplay_progression
                        SET store_revision = 1,
                            reset_epoch = @targetEpoch,
                            cumulative_xp = 0,
                            player_level = 1,
                            earned_sp = 0,
                            spent_sp = 0,
                            available_sp = 0,
                            attack_rank = 0,
                            economy_version = 'ECONOMY_V1',
                            firewall_rank = 0,
                            consecutive_early_defeats = 0,
                            is_beginner_assistance_active = 0,
                            activated_firewall_rank = 0,
                            critical_strike_rank = 0,
                            overdrive_rank = 0,
                            updated_at = @now
                        WHERE id = 1;

                        UPDATE gameplay_schema_info
                        SET value = '1'
                        WHERE key = 'store_revision';

                        UPDATE gameplay_schema_info
                        SET value = @targetEpochStr
                        WHERE key = 'reset_epoch';

                        INSERT OR REPLACE INTO gameplay_schema_info (key, value)
                        VALUES ('economy_version', 'ECONOMY_V1');
                    ";
                    resetCmd.Parameters.AddWithValue("@sector", initialRun.Sector);
                    resetCmd.Parameters.AddWithValue("@opponentIndex", initialRun.OpponentIndex);
                    resetCmd.Parameters.AddWithValue("@opponentHp", initialRun.CurrentOpponent.CurrentHp);
                    resetCmd.Parameters.AddWithValue("@playerHp", initialRun.PlayerCurrentHp);
                    resetCmd.Parameters.AddWithValue("@targetEpoch", targetEpoch);
                    resetCmd.Parameters.AddWithValue("@targetEpochStr", targetEpoch.ToString(CultureInfo.InvariantCulture));
                    resetCmd.Parameters.AddWithValue("@now", now);
                    await resetCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                transaction.Commit();

                resetSnapshot = new CyberDefenseAttackUpgradeSnapshot(
                    currentAttackRank: 0,
                    nextAttackRank: 1,
                    currentBaseDamage: 1,
                    nextBaseDamage: 2,
                    nextRankCost: 1,
                    availableSkillPoints: 0,
                    hasSufficientSkillPoints: false,
                    playerLevel: 1,
                    resetEpoch: targetEpoch,
                    storeRevision: 1);

                resetFirewallSnapshot = new CyberDefenseFirewallUpgradeSnapshot(
                    currentFirewallRank: 0,
                    nextFirewallRank: 1,
                    requiredPlayerLevel: 5,
                    nextRankCost: 3,
                    availableSkillPoints: 0,
                    playerLevel: 1,
                    isLevelUnlocked: false,
                    hasSufficientSkillPoints: false,
                    canPurchaseNextRank: false,
                    resetEpoch: targetEpoch,
                    storeRevision: 1);
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);

        if (resetSnapshot != null)
        {
            NotifyAttackUpgradeSnapshotChanged(resetSnapshot);
        }

        if (resetFirewallSnapshot != null)
        {
            NotifyFirewallUpgradeSnapshotChanged(resetFirewallSnapshot);
        }
    }

    public Task ClearResetIntentAsync(long targetEpoch, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetEpoch);

        CyberDefenseCombatModifierSnapshot? resetBaseline = null;

        var clearTask = ExecuteWithConnectionGateAsync(async ct =>
        {
            using (var pragmaCmd = _connection!.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA synchronous = FULL;";
                await pragmaCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            using var transaction = _connection.BeginTransaction();
            try
            {
                using (var checkCmd = _connection.CreateCommand())
                {
                    checkCmd.Transaction = transaction;
                    checkCmd.CommandText = @"
                        SELECT r.is_pending, r.target_epoch, p.reset_epoch, rs.reset_epoch
                        FROM gameplay_reset_intent r
                        JOIN gameplay_progression p ON p.id = 1
                        JOIN gameplay_run_state rs ON rs.id = 1
                        WHERE r.id = 1;
                    ";
                    using var reader = await checkCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException("Missing reset intent or progression row.");
                    }
                    bool isPending = reader.GetInt32(0) == 1;
                    long storedTargetEpoch = reader.GetInt64(1);
                    long progressionEpoch = reader.GetInt64(2);
                    long runStateEpoch = reader.GetInt64(3);

                    if (!isPending || storedTargetEpoch != targetEpoch)
                    {
                        throw new InvalidOperationException(
                            $"Cannot clear reset intent for epoch {targetEpoch}. Active pending target is {storedTargetEpoch} (is_pending={isPending}).");
                    }

                    if (progressionEpoch != targetEpoch || runStateEpoch != targetEpoch)
                    {
                        throw new InvalidOperationException(
                            $"Cannot clear reset intent before gameplay state has been reset to target epoch {targetEpoch} (progression={progressionEpoch}, runState={runStateEpoch}).");
                    }
                }

                var now = DateTimeOffset.UtcNow.ToString("O");

                using (var updateCmd = _connection.CreateCommand())
                {
                    updateCmd.Transaction = transaction;
                    updateCmd.CommandText = @"
                        UPDATE gameplay_reset_intent
                        SET is_pending = 0,
                            current_epoch = @targetEpoch,
                            target_epoch = @targetEpoch,
                            created_at = NULL,
                            updated_at = @now
                        WHERE id = 1;
                    ";
                    updateCmd.Parameters.AddWithValue("@targetEpoch", targetEpoch);
                    updateCmd.Parameters.AddWithValue("@now", now);
                    await updateCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                // Read the coherent post-reset baseline inside the same transaction (intent is no longer
                // pending here). Publication happens only after commit and after the gate is released.
                CyberDefenseCombatModifierSnapshot? candidateBaseline = null;
                try
                {
                    candidateBaseline = await ReadCombatModifierSnapshotAsync(_connection, transaction, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // An unreadable baseline is never published; coordinators stay conservative.
                }

                transaction.Commit();
                resetBaseline = candidateBaseline;

                using (var restoreCmd = _connection.CreateCommand())
                {
                    restoreCmd.CommandText = "PRAGMA synchronous = NORMAL;";
                    await restoreCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                try
                {
                    using var restoreCmd = _connection.CreateCommand();
                    restoreCmd.CommandText = "PRAGMA synchronous = NORMAL;";
                    restoreCmd.ExecuteNonQuery();
                }
                catch { }
                throw;
            }
        }, cancellationToken);

        return PublishResetBaselineAfterClearAsync(clearTask, () => resetBaseline);
    }

    public static GameplayResetIntentRecord ReadResetIntent(SqliteDataReader reader)
    {
        bool isPending = reader.GetInt32(0) == 1;
        long currentEpoch = reader.GetInt64(1);
        long targetEpoch = reader.GetInt64(2);
        string? createdAtStr = reader.IsDBNull(3) ? null : reader.GetString(3);
        string updatedAtStr = reader.GetString(4);

        DateTimeOffset? createdAt = null;
        if (!string.IsNullOrEmpty(createdAtStr))
        {
            if (!DateTimeOffset.TryParse(createdAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedCreated))
            {
                throw new InvalidOperationException($"Invalid created_at timestamp '{createdAtStr}' in reset intent table.");
            }
            createdAt = parsedCreated;
        }

        if (!DateTimeOffset.TryParse(updatedAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedUpdated))
        {
            throw new InvalidOperationException($"Invalid updated_at timestamp '{updatedAtStr}' in reset intent table.");
        }

        return new GameplayResetIntentRecord(isPending, currentEpoch, targetEpoch, createdAt, parsedUpdated);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        RegisterOperation();
        try
        {
            await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();

                lock (_lock)
                {
                    if (_connection != null)
                    {
                        _connection.Close();
                        _connection.Dispose();
                        SqliteConnection.ClearPool(_connection);
                        _connection = null;
                    }
                    _isInitialized = false;
                }
            }
            finally
            {
                _connectionGate.Release();
            }
        }
        finally
        {
            UnregisterOperation();
        }
    }

    private void CleanupResources()
    {
        lock (_lock)
        {
            if (_resourcesCleanedUp)
            {
                return;
            }
            _resourcesCleanedUp = true;
        }

        bool gateAcquired = false;
        try
        {
            gateAcquired = _connectionGate.Wait(0);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (!gateAcquired)
        {
            // Another operation currently owns the connection gate.
            // In accordance with unified gate ownership, never forcibly destroy the connection.
            // Deferred cleanup will be executed when the active owner unregisters.
            lock (_lock)
            {
                _resourcesCleanedUp = false;
            }
            return;
        }

        try
        {
            lock (_lock)
            {
                if (_connection != null)
                {
                    try
                    {
                        _connection.Dispose();
                    }
                    finally
                    {
                        SqliteConnection.ClearPool(_connection);
                        _connection = null;
                    }
                }
                _isInitialized = false;
            }
        }
        finally
        {
            try
            {
                _connectionGate.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        try
        {
            _connectionGate.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        bool shouldCleanup = false;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            if (_activeOperations == 0)
            {
                shouldCleanup = true;
            }
        }

        if (shouldCleanup)
        {
            AttackUpgradeSnapshotChanged = null;
            FirewallUpgradeSnapshotChanged = null;
            CombatModifierSnapshotChanged = null;
            lock (_notificationLock)
            {
                _lastPublishedEpoch = -1;
                _lastPublishedRevision = -1;
                _lastPublishedFirewallEpoch = -1;
                _lastPublishedFirewallRevision = -1;
                _lastPublishedModifierEpoch = -1;
                _lastPublishedModifierRevision = -1;
            }
            CleanupResources();
        }
    }
}
