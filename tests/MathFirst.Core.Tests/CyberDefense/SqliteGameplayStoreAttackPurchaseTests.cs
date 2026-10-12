namespace MathFirst.Core.Tests.CyberDefense;

using System;
using System.Collections.Generic;
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

public sealed class SqliteGameplayStoreAttackPurchaseTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MathFirst_Tests_AttackPurchase_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private SqliteGameplayStore CreateStore(string? dir = null)
    {
        dir ??= CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");
        return new SqliteGameplayStore(dbPath);
    }

    private SqliteLearnerStore CreateLearnerStore(string? dir = null)
    {
        dir ??= CreateTempDirectory();
        var dbPath = Path.Combine(dir, "learner_test.db");
        return new SqliteLearnerStore(dbPath);
    }

    private static async Task SetProgressionSpAsync(string dbPath, int earnedSp, int spentSp, int availableSp, int attackRank)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE gameplay_progression
            SET earned_sp = @earned,
                spent_sp = @spent,
                available_sp = @available,
                attack_rank = @rank
            WHERE id = 1;
        ";
        cmd.Parameters.AddWithValue("@earned", earnedSp);
        cmd.Parameters.AddWithValue("@spent", spentSp);
        cmd.Parameters.AddWithValue("@available", availableSp);
        cmd.Parameters.AddWithValue("@rank", attackRank);
        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // Ignored in test cleanup
            }
        }
    }

    // =========================================================================
    // Core Purchasing & SP Conservation (Tests 8, 9, 10, 11, 12, 13)
    // =========================================================================

    [Fact]
    public async Task Test08_Level1PurchaseEligibility_SucceedsWhenSpAvailable()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Player is level 1, grant 1 earned and 1 available SP
        await SetProgressionSpAsync(dbPath, earnedSp: 1, spentSp: 0, availableSp: 1, attackRank: 0);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var updated = await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

            Assert.Equal(1, updated.AttackRank);
            Assert.Equal(1, updated.PlayerLevel);
            Assert.Equal(1, updated.EarnedSkillPoints);
            Assert.Equal(1, updated.SpentSkillPoints);
            Assert.Equal(0, updated.AvailableSkillPoints);
        }
    }

    [Fact]
    public async Task Test09_InsufficientSp_RejectsWithoutChanges()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var initialProg = await store.GetProgressionAsync();
        Assert.Equal(0, initialProg.AvailableSkillPoints);
        Assert.Equal(0, initialProg.AttackRank);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0));

        Assert.Contains("Insufficient available skill points", ex.Message, StringComparison.OrdinalIgnoreCase);

        var afterProg = await store.GetProgressionAsync();
        Assert.Equal(initialProg.StoreRevision, afterProg.StoreRevision);
        Assert.Equal(initialProg.AttackRank, afterProg.AttackRank);
        Assert.Equal(initialProg.AvailableSkillPoints, afterProg.AvailableSkillPoints);
        Assert.Equal(initialProg.SpentSkillPoints, afterProg.SpentSkillPoints);
        Assert.Equal(initialProg.EarnedSkillPoints, afterProg.EarnedSkillPoints);
    }

    [Fact]
    public async Task Test10_11_12_13_SuccessfulPurchase_IncrementsRank_ChargesCost_ConservesSp()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Grant 5 SP at rank 0
        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var progBefore = await store.GetProgressionAsync();

            // Rank 1 costs 1 SP
            var prog1 = await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

            // Test 10: Increments rank by exactly 1
            Assert.Equal(1, prog1.AttackRank);

            // Test 11: Charges exact cost (1 SP)
            Assert.Equal(1, prog1.SpentSkillPoints);
            Assert.Equal(4, prog1.AvailableSkillPoints);

            // Test 12: Earned SP remains unchanged
            Assert.Equal(5, prog1.EarnedSkillPoints);
            Assert.Equal(progBefore.EarnedSkillPoints, prog1.EarnedSkillPoints);

            // Test 13: SP conservation: earned == spent + available
            Assert.Equal(prog1.EarnedSkillPoints, checked(prog1.SpentSkillPoints + prog1.AvailableSkillPoints));

            // Revision advanced by 1
            Assert.Equal(progBefore.StoreRevision + 1, prog1.StoreRevision);

            // Purchase rank 2 (costs 2 SP)
            var prog2 = await store.PurchaseAttackUpgradeAsync(expectedRank: 1, expectedResetEpoch: 0);

            Assert.Equal(2, prog2.AttackRank);
            Assert.Equal(3, prog2.SpentSkillPoints);     // 1 + 2
            Assert.Equal(2, prog2.AvailableSkillPoints); // 4 - 2
            Assert.Equal(5, prog2.EarnedSkillPoints);
            Assert.Equal(prog2.EarnedSkillPoints, checked(prog2.SpentSkillPoints + prog2.AvailableSkillPoints));
        }
    }

    // =========================================================================
    // Concurrency, Duplicate and Stale Purchase Protection (Tests 14, 15, 16, 17, 18)
    // =========================================================================

    [Fact]
    public async Task Test14_DuplicateSameExpectedRankPurchase_CannotDoubleSpend()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Grant 5 SP (enough for rank 1 and rank 2)
        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();

            // First call with expectedRank 0 succeeds
            var prog1 = await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
            Assert.Equal(1, prog1.AttackRank);
            Assert.Equal(4, prog1.AvailableSkillPoints);

            // Immediate duplicate call with same expectedRank 0 must be rejected
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0));

            Assert.Contains("Expected attack rank 0 does not match current rank 1", ex.Message);

            // Durable state is unchanged from prog1
            var currentProg = await store.GetProgressionAsync();
            Assert.Equal(1, currentProg.AttackRank);
            Assert.Equal(4, currentProg.AvailableSkillPoints);
            Assert.Equal(1, currentProg.SpentSkillPoints);
            Assert.Equal(prog1.StoreRevision, currentProg.StoreRevision);
        }
    }

    [Fact]
    public async Task Test15_ConcurrentPurchases_CannotSpendSameRankTwice()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Grant 10 SP
        await SetProgressionSpAsync(dbPath, earnedSp: 10, spentSp: 0, availableSp: 10, attackRank: 0);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();

            int successCount = 0;
            int failureCount = 0;

            // Launch 10 concurrent requests all specifying expectedRank: 0
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
                        System.Threading.Interlocked.Increment(ref successCount);
                    }
                    catch (InvalidOperationException)
                    {
                        System.Threading.Interlocked.Increment(ref failureCount);
                    }
                }));
            }

            await Task.WhenAll(tasks);

            Assert.Equal(1, successCount);
            Assert.Equal(9, failureCount);

            var finalProg = await store.GetProgressionAsync();
            Assert.Equal(1, finalProg.AttackRank);
            Assert.Equal(1, finalProg.SpentSkillPoints);
            Assert.Equal(9, finalProg.AvailableSkillPoints);
        }
    }

    [Fact]
    public async Task Test16_StaleExpectedRankPurchase_Rejects()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 2);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();

            // Store has rank 2, but caller specifies stale rank 1
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.PurchaseAttackUpgradeAsync(expectedRank: 1, expectedResetEpoch: 0));

            Assert.Contains("Expected attack rank 1 does not match current rank 2", ex.Message);
        }
    }

    [Fact]
    public async Task Test17_ResetEpochMismatch_Rejects()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();

            // Store epoch is 0, caller specifies epoch 1
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 1));

            Assert.Contains("does not match current store reset epoch 0", ex.Message);
        }
    }

    [Fact]
    public async Task Test18_PendingResetIntent_BlocksPurchase()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();

            // Begin reset intent
            await store.BeginOrGetResetIntentAsync();

            // Attempt purchase while reset is pending
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0));

            Assert.Contains("Cannot purchase attack upgrade while a reset is pending", ex.Message);
        }
    }

    // =========================================================================
    // Rollback, Persistence, and Reset (Tests 19, 20, 21, 22)
    // =========================================================================

    [Fact]
    public async Task Test19_FailedSqliteOperation_RollsBackCleanly()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            var progBefore = await store.GetProgressionAsync();

            // Deliberately trigger failure by passing invalid epoch
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 99));

            var progAfter = await store.GetProgressionAsync();
            Assert.Equal(progBefore.StoreRevision, progAfter.StoreRevision);
            Assert.Equal(progBefore.AttackRank, progAfter.AttackRank);
            Assert.Equal(progBefore.SpentSkillPoints, progAfter.SpentSkillPoints);
            Assert.Equal(progBefore.AvailableSkillPoints, progAfter.AvailableSkillPoints);
        }
    }

    [Fact]
    public async Task Test20_ColdRestart_PreservesRankAndSp()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 10, spentSp: 0, availableSp: 10, attackRank: 0);

        CyberDefenseProgressionRecord purchasedProg;
        using (var store1 = new SqliteGameplayStore(dbPath))
        {
            await store1.InitializeAsync();
            // Purchase rank 1 (cost 1)
            await store1.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);
            // Purchase rank 2 (cost 2)
            purchasedProg = await store1.PurchaseAttackUpgradeAsync(expectedRank: 1, expectedResetEpoch: 0);

            Assert.Equal(2, purchasedProg.AttackRank);
            Assert.Equal(3, purchasedProg.SpentSkillPoints);
            Assert.Equal(7, purchasedProg.AvailableSkillPoints);

            await store1.CloseAsync();
        }

        // Cold restart with new store instance
        using (var store2 = new SqliteGameplayStore(dbPath))
        {
            await store2.InitializeAsync();
            var coldProg = await store2.GetProgressionAsync();

            Assert.Equal(purchasedProg.AttackRank, coldProg.AttackRank);
            Assert.Equal(purchasedProg.SpentSkillPoints, coldProg.SpentSkillPoints);
            Assert.Equal(purchasedProg.AvailableSkillPoints, coldProg.AvailableSkillPoints);
            Assert.Equal(purchasedProg.EarnedSkillPoints, coldProg.EarnedSkillPoints);
            Assert.Equal(purchasedProg.StoreRevision, coldProg.StoreRevision);
        }
    }

    [Fact]
    public async Task Test21_GameOver_RetainsRankAndSp()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

            var progAfterPurchase = await store.GetProgressionAsync();
            Assert.Equal(1, progAfterPurchase.AttackRank);
            Assert.Equal(1, progAfterPurchase.SpentSkillPoints);

            // Execute fatal counter-damage attempts until game-over
            // Initial HP is 100, Sector 1 enemy counter-damage is 4.
            // 25 incorrect answers = 100 damage -> Game Over!
            for (int i = 1; i <= 25; i++)
            {
                var intent = new CyberDefensePendingIntentRecord(
                    $"sub-fatal-{i}", "1+1", isCorrect: false, isEligible: true, 100, 0, DateTimeOffset.UtcNow);
                await store.StagePendingIntentAsync(intent);
                var receipt = await store.ApplyAttemptTransactionAsync(intent);

                if (receipt.TransitionResult!.IsGameOver)
                {
                    break;
                }
            }

            var runState = await store.GetRunStateAsync();
            Assert.Equal(1, runState.Sector);
            Assert.Equal(100, runState.PlayerCurrentHp);

            // Progression must retain rank and SP!
            var progAfterGameOver = await store.GetProgressionAsync();
            Assert.Equal(1, progAfterGameOver.AttackRank);
            Assert.Equal(1, progAfterGameOver.SpentSkillPoints);
        }
    }

    [Fact]
    public async Task Test22_FullLocalReset_ClearsRankAndSp()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

            // Execute two-phase crash-safe reset
            var intent = await store.BeginOrGetResetIntentAsync();
            await store.ResetGameplayStateAsync(intent.TargetEpoch);
            await store.ClearResetIntentAsync(intent.TargetEpoch);

            var resetProg = await store.GetProgressionAsync();
            Assert.Equal(0, resetProg.AttackRank);
            Assert.Equal(0, resetProg.EarnedSkillPoints);
            Assert.Equal(0, resetProg.SpentSkillPoints);
            Assert.Equal(0, resetProg.AvailableSkillPoints);
            Assert.Equal(0L, resetProg.CumulativeXp);
            Assert.Equal(1, resetProg.PlayerLevel);
            Assert.Equal(1L, resetProg.ResetEpoch);

            // Old pre-reset purchase request must be rejected
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0));
        }
    }

    // =========================================================================
    // XP Coexistence and Level Progression (Tests 23, 24)
    // =========================================================================

    [Fact]
    public async Task Test23_SubsequentXpSettlement_PreservesPurchasedRank()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();
            await store.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

            // Solve a question to earn XP
            var intent = new CyberDefensePendingIntentRecord(
                "sub-xp-1", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);
            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            Assert.Equal(6, receipt.AwardedXp);

            var prog = await store.GetProgressionAsync();
            Assert.Equal(1, prog.AttackRank); // Rank preserved!
            Assert.Equal(1, prog.SpentSkillPoints);
            Assert.Equal(4, prog.AvailableSkillPoints);
            Assert.Equal(5, prog.EarnedSkillPoints);
            Assert.Equal(6L, prog.CumulativeXp);
        }
    }

    [Fact]
    public async Task Test24_SubsequentLevelUp_PreservesSpentSp()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Set XP to 75 (5 away from Level 2 at 80), grant 1 earned and 1 spent SP, rank 1
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE gameplay_progression
                SET cumulative_xp = 75,
                    player_level = 1,
                    earned_sp = 1,
                    spent_sp = 1,
                    available_sp = 0,
                    attack_rank = 1
                WHERE id = 1;
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();

            // Correct attempt awards 6 XP: 75 + 6 = 81 XP -> Level 2 reached!
            var intent = new CyberDefensePendingIntentRecord(
                "sub-levelup-spend", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);
            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            Assert.Equal(2, receipt.PlayerLevelAfter);

            var prog = await store.GetProgressionAsync();
            Assert.Equal(2, prog.PlayerLevel);
            Assert.Equal(2, prog.EarnedSkillPoints);    // +1 gained from level up
            Assert.Equal(1, prog.SpentSkillPoints);     // Spent SP preserved!
            Assert.Equal(1, prog.AvailableSkillPoints); // +1 available
            Assert.Equal(1, prog.AttackRank);           // Rank preserved!
            Assert.Equal(prog.EarnedSkillPoints, checked(prog.SpentSkillPoints + prog.AvailableSkillPoints));
        }
    }

    // =========================================================================
    // Non-Interference, Overflow & Snapshot Tests (Tests 26, 27, 28, 29, 30)
    // =========================================================================

    [Fact]
    public async Task Test26_AttackRankOverflow_FailsClosedInStore()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Set rank to int.MaxValue
        await SetProgressionSpAsync(dbPath, earnedSp: 100, spentSp: 0, availableSp: 100, attackRank: int.MaxValue);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.PurchaseAttackUpgradeAsync(expectedRank: int.MaxValue, expectedResetEpoch: 0));

            Assert.Contains("overflow", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Test27_ReadOnlySnapshot_ViaGetAttackUpgradeSnapshotAsync_MatchesStore()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        await SetProgressionSpAsync(dbPath, earnedSp: 5, spentSp: 1, availableSp: 4, attackRank: 1);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();

            var snapshot = await store.GetAttackUpgradeSnapshotAsync();

            Assert.Equal(1, snapshot.CurrentAttackRank);
            Assert.Equal(2, snapshot.NextAttackRank);
            Assert.Equal(2, snapshot.CurrentBaseDamage);
            Assert.Equal(3, snapshot.NextBaseDamage);
            Assert.Equal(2, snapshot.NextRankCost);
            Assert.Equal(4, snapshot.AvailableSkillPoints);
            Assert.True(snapshot.HasSufficientSkillPoints);
            Assert.True(snapshot.CanAfford);
            Assert.Equal(1, snapshot.PlayerLevel);
            Assert.Equal(0L, snapshot.ResetEpoch);
        }
    }

    [Fact]
    public async Task Test28_NoCombatDamageActivationInThisSlice_EffectiveDamageRemainsOne()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using (var setupStore = new SqliteGameplayStore(dbPath))
        {
            await setupStore.InitializeAsync();
        }

        // Set player attack rank to 5 (which would be 6 damage if active)
        await SetProgressionSpAsync(dbPath, earnedSp: 15, spentSp: 15, availableSp: 0, attackRank: 5);

        using (var store = new SqliteGameplayStore(dbPath))
        {
            await store.InitializeAsync();

            var intent = new CyberDefensePendingIntentRecord(
                "sub-damage-check", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);
            await store.StagePendingIntentAsync(intent);
            var receipt = await store.ApplyAttemptTransactionAsync(intent);

            // Effective combat damage applied in this slice MUST remain 1
            Assert.Equal(1, receipt.TransitionResult!.AppliedOpponentDamage);
            Assert.Equal(1, receipt.TransitionResult.RequestedAttackDamage);
        }
    }

    [Fact]
    public async Task Test29_DuplicateSubmission_PreservesExactReceiptWithoutReAwardingXp()
    {
        var dir = CreateTempDirectory();
        var dbPath = Path.Combine(dir, "gameplay_test.db");

        using var store = new SqliteGameplayStore(dbPath);
        await store.InitializeAsync();

        var intent = new CyberDefensePendingIntentRecord(
            "sub-dup-check", "1+1", isCorrect: true, isEligible: true, 100, 0, DateTimeOffset.UtcNow);
        await store.StagePendingIntentAsync(intent);
        var receipt1 = await store.ApplyAttemptTransactionAsync(intent);

        var prog1 = await store.GetProgressionAsync();

        // Re-apply identical intent
        var receipt2 = await store.ApplyAttemptTransactionAsync(intent);

        Assert.Equal(receipt1.SubmissionId, receipt2.SubmissionId);
        Assert.Equal(receipt1.AwardedXp, receipt2.AwardedXp);
        Assert.Equal(receipt1.CumulativeXpAfter, receipt2.CumulativeXpAfter);

        var prog2 = await store.GetProgressionAsync();
        Assert.Equal(prog1.CumulativeXp, prog2.CumulativeXp);
        Assert.Equal(prog1.StoreRevision, prog2.StoreRevision);
    }

    [Fact]
    public async Task Test30_MathCurriculumAndLearnerStore_RemainIndependentAndUnaffected()
    {
        var dir = CreateTempDirectory();
        var gameplayDbPath = Path.Combine(dir, "gameplay_test.db");
        var learnerDbPath = Path.Combine(dir, "learner_test.db");

        using var gameplayStore = new SqliteGameplayStore(gameplayDbPath);
        await gameplayStore.InitializeAsync();

        using var learnerStore = new SqliteLearnerStore(learnerDbPath);
        await learnerStore.InitializeAsync();

        // Grant SP and purchase in gameplay store
        await SetProgressionSpAsync(gameplayDbPath, earnedSp: 5, spentSp: 0, availableSp: 5, attackRank: 0);
        await gameplayStore.PurchaseAttackUpgradeAsync(expectedRank: 0, expectedResetEpoch: 0);

        // Verify learner store snapshot is pure and untouched
        var learnerSnapshot = await learnerStore.LoadSnapshotAsync();
        Assert.Equal(0, learnerSnapshot.Progression.PracticePosition);
        Assert.Equal(CurriculumStage.Stage1_Addition, learnerSnapshot.Progression.CurriculumStage);
        Assert.Empty(learnerSnapshot.ItemStates);
    }
}
