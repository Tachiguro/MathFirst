namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.IO;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class SqliteGameplayStoreFirewallPersistenceTests : IDisposable
{
    private readonly string _testDirectory;

    public SqliteGameplayStoreFirewallPersistenceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_Firewall_" + Guid.NewGuid().ToString("N"));
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

    private string GetTempDbPath(string name = "gameplay_firewall_test")
    {
        return Path.Combine(_testDirectory, $"{name}_{Guid.NewGuid():N}.db");
    }

    private static CyberDefensePendingIntentRecord CreateIntent(
        string submissionId,
        bool isCorrect = true,
        bool isEligible = true,
        int effectiveDamage = 1,
        long resetEpoch = 0,
        string factId = "add:1+1")
    {
        return new CyberDefensePendingIntentRecord(
            submissionId: submissionId,
            factId: factId,
            isCorrect: isCorrect,
            isEligible: isEligible,
            responseLatencyMs: 250,
            resetEpoch: resetEpoch,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: effectiveDamage);
    }

    private static async Task SetRunShieldStateAsync(SqliteGameplayStore store, int capacity, int active, int recharge)
    {
        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_run_state
                SET shield_capacity = @cap,
                    active_shields = @active,
                    shield_recharge_units = @recharge
                WHERE id = 1;
            ";
            cmd.Parameters.AddWithValue("@cap", capacity);
            cmd.Parameters.AddWithValue("@active", active);
            cmd.Parameters.AddWithValue("@recharge", recharge);
            await cmd.ExecuteNonQueryAsync();
        });
    }

    [Fact]
    public async Task RunState_SaveAndReload_NonzeroShieldCapacityAndActiveShields_Preserved()
    {
        var dbPath = GetTempDbPath("save_reload_shields");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetRunShieldStateAsync(store, capacity: 2, active: 2, recharge: 0);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(2, runState.ShieldCapacity);
        Assert.Equal(2, runState.ActiveShields);
        Assert.Equal(0, runState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task RunState_SaveAndReload_PartialRechargeUnits_Preserved()
    {
        var dbPath = GetTempDbPath("save_reload_partial");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetRunShieldStateAsync(store, capacity: 2, active: 1, recharge: 15);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(2, runState.ShieldCapacity);
        Assert.Equal(1, runState.ActiveShields);
        Assert.Equal(15, runState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task AttemptTransaction_CorrectAnswer_RechargesShields_AndPersists()
    {
        var dbPath = GetTempDbPath("correct_recharge");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Capacity 1, active 0, recharge 28
        await SetRunShieldStateAsync(store, capacity: 1, active: 0, recharge: 28);

        // Correct answer grants +2 recharge units. 28 + 2 = 30 -> restores 1 shield, recharge resets to 0.
        var intent = CreateIntent("sub-corr-1", isCorrect: true, effectiveDamage: 1);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.Equal(CyberDefenseReceiptKind.Applied, receipt.ReceiptKind);
        Assert.NotNull(receipt.TransitionResult);
        Assert.False(receipt.TransitionResult.IsShieldAbsorbed);
        Assert.Equal(0, receipt.TransitionResult.AbsorbedEnemyDamage);
        Assert.Equal(1, receipt.TransitionResult.NextState.ShieldCapacity);
        Assert.Equal(1, receipt.TransitionResult.NextState.ActiveShields);
        Assert.Equal(0, receipt.TransitionResult.NextState.ShieldRechargeUnits);

        var durableRun = await store.GetRunStateAsync();
        Assert.Equal(1, durableRun.ShieldCapacity);
        Assert.Equal(1, durableRun.ActiveShields);
        Assert.Equal(0, durableRun.ShieldRechargeUnits);

        var replay = await store.GetReceiptAsync("sub-corr-1");
        Assert.NotNull(replay);
        Assert.NotNull(replay.TransitionResult);
        Assert.Equal(1, replay.TransitionResult.NextState.ActiveShields);
        Assert.Equal(0, replay.TransitionResult.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task AttemptTransaction_IncorrectAnswer_ShieldAbsorbsDamage_AndPersists()
    {
        var dbPath = GetTempDbPath("incorrect_absorb");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Player HP 100, Capacity 1, active 1, recharge 0
        await SetRunShieldStateAsync(store, capacity: 1, active: 1, recharge: 0);

        // Incorrect answer. Normal enemy in sector 1 deals 4 damage.
        // Active shield absorbs all 4 damage! Player takes 0 damage.
        // Active shields drops to 0, recharge receives +1 unit.
        var intent = CreateIntent("sub-inc-1", isCorrect: false);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.Equal(CyberDefenseReceiptKind.Applied, receipt.ReceiptKind);
        Assert.NotNull(receipt.TransitionResult);
        Assert.True(receipt.TransitionResult.IsShieldAbsorbed);
        Assert.Equal(4, receipt.TransitionResult.AbsorbedEnemyDamage);
        Assert.Equal(0, receipt.TransitionResult.AppliedPlayerDamage);
        Assert.Equal(0, receipt.TransitionResult.ExcessEnemyDamage);
        Assert.Equal(100, receipt.TransitionResult.NextState.PlayerCurrentHp);
        Assert.Equal(1, receipt.TransitionResult.NextState.ShieldCapacity);
        Assert.Equal(0, receipt.TransitionResult.NextState.ActiveShields);
        Assert.Equal(1, receipt.TransitionResult.NextState.ShieldRechargeUnits);

        var durableRun = await store.GetRunStateAsync();
        Assert.Equal(100, durableRun.PlayerCurrentHp);
        Assert.Equal(1, durableRun.ShieldCapacity);
        Assert.Equal(0, durableRun.ActiveShields);
        Assert.Equal(1, durableRun.ShieldRechargeUnits);

        var replay = await store.GetReceiptAsync("sub-inc-1");
        Assert.NotNull(replay);
        Assert.NotNull(replay.TransitionResult);
        Assert.True(replay.TransitionResult.IsShieldAbsorbed);
        Assert.Equal(4, replay.TransitionResult.AbsorbedEnemyDamage);
        Assert.Equal(0, replay.TransitionResult.AppliedPlayerDamage);
        Assert.Equal(0, replay.TransitionResult.NextState.ActiveShields);
        Assert.Equal(1, replay.TransitionResult.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task AttemptTransaction_GameOver_ResetsRunStateToFullShields_AndPersists()
    {
        var dbPath = GetTempDbPath("game_over_reset");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Player HP 2, Capacity 2, active 0, recharge 5
        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_run_state
                SET player_current_hp = 2,
                    shield_capacity = 2,
                    active_shields = 0,
                    shield_recharge_units = 5
                WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        });

        // Incorrect answer. Incoming enemy damage is 4 > player HP 2.
        // No shields available -> Game Over!
        var intent = CreateIntent("sub-go-1", isCorrect: false);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.NotNull(receipt.TransitionResult);
        Assert.True(receipt.TransitionResult.IsGameOver);
        Assert.NotNull(receipt.TransitionResult.TerminalSnapshot);
        Assert.Equal(0, receipt.TransitionResult.TerminalSnapshot.PlayerCurrentHp);
        Assert.Equal(1, receipt.TransitionResult.NextState.Sector);
        Assert.Equal(0, receipt.TransitionResult.NextState.OpponentIndex);
        Assert.Equal(100, receipt.TransitionResult.NextState.PlayerCurrentHp);
        Assert.Equal(2, receipt.TransitionResult.NextState.ShieldCapacity);
        Assert.Equal(2, receipt.TransitionResult.NextState.ActiveShields);
        Assert.Equal(0, receipt.TransitionResult.NextState.ShieldRechargeUnits);

        var durableRun = await store.GetRunStateAsync();
        Assert.Equal(1, durableRun.Sector);
        Assert.Equal(0, durableRun.OpponentIndex);
        Assert.Equal(100, durableRun.PlayerCurrentHp);
        Assert.Equal(2, durableRun.ShieldCapacity);
        Assert.Equal(2, durableRun.ActiveShields);
        Assert.Equal(0, durableRun.ShieldRechargeUnits);

        var replay = await store.GetReceiptAsync("sub-go-1");
        Assert.NotNull(replay);
        Assert.NotNull(replay.TransitionResult);
        Assert.True(replay.TransitionResult.IsGameOver);
        Assert.Equal(2, replay.TransitionResult.NextState.ActiveShields);
        Assert.Equal(0, replay.TransitionResult.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task AttemptTransaction_OpponentDefeat_CarriesRechargeToNextOpponent()
    {
        var dbPath = GetTempDbPath("defeat_recharge_carry");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Opponent HP 1, Capacity 2, active 0, recharge 10
        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_run_state
                SET opponent_current_hp = 1,
                    shield_capacity = 2,
                    active_shields = 0,
                    shield_recharge_units = 10
                WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        });

        var intent = CreateIntent("sub-def-1", isCorrect: true, effectiveDamage: 1);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.NotNull(receipt.TransitionResult);
        Assert.True(receipt.TransitionResult.IsOpponentDefeated);
        // Correct answer gives +2 recharge: 10 + 2 = 12
        Assert.Equal(12, receipt.TransitionResult.NextState.ShieldRechargeUnits);
        Assert.Equal(0, receipt.TransitionResult.NextState.ActiveShields);
        Assert.Equal(2, receipt.TransitionResult.NextState.ShieldCapacity);

        var durableRun = await store.GetRunStateAsync();
        Assert.Equal(12, durableRun.ShieldRechargeUnits);
        Assert.Equal(0, durableRun.ActiveShields);
        Assert.Equal(2, durableRun.ShieldCapacity);
    }

    [Fact]
    public async Task ColdRestart_PreservesAuthoritativeShieldState()
    {
        var dbPath = GetTempDbPath("cold_restart_shields");

        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
            await SetRunShieldStateAsync(store1, capacity: 3, active: 2, recharge: 17);
        }

        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();
            var runState = await store2.GetRunStateAsync();
            Assert.Equal(3, runState.ShieldCapacity);
            Assert.Equal(2, runState.ActiveShields);
            Assert.Equal(17, runState.ShieldRechargeUnits);
        }
    }

    [Fact]
    public async Task DuplicateSubmission_DoesNotConsumeShieldTwice()
    {
        var dbPath = GetTempDbPath("dup_no_double_consume");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetRunShieldStateAsync(store, capacity: 1, active: 1, recharge: 0);

        var intent = CreateIntent("sub-dup-1", isCorrect: false);
        var receipt1 = await store.ApplyAttemptTransactionAsync(intent);
        Assert.True(receipt1.TransitionResult?.IsShieldAbsorbed);
        Assert.Equal(0, receipt1.TransitionResult?.NextState.ActiveShields);
        Assert.Equal(1, receipt1.TransitionResult?.NextState.ShieldRechargeUnits);

        // Submit same intent again (duplicate)
        var receipt2 = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(receipt1.SubmissionId, receipt2.SubmissionId);
        Assert.Equal(receipt1.ProcessedAt, receipt2.ProcessedAt);

        var durableRun = await store.GetRunStateAsync();
        Assert.Equal(0, durableRun.ActiveShields);
        Assert.Equal(1, durableRun.ShieldRechargeUnits); // Still 1, NOT 2!
    }

    [Fact]
    public async Task DuplicateSubmission_DoesNotRechargeShieldTwice()
    {
        var dbPath = GetTempDbPath("dup_no_double_recharge");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetRunShieldStateAsync(store, capacity: 2, active: 0, recharge: 10);

        var intent = CreateIntent("sub-dup-rech", isCorrect: true, effectiveDamage: 1);
        var receipt1 = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(12, receipt1.TransitionResult?.NextState.ShieldRechargeUnits);

        var receipt2 = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(receipt1.SubmissionId, receipt2.SubmissionId);

        var durableRun = await store.GetRunStateAsync();
        Assert.Equal(12, durableRun.ShieldRechargeUnits); // Still 12, NOT 14!
    }

    [Fact]
    public async Task ReceiptReplay_AppliedReceipt_ReturnsExactShieldFields()
    {
        var dbPath = GetTempDbPath("receipt_replay");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetRunShieldStateAsync(store, capacity: 3, active: 1, recharge: 20);

        var intent = CreateIntent("sub-replay-1", isCorrect: true, effectiveDamage: 1);
        var originalReceipt = await store.ApplyAttemptTransactionAsync(intent);

        var replayedReceipt = await store.GetReceiptAsync("sub-replay-1");
        Assert.NotNull(replayedReceipt);
        Assert.NotNull(replayedReceipt.TransitionResult);

        Assert.Equal(originalReceipt.TransitionResult?.IsShieldAbsorbed, replayedReceipt.TransitionResult.IsShieldAbsorbed);
        Assert.Equal(originalReceipt.TransitionResult?.AbsorbedEnemyDamage, replayedReceipt.TransitionResult.AbsorbedEnemyDamage);
        Assert.Equal(originalReceipt.TransitionResult?.NextState.ShieldCapacity, replayedReceipt.TransitionResult.NextState.ShieldCapacity);
        Assert.Equal(originalReceipt.TransitionResult?.NextState.ActiveShields, replayedReceipt.TransitionResult.NextState.ActiveShields);
        Assert.Equal(originalReceipt.TransitionResult?.NextState.ShieldRechargeUnits, replayedReceipt.TransitionResult.NextState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task CalmModeSuppressed_DoesNotAlterShieldState()
    {
        var dbPath = GetTempDbPath("calm_no_alter");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetRunShieldStateAsync(store, capacity: 2, active: 1, recharge: 10);

        var intent = CreateIntent("sub-calm-1", isEligible: false);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, receipt.ReceiptKind);
        Assert.Null(receipt.TransitionResult);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(2, runState.ShieldCapacity);
        Assert.Equal(1, runState.ActiveShields);
        Assert.Equal(10, runState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task ResetGameplayStateAsync_ResetsShieldStateAndFirewallProgressionToZero()
    {
        var dbPath = GetTempDbPath("reset_shields");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetRunShieldStateAsync(store, capacity: 2, active: 1, recharge: 15);
        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_progression SET firewall_rank = 2, consecutive_early_defeats = 2, is_beginner_assistance_active = 1 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        });

        await store.BeginOrGetResetIntentAsync();
        await store.ResetGameplayStateAsync(targetEpoch: 1);
        await store.ClearResetIntentAsync(targetEpoch: 1);

        var runState = await store.GetRunStateAsync();
        Assert.Equal(0, runState.ShieldCapacity);
        Assert.Equal(0, runState.ActiveShields);
        Assert.Equal(0, runState.ShieldRechargeUnits);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT firewall_rank, consecutive_early_defeats, is_beginner_assistance_active FROM gameplay_progression WHERE id = 1;";
        using var reader = await checkCmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0)); // firewall_rank
        Assert.Equal(0, reader.GetInt32(1)); // consecutive_early_defeats
        Assert.Equal(0, reader.GetInt32(2)); // is_beginner_assistance_active
    }

    [Fact]
    public async Task ResetEpochMismatch_RejectsAttemptWithoutMutatingShields()
    {
        var dbPath = GetTempDbPath("epoch_mismatch");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await SetRunShieldStateAsync(store, capacity: 2, active: 1, recharge: 10);

        var intent = CreateIntent("sub-bad-epoch", resetEpoch: 999);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(intent));

        var runState = await store.GetRunStateAsync();
        Assert.Equal(2, runState.ShieldCapacity);
        Assert.Equal(1, runState.ActiveShields);
        Assert.Equal(10, runState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task ActivatePendingFirewallCapacityAsync_ZeroRank_ReturnsCurrentStateNoOp()
    {
        var dbPath = GetTempDbPath("activate_zero_rank");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var result = await store.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 0, expectedResetEpoch: 0);

        Assert.Equal(0, result.RunState!.ShieldCapacity);
        Assert.Equal(0, result.RunState.ActiveShields);
        Assert.Equal(0, result.RunState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task ActivatePendingFirewallCapacityAsync_DeltaPositive_ActivatesCapacityAndPreservesDepletedSlots()
    {
        var dbPath = GetTempDbPath("activate_delta_positive");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Simulate purchased Rank 2 in progression, but currently capacity 1, active 0, recharge 15 in run state
        await SetRunShieldStateAsync(store, capacity: 1, active: 0, recharge: 15);
        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_progression SET firewall_rank = 2, player_level = 20 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        });

        var result = await store.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 2, expectedResetEpoch: 0);

        Assert.Equal(2, result.RunState!.ShieldCapacity);
        Assert.Equal(1, result.RunState.ActiveShields);
        Assert.Equal(15, result.RunState.ShieldRechargeUnits);
    }

    [Fact]
    public async Task ActivatePendingFirewallCapacityAsync_PendingReset_ThrowsInvalidOperationException()
    {
        var dbPath = GetTempDbPath("activate_pending_reset");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.BeginOrGetResetIntentAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 1, expectedResetEpoch: 0));
    }

    [Fact]
    public async Task ActivatePendingFirewallCapacityAsync_EpochMismatch_ThrowsInvalidOperationException()
    {
        var dbPath = GetTempDbPath("activate_epoch_mismatch");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 1, expectedResetEpoch: 99));
    }

    [Fact]
    public async Task ActivatePendingFirewallCapacityAsync_UnsettledIntent_ReturnsDeferredPendingIntent()
    {
        var dbPath = GetTempDbPath("activate_unsettled_intent");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-unsettled");
        await store.StagePendingIntentAsync(intent);

        var result = await store.ActivatePendingFirewallCapacityAsync(questionBoundTargetCapacity: 1, expectedResetEpoch: 0);
        Assert.Equal(CyberDefenseFirewallActivationStatus.DeferredPendingIntent, result.Status);
    }
}
