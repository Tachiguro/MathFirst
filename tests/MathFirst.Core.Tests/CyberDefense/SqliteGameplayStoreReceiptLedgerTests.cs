namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.IO;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain;
using MathFirst.Domain.Curriculum;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SqliteGameplayStoreReceiptLedgerTests : IDisposable
{
    private readonly string _testDbDir;

    public SqliteGameplayStoreReceiptLedgerTests()
    {
        _testDbDir = Path.Combine(Path.GetTempPath(), "MathFirstReceiptTests_" + Guid.NewGuid().ToString("N"));
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

    private string GetTempDbPath(string prefix = "receipt_test") =>
        Path.Combine(_testDbDir, $"{prefix}_{Guid.NewGuid():N}.db");

    // =========================================================================
    // 1. RECEIPT ROUNDTRIP — NORMAL CORRECT ATTEMPT
    // =========================================================================

    [Fact]
    public async Task Receipt_Roundtrip_NormalCorrectAttempt_PreservesAllFields()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var initialRun = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(initialRun, isCorrect: true, effectiveAttackDamage: 1);

        var processedAt = new DateTimeOffset(2026, 10, 9, 12, 34, 56, 789, TimeSpan.Zero);
        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            submissionId: "sub-normal-correct-001",
            factId: "3+4",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 450,
            resetEpoch: 0,
            processedAt: processedAt,
            transitionResult: transition);

        await store.RecordReceiptDirectAsync(receipt);

        var retrieved = await store.GetReceiptAsync("sub-normal-correct-001");

        Assert.NotNull(retrieved);
        Assert.Equal("sub-normal-correct-001", retrieved.SubmissionId);
        Assert.Equal(CyberDefenseReceiptKind.Applied, retrieved.ReceiptKind);
        Assert.Equal("3+4", retrieved.FactId);
        Assert.True(retrieved.IsCorrect);
        Assert.True(retrieved.IsEligible);
        Assert.Equal(450, retrieved.ResponseLatencyMs);
        Assert.Equal(0, retrieved.ResetEpoch);
        Assert.Equal(processedAt, retrieved.ProcessedAt);
        Assert.NotNull(retrieved.TransitionResult);
        Assert.Equal(transition, retrieved.TransitionResult);
        Assert.Equal(receipt, retrieved);
    }

    // =========================================================================
    // 2. RECEIPT ROUNDTRIP — NONFATAL INCORRECT ATTEMPT
    // =========================================================================

    [Fact]
    public async Task Receipt_Roundtrip_NonfatalIncorrectAttempt_PreservesPlayerDamage()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var initialRun = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(initialRun, isCorrect: false, effectiveAttackDamage: 1);

        var processedAt = DateTimeOffset.UtcNow;
        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            submissionId: "sub-nonfatal-incorrect-002",
            factId: "7+8",
            isCorrect: false,
            isEligible: true,
            responseLatencyMs: 1200,
            resetEpoch: 0,
            processedAt: processedAt,
            transitionResult: transition);

        await store.RecordReceiptDirectAsync(receipt);

        var retrieved = await store.GetReceiptAsync("sub-nonfatal-incorrect-002");

        Assert.NotNull(retrieved);
        Assert.Equal(CyberDefenseReceiptKind.Applied, retrieved.ReceiptKind);
        Assert.False(retrieved.IsCorrect);
        Assert.NotNull(retrieved.TransitionResult);
        Assert.False(retrieved.TransitionResult.IsGameOver);
        Assert.Equal(4, retrieved.TransitionResult.IncomingEnemyDamage);
        Assert.Equal(4, retrieved.TransitionResult.AppliedPlayerDamage);
        Assert.Equal(0, retrieved.TransitionResult.ExcessEnemyDamage);
        Assert.Equal(96, retrieved.TransitionResult.NextState.PlayerCurrentHp);
        Assert.Null(retrieved.TransitionResult.TerminalSnapshot);
        Assert.Equal(receipt, retrieved);
    }

    // =========================================================================
    // 3. RECEIPT ROUNDTRIP — NORMAL OPPONENT DEFEAT
    // =========================================================================

    [Fact]
    public async Task Receipt_Roundtrip_NormalOpponentDefeat_PreservesAdvancementAndHealing()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Start with player HP at 90 so defeat healing (2 HP) is fully applied without exceeding 100
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var runState = CyberDefenseRunState.CreateActive(1, 0, 90, opponent);
        var transition = CyberDefenseStateMachine.ApplyAttempt(runState, isCorrect: true, effectiveAttackDamage: 5);

        var processedAt = DateTimeOffset.UtcNow;
        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            submissionId: "sub-normal-defeat-003",
            factId: "5x5",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 380,
            resetEpoch: 1,
            processedAt: processedAt,
            transitionResult: transition);

        await store.RecordReceiptDirectAsync(receipt);

        var retrieved = await store.GetReceiptAsync("sub-normal-defeat-003");

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.TransitionResult);
        Assert.True(retrieved.TransitionResult.IsOpponentDefeated);
        Assert.False(retrieved.TransitionResult.IsSectorCompleted);
        Assert.Equal(5, retrieved.TransitionResult.RequestedAttackDamage);
        Assert.Equal(2, retrieved.TransitionResult.AppliedOpponentDamage);
        Assert.Equal(3, retrieved.TransitionResult.ExcessOpponentDamage);
        Assert.Equal(2, retrieved.TransitionResult.PotentialHealing);
        Assert.Equal(2, retrieved.TransitionResult.AppliedHealing);
        Assert.Equal(92, retrieved.TransitionResult.NextState.PlayerCurrentHp);
        Assert.Equal(1, retrieved.TransitionResult.NextState.OpponentIndex);
        Assert.Equal(receipt, retrieved);
    }

    // =========================================================================
    // 4. RECEIPT ROUNDTRIP — BOSS DEFEAT
    // =========================================================================

    [Fact]
    public async Task Receipt_Roundtrip_BossDefeat_PreservesSectorTransition()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Sector 1 boss is at index 5, max HP is 12. Create state at boss with 4 HP remaining, player HP 80.
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);
        var bossOpponent = new OpponentState(OpponentKind.Boss, bossMaxHp, 4);
        var runState = CyberDefenseRunState.CreateActive(1, bossIndex, 80, bossOpponent);

        var transition = CyberDefenseStateMachine.ApplyAttempt(runState, isCorrect: true, effectiveAttackDamage: 10);

        var processedAt = DateTimeOffset.UtcNow;
        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            submissionId: "sub-boss-defeat-004",
            factId: "9x9",
            isCorrect: true,
            isEligible: false,
            responseLatencyMs: 2500,
            resetEpoch: 2,
            processedAt: processedAt,
            transitionResult: transition);

        await store.RecordReceiptDirectAsync(receipt);

        var retrieved = await store.GetReceiptAsync("sub-boss-defeat-004");

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.TransitionResult);
        Assert.True(retrieved.TransitionResult.IsOpponentDefeated);
        Assert.True(retrieved.TransitionResult.IsSectorCompleted);
        Assert.False(retrieved.TransitionResult.IsGameOver);
        Assert.Equal(2, retrieved.TransitionResult.NextState.Sector);
        Assert.Equal(0, retrieved.TransitionResult.NextState.OpponentIndex);
        Assert.Equal(86, retrieved.TransitionResult.NextState.PlayerCurrentHp);
        Assert.Equal(6, retrieved.TransitionResult.AppliedHealing);
        Assert.Equal(receipt, retrieved);
    }

    // =========================================================================
    // 5. RECEIPT ROUNDTRIP — GAME OVER & TERMINAL SNAPSHOT
    // =========================================================================

    [Fact]
    public async Task Receipt_Roundtrip_GameOver_PreservesExactTerminalSnapshot()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Player HP is 3, enemy counter-damage is 4 -> Fatal counter-damage
        var opponent = new OpponentState(OpponentKind.Normal, 2, 2);
        var runState = CyberDefenseRunState.CreateActive(1, 2, 3, opponent);

        var transition = CyberDefenseStateMachine.ApplyAttempt(runState, isCorrect: false, effectiveAttackDamage: 1);

        Assert.True(transition.IsGameOver);
        Assert.NotNull(transition.TerminalSnapshot);

        var processedAt = DateTimeOffset.UtcNow;
        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            submissionId: "sub-game-over-005",
            factId: "12-7",
            isCorrect: false,
            isEligible: true,
            responseLatencyMs: 3100,
            resetEpoch: 0,
            processedAt: processedAt,
            transitionResult: transition);

        await store.RecordReceiptDirectAsync(receipt);

        var retrieved = await store.GetReceiptAsync("sub-game-over-005");

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.TransitionResult);
        Assert.True(retrieved.TransitionResult.IsGameOver);
        Assert.NotNull(retrieved.TransitionResult.TerminalSnapshot);

        var term = retrieved.TransitionResult.TerminalSnapshot;
        Assert.Equal(1, term.Sector);
        Assert.Equal(2, term.OpponentIndex);
        Assert.Equal(OpponentKind.Normal, term.Kind);
        Assert.Equal(2, term.OpponentCurrentHp);
        Assert.Equal(2, term.OpponentMaxHp);
        Assert.Equal(0, term.PlayerCurrentHp);

        // NextState must be reset to initial run
        Assert.Equal(CyberDefenseRunState.InitialRun(), retrieved.TransitionResult.NextState);
        Assert.Equal(receipt, retrieved);
    }

    // =========================================================================
    // 6. RECEIPT ROUNDTRIP — CALM MODE SUPPRESSED ATTEMPT
    // =========================================================================

    [Fact]
    public async Task Receipt_Roundtrip_SuppressedAttempt_HasNoCombatTransition()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var processedAt = new DateTimeOffset(2026, 10, 9, 14, 0, 0, TimeSpan.Zero);
        var receipt = CyberDefenseReceiptRecord.CreateCalmModeSuppressed(
            submissionId: "sub-calm-suppressed-006",
            factId: "4+4",
            isCorrect: true,
            isEligible: true,
            responseLatencyMs: 500,
            resetEpoch: 3,
            processedAt: processedAt);

        await store.RecordReceiptDirectAsync(receipt);

        var retrieved = await store.GetReceiptAsync("sub-calm-suppressed-006");

        Assert.NotNull(retrieved);
        Assert.Equal("sub-calm-suppressed-006", retrieved.SubmissionId);
        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, retrieved.ReceiptKind);
        Assert.Equal("4+4", retrieved.FactId);
        Assert.True(retrieved.IsCorrect);
        Assert.True(retrieved.IsEligible);
        Assert.Equal(500, retrieved.ResponseLatencyMs);
        Assert.Equal(3, retrieved.ResetEpoch);
        Assert.Equal(processedAt, retrieved.ProcessedAt);
        Assert.Null(retrieved.TransitionResult);
        Assert.Equal(receipt, retrieved);
    }

    // =========================================================================
    // 7. RECEIPT ROUNDTRIP — AFTER STORE REOPEN
    // =========================================================================

    [Fact]
    public async Task Receipt_Roundtrip_AfterStoreReopen_PreservesExactResult()
    {
        var dbPath = GetTempDbPath();
        var appliedReceiptId = "sub-reopen-applied-007a";
        var suppressedReceiptId = "sub-reopen-suppressed-007b";

        var initialRun = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(initialRun, isCorrect: true, effectiveAttackDamage: 2);
        var appliedReceipt = CyberDefenseReceiptRecord.CreateApplied(
            appliedReceiptId, "6x7", isCorrect: true, isEligible: true, responseLatencyMs: 600, resetEpoch: 0,
            DateTimeOffset.UtcNow, transition);

        var suppressedReceipt = CyberDefenseReceiptRecord.CreateCalmModeSuppressed(
            suppressedReceiptId, "8x8", isCorrect: false, isEligible: false, responseLatencyMs: 1500, resetEpoch: 0,
            DateTimeOffset.UtcNow);

        // First session: open, record, close
        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
            await store1.RecordReceiptDirectAsync(appliedReceipt);
            await store1.RecordReceiptDirectAsync(suppressedReceipt);
            await store1.CloseAsync();
        }

        // Second session: reopen from same path
        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();

            var retrievedApplied = await store2.GetReceiptAsync(appliedReceiptId);
            var retrievedSuppressed = await store2.GetReceiptAsync(suppressedReceiptId);

            Assert.NotNull(retrievedApplied);
            Assert.Equal(appliedReceipt, retrievedApplied);

            Assert.NotNull(retrievedSuppressed);
            Assert.Equal(suppressedReceipt, retrievedSuppressed);
        }
    }

    // =========================================================================
    // 8. DUPLICATE SUBMISSION ID REJECTION
    // =========================================================================

    [Fact]
    public async Task Receipt_DuplicateSubmissionId_DoesNotOverwriteOriginal()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var initialRun = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(initialRun, isCorrect: true, effectiveAttackDamage: 1);
        var receipt1 = CyberDefenseReceiptRecord.CreateApplied(
            "sub-dup-008", "2+2", isCorrect: true, isEligible: true, responseLatencyMs: 300, resetEpoch: 0,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), transition);

        await store.RecordReceiptDirectAsync(receipt1);

        // Attempt duplicate insert with same submission ID
        var receipt2 = CyberDefenseReceiptRecord.CreateApplied(
            "sub-dup-008", "2+2", isCorrect: true, isEligible: true, responseLatencyMs: 300, resetEpoch: 0,
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), transition);

        await Assert.ThrowsAnyAsync<Exception>(() => store.RecordReceiptDirectAsync(receipt2));

        var retrieved = await store.GetReceiptAsync("sub-dup-008");
        Assert.NotNull(retrieved);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), retrieved.ProcessedAt);
    }

    // =========================================================================
    // 9. CONFLICTING DUPLICATE PRESERVES ORIGINAL DATA
    // =========================================================================

    [Fact]
    public async Task Receipt_ConflictingDuplicate_PreservesOriginalData()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var initialRun = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(initialRun, isCorrect: true, effectiveAttackDamage: 1);
        var originalApplied = CyberDefenseReceiptRecord.CreateApplied(
            "sub-conflict-009", "3+3", isCorrect: true, isEligible: true, responseLatencyMs: 400, resetEpoch: 0,
            DateTimeOffset.UtcNow, transition);

        await store.RecordReceiptDirectAsync(originalApplied);

        // Attempt conflicting duplicate (suppressed instead of applied, incorrect instead of correct)
        var conflictingSuppressed = CyberDefenseReceiptRecord.CreateCalmModeSuppressed(
            "sub-conflict-009", "9-9", isCorrect: false, isEligible: false, responseLatencyMs: 9000, resetEpoch: 5,
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAnyAsync<Exception>(() => store.RecordReceiptDirectAsync(conflictingSuppressed));

        var retrieved = await store.GetReceiptAsync("sub-conflict-009");
        Assert.NotNull(retrieved);
        Assert.Equal(CyberDefenseReceiptKind.Applied, retrieved.ReceiptKind);
        Assert.Equal("3+3", retrieved.FactId);
        Assert.True(retrieved.IsCorrect);
        Assert.Equal(originalApplied, retrieved);
    }

    // =========================================================================
    // 10. EMPTY SUBMISSION ID REJECTION
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Receipt_EmptySubmissionId_IsRejected(string invalidSubId)
    {
        var transition = CyberDefenseStateMachine.ApplyAttempt(CyberDefenseRunState.InitialRun(), isCorrect: true, effectiveAttackDamage: 1);

        Assert.Throws<ArgumentException>(() =>
            CyberDefenseReceiptRecord.CreateApplied(invalidSubId, "1+1", true, true, 100, 0, DateTimeOffset.UtcNow, transition));

        Assert.Throws<ArgumentException>(() =>
            CyberDefenseReceiptRecord.CreateCalmModeSuppressed(invalidSubId, "1+1", true, true, 100, 0, DateTimeOffset.UtcNow));
    }

    // =========================================================================
    // 11. MALFORMED APPLIED PAYLOAD FAILS CLOSED
    // =========================================================================

    [Fact]
    public async Task Receipt_MalformedAppliedPayload_FailsClosed()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Inject corrupted raw row directly via SQLite (e.g. unknown receipt_kind or NULL required transition column)
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        // Temporarily disable check constraints to insert corrupt data for testing fail-closed rehydration
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                    incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                    potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
                    is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
                    next_player_current_hp
                ) VALUES (
                    'sub-malformed-011', 'Applied', '1+1', 1, 1,
                    100, 0, '2026-10-09T00:00:00.0000000Z',
                    1, NULL, 0,
                    0, 0, 0,
                    0, 0, 0, 0,
                    0, 1, 0, 2,
                    100
                );
            ";
            // Note: If DB check constraint rejects the insert, we verify that either DB rejects it or store reader fails closed.
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqliteException)
            {
                // Schema V1 check constraint prevented corrupt insert at DB level (excellent fail-closed defense)
                return;
            }
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetReceiptAsync("sub-malformed-011"));
    }

    // =========================================================================
    // 12. INVALID TERMINAL SNAPSHOT FAILS CLOSED
    // =========================================================================

    [Fact]
    public async Task Receipt_InvalidTerminalSnapshot_FailsClosed()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        using (var cmd = connection.CreateCommand())
        {
            // Terminal snapshot with invalid player HP (> 0)
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
                    terminal_player_current_hp
                ) VALUES (
                    'sub-bad-term-012', 'Applied', '1+1', 0, 1,
                    100, 0, '2026-10-09T00:00:00.0000000Z',
                    0, 0, 0,
                    4, 4, 0,
                    0, 0, 0, 0,
                    1, 1, 0, 2,
                    100, 1, 0,
                    'Normal', 2, 2,
                    50
                );
            ";
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqliteException)
            {
                // Schema V1 check constraint prevented corrupt insert at DB level
                return;
            }
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetReceiptAsync("sub-bad-term-012"));
    }

    // =========================================================================
    // 13. SUPPRESSED PAYLOAD WITH COMBAT FIELDS FAILS CLOSED
    // =========================================================================

    [Fact]
    public async Task Receipt_SuppressedPayloadWithCombatFields_FailsClosed()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage
                ) VALUES (
                    'sub-suppressed-dirty-013', 'CalmModeSuppressed', '1+1', 1, 1,
                    100, 0, '2026-10-09T00:00:00.0000000Z',
                    10
                );
            ";
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqliteException)
            {
                // Schema V1 check constraint prevented corrupt insert
                return;
            }
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetReceiptAsync("sub-suppressed-dirty-013"));
    }

    // =========================================================================
    // 14. NON-GAME-OVER WITH TERMINAL SNAPSHOT FAILS CLOSED
    // =========================================================================

    [Fact]
    public async Task Receipt_NonGameOverWithTerminalSnapshot_FailsClosed()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        using (var cmd = connection.CreateCommand())
        {
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
                    terminal_player_current_hp
                ) VALUES (
                    'sub-nongameover-with-term-014', 'Applied', '1+1', 1, 1,
                    100, 0, '2026-10-09T00:00:00.0000000Z',
                    1, 1, 0,
                    0, 0, 0,
                    0, 0, 0, 0,
                    0, 1, 0, 1,
                    100, 1, 0,
                    'Normal', 2, 2,
                    0
                );
            ";
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqliteException)
            {
                // Schema V1 check constraint prevented corrupt insert
                return;
            }
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetReceiptAsync("sub-nongameover-with-term-014"));
    }

    // =========================================================================
    // 15. INVALID DOMAIN NEXT STATE FAILS CLOSED
    // =========================================================================

    [Fact]
    public async Task Receipt_InvalidDomainNextState_FailsClosed()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        using (var cmd = connection.CreateCommand())
        {
            // next_opponent_current_hp is 99 (exceeds max HP 2 for sector 1 opponent 0)
            cmd.CommandText = @"
                INSERT INTO gameplay_receipt_ledger (
                    submission_id, receipt_kind, fact_id, is_correct, is_eligible,
                    response_latency_ms, reset_epoch, processed_at,
                    requested_attack_damage, applied_opponent_damage, excess_opponent_damage,
                    incoming_enemy_damage, applied_player_damage, excess_enemy_damage,
                    potential_healing, applied_healing, is_opponent_defeated, is_sector_completed,
                    is_game_over, next_sector, next_opponent_index, next_opponent_current_hp,
                    next_player_current_hp
                ) VALUES (
                    'sub-invalid-next-015', 'Applied', '1+1', 1, 1,
                    100, 0, '2026-10-09T00:00:00.0000000Z',
                    1, 1, 0,
                    0, 0, 0,
                    0, 0, 0, 0,
                    0, 1, 0, 99,
                    100
                );
            ";
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqliteException)
            {
                return;
            }
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetReceiptAsync("sub-invalid-next-015"));
    }

    // =========================================================================
    // 16. RECEIPT INSERT FAILURE DOES NOT LEAVE PARTIAL RECEIPT
    // =========================================================================

    [Fact]
    public async Task Receipt_InsertFailure_DoesNotLeavePartialReceipt()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        // Execute a transaction where insert succeeds but transaction rolls back
        using (var transaction = connection.BeginTransaction())
        {
            var initialRun = CyberDefenseRunState.InitialRun();
            var transition = CyberDefenseStateMachine.ApplyAttempt(initialRun, isCorrect: true, effectiveAttackDamage: 1);
            var receipt = CyberDefenseReceiptRecord.CreateApplied(
                "sub-rollback-016", "2+3", true, true, 200, 0, DateTimeOffset.UtcNow, transition);

            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            SqliteGameplayStore.InsertReceiptRecord(cmd, receipt);
            await cmd.ExecuteNonQueryAsync();

            // Explicit rollback
            transaction.Rollback();
        }

        var retrieved = await store.GetReceiptAsync("sub-rollback-016");
        Assert.Null(retrieved);
    }

    // =========================================================================
    // 17. PRESERVES SUBMISSION METADATA AND RESET EPOCH
    // =========================================================================

    [Fact]
    public async Task Receipt_PreservesSubmissionMetadataAndResetEpoch()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var initialRun = CyberDefenseRunState.InitialRun();
        var transition = CyberDefenseStateMachine.ApplyAttempt(initialRun, isCorrect: true, effectiveAttackDamage: 1);

        var processedAt = new DateTimeOffset(2026, 10, 9, 23, 59, 59, TimeSpan.Zero);
        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            submissionId: "sub-epoch-42",
            factId: "12x12",
            isCorrect: true,
            isEligible: false,
            responseLatencyMs: 9876,
            resetEpoch: 42,
            processedAt: processedAt,
            transitionResult: transition);

        await store.RecordReceiptDirectAsync(receipt);

        var retrieved = await store.GetReceiptAsync("sub-epoch-42");
        Assert.NotNull(retrieved);
        Assert.Equal("sub-epoch-42", retrieved.SubmissionId);
        Assert.Equal("12x12", retrieved.FactId);
        Assert.True(retrieved.IsCorrect);
        Assert.False(retrieved.IsEligible);
        Assert.Equal(9876, retrieved.ResponseLatencyMs);
        Assert.Equal(42, retrieved.ResetEpoch);
        Assert.Equal(processedAt, retrieved.ProcessedAt);
    }

    // =========================================================================
    // 18. RECEIPT STORAGE DOES NOT MUTATE ACTIVE RUN STATE IN ISOLATION
    // =========================================================================

    [Fact]
    public async Task Receipt_DoesNotMutateActiveRunStateWhenStoredInIsolation()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var initialRun = await store.GetRunStateAsync();
        Assert.Equal(1, initialRun.Sector);
        Assert.Equal(0, initialRun.OpponentIndex);
        Assert.Equal(100, initialRun.PlayerCurrentHp);

        // Create a transition that advances sector
        int bossIndex = CyberDefenseScalingPolicy.GetBossIndex(1);
        int bossMaxHp = CyberDefenseScalingPolicy.GetBossMaxHp(1);
        var bossOpponent = new OpponentState(OpponentKind.Boss, bossMaxHp, 1);
        var bossRun = CyberDefenseRunState.CreateActive(1, bossIndex, 80, bossOpponent);
        var transition = CyberDefenseStateMachine.ApplyAttempt(bossRun, isCorrect: true, effectiveAttackDamage: 10);
        Assert.Equal(2, transition.NextState.Sector);

        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            "sub-isolation-018", "5+5", true, true, 300, 0, DateTimeOffset.UtcNow, transition);

        await store.RecordReceiptDirectAsync(receipt);

        // Active run state in database must NOT be mutated by isolated receipt write
        var currentRun = await store.GetRunStateAsync();
        Assert.Equal(1, currentRun.Sector);
        Assert.Equal(0, currentRun.OpponentIndex);
        Assert.Equal(100, currentRun.PlayerCurrentHp);
        Assert.Equal(initialRun, currentRun);
    }

    // =========================================================================
    // 19. READ DOES NOT MUTATE STORE REVISION
    // =========================================================================

    [Fact]
    public async Task Receipt_ReadDoesNotMutateStoreRevision()
    {
        var dbPath = GetTempDbPath();
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        long revisionBefore = await store.GetStoreRevisionAsync();

        var nonExistent = await store.GetReceiptAsync("sub-nonexistent-019");
        Assert.Null(nonExistent);

        long revisionAfter = await store.GetStoreRevisionAsync();
        Assert.Equal(revisionBefore, revisionAfter);
    }

    // =========================================================================
    // 20. RECEIPT PERSISTENCE DOES NOT TOUCH LEARNER STORE SCHEMA V9
    // =========================================================================

    [Fact]
    public async Task Receipt_Persistence_DoesNotTouchLearnerStoreSchemaV9()
    {
        var gameplayDbPath = GetTempDbPath("gameplay");
        var learnerDbPath = GetTempDbPath("learner");

        using var learnerStore = new SqliteLearnerStore(learnerDbPath);
        await learnerStore.InitializeAsync();

        using var gameplayStore = new SqliteGameplayStore(gameplayDbPath);
        await gameplayStore.InitializeAsync();

        var transition = CyberDefenseStateMachine.ApplyAttempt(CyberDefenseRunState.InitialRun(), true, 1);
        var receipt = CyberDefenseReceiptRecord.CreateApplied(
            "sub-learner-isolation-020", "2+2", true, true, 250, 0, DateTimeOffset.UtcNow, transition);

        await gameplayStore.RecordReceiptDirectAsync(receipt);

        // Verify learner store schema is completely untouched V9
        using var learnerConn = new SqliteConnection($"Data Source={learnerDbPath}");
        await learnerConn.OpenAsync();

        using var cmd = learnerConn.CreateCommand();
        cmd.CommandText = "SELECT value FROM schema_info WHERE key = 'schema_version';";
        var version = (string?)await cmd.ExecuteScalarAsync();
        Assert.Equal(LearnerProgression.DefaultSchemaVersion.ToString(), version);

        // Verify gameplay tables do NOT exist in learner store
        using var tableCmd = learnerConn.CreateCommand();
        tableCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'gameplay_receipt_ledger';";
        long count = (long)(await tableCmd.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(0, count);
    }

    // =========================================================================
    // 21. RECEIPT RECORD MODEL INVARIANTS & CONSTRUCTOR VALIDATION
    // =========================================================================

    [Fact]
    public void ReceiptRecord_Constructor_AppliedWithoutTransition_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseReceiptRecord(
                "sub-inv-01", CyberDefenseReceiptKind.Applied, "1+1", true, true, 100, 0, DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void ReceiptRecord_Constructor_CalmModeWithTransition_ThrowsArgumentException()
    {
        var transition = CyberDefenseStateMachine.ApplyAttempt(CyberDefenseRunState.InitialRun(), true, 1);

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseReceiptRecord(
                "sub-inv-02", CyberDefenseReceiptKind.CalmModeSuppressed, "1+1", true, true, 100, 0, DateTimeOffset.UtcNow, transition));
    }

    [Fact]
    public void ReceiptRecord_Constructor_CorrectnessMismatch_ThrowsArgumentException()
    {
        var transition = CyberDefenseStateMachine.ApplyAttempt(CyberDefenseRunState.InitialRun(), isCorrect: true, effectiveAttackDamage: 1);

        // Attempt claims isCorrect = false, but transition is correct (true)
        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseReceiptRecord(
                "sub-inv-03", CyberDefenseReceiptKind.Applied, "1+1", isCorrect: false, isEligible: true, responseLatencyMs: 100, resetEpoch: 0, DateTimeOffset.UtcNow, transition));
    }

    [Fact]
    public void ReceiptRecord_Constructor_NegativeLatency_ThrowsArgumentOutOfRangeException()
    {
        var transition = CyberDefenseStateMachine.ApplyAttempt(CyberDefenseRunState.InitialRun(), true, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseReceiptRecord(
                "sub-inv-04", CyberDefenseReceiptKind.Applied, "1+1", true, true, -1, 0, DateTimeOffset.UtcNow, transition));
    }

    [Fact]
    public void ReceiptRecord_Constructor_NegativeResetEpoch_ThrowsArgumentOutOfRangeException()
    {
        var transition = CyberDefenseStateMachine.ApplyAttempt(CyberDefenseRunState.InitialRun(), true, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseReceiptRecord(
                "sub-inv-05", CyberDefenseReceiptKind.Applied, "1+1", true, true, 100, -1, DateTimeOffset.UtcNow, transition));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ReceiptRecord_Constructor_NullOrWhitespaceFactId_ThrowsArgumentException(string invalidFactId)
    {
        var transition = CyberDefenseStateMachine.ApplyAttempt(CyberDefenseRunState.InitialRun(), true, 1);

        Assert.Throws<ArgumentException>(() =>
            new CyberDefenseReceiptRecord(
                "sub-inv-06", CyberDefenseReceiptKind.Applied, invalidFactId, true, true, 100, 0, DateTimeOffset.UtcNow, transition));
    }

    [Fact]
    public void ReceiptRecord_Constructor_InvalidEnum_ThrowsArgumentOutOfRangeException()
    {
        var transition = CyberDefenseStateMachine.ApplyAttempt(CyberDefenseRunState.InitialRun(), true, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CyberDefenseReceiptRecord(
                "sub-inv-07", (CyberDefenseReceiptKind)999, "1+1", true, true, 100, 0, DateTimeOffset.UtcNow, transition));
    }
}

