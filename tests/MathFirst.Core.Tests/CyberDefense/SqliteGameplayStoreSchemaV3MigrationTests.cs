namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.IO;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SqliteGameplayStoreSchemaV3MigrationTests : IDisposable
{
    private readonly string _testDirectory;

    public SqliteGameplayStoreSchemaV3MigrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_V3_" + Guid.NewGuid().ToString("N"));
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

    private string GetTempDbPath(string name = "gameplay_v3_test")
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

    // 1. Fresh V3 initialization
    [Fact]
    public async Task Case01_FreshV3Initialization_InitializesSchemaV3_WithAuthoritativeProvenance()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
        Assert.Equal("7", (string?)await cmd.ExecuteScalarAsync());

        var run = await store.GetRunStateAsync();
        Assert.Equal(1, run.Sector);
        Assert.Equal(0, run.OpponentIndex);
        Assert.Equal(0, run.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.Authoritative, run.CurrentOpponent.Provenance);
    }

    // 2. Fresh encounter Authoritative provenance
    [Fact]
    public void Case02_FreshEncounter_HasAuthoritativeProvenance_AndZeroAttacks()
    {
        var run = CyberDefenseRunState.InitialRun();
        Assert.Equal(OpponentEncounterProvenance.Authoritative, run.CurrentOpponent.Provenance);
        Assert.Equal(0, run.CurrentOpponent.ExecutedAttacks);
    }

    // 3. Successful attack preserves provenance and increments count
    [Fact]
    public void Case03_SuccessfulAttack_PreservesProvenance_AndIncrementsCount()
    {
        var run = CyberDefenseRunState.InitialRun(); // 2 HP normal opponent
        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        Assert.False(result.IsOpponentDefeated);
        Assert.Equal(1, result.NextState.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.Authoritative, result.NextState.CurrentOpponent.Provenance);
    }

    // 4. Incorrect answer preserves provenance and count
    [Fact]
    public void Case04_IncorrectAnswer_PreservesProvenance_AndCount()
    {
        var run = CyberDefenseRunState.InitialRun();
        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1);

        Assert.False(result.IsOpponentDefeated);
        Assert.Equal(0, result.NextState.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.Authoritative, result.NextState.CurrentOpponent.Provenance);
    }

    // 5. MigratedPartial/UnverifiedLegacy persists across multiple turns
    [Fact]
    public void Case05_UnverifiedLegacy_PersistsAcrossMultipleSurvivingTurns()
    {
        // Sector 1, Boss (index 5) has 12 HP
        var bossOpponent = new OpponentState(
            OpponentKind.Boss, 12, 10, executedAttacks: 2, OpponentEncounterProvenance.UnverifiedLegacy);
        var run = CyberDefenseRunState.CreateActive(1, 5, 100, bossOpponent);

        // Turn 1: Correct attack (1 dmg) -> 9 HP
        var t1 = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);
        Assert.False(t1.IsOpponentDefeated);
        Assert.Equal(3, t1.NextState.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, t1.NextState.CurrentOpponent.Provenance);

        // Turn 2: Incorrect answer -> 9 HP, no count advance
        var t2 = CyberDefenseStateMachine.ApplyAttempt(t1.NextState, isCorrect: false, effectiveAttackDamage: 1);
        Assert.False(t2.IsOpponentDefeated);
        Assert.Equal(3, t2.NextState.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, t2.NextState.CurrentOpponent.Provenance);

        // Turn 3: Correct attack (1 dmg) -> 8 HP
        var t3 = CyberDefenseStateMachine.ApplyAttempt(t2.NextState, isCorrect: true, effectiveAttackDamage: 1);
        Assert.False(t3.IsOpponentDefeated);
        Assert.Equal(4, t3.NextState.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, t3.NextState.CurrentOpponent.Provenance);
    }

    // 6. Defeat carries defeated encounter provenance
    [Fact]
    public void Case06_Defeat_CarriesDefeatedEncounterProvenance()
    {
        var legacyOpponent = new OpponentState(
            OpponentKind.Normal, 2, 1, executedAttacks: 2, OpponentEncounterProvenance.UnverifiedLegacy);
        var runLegacy = CyberDefenseRunState.CreateActive(1, 0, 100, legacyOpponent);

        var resultLegacy = CyberDefenseStateMachine.ApplyAttempt(runLegacy, isCorrect: true, effectiveAttackDamage: 1);
        Assert.True(resultLegacy.IsOpponentDefeated);
        Assert.Equal(3, resultLegacy.ExecutedAttacksOnDefeatedOpponent);
        Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, resultLegacy.DefeatedOpponentProvenance);

        var authOpponent = new OpponentState(
            OpponentKind.Normal, 2, 1, executedAttacks: 1, OpponentEncounterProvenance.Authoritative);
        var runAuth = CyberDefenseRunState.CreateActive(1, 0, 100, authOpponent);

        var resultAuth = CyberDefenseStateMachine.ApplyAttempt(runAuth, isCorrect: true, effectiveAttackDamage: 1);
        Assert.True(resultAuth.IsOpponentDefeated);
        Assert.Equal(2, resultAuth.ExecutedAttacksOnDefeatedOpponent);
        Assert.Equal(OpponentEncounterProvenance.Authoritative, resultAuth.DefeatedOpponentProvenance);
    }

    // 7. Next opponent resets provenance to Authoritative
    [Fact]
    public void Case07_NextOpponent_ResetsProvenanceToAuthoritative_WithCountZero()
    {
        var legacyOpponent = new OpponentState(
            OpponentKind.Normal, 2, 1, executedAttacks: 2, OpponentEncounterProvenance.UnverifiedLegacy);
        var runLegacy = CyberDefenseRunState.CreateActive(1, 0, 100, legacyOpponent);

        var result = CyberDefenseStateMachine.ApplyAttempt(runLegacy, isCorrect: true, effectiveAttackDamage: 1);
        Assert.True(result.IsOpponentDefeated);

        var nextOpponent = result.NextState.CurrentOpponent;
        Assert.Equal(0, nextOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.Authoritative, nextOpponent.Provenance);
    }

    // 8. Boss transition resets new encounter provenance
    [Fact]
    public void Case08_BossTransition_ResetsNewEncounterProvenance_ToAuthoritative()
    {
        var legacyBoss = new OpponentState(
            OpponentKind.Boss, 12, 1, executedAttacks: 5, OpponentEncounterProvenance.UnverifiedLegacy);
        var runBoss = CyberDefenseRunState.CreateActive(1, 5, 100, legacyBoss);

        var result = CyberDefenseStateMachine.ApplyAttempt(runBoss, isCorrect: true, effectiveAttackDamage: 1);
        Assert.True(result.IsOpponentDefeated);
        Assert.True(result.IsSectorCompleted);
        Assert.Equal(2, result.NextState.Sector);
        Assert.Equal(0, result.NextState.OpponentIndex);
        Assert.Equal(0, result.NextState.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.Authoritative, result.NextState.CurrentOpponent.Provenance);
        Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, result.DefeatedOpponentProvenance);
    }

    // 9. Game-over resets provenance
    [Fact]
    public void Case09_GameOver_ResetsProvenance_ToAuthoritative()
    {
        var legacyOpponent = new OpponentState(
            OpponentKind.Normal, 2, 1, executedAttacks: 3, OpponentEncounterProvenance.UnverifiedLegacy);
        var run = CyberDefenseRunState.CreateActive(1, 0, playerCurrentHp: 2, legacyOpponent);

        // Player takes fatal damage (4 enemy damage > 2 hp)
        var result = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: false, effectiveAttackDamage: 1);
        Assert.True(result.IsGameOver);
        Assert.Equal(1, result.NextState.Sector);
        Assert.Equal(0, result.NextState.OpponentIndex);
        Assert.Equal(0, result.NextState.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.Authoritative, result.NextState.CurrentOpponent.Provenance);
    }

    // 10. Full Local Reset resets provenance
    [Fact]
    public async Task Case10_FullLocalReset_ResetsProvenance_ToAuthoritative()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var resetIntent = await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(resetIntent.TargetEpoch);
        await store.ClearResetIntentAsync(resetIntent.TargetEpoch);

        var run = await store.GetRunStateAsync();
        Assert.Equal(OpponentEncounterProvenance.Authoritative, run.CurrentOpponent.Provenance);
        Assert.Equal(0, run.CurrentOpponent.ExecutedAttacks);
    }

    // 11. V1-to-V3 partially damaged encounter
    [Fact]
    public async Task Case11_V1ToV3_PartiallyDamagedEncounter_MigratesToUnverifiedLegacy_CountZero()
    {
        var dbPath = GetTempDbPath();

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

                -- Opponent at sector 4, index 2 has maxHp = 2. Set currentHp = 1 (partially damaged).
                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, reset_epoch, updated_at)
                VALUES (1, 4, 2, 1, 80, 0, '2026-10-09T10:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-09T10:00:00Z');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var run = await store.GetRunStateAsync();
        Assert.Equal(1, run.CurrentOpponent.CurrentHp);
        Assert.Equal(0, run.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, run.CurrentOpponent.Provenance);
    }

    // 12. V1-to-V3 untouched encounter
    [Fact]
    public async Task Case12_V1ToV3_UntouchedEncounter_MigratesToAuthoritative_CountZero()
    {
        var dbPath = GetTempDbPath();

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

                -- Opponent at sector 4, index 2 has maxHp = 2. Set currentHp = 2 (untouched).
                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, reset_epoch, updated_at)
                VALUES (1, 4, 2, 2, 80, 0, '2026-10-09T10:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-09T10:00:00Z');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var run = await store.GetRunStateAsync();
        Assert.Equal(2, run.CurrentOpponent.CurrentHp);
        Assert.Equal(0, run.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.Authoritative, run.CurrentOpponent.Provenance);
    }

    // 13. V2-to-V3 with count 0 and partial HP
    [Fact]
    public async Task Case13_V2ToV3_WithCountZeroAndPartialHp_MigratesToUnverifiedLegacy()
    {
        var dbPath = GetTempDbPath();

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RawSchemaV2Ddl;
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = @"
                INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '2');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '1');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');

                INSERT INTO gameplay_progression (
                    id, store_revision, reset_epoch, cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank, economy_version, updated_at
                ) VALUES (1, 1, 0, 0, 1, 0, 0, 0, 0, 'ECONOMY_V1', '2026-10-10T10:00:00Z');

                -- Opponent at sector 4, index 2 has maxHp = 2. currentHp = 1 (partial HP), executed_attacks = 0.
                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, opponent_executed_attacks, reset_epoch, updated_at)
                VALUES (1, 4, 2, 1, 80, 0, 0, '2026-10-10T10:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-10T10:00:00Z');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var run = await store.GetRunStateAsync();
        Assert.Equal(1, run.CurrentOpponent.CurrentHp);
        Assert.Equal(0, run.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, run.CurrentOpponent.Provenance);
    }

    // 14. V2-to-V3 with positive attack count and partial HP
    [Fact]
    public async Task Case14_V2ToV3_WithPositiveAttackCountAndPartialHp_MigratesToUnverifiedLegacy_PreservingCount()
    {
        var dbPath = GetTempDbPath();

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RawSchemaV2Ddl;
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = @"
                INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '2');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '5');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');

                INSERT INTO gameplay_progression (
                    id, store_revision, reset_epoch, cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank, economy_version, updated_at
                ) VALUES (1, 5, 0, 0, 1, 0, 0, 0, 0, 'ECONOMY_V1', '2026-10-10T10:00:00Z');

                -- Boss at sector 1 has maxHp = 12. currentHp = 8, opponent_executed_attacks = 4.
                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, opponent_executed_attacks, reset_epoch, updated_at)
                VALUES (1, 1, 5, 8, 90, 4, 0, '2026-10-10T10:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-10T10:00:00Z');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var run = await store.GetRunStateAsync();
        Assert.Equal(8, run.CurrentOpponent.CurrentHp);
        Assert.Equal(4, run.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, run.CurrentOpponent.Provenance);
    }

    // 15. V2-to-V3 with positive count originating from an older V1 encounter
    [Fact]
    public async Task Case15_V2ToV3_PositiveCountOriginatingFromOlderV1Encounter_IsUnverifiedLegacy()
    {
        var dbPath = GetTempDbPath();

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RawSchemaV2Ddl;
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = @"
                INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '2');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '2');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');

                INSERT INTO gameplay_progression (
                    id, store_revision, reset_epoch, cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank, economy_version, updated_at
                ) VALUES (1, 2, 0, 0, 1, 0, 0, 0, 0, 'ECONOMY_V1', '2026-10-10T10:00:00Z');

                -- Opponent took hits in V1 and 1 hit in V2 (executed_attacks = 1, currentHp = 2, maxHp = 5 at sector 20 index 0).
                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, opponent_executed_attacks, reset_epoch, updated_at)
                VALUES (1, 20, 0, 2, 100, 1, 0, '2026-10-10T10:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-10T10:00:00Z');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var run = await store.GetRunStateAsync();
        Assert.Equal(2, run.CurrentOpponent.CurrentHp);
        Assert.Equal(1, run.CurrentOpponent.ExecutedAttacks);
        Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, run.CurrentOpponent.Provenance);
    }

    // 16. V2-to-V3 with an already tracked V2 encounter and incomplete historical proof
    [Fact]
    public async Task Case16_V2ToV3_AlreadyTrackedV2Encounter_WithoutProofOfFreshOrigin_IsUnverifiedLegacy()
    {
        var dbPath = GetTempDbPath();

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RawSchemaV2Ddl;
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = @"
                INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '2');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '3');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');

                INSERT INTO gameplay_progression (
                    id, store_revision, reset_epoch, cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank, economy_version, updated_at
                ) VALUES (1, 3, 0, 0, 1, 0, 0, 0, 0, 'ECONOMY_V1', '2026-10-10T10:00:00Z');

                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, opponent_executed_attacks, reset_epoch, updated_at)
                VALUES (1, 1, 5, 10, 95, 2, 0, '2026-10-10T10:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-10T10:00:00Z');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var run = await store.GetRunStateAsync();
        Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, run.CurrentOpponent.Provenance);
        Assert.Equal(2, run.CurrentOpponent.ExecutedAttacks);
    }

    // 17. Preservation of all historical V1 receipt information
    [Fact]
    public async Task Case17_PreservationOfAllHistoricalV1ReceiptInformation()
    {
        var dbPath = GetTempDbPath();

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
                    'sub-v1-preserve', 'Applied', '7+8', 1, 1,
                    550, 0, '2026-10-09T10:05:00Z',
                    1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                    1, 0, 1, 100,
                    NULL, NULL, NULL, NULL, NULL, NULL
                );
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var receipt = await store.GetReceiptAsync("sub-v1-preserve");
        Assert.NotNull(receipt);
        Assert.Equal("sub-v1-preserve", receipt.SubmissionId);
        Assert.Equal("7+8", receipt.FactId);
        Assert.Equal(550, receipt.ResponseLatencyMs);
        Assert.True(receipt.IsCorrect);
        Assert.True(receipt.IsEligible);
        Assert.NotNull(receipt.TransitionResult);
    }

    // 18. Preservation of actual recorded V2 receipt counts
    [Fact]
    public async Task Case18_PreservationOfActualRecordedV2ReceiptCounts()
    {
        var dbPath = GetTempDbPath();

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RawSchemaV2Ddl;
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = @"
                INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '2');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '1');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');

                INSERT INTO gameplay_progression (
                    id, store_revision, reset_epoch, cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank, economy_version, updated_at
                ) VALUES (1, 1, 0, 0, 1, 0, 0, 0, 0, 'ECONOMY_V1', '2026-10-10T10:00:00Z');

                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, opponent_executed_attacks, reset_epoch, updated_at)
                VALUES (1, 1, 1, 2, 100, 0, 0, '2026-10-10T10:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-10T10:00:00Z');

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
                    awarded_xp, player_level_after, cumulative_xp_after
                ) VALUES (
                    'sub-v2-defeat-rec', 'Applied', '3+3', 1, 1,
                    300, 0, '2026-10-10T10:05:00Z',
                    2, 2, 0, 0, 0, 0, 2, 0, 1, 0, 0,
                    1, 1, 2, 100,
                    NULL, NULL, NULL, NULL, NULL, NULL,
                    0, 3,
                    NULL, NULL, NULL
                );
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var receipt = await store.GetReceiptAsync("sub-v2-defeat-rec");
        Assert.NotNull(receipt);
        Assert.NotNull(receipt.TransitionResult);
        Assert.Equal(3, receipt.TransitionResult.ExecutedAttacksOnDefeatedOpponent);
        Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, receipt.TransitionResult.DefeatedOpponentProvenance);
    }

    // 19. Historical unknown attack-count metadata remains unknown
    [Fact]
    public async Task Case19_HistoricalUnknownAttackCountMetadata_RemainsUnknown()
    {
        var dbPath = GetTempDbPath();

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
                VALUES (1, 1, 1, 2, 100, 0, '2026-10-09T10:00:00Z');

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
                    'sub-v1-hist-unknown', 'Applied', '4+4', 1, 1,
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

        var receipt = await store.GetReceiptAsync("sub-v1-hist-unknown");
        Assert.NotNull(receipt);
        Assert.NotNull(receipt.TransitionResult);
        Assert.Null(receipt.TransitionResult.ExecutedAttacksOnDefeatedOpponent);
        Assert.Null(receipt.TransitionResult.DefeatedOpponentProvenance);
    }

    // 20. Exact new V3 receipt roundtrip equality
    [Fact]
    public async Task Case20_ExactNewV3ReceiptRoundtripEquality()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var run = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(run, isCorrect: true, effectiveAttackDamage: 1);

        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            "sub-v3-roundtrip", "1+2", true, true, 333, 0, DateTimeOffset.UtcNow, transition);

        await store.RecordReceiptDirectAsync(receipt);

        var retrieved = await store.GetReceiptAsync("sub-v3-roundtrip");
        Assert.NotNull(retrieved);
        Assert.Equal(receipt, retrieved);
        Assert.Equal(transition, retrieved.TransitionResult);
        Assert.Equal(OpponentEncounterProvenance.Authoritative, retrieved.TransitionResult!.NextState.CurrentOpponent.Provenance);
    }

    // 21. Cold restart during an UnverifiedLegacy encounter
    [Fact]
    public async Task Case21_ColdRestartDuringUnverifiedLegacyEncounter_PreservesProvenance()
    {
        var dbPath = GetTempDbPath();

        // 1. Setup V1 DB with partially damaged opponent
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
                VALUES (1, 1, 0, 1, 100, 0, '2026-10-09T10:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-09T10:00:00Z');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Open store (migrates to V3 UnverifiedLegacy)
        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
            var runBefore = await store1.GetRunStateAsync();
            Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, runBefore.CurrentOpponent.Provenance);
            await store1.CloseAsync();
        }

        // 3. Cold restart: open fresh store instance on same DB file
        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();
            var runAfter = await store2.GetRunStateAsync();
            Assert.Equal(OpponentEncounterProvenance.UnverifiedLegacy, runAfter.CurrentOpponent.Provenance);
            Assert.Equal(1, runAfter.CurrentOpponent.CurrentHp);
            Assert.Equal(0, runAfter.CurrentOpponent.ExecutedAttacks);
        }
    }

    // 22. Duplicate SubmissionId replay
    [Fact]
    public async Task Case22_DuplicateSubmissionIdReplay_IsIdempotent_PreservesProvenance()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord(
            "sub-dup-replay-v3", "2+3", true, true, 200, 0, DateTimeOffset.UtcNow);

        await store.StagePendingIntentAsync(intent);
        var receipt1 = await store.ApplyAttemptTransactionAsync(intent);

        // Replay same submission
        var receipt2 = await store.ApplyAttemptTransactionAsync(intent);

        Assert.Equal(receipt1, receipt2);
        Assert.Equal(receipt1.TransitionResult, receipt2.TransitionResult);
    }

    // 23. Crash recovery after transaction commit
    [Fact]
    public async Task Case23_CrashRecoveryAfterTransactionCommit_PreservesCommittedProvenance()
    {
        var dbPath = GetTempDbPath();
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var intent = new CyberDefensePendingIntentRecord(
                "sub-crash-v3", "3+3", true, true, 250, 0, DateTimeOffset.UtcNow);
            await store.StagePendingIntentAsync(intent);
            await store.ApplyAttemptTransactionAsync(intent);
            // Simulate crash by disposing without orderly close
        }

        using (var recoveredStore = new SqliteGameplayStore(dbPath))
        {
            await recoveredStore.InitializeAsync();
            var run = await recoveredStore.GetRunStateAsync();
            Assert.Equal(1, run.CurrentOpponent.ExecutedAttacks);
            Assert.Equal(OpponentEncounterProvenance.Authoritative, run.CurrentOpponent.Provenance);
            var receipt = await recoveredStore.GetReceiptAsync("sub-crash-v3");
            Assert.NotNull(receipt);
        }
    }

    // 24. Transaction rollback from V1 on migration failure
    [Fact]
    public async Task Case24_TransactionRollbackFromV1_OnMigrationFailure_LeavesV1Intact()
    {
        var dbPath = GetTempDbPath();

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

                -- Corrupt receipt kind to force migration to fail
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at
                ) VALUES (
                    'sub-corrupt-v1', 'IllegalReceiptKind', '1+1', 1, 1, 100, 0, '2026-10-09T10:00:00Z'
                );
            ";
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA ignore_check_constraints = ON;";
            await pragma.ExecuteNonQueryAsync();
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await Assert.ThrowsAnyAsync<Exception>(() => store.InitializeAsync());

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
            Assert.Equal("1", (string?)await cmd.ExecuteScalarAsync());
        }
    }

    // 25. Transaction rollback from V2 on migration failure
    [Fact]
    public async Task Case25_TransactionRollbackFromV2_OnMigrationFailure_LeavesV2Intact()
    {
        var dbPath = GetTempDbPath();

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = RawSchemaV2Ddl;
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = @"
                INSERT INTO gameplay_schema_info (key, value) VALUES ('schema_version', '2');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('store_revision', '1');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('reset_epoch', '0');
                INSERT INTO gameplay_schema_info (key, value) VALUES ('economy_version', 'ECONOMY_V1');

                INSERT INTO gameplay_progression (
                    id, store_revision, reset_epoch, cumulative_xp, player_level, earned_sp, spent_sp, available_sp, attack_rank, economy_version, updated_at
                ) VALUES (1, 1, 0, 0, 1, 0, 0, 0, 0, 'ECONOMY_V1', '2026-10-10T10:00:00Z');

                INSERT INTO gameplay_run_state (id, sector, opponent_index, opponent_current_hp, player_current_hp, opponent_executed_attacks, reset_epoch, updated_at)
                VALUES (1, 1, 0, 2, 100, 0, 0, '2026-10-10T10:00:00Z');

                INSERT INTO gameplay_reset_intent (id, is_pending, current_epoch, target_epoch, created_at, updated_at)
                VALUES (1, 0, 0, 0, NULL, '2026-10-10T10:00:00Z');

                -- Corrupt receipt kind to force V2-to-V3 migration copy to fail
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at
                ) VALUES (
                    'sub-corrupt-v2', 'CorruptKind', '1+1', 1, 1, 100, 0, '2026-10-10T10:00:00Z'
                );
            ";
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA ignore_check_constraints = ON;";
            await pragma.ExecuteNonQueryAsync();
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        await Assert.ThrowsAnyAsync<Exception>(() => store.InitializeAsync());

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM gameplay_schema_info WHERE key = 'schema_version';";
            Assert.Equal("2", (string?)await cmd.ExecuteScalarAsync());
        }
    }

    // 26. Idempotent V3 reopening
    [Fact]
    public async Task Case26_IdempotentV3Reopening_DoesNotRerunMigration()
    {
        var dbPath = GetTempDbPath();
        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
        }

        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();
            var run = await store2.GetRunStateAsync();
            Assert.Equal(1, run.Sector);
            Assert.Equal(OpponentEncounterProvenance.Authoritative, run.CurrentOpponent.Provenance);
        }
    }

    // 27. Future schema version rejection
    [Fact]
    public async Task Case27_FutureSchemaVersionRejection_FailsClosed()
    {
        var dbPath = GetTempDbPath();
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE gameplay_schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO gameplay_schema_info VALUES ('schema_version', '8');
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using var store = new SqliteGameplayStore(dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
        Assert.Contains("Unsupported database schema version 8", ex.Message);
    }

    // 28. Learner Store Schema V9 isolation
    [Fact]
    public async Task Case28_LearnerStoreSchemaV9Isolation_PreservedWithoutInterference()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Verify that gameplay store SQLite connection does NOT contain or create any learner store tables
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'learner_%';";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.False(await reader.ReadAsync(), "Gameplay store must not touch or contain learner store tables.");
    }
}
