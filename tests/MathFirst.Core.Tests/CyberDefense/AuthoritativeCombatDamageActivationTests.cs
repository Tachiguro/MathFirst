namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.IO;
using System.Threading.Tasks;
using MathFirst.Application.Persistence;
using MathFirst.Domain.CyberDefense;
using MathFirst.Infrastructure.Sqlite.Gameplay;
using Xunit;

public sealed class AuthoritativeCombatDamageActivationTests : IDisposable
{
    private readonly string _testDirectory;

    public AuthoritativeCombatDamageActivationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MathFirstTests_Combat_" + Guid.NewGuid().ToString("N"));
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

    private string GetTempDbPath(string name = "combat_activation_test")
    {
        return Path.Combine(_testDirectory, $"{name}_{Guid.NewGuid():N}.db");
    }

    private static CyberDefensePendingIntentRecord CreateIntent(
        string submissionId,
        bool isCorrect = true,
        bool isEligible = true,
        int effectiveDamage = 1,
        long resetEpoch = 0,
        string factId = "add:2+2",
        long responseLatencyMs = 150)
    {
        return new CyberDefensePendingIntentRecord(
            submissionId: submissionId,
            factId: factId,
            isCorrect: isCorrect,
            isEligible: isEligible,
            responseLatencyMs: responseLatencyMs,
            resetEpoch: resetEpoch,
            createdAt: DateTimeOffset.UtcNow,
            effectiveAttackDamage: effectiveDamage);
    }

    [Fact]
    public async Task T01_Rank0_DirectApplication_RequestsExcessiveDamage_FailsClosed()
    {
        var dbPath = GetTempDbPath("t01_excessive_dmg");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-01", effectiveDamage: 999999);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(intent));

        var runState = await store.GetRunStateAsync();
        Assert.Equal(2, runState.CurrentOpponent.CurrentHp);
        Assert.Equal(100, runState.PlayerCurrentHp);

        var prog = await store.GetProgressionAsync();
        Assert.Equal(0, prog.CumulativeXp);
        Assert.Equal(1, prog.StoreRevision);

        var receipt = await store.GetReceiptAsync("sub-01");
        Assert.Null(receipt);
    }

    [Fact]
    public async Task T02_Rank0_DirectApplication_RequestsDamage2_FailsClosed()
    {
        var dbPath = GetTempDbPath("t02_rank0_dmg2");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-02", effectiveDamage: 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(intent));

        var runState = await store.GetRunStateAsync();
        Assert.Equal(2, runState.CurrentOpponent.CurrentHp);

        var receipt = await store.GetReceiptAsync("sub-02");
        Assert.Null(receipt);
    }

    [Fact]
    public async Task T03_StagedDamage1_DirectApplicationCraftedDamage50_FailsClosed()
    {
        var dbPath = GetTempDbPath("t03_crafted_damage50");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var staged = CreateIntent("sub-03", effectiveDamage: 1);
        await store.StagePendingIntentAsync(staged);

        var crafted = CreateIntent("sub-03", effectiveDamage: 50);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(crafted));

        // Staged intent remains intact in store
        var stored = await store.GetPendingIntentAsync("sub-03");
        Assert.NotNull(stored);
        Assert.Equal(1, stored.EffectiveAttackDamage);

        // No receipt created
        var receipt = await store.GetReceiptAsync("sub-03");
        Assert.Null(receipt);
    }

    [Fact]
    public async Task T04_StagedDamage2_DirectApplicationDamage3_Rank3Entitles4_FailsClosed()
    {
        var dbPath = GetTempDbPath("t04_conflict_within_entitlement");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 10, available_sp = 10 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0); // rank 1, dmg 2
        var staged = CreateIntent("sub-04", effectiveDamage: 2);
        await store.StagePendingIntentAsync(staged);

        await store.PurchaseAttackUpgradeAsync(1, 0); // rank 2, dmg 3
        await store.PurchaseAttackUpgradeAsync(2, 0); // rank 3, dmg 4

        // Crafted direct application passes damage 3 (less than entitlement 4, but conflicts with staged 2)
        var crafted = CreateIntent("sub-04", effectiveDamage: 3);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(crafted));

        var stored = await store.GetPendingIntentAsync("sub-04");
        Assert.NotNull(stored);
        Assert.Equal(2, stored.EffectiveAttackDamage);

        var receipt = await store.GetReceiptAsync("sub-04");
        Assert.Null(receipt);
    }

    [Fact]
    public async Task T05_ValidStagedIntent_WithDamageLessThanCurrentEntitlement_AppliesOriginalDamage()
    {
        var dbPath = GetTempDbPath("t05_dmg_less_than_entitlement");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 10, available_sp = 10 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0); // rank 1, dmg 2
        var staged = CreateIntent("sub-05", effectiveDamage: 2);
        await store.StagePendingIntentAsync(staged);

        await store.PurchaseAttackUpgradeAsync(1, 0); // rank 2, dmg 3
        await store.PurchaseAttackUpgradeAsync(2, 0); // rank 3, dmg 4

        // Apply original staged intent (dmg 2)
        var receipt = await store.ApplyAttemptTransactionAsync(staged);
        Assert.Equal(2, receipt.TransitionResult?.RequestedAttackDamage);
        Assert.Equal(CyberDefenseReceiptKind.Applied, receipt.ReceiptKind);

        // Pending intent deleted
        var stored = await store.GetPendingIntentAsync("sub-05");
        Assert.Null(stored);
    }

    [Fact]
    public async Task T06_ConflictingPendingIntentIdentityFields_RejectedFailClosed()
    {
        var dbPath = GetTempDbPath("t06_conflicting_identity");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var staged = CreateIntent("sub-06", isCorrect: true, isEligible: true, effectiveDamage: 1, resetEpoch: 0, factId: "add:2+2", responseLatencyMs: 150);
        await store.StagePendingIntentAsync(staged);

        // Conflict in FactId
        var conflictFact = CreateIntent("sub-06", isCorrect: true, isEligible: true, effectiveDamage: 1, resetEpoch: 0, factId: "add:9+9", responseLatencyMs: 150);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(conflictFact));

        // Conflict in IsCorrect
        var conflictCorrect = CreateIntent("sub-06", isCorrect: false, isEligible: true, effectiveDamage: 1, resetEpoch: 0, factId: "add:2+2", responseLatencyMs: 150);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(conflictCorrect));

        // Conflict in IsEligible
        var conflictEligible = CreateIntent("sub-06", isCorrect: true, isEligible: false, effectiveDamage: 1, resetEpoch: 0, factId: "add:2+2", responseLatencyMs: 150);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(conflictEligible));

        // Conflict in ResponseLatencyMs
        var conflictLatency = CreateIntent("sub-06", isCorrect: true, isEligible: true, effectiveDamage: 1, resetEpoch: 0, factId: "add:2+2", responseLatencyMs: 9999);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(conflictLatency));

        // Conflict in ResetEpoch
        var conflictEpoch = CreateIntent("sub-06", isCorrect: true, isEligible: true, effectiveDamage: 1, resetEpoch: 5, factId: "add:2+2", responseLatencyMs: 150);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(conflictEpoch));

        // Staged row untouched
        var stored = await store.GetPendingIntentAsync("sub-06");
        Assert.NotNull(stored);
        Assert.Equal("add:2+2", stored.FactId);
        Assert.True(stored.IsCorrect);
    }

    [Fact]
    public async Task T07_PendingIntent_PreservesSuppliedDamageThroughColdRestart()
    {
        var dbPath = GetTempDbPath("t07_cold_restart");
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            // Upgrade attack rank to 1 so entitlement check passes
            // Give 1 SP to purchase
            await store.ExecuteUnderGateForTestAsync(async conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 1, available_sp = 1 WHERE id = 1;";
                await cmd.ExecuteNonQueryAsync();
            });
            await store.PurchaseAttackUpgradeAsync(0, 0);

            var intent = CreateIntent("sub-07", effectiveDamage: 2);
            await store.StagePendingIntentAsync(intent);
        }

        // Cold restart
        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var restored = await store.GetPendingIntentAsync("sub-07");
            Assert.NotNull(restored);
            Assert.Equal(2, restored.EffectiveAttackDamage);
        }
    }

    [Fact]
    public async Task T08_DuplicateIntentStaging_WithDifferentDamage_IsRejectedFailClosed()
    {
        var dbPath = GetTempDbPath("t08_duplicate_conflict");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 5, available_sp = 5 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0); // rank 1, dmg 2

        var intent1 = CreateIntent("sub-08", effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent1);

        // Attempting to stage same submission ID with damage 2 must fail closed
        var intentConflict = CreateIntent("sub-08", effectiveDamage: 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StagePendingIntentAsync(intentConflict));

        // Original intent remains intact with damage 1
        var stored = await store.GetPendingIntentAsync("sub-08");
        Assert.NotNull(stored);
        Assert.Equal(1, stored.EffectiveAttackDamage);
    }

    [Fact]
    public async Task T09_BaseRank0_ExecutesDamage1()
    {
        var dbPath = GetTempDbPath("t09_rank0_dmg1");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var runInitial = await store.GetRunStateAsync();
        Assert.Equal(2, runInitial.CurrentOpponent.CurrentHp);

        var intent = CreateIntent("sub-09", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);

        var receipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(1, receipt.TransitionResult?.AppliedOpponentDamage);
        Assert.Equal(1, receipt.TransitionResult?.RequestedAttackDamage);

        var runAfter = await store.GetRunStateAsync();
        Assert.Equal(1, runAfter.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public async Task T10_PurchasedRank1_ExecutesDamage2()
    {
        var dbPath = GetTempDbPath("t10_rank1_dmg2");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 1, available_sp = 1 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0); // Rank 1 (Damage 2)

        var runInitial = await store.GetRunStateAsync();
        Assert.Equal(2, runInitial.CurrentOpponent.CurrentHp);

        var intent = CreateIntent("sub-10", isCorrect: true, effectiveDamage: 2);
        await store.StagePendingIntentAsync(intent);

        var receipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(2, receipt.TransitionResult?.AppliedOpponentDamage);
        Assert.Equal(2, receipt.TransitionResult?.RequestedAttackDamage);
        Assert.True(receipt.TransitionResult?.IsOpponentDefeated);

        var runAfter = await store.GetRunStateAsync();
        Assert.Equal(1, runAfter.OpponentIndex); // Advanced to next opponent!
    }

    [Fact]
    public async Task T11_PurchasedRank3_ExecutesDamage4()
    {
        var dbPath = GetTempDbPath("t11_rank3_dmg4");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Ranks 1 (cost 1), 2 (cost 2), 3 (cost 2) => total 5 SP
        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 10, available_sp = 10 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0);
        await store.PurchaseAttackUpgradeAsync(1, 0);
        await store.PurchaseAttackUpgradeAsync(2, 0);

        var intent = CreateIntent("sub-11", isCorrect: true, effectiveDamage: 4);
        await store.StagePendingIntentAsync(intent);

        var receipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(4, receipt.TransitionResult?.RequestedAttackDamage);
        // Opponent HP was 2, so applied was 2, excess was 2
        Assert.Equal(2, receipt.TransitionResult?.AppliedOpponentDamage);
        Assert.Equal(2, receipt.TransitionResult?.ExcessOpponentDamage);
        Assert.True(receipt.TransitionResult?.IsOpponentDefeated);
    }

    [Fact]
    public async Task T12_HigherDamage_ChangesActualOpponentHp_ByPermittedAmount()
    {
        var dbPath = GetTempDbPath("t12_opp_hp");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Advance to a sector or opponent with larger HP (Sector 10 Boss has 18 HP, index 8)
        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_run_state SET sector = 10, opponent_index = 8, opponent_current_hp = 18, player_current_hp = 100 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();

            using var progCmd = conn.CreateCommand();
            progCmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 10, available_sp = 10 WHERE id = 1;";
            await progCmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0);
        await store.PurchaseAttackUpgradeAsync(1, 0); // Rank 2, damage 3

        var intent = CreateIntent("sub-12", isCorrect: true, effectiveDamage: 3);
        await store.StagePendingIntentAsync(intent);

        var receipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(3, receipt.TransitionResult?.AppliedOpponentDamage);
        Assert.Equal(15, receipt.TransitionResult?.NextState.CurrentOpponent.CurrentHp);

        var stateAfter = await store.GetRunStateAsync();
        Assert.Equal(15, stateAfter.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public async Task T13_Overkill_DoesNotFabricateExtraExecutedHits()
    {
        var dbPath = GetTempDbPath("t13_overkill_hits");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var progCmd = conn.CreateCommand();
            progCmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 10, available_sp = 10 WHERE id = 1;";
            await progCmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0);
        await store.PurchaseAttackUpgradeAsync(1, 0);
        await store.PurchaseAttackUpgradeAsync(2, 0); // Damage 4

        // Opponent HP is 2. Dealing 4 damage defeats opponent in 1 single hit.
        var intent = CreateIntent("sub-13", isCorrect: true, effectiveDamage: 4);
        await store.StagePendingIntentAsync(intent);

        var receipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(1, receipt.TransitionResult?.ExecutedAttacksOnDefeatedOpponent);
        Assert.Equal(2, receipt.TransitionResult?.ExcessOpponentDamage);
    }

    [Fact]
    public async Task T14_IncorrectAnswer_DoesNotPerformPlayerAttack()
    {
        var dbPath = GetTempDbPath("t14_incorrect_no_attack");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var progCmd = conn.CreateCommand();
            progCmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 5, available_sp = 5 WHERE id = 1;";
            await progCmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0); // Rank 1, damage 2

        var intent = CreateIntent("sub-14", isCorrect: false, effectiveDamage: 2);
        await store.StagePendingIntentAsync(intent);

        var receipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(0, receipt.TransitionResult?.RequestedAttackDamage);
        Assert.Equal(0, receipt.TransitionResult?.AppliedOpponentDamage);
        Assert.False(receipt.TransitionResult?.IsOpponentDefeated);
    }

    [Fact]
    public async Task T15_Receipt_RecordsBoundRequestedDamage()
    {
        var dbPath = GetTempDbPath("t15_receipt_damage");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var progCmd = conn.CreateCommand();
            progCmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 5, available_sp = 5 WHERE id = 1;";
            await progCmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0);

        var intent = CreateIntent("sub-15", isCorrect: true, effectiveDamage: 2);
        await store.StagePendingIntentAsync(intent);

        var receipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(2, receipt.TransitionResult?.RequestedAttackDamage);

        // Verify receipt in ledger directly
        var fetched = await store.GetReceiptAsync("sub-15");
        Assert.NotNull(fetched);
        Assert.Equal(2, fetched.TransitionResult?.RequestedAttackDamage);
    }

    [Fact]
    public async Task T16_DuplicateSubmissionId_DoesNotDealDamageTwice()
    {
        var dbPath = GetTempDbPath("t16_duplicate_no_double_damage");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var progCmd = conn.CreateCommand();
            progCmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 5, available_sp = 5 WHERE id = 1;";
            await progCmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0);

        var intent = CreateIntent("sub-16", isCorrect: true, effectiveDamage: 2);
        await store.StagePendingIntentAsync(intent);

        var receipt1 = await store.ApplyAttemptTransactionAsync(intent);
        var runStateAfterFirst = await store.GetRunStateAsync();

        // Replay same intent
        var receipt2 = await store.ApplyAttemptTransactionAsync(intent);
        var runStateAfterSecond = await store.GetRunStateAsync();

        Assert.Equal(receipt1.SubmissionId, receipt2.SubmissionId);
        Assert.Equal(runStateAfterFirst.Sector, runStateAfterSecond.Sector);
        Assert.Equal(runStateAfterFirst.OpponentIndex, runStateAfterSecond.OpponentIndex);
        Assert.Equal(runStateAfterFirst.CurrentOpponent.CurrentHp, runStateAfterSecond.CurrentOpponent.CurrentHp);
    }

    [Fact]
    public async Task T17_DuplicateSubmissionId_DoesNotAwardXpTwice()
    {
        var dbPath = GetTempDbPath("t17_duplicate_no_double_xp");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-17", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);

        var receipt1 = await store.ApplyAttemptTransactionAsync(intent);
        var prog1 = await store.GetProgressionAsync();

        var receipt2 = await store.ApplyAttemptTransactionAsync(intent);
        var prog2 = await store.GetProgressionAsync();

        Assert.Equal(receipt1.AwardedXp, receipt2.AwardedXp);
        Assert.Equal(prog1.CumulativeXp, prog2.CumulativeXp);
        Assert.Equal(prog1.PlayerLevel, prog2.PlayerLevel);
    }

    [Fact]
    public async Task T18_ConflictingDuplicateIntent_WithChangedDamage_CannotRewriteOriginalReceipt()
    {
        var dbPath = GetTempDbPath("t18_conflict_no_rewrite");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var progCmd = conn.CreateCommand();
            progCmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 5, available_sp = 5 WHERE id = 1;";
            await progCmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0);

        var intent1 = CreateIntent("sub-18", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent1);
        var receiptOriginal = await store.ApplyAttemptTransactionAsync(intent1);
        Assert.Equal(1, receiptOriginal.TransitionResult?.RequestedAttackDamage);

        // Conflicting duplicate attempt with damage 2
        var intentConflict = CreateIntent("sub-18", isCorrect: true, effectiveDamage: 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(intentConflict));

        // Receipt remains identical
        var fetched = await store.GetReceiptAsync("sub-18");
        Assert.NotNull(fetched);
        Assert.Equal(1, fetched.TransitionResult?.RequestedAttackDamage);
    }

    [Fact]
    public async Task T19_RejectedCraftedDamage_LeavesOriginalStagedIntentIntactAndRecoverable()
    {
        var dbPath = GetTempDbPath("t19_crafted_intact");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 5, available_sp = 5 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0); // Rank 1, dmg 2

        var legitimate = CreateIntent("sub-19", effectiveDamage: 2);
        await store.StagePendingIntentAsync(legitimate);

        var crafted = CreateIntent("sub-19", effectiveDamage: 999);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(crafted));

        var pending = await store.GetPendingIntentAsync("sub-19");
        Assert.NotNull(pending);
        Assert.Equal(2, pending.EffectiveAttackDamage);

        // Legitimate intent can now be successfully applied
        var receipt = await store.ApplyAttemptTransactionAsync(legitimate);
        Assert.NotNull(receipt);
        Assert.Equal(2, receipt.TransitionResult?.RequestedAttackDamage);
    }

    [Fact]
    public async Task T20_NewerPurchasedRank_CannotRetroactivelyAlterOlderStagedIntent()
    {
        var dbPath = GetTempDbPath("t20_no_retroactive_upgrade");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // 1. Stage an intent at rank 0 (damage 1)
        var intent = CreateIntent("sub-20", isCorrect: true, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);

        // 2. Player purchases upgrade to rank 1 (damage 2) while intent is staged
        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var progCmd = conn.CreateCommand();
            progCmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 5, available_sp = 5 WHERE id = 1;";
            await progCmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0);

        // 3. Apply the previously staged intent
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        // 4. Must consume the bound damage 1, NOT the newly purchased rank's damage 2
        Assert.Equal(1, receipt.TransitionResult?.RequestedAttackDamage);
    }

    [Fact]
    public async Task T21_RejectedCraftedDamage_LeavesRunHpXpSpRankReceiptCountAndRevisionUnchanged()
    {
        var dbPath = GetTempDbPath("t21_crafted_no_mutation");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var initialRun = await store.GetRunStateAsync();
        var initialProg = await store.GetProgressionAsync();

        var crafted = CreateIntent("sub-21", effectiveDamage: 999999);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(crafted));

        var finalRun = await store.GetRunStateAsync();
        var finalProg = await store.GetProgressionAsync();

        Assert.Equal(initialRun.Sector, finalRun.Sector);
        Assert.Equal(initialRun.OpponentIndex, finalRun.OpponentIndex);
        Assert.Equal(initialRun.CurrentOpponent.CurrentHp, finalRun.CurrentOpponent.CurrentHp);
        Assert.Equal(initialRun.PlayerCurrentHp, finalRun.PlayerCurrentHp);

        Assert.Equal(initialProg.StoreRevision, finalProg.StoreRevision);
        Assert.Equal(initialProg.CumulativeXp, finalProg.CumulativeXp);
        Assert.Equal(initialProg.PlayerLevel, finalProg.PlayerLevel);
        Assert.Equal(initialProg.EarnedSkillPoints, finalProg.EarnedSkillPoints);
        Assert.Equal(initialProg.SpentSkillPoints, finalProg.SpentSkillPoints);
        Assert.Equal(initialProg.AvailableSkillPoints, finalProg.AvailableSkillPoints);
        Assert.Equal(initialProg.AttackRank, finalProg.AttackRank);

        var receipt = await store.GetReceiptAsync("sub-21");
        Assert.Null(receipt);
    }

    [Fact]
    public async Task T22_DuplicateReceiptReplay_SucceedsAfterPendingIntentDeletion_AndDoesNotReapplyCombat()
    {
        var dbPath = GetTempDbPath("t22_replay_after_deletion");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-22", effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);

        var r1 = await store.ApplyAttemptTransactionAsync(intent);
        Assert.NotNull(r1);

        // Verify pending intent deleted
        var pending = await store.GetPendingIntentAsync("sub-22");
        Assert.Null(pending);

        var runAfterR1 = await store.GetRunStateAsync();
        var revAfterR1 = await store.GetStoreRevisionAsync();

        // Replay
        var r2 = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(r1.SubmissionId, r2.SubmissionId);
        Assert.Equal(r1.AwardedXp, r2.AwardedXp);

        var runAfterR2 = await store.GetRunStateAsync();
        var revAfterR2 = await store.GetStoreRevisionAsync();

        Assert.Equal(runAfterR1.CurrentOpponent.CurrentHp, runAfterR2.CurrentOpponent.CurrentHp);
        Assert.Equal(revAfterR1, revAfterR2);
    }

    [Fact]
    public async Task T23_HistoricalDamage1IntentRecovery_AndHistoricalReceiptReplay_Preserved()
    {
        var dbPath = GetTempDbPath("t23_historical_compat");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Insert historical-style pending intent directly with damage 1
        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO gameplay_pending_intent (
                    submission_id, fact_id, is_correct, is_eligible, response_latency_ms, reset_epoch, created_at, effective_attack_damage
                ) VALUES (
                    'sub-hist-1', 'add:2+2', 1, 1, 150, 0, @now, 1
                );
            ";
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        });

        var restoredIntent = await store.GetPendingIntentAsync("sub-hist-1");
        Assert.NotNull(restoredIntent);
        Assert.Equal(1, restoredIntent.EffectiveAttackDamage);

        // Apply attempt
        var receipt = await store.ApplyAttemptTransactionAsync(restoredIntent);
        Assert.Equal(1, receipt.TransitionResult?.RequestedAttackDamage);

        // Replay historical receipt
        var replay = await store.ApplyAttemptTransactionAsync(restoredIntent);
        Assert.Equal(receipt.SubmissionId, replay.SubmissionId);
    }

    [Fact]
    public async Task T24_CalmModeSuppression_AndNullCombatBehavior_RemainUnchanged()
    {
        var dbPath = GetTempDbPath("t24_calm_mode");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-24", isCorrect: true, isEligible: false, effectiveDamage: 1);
        await store.StagePendingIntentAsync(intent);

        var receipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(CyberDefenseReceiptKind.CalmModeSuppressed, receipt.ReceiptKind);
        Assert.Null(receipt.TransitionResult);
    }

    [Fact]
    public async Task T25_StaleResetEpoch_IsRejected_EvenWhenDamageWithinEntitlement()
    {
        var dbPath = GetTempDbPath("t25_stale_epoch");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_progression SET earned_sp = 5, available_sp = 5 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        });
        await store.PurchaseAttackUpgradeAsync(0, 0); // Rank 1, dmg 2

        // Store is epoch 0, intent has epoch 1, damage is 2 (within entitlement)
        var staleIntent = CreateIntent("sub-25", effectiveDamage: 2, resetEpoch: 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(staleIntent));
    }

    [Fact]
    public async Task T26_SpConservation_SurvivesUpgradedAttacksAndResultingLevelUps()
    {
        var dbPath = GetTempDbPath("t26_sp_conservation");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        // Set XP just 5 below level-up threshold (Level 2 requires 80 XP)
        await store.ExecuteUnderGateForTestAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE gameplay_progression SET cumulative_xp = 75, player_level = 1, earned_sp = 1, spent_sp = 1, available_sp = 0, attack_rank = 1 WHERE id = 1;";
            await cmd.ExecuteNonQueryAsync();
        });

        var intent = CreateIntent("sub-26", isCorrect: true, effectiveDamage: 2);
        await store.StagePendingIntentAsync(intent);

        var receipt = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(7, receipt.AwardedXp);
        Assert.Equal(2, receipt.PlayerLevelAfter);

        var prog = await store.GetProgressionAsync();
        Assert.Equal(2, prog.PlayerLevel);
        Assert.Equal(prog.EarnedSkillPoints, checked(prog.SpentSkillPoints + prog.AvailableSkillPoints));
    }

    [Fact]
    public async Task T27_PendingFullLocalReset_BlocksNewApplications()
    {
        var dbPath = GetTempDbPath("t27_pending_reset");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        await store.BeginOrGetResetIntentAsync();

        var intent = CreateIntent("sub-27", effectiveDamage: 1);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAttemptTransactionAsync(intent));
        Assert.Contains("Cannot apply attempt transaction while a reset is pending", ex.Message);
    }

    [Fact]
    public async Task T28_DirectApplication_WithoutStaging_WithinEntitlement_AppliesCorrectly()
    {
        var dbPath = GetTempDbPath("t28_direct_apply");
        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = CreateIntent("sub-28", effectiveDamage: 1);
        var receipt = await store.ApplyAttemptTransactionAsync(intent);

        Assert.NotNull(receipt);
        Assert.Equal(CyberDefenseReceiptKind.Applied, receipt.ReceiptKind);
        Assert.Equal(1, receipt.TransitionResult?.RequestedAttackDamage);
        Assert.Equal(1, receipt.TransitionResult?.AppliedOpponentDamage);

        var run = await store.GetRunStateAsync();
        Assert.Equal(1, run.CurrentOpponent.CurrentHp);

        var rev = await store.GetStoreRevisionAsync();
        Assert.Equal(2, rev);

        // Replay of this direct receipt also works
        var replay = await store.ApplyAttemptTransactionAsync(intent);
        Assert.Equal(receipt.SubmissionId, replay.SubmissionId);
        Assert.Equal(2, await store.GetStoreRevisionAsync());
    }
}
